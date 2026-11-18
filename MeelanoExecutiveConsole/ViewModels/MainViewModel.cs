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
    private readonly FinanceService service = new(new DemoRepository());
    private List<CheckItem> selectedChecks = [];
    private string query = "";
    private string selectedCheckType = AllTypes;
    private string selectedStatus = AllStatuses;
    private double shiftDays;

    public MainViewModel()
    {
        Checks = new ObservableCollection<CheckItem>(service.Checks);
        Sales = new ObservableCollection<SaleItem>(service.Sales
            .OrderBy(sale => sale.Date)
            .ThenBy(sale => sale.Customer));
        FilteredChecks = new ObservableCollection<CheckItem>();
        UpcomingChecks = new ObservableCollection<CheckItem>();
        Radar = new ObservableCollection<RadarPoint>();
        CheckTypeFilters = new ObservableCollection<string>([AllTypes, CheckKind.دریافتی.ToString(), CheckKind.پرداختی.ToString()]);
        StatusFilters = new ObservableCollection<string>(
        [
            AllStatuses,
            CheckStatuses.Pending,
            CheckStatuses.Collected,
            CheckStatuses.Paid,
            CheckStatuses.Returned,
            CheckStatuses.Suspended
        ]);

        SetSalesChartHeights();
        Refresh();
        RefreshUpcomingChecks();
        RefreshForecast();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<CheckItem> Checks { get; }
    public ObservableCollection<SaleItem> Sales { get; }
    public ObservableCollection<CheckItem> FilteredChecks { get; }
    public ObservableCollection<CheckItem> UpcomingChecks { get; }
    public ObservableCollection<RadarPoint> Radar { get; }
    public ObservableCollection<string> CheckTypeFilters { get; }
    public ObservableCollection<string> StatusFilters { get; }

    public string Query
    {
        get => query;
        set
        {
            if (query == value)
                return;
            query = value;
            OnPropertyChanged();
            Refresh();
        }
    }

    public string SelectedCheckType
    {
        get => selectedCheckType;
        set
        {
            if (selectedCheckType == value)
                return;
            selectedCheckType = value;
            OnPropertyChanged();
            Refresh();
        }
    }

    public string SelectedStatus
    {
        get => selectedStatus;
        set
        {
            if (selectedStatus == value)
                return;
            selectedStatus = value;
            OnPropertyChanged();
            Refresh();
        }
    }

    public double ShiftDays
    {
        get => shiftDays;
        set
        {
            var normalized = Math.Clamp(Math.Round(value), -14, 30);
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

    public string TodayDateText => $"{PersianFormatting.GregorianDate(DateTime.Today)}  ·  میلادی";
    public string TodaySales => PersianFormatting.Money(Sales.Where(sale => sale.Date.Date == DateTime.Today).Sum(sale => sale.Amount));
    public string Incoming7 => PersianFormatting.Money(service.Incoming(DateTime.Today, DateTime.Today.AddDays(7)));
    public string Outgoing7 => PersianFormatting.Money(service.Outgoing(DateTime.Today, DateTime.Today.AddDays(7)));
    public string Net7 => PersianFormatting.Money(service.Net7);
    public string Warnings => PersianFormatting.Number(service.Checks.Count(check =>
        check.Status == CheckStatuses.Pending && (check.Risk >= 70 || check.DueDate.Date == DateTime.Today)));
    public string RiskCustomers => PersianFormatting.Number(service.Checks
        .Where(check => check.Status == CheckStatuses.Pending && check.Risk >= 70)
        .Select(check => check.Party)
        .Distinct(StringComparer.Ordinal)
        .Count());
    public string SelectedTotal => PersianFormatting.Money(selectedChecks.Sum(check => check.Amount));
    public string SelectedCountText => PersianFormatting.Number(selectedChecks.Count);
    public string SelectedIncomingCountText => PersianFormatting.Number(selectedChecks.Count(check =>
        check.Kind == CheckKind.دریافتی && check.Status == CheckStatuses.Pending));
    public string WeightedText
    {
        get
        {
            var result = service.WeightedDate(selectedChecks);
            return result.Date is null
                ? "برای رأس‌گیری، چکِ در انتظار انتخاب کنید"
                : $"{PersianFormatting.GregorianDate(result.Date.Value)}  ·  {PersianFormatting.Money(result.Amount)}";
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

    public string SalesTotal => PersianFormatting.Money(Sales.Sum(sale => sale.Amount));
    public string SalesProfit => PersianFormatting.Money(Sales.Sum(sale => sale.Profit));
    public string TopCustomer => TopName(Sales.GroupBy(sale => sale.Customer), group => group.Sum(sale => sale.Amount));
    public string TopSeller => TopName(Sales.GroupBy(sale => sale.Seller), group => group.Sum(sale => sale.Amount));

    public void UpdateSelection(IEnumerable<CheckItem> items)
    {
        selectedChecks = items.DistinctBy(check => check.Number, StringComparer.Ordinal).ToList();
        OnPropertyChanged(nameof(SelectedTotal));
        OnPropertyChanged(nameof(SelectedCountText));
        OnPropertyChanged(nameof(SelectedIncomingCountText));
        OnPropertyChanged(nameof(WeightedText));
        RefreshForecast();
    }

    public (int Updated, int Skipped) ApplySelectedStatus(string targetStatus)
    {
        var updated = 0;
        foreach (var check in selectedChecks)
        {
            if (!CanTransition(check, targetStatus))
                continue;

            check.Status = targetStatus;
            updated++;
        }

        if (updated > 0)
        {
            Refresh();
            RefreshUpcomingChecks();
            RefreshForecast();
            NotifyDashboard();
            OnPropertyChanged(nameof(WeightedText));
        }

        return (updated, selectedChecks.Count - updated);
    }

    public void Notify()
    {
        OnPropertyChanged(nameof(SelectedTotal));
        OnPropertyChanged(nameof(SelectedCountText));
        OnPropertyChanged(nameof(SelectedIncomingCountText));
        OnPropertyChanged(nameof(WeightedText));
        OnPropertyChanged(nameof(SimulatedGap));
        NotifyDashboard();
    }

    private static string TopName<TGroup>(IEnumerable<TGroup> groups, Func<TGroup, decimal> amount)
        where TGroup : IGrouping<string, SaleItem>
    {
        var leader = groups.OrderByDescending(amount).FirstOrDefault();
        return leader is null ? "—" : $"{leader.Key}  ·  {PersianFormatting.Money(amount(leader))}";
    }

    private static bool CanTransition(CheckItem check, string targetStatus)
    {
        if (check.Status == targetStatus)
            return false;

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

    private void SetSalesChartHeights()
    {
        var maximum = Sales.Select(sale => sale.Amount).DefaultIfEmpty(1).Max();
        foreach (var sale in Sales)
        {
            var ratio = maximum <= 0 ? 0 : (double)(sale.Amount / maximum);
            sale.ChartBarHeight = Math.Clamp(20 + (ratio * 84), 20, 104);
        }
    }

    private void Refresh()
    {
        IEnumerable<CheckItem> results = Checks;

        if (!string.IsNullOrWhiteSpace(Query))
        {
            results = results.Where(check =>
                $"{check.Number} {check.Party} {check.Kind} {check.Bank} {check.Status}"
                    .Contains(Query.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        if (SelectedCheckType != AllTypes)
            results = results.Where(check => string.Equals(check.Kind.ToString(), SelectedCheckType, StringComparison.Ordinal));

        if (SelectedStatus != AllStatuses)
            results = results.Where(check => string.Equals(check.Status, SelectedStatus, StringComparison.Ordinal));

        FilteredChecks.Clear();
        foreach (var check in results)
            FilteredChecks.Add(check);
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
