using MeelanoExecutiveConsole.Models;
using MeelanoExecutiveConsole.Services;
using MeelanoExecutiveConsole.ViewModels;

namespace MeelanoExecutiveConsole.Specs;

internal static class Program
{
    private static readonly DateTime Today = new(2026, 11, 18);

    private static int Main()
    {
        var specifications = new (string Name, Action Run)[]
        {
            ("Seven-day cash totals use exactly seven inclusive calendar days", SevenDayTotals),
            ("Thirty-day radar includes overdue items on day zero", OverdueRadar),
            ("Scenario shifts only selected pending incoming checks", ScenarioShift),
            ("Status transitions enforce check type and terminal states", StatusTransitions),
            ("Weighted date excludes completed checks and rounds to a whole day", WeightedDate),
            ("Persian display uses grouped digits, toman and Solar Hijri dates", PersianFormattingDisplay),
            ("Jalali date parsing handles Persian digits and Gregorian SQL dates", PersianDateParsing),
            ("Direct SQL endpoint validation rejects malformed hosts and ports", SqlEndpointValidation),
            ("SQL source-value keys preserve exact values and distinguish NULL", SqlValueKeys),
            ("Live SQL snapshots cannot mutate check status", LiveSqlIsReadOnly),
            ("Persian search normalizes keyboard and digit variants", PersianSearchNormalization),
            ("CSV quotes correctly and protects text from formulas", CsvFormatting),
            ("View-model search supports Persian digits and empty results", ViewModelSearch),
            ("View-model status totals remain correct after refresh", ViewModelStatusCounts),
            ("Sales charts aggregate daily sales within the latest 90 calendar days", SalesChartAggregation),
            ("Demo fixtures have unique IDs and valid financial ranges", DemoFixtureValidation)
        };

        var failures = 0;
        foreach (var specification in specifications)
        {
            try
            {
                specification.Run();
                Console.WriteLine($"PASS  {specification.Name}");
            }
            catch (Exception exception)
            {
                failures++;
                Console.Error.WriteLine($"FAIL  {specification.Name}: {exception.Message}");
            }
        }

        Console.WriteLine($"{specifications.Length - failures}/{specifications.Length} specifications passed.");
        return failures == 0 ? 0 : 1;
    }

    private static void SevenDayTotals()
    {
        var checks = new[]
        {
            Check("RC-OLD", CheckKind.دریافتی, 100, Today.AddDays(-1)),
            Check("RC-TODAY", CheckKind.دریافتی, 11, Today),
            Check("RC-LAST", CheckKind.دریافتی, 22, Today.AddDays(6)),
            Check("RC-NEXT", CheckKind.دریافتی, 100, Today.AddDays(7)),
            Check("PC-LAST", CheckKind.پرداختی, 3, Today.AddDays(6)),
            Check("PC-NEXT", CheckKind.پرداختی, 50, Today.AddDays(7))
        };
        var service = Service(checks);

        Equal(33m, service.Incoming(Today, Today.AddDays(6)), "7-day incoming");
        Equal(3m, service.Outgoing(Today, Today.AddDays(6)), "7-day outgoing");
        Equal(30m, service.Net7, "7-day net");
        Equal(0m, service.Incoming(Today.AddDays(1), Today), "inverted date interval");
    }

    private static void OverdueRadar()
    {
        var service = Service(
        [
            Check("RC-OVERDUE", CheckKind.دریافتی, 100, Today.AddDays(-4)),
            Check("PC-OVERDUE", CheckKind.پرداختی, 150, Today.AddDays(-1)),
            Check("RC-LAST", CheckKind.دریافتی, 10, Today.AddDays(29)),
            Check("RC-OUTSIDE", CheckKind.دریافتی, 99, Today.AddDays(30)),
            Check("PC-OUTSIDE", CheckKind.پرداختی, 99, Today.AddDays(31))
        ]);

        var radar = service.BuildRadar();
        Equal(30, radar.Count, "radar day count");
        Equal(Today, radar[0].Day, "radar starts today");
        Equal(100m, radar[0].Incoming, "overdue incoming on day zero");
        Equal(150m, radar[0].Outgoing, "overdue outgoing on day zero");
        Equal(-50m, radar[0].Balance, "day-zero projected balance");
        Equal(10m, radar[29].Incoming, "last in-range day");
        Equal(-40m, radar[29].Balance, "last in-range balance");
    }

