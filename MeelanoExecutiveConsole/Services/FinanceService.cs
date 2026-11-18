using MeelanoExecutiveConsole.Models;

namespace MeelanoExecutiveConsole.Services;

/// <summary>
/// Sample cash-flow calculations. Only pending demo checks are included in projections;
/// suspended, returned, collected, and paid checks are excluded.
/// </summary>
public sealed class FinanceService(IRepository repository)
{
    public IReadOnlyList<CheckItem> Checks => repository.GetChecks();
    public IReadOnlyList<SaleItem> Sales => repository.GetSales();

    public decimal Incoming(DateTime from, DateTime to) => Checks
        .Where(check => check.Kind == CheckKind.دریافتی
                        && check.Status == CheckStatuses.Pending
                        && check.DueDate.Date >= from.Date
                        && check.DueDate.Date <= to.Date)
        .Sum(check => check.Amount);

    public decimal Outgoing(DateTime from, DateTime to) => Checks
        .Where(check => check.Kind == CheckKind.پرداختی
                        && check.Status == CheckStatuses.Pending
                        && check.DueDate.Date >= from.Date
                        && check.DueDate.Date <= to.Date)
        .Sum(check => check.Amount);

    public decimal Net7 => Incoming(DateTime.Today, DateTime.Today.AddDays(7))
                           - Outgoing(DateTime.Today, DateTime.Today.AddDays(7));

    public IReadOnlyList<RadarPoint> BuildRadar(IEnumerable<CheckItem>? selectedIncoming = null, int shiftDays = 0)
    {
        var today = DateTime.Today;
        var shiftedNumbers = (selectedIncoming ?? [])
            .Where(check => check.Kind == CheckKind.دریافتی && check.Status == CheckStatuses.Pending)
            .Select(check => check.Number)
            .ToHashSet(StringComparer.Ordinal);
        var daily = new List<RadarPoint>(30);
        decimal balance = 0;

        for (var index = 0; index < 30; index++)
        {
            var day = today.AddDays(index);
            var incoming = Checks
                .Where(check => check.Kind == CheckKind.دریافتی && check.Status == CheckStatuses.Pending)
                .Where(check => AdjustedDueDate(check, shiftedNumbers, shiftDays, today) == day)
                .Sum(check => check.Amount);
            var outgoing = Checks
                .Where(check => check.Kind == CheckKind.پرداختی
                                && check.Status == CheckStatuses.Pending
                                && check.DueDate.Date == day)
                .Sum(check => check.Amount);

            balance += incoming - outgoing;
            daily.Add(new RadarPoint(day, incoming, outgoing, balance));
        }

        return daily;
    }

    public (DateTime? Date, decimal Amount) WeightedDate(IEnumerable<CheckItem> items)
    {
        var pending = items.Where(check => check.Status == CheckStatuses.Pending).ToList();
        var total = pending.Sum(check => check.Amount);
        if (total <= 0)
            return (null, 0);

        var weightedDays = pending.Sum(check => check.Amount * (decimal)(check.DueDate.Date - DateTime.Today).TotalDays) / total;
        return (DateTime.Today.AddDays((double)weightedDays), total);
    }

    private static DateTime AdjustedDueDate(CheckItem check, ISet<string> shiftedNumbers, int shiftDays, DateTime today)
    {
        var dueDate = check.DueDate.Date;
        if (shiftDays != 0 && shiftedNumbers.Contains(check.Number))
            dueDate = dueDate.AddDays(shiftDays);

        // Earlier-than-today scenarios remain visible in the first day of the forecast.
        return dueDate < today ? today : dueDate;
    }
}
