using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using MeelanoExecutiveConsole.Models;
using MeelanoExecutiveConsole.Services;

namespace MeelanoExecutiveConsole.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private const string AllTypes = "همهٔ چک‌ها";
    private const string AllStatuses = "همهٔ وضعیت‌ها";
    private readonly IRepository repository;
    private readonly FinanceService service;
    private List<CheckItem> selectedChecks = [];
    private string query = string.Empty;
    private string selectedCheckType = AllTypes;
    private string selectedStatus = AllStatuses;
    private double shiftDays;
    private int alertDays = 7;
    private DateTime alertsAsOf;
    private TreasuryAnalysisService.Report treasury = null!;

    public MainViewModel(IRepository? repository = null, Func<DateTime>? todayProvider = null)
    {
        this.repository = repository ?? new DemoRepository();
        service = new FinanceService(this.repository, todayProvider);
        Checks = new ObservableCollection<CheckItem>(service.Checks);
        Sales = new ObservableCollection<SaleItem>(service.Sales
            .OrderBy(sale => sale.Date)
            .ThenBy(sale => sale.Customer));
        var chartEnd = service.Today;
        var chartStart = chartEnd.AddDays(-89);
        SalesChart = new ObservableCollection<SalesDayPoint>(Sales
            .Where(sale => sale.Date.Date >= chartStart && sale.Date.Date <= chartEnd)
            .GroupBy(sale => sale.Date.Date)
            .OrderBy(group => group.Key)
            .Select(group => new SalesDayPoint(group.Key, group.Sum(sale => sale.Amount))));
        FilteredChecks = new ObservableCollection<CheckItem>();
        UpcomingChecks = new ObservableCollection<CheckItem>();
        Radar = new ObservableCollection<RadarPoint>();
        TreasuryBuckets = new ObservableCollection<TreasuryAnalysisService.Bucket>();
        CheckAlerts = new ObservableCollection<CheckAlertService.Alert>();
        CheckTypeFilters = new ObservableCollection<string>(
            [AllTypes, CheckKind.دریافتی.ToString(), CheckKind.پرداختی.ToString()]);
        StatusFilters = new ObservableCollection<string>([AllStatuses, .. CheckStatuses.All]);

        SetSalesChartHeights();
        Refresh();
        RefreshUpcomingChecks();
        RefreshForecast();
        RefreshTreasury();
        RefreshAlerts();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<CheckItem> Checks { get; }
    public ObservableCollection<SaleItem> Sales { get; }
    public ObservableCollection<SalesDayPoint> SalesChart { get; }
    public ObservableCollection<CheckItem> FilteredChecks { get; }
    public ObservableCollection<CheckItem> UpcomingChecks { get; }
    public ObservableCollection<RadarPoint> Radar { get; }
    public ObservableCollection<TreasuryAnalysisService.Bucket> TreasuryBuckets { get; }
    public ObservableCollection<CheckAlertService.Alert> CheckAlerts { get; }
    public string AlertsDescription => $"بررسی تا {PersianFormatting.Number(alertDays)} روز آینده به‌همراه همهٔ موارد سررسیدگذشته · {PersianFormatting.PersianDate(alertsAsOf)}";
    public string AlertsCount => PersianFormatting.Number(CheckAlerts.Count);
    public ObservableCollection<string> CheckTypeFilters { get; }
    public ObservableCollection<string> StatusFilters { get; }

    public string Query
    {
        get => query;
        set
        {
            var normalized = value ?? string.Empty;
            if (query == normalized)
                return;
            query = normalized;
            OnPropertyChanged();
            Refresh();
        }
    }

    public string SelectedCheckType
    {
        get => selectedCheckType;
        set
        {
            var normalized = value ?? AllTypes;
            if (selectedCheckType == normalized)
                return;
            selectedCheckType = normalized;
            OnPropertyChanged();
            Refresh();
        }
    }

    public string SelectedStatus
    {
        get => selectedStatus;
        set
        {
            var normalized = value ?? AllStatuses;
            if (selectedStatus == normalized)
                return;
            selectedStatus = normalized;
            OnPropertyChanged();
            Refresh();
        }
    }

    public double ShiftDays
    {
        get => shiftDays;
        set
        {
            var normalized = double.IsFinite(value)
                ? Math.Clamp(Math.Round(value), -14, 30)
                : 0;
            if (Math.Abs(shiftDays - normalized) < 0.001)
                return;
            shiftDays = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShiftDaysLabel));
            RefreshForecast();
        }
    }

    public string ShiftDaysLabel
    {
        get
        {
            var days = (int)ShiftDays;
            if (days == 0)
                return "بدون جابه‌جایی";
            var amount = PersianFormatting.Number(Math.Abs(days));
            return days > 0 ? $"{amount} روز دیرتر" : $"{amount} روز زودتر";
        }
    }

    public bool IsReadOnlyData => repository.IsReadOnly;
    public bool CanChangeStatus => !repository.IsReadOnly;
    public string DataSourceDescription => repository.DataSourceDescription;
    public string DataModeLabel => repository.IsReadOnly ? "●  snapshot SQL · فقط‌خواندنی" : "●  محیط نمونه";
    public string TodayDateText => $"{PersianFormatting.PersianDate(service.Today)}  ·  شمسی";
    public string TodaySales
    {
        get
        {
            var today = service.Today;
            return PersianFormatting.Money(Sales.Where(sale => sale.Date.Date == today).Sum(sale => sale.Amount));
        }
    }
    public string Incoming7
    {
        get
        {
            var today = service.Today;
            return PersianFormatting.Money(service.Incoming(today, today.AddDays(6)));
        }
    }
    public string Outgoing7
    {
        get
        {
            var today = service.Today;
            return PersianFormatting.Money(service.Outgoing(today, today.AddDays(6)));
        }
    }
    public string Net7 => PersianFormatting.Money(service.Net7);
    public string Warnings
    {
        get
        {
            var today = service.Today;
            return PersianFormatting.Number(service.Checks.Count(check =>
                check.Status == CheckStatuses.Pending && ((check.HasRiskScore && check.Risk >= 70) || check.DueDate.Date <= today)));
        }
    }
    public string RiskCustomers => service.Checks.Any(check => check.HasRiskScore)
        ? PersianFormatting.Number(service.Checks
            .Where(check => check.Status == CheckStatuses.Pending && check.HasRiskScore && check.Risk >= 70)
            .Select(check => check.Party)
            .Distinct(StringComparer.Ordinal)
            .Count())
        : "—";
    public string SelectedTotal => PersianFormatting.Money(selectedChecks.Sum(check => check.Amount));
    public string SelectedCountText => PersianFormatting.Number(selectedChecks.Count);
    public bool HasNoFilteredChecks => FilteredChecks.Count == 0;

    public string WeightedText
    {
        get
        {
            var result = service.WeightedDate(selectedChecks);
            return result.Date is null
                ? "برای رأس‌گیری، چکِ در انتظار انتخاب کنید"
                : $"{PersianFormatting.PersianDate(result.Date.Value)}  ·  {PersianFormatting.Money(result.Amount)}";
        }
    }

    public string SimulatedGap
    {
        get
        {
            var minimum = Radar.Count == 0 ? 0 : Radar.Min(point => point.Balance);
            return PersianFormatting.Money(minimum);
        }
    }

    public string TreasuryAsOf => $"مبنای محاسبه: {PersianFormatting.PersianDate(treasury.AsOf)}";
    public string OverdueIncoming => PersianFormatting.Money(treasury.OverdueIncoming);
    public string OverdueOutgoing => PersianFormatting.Money(treasury.OverdueOutgoing);
    public string ReturnedIncoming => PersianFormatting.Money(treasury.ReturnedIncoming);
    public string ReturnedOutgoing => PersianFormatting.Money(treasury.ReturnedOutgoing);
    public string TreasuryIncoming30 => PersianFormatting.Money(treasury.IncomingThrough30);
    public string TreasuryOutgoing30 => PersianFormatting.Money(treasury.OutgoingThrough30);
    public string TreasuryCoverage => treasury.CoveragePercent is decimal percent
        ? $"{PersianFormatting.Number(decimal.Round(percent, 0, MidpointRounding.AwayFromZero))}٪" : "—";

    public string SalesTotal => PersianFormatting.Money(Sales.Sum(sale => sale.Amount));
    public string SalesProfit => PersianFormatting.Money(Sales.Sum(sale => sale.Profit));
    public string TopCustomer => TopName(Sales.GroupBy(sale => sale.Customer), group => group.Sum(sale => sale.Amount));
    public string TopSeller => TopName(Sales.GroupBy(sale => sale.Seller), group => group.Sum(sale => sale.Amount));

    public void UpdateSelection(IEnumerable<CheckItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        selectedChecks = items
            .Where(check => check is not null)
            .DistinctBy(check => check.Number, StringComparer.Ordinal)
            .ToList();
        NotifySelectionSummary();
        RefreshForecast();
    }

    public (int Updated, int Skipped) ApplySelectedStatus(string targetStatus)
    {
        ArgumentNullException.ThrowIfNull(targetStatus);
        var selection = selectedChecks.ToArray();
        if (repository.IsReadOnly)
            return (0, selection.Length);
        var updated = 0;
        foreach (var check in selection)
        {
            if (service.TryChangeStatus(check, targetStatus))
                updated++;
        }
        var skipped = selection.Length - updated;

        if (updated > 0)
        {
            // A refreshed filter can clear the DataGrid selection; report against the snapshot above.
            selectedChecks = [];
            Refresh();
            RefreshUpcomingChecks();
            RefreshForecast();
            RefreshTreasury();
            RefreshAlerts();
            NotifySelectionSummary();
            NotifyDashboard();
        }

        return (updated, skipped);
    }

    private static string TopName<TGroup>(IEnumerable<TGroup> groups, Func<TGroup, decimal> amount)
        where TGroup : IGrouping<string, SaleItem>
    {
        var leader = groups.OrderByDescending(amount).FirstOrDefault();
        return leader is null ? "—" : $"{leader.Key}  ·  {PersianFormatting.Money(amount(leader))}";
    }

    private void SetSalesChartHeights()
    {
        var maximum = SalesChart.Select(point => point.Amount).DefaultIfEmpty(1).Max();
        foreach (var point in SalesChart)
        {
            var ratio = maximum <= 0 ? 0 : (double)(point.Amount / maximum);
            point.ChartBarHeight = Math.Clamp(20 + (ratio * 124), 20, 144);
        }
    }

    private void Refresh()
    {
        var normalizedQuery = PersianText.NormalizeForSearch(Query);
        IEnumerable<CheckItem> results = Checks;

        if (normalizedQuery.Length > 0)
        {
            results = results.Where(check =>
            {
                var searchable = PersianText.NormalizeForSearch(
                    $"{check.Number} {check.Party} {check.Kind} {check.Bank} {check.Status} " +
                    $"{check.Amount} {check.DueDateText} {check.Risk}");
                return searchable.Contains(normalizedQuery, StringComparison.Ordinal);
            });
        }

        if (SelectedCheckType != AllTypes)
        {
            results = results.Where(check => string.Equals(
                check.Kind.ToString(), SelectedCheckType, StringComparison.Ordinal));
        }

        if (SelectedStatus != AllStatuses)
            results = results.Where(check => string.Equals(check.Status, SelectedStatus, StringComparison.Ordinal));

        var matchingChecks = results.ToList();
        FilteredChecks.Clear();
        foreach (var check in matchingChecks)
            FilteredChecks.Add(check);
        OnPropertyChanged(nameof(HasNoFilteredChecks));
    }

    private void RefreshUpcomingChecks()
    {
        UpcomingChecks.Clear();
        foreach (var check in service.Checks
                     .Where(check => check.Status == CheckStatuses.Pending)
                     .OrderBy(check => check.DueDate.Date)
                     .Take(4))
        {
            UpcomingChecks.Add(check);
        }
    }

    private void RefreshForecast()
    {
        Radar.Clear();
        foreach (var point in service.BuildRadar(selectedChecks, (int)ShiftDays))
            Radar.Add(point);
        OnPropertyChanged(nameof(SimulatedGap));
    }

    public void SetAlertDays(int days)
    {
        if (days is < 0 or > 90) throw new ArgumentOutOfRangeException(nameof(days));
        alertDays = days;
        RefreshAlerts();
    }

    private void RefreshAlerts()
    {
        alertsAsOf = service.Today;
        var alerts = CheckAlertService.Create(service.Checks, alertsAsOf, alertDays);
        CheckAlerts.Clear();
        foreach (var alert in alerts) CheckAlerts.Add(alert);
        OnPropertyChanged(nameof(AlertsCount));
        OnPropertyChanged(nameof(AlertsDescription));
    }

    private void RefreshTreasury()
    {
        treasury = TreasuryAnalysisService.Analyze(service.Checks, service.Today);
        TreasuryBuckets.Clear();
        foreach (var bucket in treasury.Buckets) TreasuryBuckets.Add(bucket);
        foreach (var name in new[] { nameof(TreasuryAsOf), nameof(OverdueIncoming), nameof(OverdueOutgoing),
            nameof(ReturnedIncoming), nameof(ReturnedOutgoing), nameof(TreasuryIncoming30), nameof(TreasuryOutgoing30), nameof(TreasuryCoverage) })
            OnPropertyChanged(name);
    }

    private void NotifySelectionSummary()
    {
        OnPropertyChanged(nameof(SelectedTotal));
        OnPropertyChanged(nameof(SelectedCountText));
        OnPropertyChanged(nameof(WeightedText));
    }

    private void NotifyDashboard()
    {
        OnPropertyChanged(nameof(TodaySales));
        OnPropertyChanged(nameof(Incoming7));
        OnPropertyChanged(nameof(Outgoing7));
        OnPropertyChanged(nameof(Net7));
        OnPropertyChanged(nameof(Warnings));
        OnPropertyChanged(nameof(RiskCustomers));
        OnPropertyChanged(nameof(SalesTotal));
        OnPropertyChanged(nameof(SalesProfit));
        OnPropertyChanged(nameof(TopCustomer));
        OnPropertyChanged(nameof(TopSeller));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