    private static void ScenarioShift()
    {
        var selectedIncoming = Check("RC-SELECTED", CheckKind.دریافتی, 30, Today.AddDays(3));
        var otherIncoming = Check("RC-OTHER", CheckKind.دریافتی, 20, Today.AddDays(3));
        var selectedOutgoing = Check("PC-SELECTED", CheckKind.پرداختی, 10, Today.AddDays(3));
        var service = Service([selectedIncoming, otherIncoming, selectedOutgoing]);
        var radar = service.BuildRadar([selectedIncoming, selectedOutgoing], shiftDays: 5);

        Equal(20m, radar[3].Incoming, "unselected receipt stays put");
        Equal(10m, radar[3].Outgoing, "payments are not shifted");
        Equal(30m, radar[8].Incoming, "selected receipt is shifted");
        Equal(10m, radar[3].Balance, "balance after original-day flows");
        Equal(40m, radar[8].Balance, "balance after shifted flow");
    }

    private static void StatusTransitions()
    {
        var receivable = Check("RC-STATUS", CheckKind.دریافتی, 100, Today);
        var payable = Check("PC-STATUS", CheckKind.پرداختی, 100, Today);
        var service = Service([receivable, payable]);

        True(service.TryChangeStatus(receivable, CheckStatuses.Collected), "receive a pending incoming check");
        False(service.TryChangeStatus(receivable, CheckStatuses.Pending), "reopen a collected check");
        False(service.TryChangeStatus(payable, CheckStatuses.Collected), "collect an outgoing check");
        True(service.TryChangeStatus(payable, CheckStatuses.Suspended), "suspend a pending outgoing check");
        True(service.TryChangeStatus(payable, CheckStatuses.Pending), "resume a suspended check");
        True(service.TryChangeStatus(payable, CheckStatuses.Paid), "pay a pending outgoing check");
        False(service.TryChangeStatus(payable, CheckStatuses.Returned), "return an already paid check");
        False(service.TryChangeStatus(receivable, "وضعیت نامعتبر"), "reject an unknown status");
    }

    private static void WeightedDate()
    {
        var checks = new[]
        {
            Check("RC-W1", CheckKind.دریافتی, 100, Today),
            Check("PC-W2", CheckKind.پرداختی, 300, Today.AddDays(2)),
            Check("RC-DONE", CheckKind.دریافتی, 500, Today.AddDays(30), CheckStatuses.Collected)
        };
        var service = Service(checks);
        var weighted = service.WeightedDate(checks);

        Equal<DateTime?>(Today.AddDays(2), weighted.Date, "weighted whole-day date");
        Equal(400m, weighted.Amount, "pending total only");
        var empty = service.WeightedDate([checks[2]]);
        Equal<DateTime?>(null, empty.Date, "no pending date");
        Equal(0m, empty.Amount, "no pending total");
    }

    private static void PersianFormattingDisplay()
    {
        Equal("۱٬۲۳۴٬۵۶۷", PersianFormatting.Number(1_234_567), "grouped Persian digits");
        Equal("۷۳٫۵ م", PersianFormatting.CompactMillions(73_500_000), "fractional compact chart value");
        True(PersianFormatting.Money(25_000).EndsWith("تومان", StringComparison.Ordinal), "currency label");
        Equal("۱۴۰۳/۰۱/۰۱", PersianFormatting.PersianDate(new DateTime(2024, 3, 20)), "Nowruz Solar Hijri date");
        Equal("۱۴۰۵/۰۸/۲۷", PersianFormatting.PersianDate(Today), "current Solar Hijri date");
    }

    private static void PersianDateParsing()
    {
        True(PersianDateParser.TryParse("۱۴۰۵/۰۸/۲۷", out var jalali), "Persian digit Jalali text parsed");
        Equal(Today, jalali, "Jalali date converts to Gregorian internal date");
        True(PersianDateParser.TryParse("2026-11-18", out var iso), "ISO SQL date parsed");
        Equal(Today, iso, "ISO Gregorian date remains correct");
        True(PersianDateParser.TryParse("20261118", out var compact), "compact year-first SQL date parsed");
        Equal(Today, compact, "compact Gregorian date remains correct");
        False(PersianDateParser.TryParse("2026-11-18garbage", out _), "malformed suffix is not stripped into a valid date");
        False(PersianDateParser.TryParse("2026/11-18", out _), "mixed date separators rejected");
        True(PersianDateParser.TryParse(new DateTime(2026, 11, 18, 13, 45, 0), out var sqlDate), "SQL datetime parsed");
        Equal(Today, sqlDate, "SQL datetime uses calendar day");
        False(PersianDateParser.TryParse("۱۴۰۵/۱۳/۳۲", out _), "invalid Jalali date rejected");
        False(PersianDateParser.TryParse("۱۲۹۹/۰۸/۲۷", out _), "unsupported calendar year rejected instead of treated as Gregorian");
        False(PersianDateParser.TryParse(DateTime.MinValue, out _), "unsupported SQL date range rejected");
        False(PersianDateParser.TryParse("January 1, 0001", out _), "unsupported textual calendar value rejected");
        False(PersianDateParser.TryParse("2026", out _), "year-only text rejected instead of guessed as a date");
    }

