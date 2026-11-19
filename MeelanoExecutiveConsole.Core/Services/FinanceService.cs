using MeelanoExecutiveConsole.Models;

namespace MeelanoExecutiveConsole.Services;

/// <summary>
/// Demo cash-flow calculations. Forecasts include only pending checks and assume an
/// opening balance of zero. This service is not an accounting ledger or bank balance.
/// </summary>
public sealed class FinanceService
{
    private const int ForecastDays = 30;
    private const int DashboardDays = 7;
    private readonly IRepository repository;
    private readonly Func<DateTime> todayProvider;

    public FinanceService(IRepository repository, Func<DateTime>? todayProvider = null)
    {
        ArgumentNullException.ThrowIfNull(repository);
        this.repository = repository;
        this.todayProvider = todayProvider ?? (() => DateTime.Today);
    }

    public DateTime Today => todayProvider().Date;
    public IReadOnlyList<CheckItem> Checks => repository.GetChecks();
    public IReadOnlyList<SaleItem> Sales => repository.GetSales();

    public decimal Incoming(DateTime from, DateTime to) => SumPending(CheckKind.دریافتی, from, to);

    public decimal Outgoing(DateTime from, DateTime to) => SumPending(CheckKind.پرداختی, from, to);

    public decimal Net7
    {
        get
        {
            var today = Today;
            var lastDay = today.AddDays(DashboardDays - 1);
            return Incoming(today, lastDay) - Outgoing(today, lastDay);
        }
    }

    public IReadOnlyList<RadarPoint> BuildRadar(IEnumerable<CheckItem>? selectedIncoming = null, int shiftDays = 0)
    {
        var today = Today;
        var lastDay = today.AddDays(ForecastDays - 1);
        var normalizedShift = Math.Clamp(shiftDays, -14, 30);
        var shiftedNumbers = (selectedIncoming ?? [])
            .Where(check => check.Kind == CheckKind.دریافتی && check.Status == CheckStatuses.Pending)
            .Select(check => check.Number)
            .ToHashSet(StringComparer.Ordinal);
        var dailyAmounts = new Dictionary<DateTime, (decimal Incoming, decimal Outgoing)>();

        foreach (var check in Checks)
        {
            if (check.Status != CheckStatuses.Pending
                || check.Kind is not (CheckKind.دریافتی or CheckKind.پرداختی))
            {
                continue;
            }

            var dueDate = check.DueDate.Date;
            if (check.Kind == CheckKind.دریافتی && normalizedShift != 0 && shiftedNumbers.Contains(check.Number))
                dueDate = ShiftDateSafely(dueDate, normalizedShift);

            // Overdue pending commitments are shown on day zero rather than silently omitted.
            if (dueDate < today)
                dueDate = today;
            if (dueDate > lastDay)
                continue;

            var previous = dailyAmounts.GetValueOrDefault(dueDate);
            dailyAmounts[dueDate] = check.Kind == CheckKind.دریافتی
                ? (previous.Incoming + check.Amount, previous.Outgoing)
                : (previous.Incoming, previous.Outgoing + check.Amount);
        }

        var daily = new List<RadarPoint>(ForecastDays);
        decimal balance = 0;
        for (var index = 0; index < ForecastDays; index++)
        {
            var day = today.AddDays(index);
            var amounts = dailyAmounts.GetValueOrDefault(day);
            balance += amounts.Incoming - amounts.Outgoing;
            daily.Add(new RadarPoint(day, amounts.Incoming, amounts.Outgoing, balance));
        }

        return daily;
    }

    public (DateTime? Date, decimal Amount) WeightedDate(IEnumerable<CheckItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var today = Today;
        var pending = items.Where(check => check.Status == CheckStatuses.Pending).ToList();
        var total = pending.Sum(check => check.Amount);
        if (total <= 0)
            return (null, 0);

        var weightedDays = pending.Sum(check => check.Amount * (check.DueDate.Date - today).Days) / total;
        var roundedDays = decimal.ToInt32(decimal.Round(weightedDays, 0, MidpointRounding.AwayFromZero));
        return (today.AddDays(roundedDays), total);
    }

    /// <summary>Applies the allowed demo status transitions; invalid transitions are rejected.</summary>
    public bool TryChangeStatus(CheckItem check, string targetStatus)
    {
        ArgumentNullException.ThrowIfNull(check);
        ArgumentNullException.ThrowIfNull(targetStatus);

        if (!Enum.IsDefined(typeof(CheckKind), check.Kind)
            || !CheckStatuses.All.Contains(check.Status, StringComparer.Ordinal)
            || !CanTransition(check, targetStatus))
        {
            return false;
        }

        check.Status = targetStatus;
        return true;
    }

    private decimal SumPending(CheckKind kind, DateTime from, DateTime to)
    {
        var start = from.Date;
        var end = to.Date;
        if (start > end)
            return 0;

        return Checks
            .Where(check => check.Kind == kind
                            && check.Status == CheckStatuses.Pending
                            && check.DueDate.Date >= start
                            && check.DueDate.Date <= end)
            .Sum(check => check.Amount);
    }

    private static bool CanTransition(CheckItem check, string targetStatus)
    {
        if (!CheckStatuses.All.Contains(targetStatus, StringComparer.Ordinal)
            || check.Status == targetStatus)
        {
            return false;
        }

        if (targetStatus == CheckStatuses.Suspended)
            return check.Status == CheckStatuses.Pending;
        if (targetStatus == CheckStatuses.Pending)
            return check.Status == CheckStatuses.Suspended;
        if (targetStatus == CheckStatuses.Collected)
            return check.Kind == CheckKind.دریافتی
                   && (check.Status == CheckStatuses.Pending || check.Status == CheckStatuses.Suspended);
        if (targetStatus == CheckStatuses.Paid)
            return check.Kind == CheckKind.پرداختی
                   && (check.Status == CheckStatuses.Pending || check.Status == CheckStatuses.Suspended);
        if (targetStatus == CheckStatuses.Returned)
            return check.Status == CheckStatuses.Pending || check.Status == CheckStatuses.Suspended;

        return false;
    }

    private static DateTime ShiftDateSafely(DateTime date, int shiftDays)
    {
        try
        {
            return date.AddDays(shiftDays);
        }
        catch (ArgumentOutOfRangeException)
        {
            return shiftDays < 0 ? DateTime.MinValue : DateTime.MaxValue;
        }
    }
}
