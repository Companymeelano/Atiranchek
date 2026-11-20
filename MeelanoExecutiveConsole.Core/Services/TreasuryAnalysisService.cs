using MeelanoExecutiveConsole.Models;

namespace MeelanoExecutiveConsole.Services;

/// <summary>Check-only maturity analysis; not a bank balance or a general ledger.</summary>
public static class TreasuryAnalysisService
{
    public sealed record Bucket(string Label, int IncomingCount, decimal Incoming, int OutgoingCount, decimal Outgoing)
    {
        public decimal Net => Incoming - Outgoing;
        public string IncomingCountText => PersianFormatting.Number(IncomingCount);
        public string OutgoingCountText => PersianFormatting.Number(OutgoingCount);
        public string IncomingText => PersianFormatting.Money(Incoming);
        public string OutgoingText => PersianFormatting.Money(Outgoing);
        public string NetText => PersianFormatting.Money(Net);
    }

    public sealed record Report(DateTime AsOf, IReadOnlyList<Bucket> Buckets,
        decimal OverdueIncoming, decimal OverdueOutgoing,
        decimal ReturnedIncoming, decimal ReturnedOutgoing,
        decimal IncomingThrough30, decimal OutgoingThrough30)
    {
        // Includes overdue and today through day 30, inclusive. Undefined when no payments are due.
        public decimal? CoveragePercent => OutgoingThrough30 == 0 ? null : IncomingThrough30 / OutgoingThrough30 * 100m;
    }

    public static Report Analyze(IEnumerable<CheckItem> checks, DateTime today)
    {
        ArgumentNullException.ThrowIfNull(checks);
        today = today.Date;
        string[] labels = ["سررسید گذشته · ۱ تا ۳۰ روز", "سررسید گذشته · ۳۱ تا ۶۰ روز",
            "سررسید گذشته · ۶۱ تا ۹۰ روز", "سررسید گذشته · بیش از ۹۰ روز",
            "امروز", "۱ تا ۷ روز آینده", "۸ تا ۳۰ روز آینده", "بیش از ۳۰ روز آینده"];
        var incoming = new decimal[labels.Length];
        var outgoing = new decimal[labels.Length];
        var incomingCount = new int[labels.Length];
        var outgoingCount = new int[labels.Length];
        decimal returnedIncoming = 0, returnedOutgoing = 0;
        foreach (var check in checks)
        {
            if (check.Kind is not (CheckKind.دریافتی or CheckKind.پرداختی)) continue;
            if (check.Status == CheckStatuses.Returned)
            {
                if (check.Kind == CheckKind.دریافتی) returnedIncoming += check.Amount;
                else returnedOutgoing += check.Amount;
                continue;
            }
            if (check.Status != CheckStatuses.Pending) continue;
            var days = (check.DueDate.Date - today).Days;
            var bucket = days switch
            {
                < -90 => 3, < -60 => 2, < -30 => 1, < 0 => 0,
                0 => 4, <= 7 => 5, <= 30 => 6, _ => 7
            };
            if (check.Kind == CheckKind.دریافتی) { incoming[bucket] += check.Amount; incomingCount[bucket]++; }
            else { outgoing[bucket] += check.Amount; outgoingCount[bucket]++; }
        }
        var buckets = labels.Select((label, i) => new Bucket(label, incomingCount[i], incoming[i], outgoingCount[i], outgoing[i])).ToArray();
        return new Report(today, Array.AsReadOnly(buckets), incoming.Take(4).Sum(), outgoing.Take(4).Sum(),
            returnedIncoming, returnedOutgoing, incoming.Take(7).Sum(), outgoing.Take(7).Sum());
    }
}