    private static void SqlEndpointValidation()
    {
        var settings = new SqlServerConnectionSettings("127.0.0.1", 5050, true, null, null);
        Equal("127.0.0.1", settings.ServerHost, "IPv4 host accepted");
        Equal(5050, settings.Port, "forwarded SQL port accepted");
        True(settings.UseWindowsAuthentication, "Windows auth selected");
        Throws<ArgumentException>(() => new SqlServerConnectionSettings("localhost;TrustServerCertificate=True", 1433, true, null, null), "connection-string fragment rejected as host");
        Throws<ArgumentException>(() => new SqlServerConnectionSettings("localhost", 65536, true, null, null), "out-of-range port rejected");
        Throws<ArgumentException>(() => new SqlServerConnectionSettings("localhost", 1433, false, "report_reader", null), "empty SQL password rejected");
    }

    private static void SqlValueKeys()
    {
        False(SqlValueOption.KeyFor("Pending") == SqlValueOption.KeyFor("pending"), "case-distinct SQL values remain separately mappable");
        False(SqlValueOption.KeyFor("ي") == SqlValueOption.KeyFor("ی"), "Arabic/Persian letter variants remain separately mappable");
        Equal(SqlValueOption.NullLookupKey, SqlValueOption.KeyFor(null), "SQL NULL has a dedicated mapping key");
        False(SqlValueOption.NullLookupKey == SqlValueOption.KeyFor(SqlValueOption.NullLookupKey), "a literal source value cannot collide with SQL NULL");
        Equal("(SQL NULL)", new SqlValueOption("", SqlValueOption.NullLookupKey, true).DisplayValue, "NULL label is explicit");
        Equal("«(SQL NULL)»", new SqlValueOption("(SQL NULL)", SqlValueOption.KeyFor("(SQL NULL)"), false).DisplayValue, "literal null-like text remains distinguishable");
        Equal("(رشتهٔ خالی)", new SqlValueOption("", SqlValueOption.KeyFor(""), false).DisplayValue, "empty text is distinguishable from NULL");
    }

    private static void LiveSqlIsReadOnly()
    {
        var incoming = Check("RC-LIVE", CheckKind.دریافتی, 40, Today);
        var viewModel = new MainViewModel(new ReadOnlyStubRepository([incoming]), () => Today);
        viewModel.UpdateSelection([incoming]);

        var result = viewModel.ApplySelectedStatus(CheckStatuses.Collected);
        Equal(0, result.Updated, "live data has no status writes");
        Equal(1, result.Skipped, "live status operation rejected");
        Equal(CheckStatuses.Pending, incoming.Status, "SQL snapshot is unchanged");
        False(viewModel.CanChangeStatus, "live edit controls disabled");
    }

    private static void PersianSearchNormalization()
    {
        Equal("کتاب ی 123", PersianText.NormalizeForSearch("كِتاب ي ۱۲٣"), "Arabic letters and mixed digits");
        Equal("هفت اقلیم", PersianText.NormalizeForSearch("هفت‌اقلیم"), "zero-width non-joiner");
    }

    private static void CsvFormatting()
    {
        var csv = CsvFormatter.Build(
            ["نام", "فرمول", "یادداشت", "مبلغ"],
            [
                new[]
                {
                    CsvCell.Text("شرکت,الف"),
                    CsvCell.Text("=1+1"),
                    CsvCell.Text("خط اول\r\nخط \"دوم\""),
                    CsvCell.Number(-25)
                }
            ]);

        Contains("\"شرکت,الف\"", csv, "comma quoting");
        Contains("'=1+1", csv, "formula prefix");
        Contains("\"خط اول\r\nخط \"\"دوم\"\"\"", csv, "newline and quote escaping");
        Contains(",-25\r\n", csv, "negative numeric value remains numeric");
        True(csv.StartsWith("نام,فرمول,یادداشت,مبلغ\r\n", StringComparison.Ordinal), "header and CRLF");
    }

    private static void ViewModelSearch()
    {
        var repository = new StubRepository(
        [Check("RC-1001", CheckKind.دریافتی, 50, Today, party: "بازرگانی کتاب")]);
        var viewModel = new MainViewModel(repository, () => Today);

        viewModel.Query = "كِتاب";
        Equal(1, viewModel.FilteredChecks.Count, "Arabic keyboard spelling search");
        viewModel.Query = "RC-١٠٠١";
        Equal(1, viewModel.FilteredChecks.Count, "Arabic-Indic digit search");
        viewModel.Query = "هیچ موردی";
        Equal(0, viewModel.FilteredChecks.Count, "empty result count");
        True(viewModel.HasNoFilteredChecks, "empty-state visibility property");
    }

