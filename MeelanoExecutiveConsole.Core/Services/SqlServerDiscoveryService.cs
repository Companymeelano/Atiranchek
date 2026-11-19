using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace MeelanoExecutiveConsole.Services;

/// <summary>Opt-in, bounded IPv4 LAN discovery. No credentials are sent by the probe.</summary>
public static class SqlServerDiscoveryService
{
    public sealed record LocalNetwork(string Adapter, string Address, int PrefixLength, string Gateway)
    {
        public string Display => $"{Adapter} · IP داخلی: {Address}/{PrefixLength} · دروازه: {Gateway}";
    }
    public sealed record Server(string Address, int Port)
    {
        public string Display => $"{Address} : {Port} · پاسخ SQL/TDS";
    }

    public static IReadOnlyList<LocalNetwork> GetLocalNetworks()
    {
        var result = new List<LocalNetwork>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;
            try
            {
                var properties = nic.GetIPProperties();
                var gateway = properties.GatewayAddresses.Select(g => g.Address)
                    .FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork && !ip.Equals(IPAddress.Any));
                foreach (var address in properties.UnicastAddresses)
                {
                    if (address.Address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(address.Address))
                        continue;
                    int prefix = address.PrefixLength;
                    if (prefix is >= 1 and <= 32)
                        result.Add(new LocalNetwork(nic.Name, address.Address.ToString(), prefix, gateway?.ToString() ?? "—"));
                }
            }
            catch (NetworkInformationException) { /* Adapter disappeared; continue with other adapters. */ }
        }
        return result;
    }

    // Never scan more than the local /24. For narrower masks, stay inside the actual subnet.
    public static IReadOnlyList<IPAddress> EnumerateTargets(string address, int prefixLength)
    {
        if (!IPAddress.TryParse(address, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork || prefixLength is < 1 or > 32)
            throw new ArgumentException("آدرس یا ماسک شبکه معتبر نیست.");
        int prefix = Math.Max(prefixLength, 24);
        uint value = BinaryPrimitives.ReadUInt32BigEndian(ip.GetAddressBytes());
        uint mask = prefix == 32 ? uint.MaxValue : uint.MaxValue << (32 - prefix);
        uint start = value & mask;
        uint count = 1u << (32 - prefix);
        var targets = new List<IPAddress>();
        for (uint i = 0; i < count; i++)
        {
            // /31 and /32 can be point-to-point/single-host networks.
            if (prefix < 31 && (i == 0 || i == count - 1)) continue;
            var bytes = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(bytes, start + i);
            targets.Add(new IPAddress(bytes));
        }
        return targets;
    }

    public static async Task<IReadOnlyList<Server>> DiscoverAsync(LocalNetwork network, IEnumerable<int> ports,
        IProgress<(int Done, int Total)>? progress, CancellationToken cancellationToken)
    {
        var portList = ports.Distinct().ToArray();
        if (portList.Length == 0 || portList.Length > 3 || portList.Any(p => p is < 1 or > 65535))
            throw new ArgumentException("پورت جستجو نامعتبر است.");
        var targets = (from ip in EnumerateTargets(network.Address, network.PrefixLength)
                       from port in portList select (ip, port)).ToArray();
        var found = new ConcurrentBag<Server>();
        int done = 0;
        await Parallel.ForEachAsync(targets, new ParallelOptions { MaxDegreeOfParallelism = 32, CancellationToken = cancellationToken },
            async (target, token) =>
            {
                if (await ProbeAsync(target.ip, target.port, token).ConfigureAwait(false))
                    found.Add(new Server(target.ip.ToString(), target.port));
                var current = Interlocked.Increment(ref done);
                if (current % 8 == 0 || current == targets.Length)
                    progress?.Report((current, targets.Length));
            }).ConfigureAwait(false);
        return found.OrderBy(s => BinaryPrimitives.ReadUInt32BigEndian(IPAddress.Parse(s.Address).GetAddressBytes()))
            .ThenBy(s => s.Port).ToArray();
    }

    // TDS PRELOGIN: descriptors are token, offset, length; offsets are relative to payload.
    public static byte[] CreatePreloginPacket() =>
    [
        0x12, 0x01, 0x00, 0x1a, 0, 0, 1, 0,
        0x00, 0x00, 0x0b, 0x00, 0x06, // VERSION
        0x01, 0x00, 0x11, 0x00, 0x01, // ENCRYPTION
        0xff,
        0x0f, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x01 // ENCRYPT_ON; no login follows
    ];

    public static bool IsPreloginResponse(ReadOnlySpan<byte> packet)
    {
        if (packet.Length < 14 || packet[0] != 0x04 || (packet[1] & 1) == 0 ||
            BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(2, 2)) != packet.Length)
            return false;
        var payload = packet[8..];
        int pos = 0;
        bool version = false, encryption = false;
        while (pos < payload.Length && payload[pos] != 0xff)
        {
            if (pos + 5 > payload.Length) return false;
            byte token = payload[pos];
            int offset = BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(pos + 1, 2));
            int length = BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(pos + 3, 2));
            if (offset + length > payload.Length) return false;
            if (token == 0 && length == 6) version = true;
            if (token == 1 && length == 1 && payload[offset] <= 3) encryption = true;
            pos += 5;
        }
        return pos < payload.Length && payload[pos] == 0xff && version && encryption;
    }

    private static async Task<bool> ProbeAsync(IPAddress ip, int port, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(1200));
        try
        {
            using var client = new TcpClient(AddressFamily.InterNetwork);
            await client.ConnectAsync(ip, port, timeout.Token).ConfigureAwait(false);
            using var stream = client.GetStream();
            await stream.WriteAsync(CreatePreloginPacket(), timeout.Token).ConfigureAwait(false);
            var header = new byte[8];
            await stream.ReadExactlyAsync(header, timeout.Token).ConfigureAwait(false);
            int length = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(2, 2));
            if (length < 8 || length > 4096) return false;
            var packet = new byte[length];
            header.CopyTo(packet, 0);
            await stream.ReadExactlyAsync(packet.AsMemory(8), timeout.Token).ConfigureAwait(false);
            return IsPreloginResponse(packet);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return false; }
        catch (SocketException) { return false; }
        catch (IOException) { return false; }
    }
}
