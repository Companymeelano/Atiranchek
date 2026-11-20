using MeelanoExecutiveConsole.Models;

namespace MeelanoExecutiveConsole.Services;

/// <summary>Check-only counterparty exposure; groups exact names, without claiming ledger balances.</summary>
public static class CounterpartyReportService
{
    public sealed record Row(string Party, int IncomingCount, decimal IncomingPending, decimal IncomingOverdue,
        int OutgoingCount, decimal OutgoingPending, decimal OutgoingOverdue,
        decimal IncomingReturned, decimal OutgoingReturned)
    {
        public string PartyDisplay => string.IsNullOrEmpty(Party) ? "(نام خالی در منبع)" : Party;
        public decimal NetPending => IncomingPending - OutgoingPending;
        public string IncomingCountText => PersianFormatting.Number(IncomingCount);
        public string OutgoingCountText => PersianFormatting.Number(OutgoingCount);
        public string IncomingText => PersianFormatting.MoneyExact(IncomingPending);
        public string IncomingOverdueText => PersianFormatting.MoneyExact(IncomingOverdue);
        public string OutgoingText => PersianFormatting.MoneyExact(OutgoingPending);
        public string OutgoingOverdueText => PersianFormatting.MoneyExact(OutgoingOverdue);
        public string IncomingReturnedText => PersianFormatting.MoneyExact(IncomingReturned);
        public string OutgoingReturnedText => PersianFormatting.MoneyExact(OutgoingReturned);
        public string NetText => PersianFormatting.MoneyExact(NetPending);
    }

    public static IReadOnlyList<Row> Analyze(IEnumerable<CheckItem> checks, DateTime asOf)
    {
        ArgumentNullException.ThrowIfNull(checks);
        var day = asOf.Date;
        return checks.Where(check => (check.Status is CheckStatuses.Pending or CheckStatuses.Returned)
                                     && (check.Kind is CheckKind.دریافتی or CheckKind.پرداختی))
            .GroupBy(check => check.Party ?? string.Empty, StringComparer.Ordinal)
            .Select(group =>
            {
                var incoming = group.Where(c => c.Status == CheckStatuses.Pending && c.Kind == CheckKind.دریافتی).ToArray();
                var outgoing = group.Where(c => c.Status == CheckStatuses.Pending && c.Kind == CheckKind.پرداختی).ToArray();
                return new Row(group.Key, incoming.Length, incoming.Sum(c => c.Amount), incoming.Where(c => c.DueDate.Date < day).Sum(c => c.Amount),
                    outgoing.Length, outgoing.Sum(c => c.Amount), outgoing.Where(c => c.DueDate.Date < day).Sum(c => c.Amount),
                    group.Where(c => c.Status == CheckStatuses.Returned && c.Kind == CheckKind.دریافتی).Sum(c => c.Amount),
                    group.Where(c => c.Status == CheckStatuses.Returned && c.Kind == CheckKind.پرداختی).Sum(c => c.Amount));
            })
            .OrderByDescending(row => row.IncomingPending + row.OutgoingPending)
            .ThenBy(row => row.Party, StringComparer.Ordinal)
            .ToArray();
    }
}