    private static void ViewModelStatusCounts()
    {
        var incoming = Check("RC-2001", CheckKind.دریافتی, 50, Today);
        var outgoing = Check("PC-2001", CheckKind.پرداختی, 70, Today);
        var viewModel = new MainViewModel(new StubRepository([incoming, outgoing]), () => Today);
        viewModel.UpdateSelection([incoming, outgoing]);

        var result = viewModel.ApplySelectedStatus(CheckStatuses.Collected);
        Equal(1, result.Updated, "successful transition count");
        Equal(1, result.Skipped, "rejected transition count after refresh");
        Equal(CheckStatuses.Collected, incoming.Status, "incoming status");
        Equal(CheckStatuses.Pending, outgoing.Status, "outgoing status remains pending");
        Equal("۰", viewModel.SelectedCountText, "selection cleared after update");
    }

    private static void SalesChartAggregation()
    {
        var sales = new[]
        {
            Sale(Today.AddDays(-90), 100),
            Sale(Today.AddDays(-89), 10),
            Sale(Today.AddDays(-89), 15),
            Sale(Today, 30),
            Sale(Today.AddDays(1), 40)
        };
        var viewModel = new MainViewModel(new StubRepository([], sales), () => Today);

        Equal(2, viewModel.SalesChart.Count, "old and future records are outside the 90-day chart window");
        Equal(Today.AddDays(-89), viewModel.SalesChart[0].Date, "chart starts at the 90th inclusive calendar day");
        Equal(25m, viewModel.SalesChart[0].Amount, "transactions on the first day are aggregated");
        Equal(30m, viewModel.SalesChart[1].Amount, "today is included and same-day sales are aggregated");
    }

    private static SaleItem Sale(DateTime date, decimal amount) => new()
    {
        Date = date,
        Customer = "مشتری",
        Seller = "فروشنده",
        Amount = amount,
        Profit = amount / 10,
        CollectionStatus = "وصول‌شده"
    };

    private static void DemoFixtureValidation()
    {
        var repository = new DemoRepository();
        var checks = repository.GetChecks();
        var sales = repository.GetSales();
        Equal(checks.Count, checks.Select(check => check.Number).Distinct(StringComparer.Ordinal).Count(), "unique check IDs");
        True(checks.All(check => check.Amount > 0 && check.Risk is >= 0 and <= 100), "check value ranges");
        True(sales.All(sale => sale.Amount > 0 && sale.Profit >= 0 && sale.Profit <= sale.Amount), "sales value ranges");
    }

    private static FinanceService Service(IReadOnlyList<CheckItem> checks) =>
        new(new StubRepository(checks), () => Today);

    private static CheckItem Check(
        string number,
        CheckKind kind,
        decimal amount,
        DateTime dueDate,
        string status = CheckStatuses.Pending,
        string party = "نمونه") =>
        new()
        {
            Number = number,
            Kind = kind,
            Amount = amount,
            DueDate = dueDate,
            Status = status,
            Party = party,
            Bank = "ملت",
            Risk = 20
        };

    private static void Equal<T>(T expected, T actual, string description)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{description}: expected '{expected}', got '{actual}'.");
    }

    private static void True(bool value, string description)
    {
        if (!value)
            throw new InvalidOperationException($"Expected true: {description}.");
    }

    private static void False(bool value, string description) => True(!value, description);

    private static void Contains(string expected, string actual, string description)
    {
        if (!actual.Contains(expected, StringComparison.Ordinal))
            throw new InvalidOperationException($"{description}: '{expected}' was not present.");
    }

    private static void Throws<TException>(Action action, string description) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}: {description}.");
    }

    private sealed class StubRepository : IRepository
    {
        private static readonly IReadOnlyList<SaleItem> NoSales = Array.Empty<SaleItem>();
        private readonly IReadOnlyList<CheckItem> checks;
        private readonly IReadOnlyList<SaleItem> sales;

        public StubRepository(IReadOnlyList<CheckItem> checks, IReadOnlyList<SaleItem>? sales = null)
        {
            this.checks = checks;
            this.sales = sales ?? NoSales;
        }

        public IReadOnlyList<CheckItem> GetChecks() => checks;
        public IReadOnlyList<SaleItem> GetSales() => sales;
    }

    private sealed class ReadOnlyStubRepository : IRepository
    {
        private readonly IReadOnlyList<CheckItem> checks;
        private static readonly IReadOnlyList<SaleItem> NoSales = Array.Empty<SaleItem>();

        public ReadOnlyStubRepository(IReadOnlyList<CheckItem> checks) => this.checks = checks;
        public IReadOnlyList<CheckItem> GetChecks() => checks;
        public IReadOnlyList<SaleItem> GetSales() => NoSales;
        public bool IsReadOnly => true;
        public string DataSourceDescription => "SQL Server · تست · فقط‌خواندنی";
    }
}
