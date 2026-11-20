using System.IO;
using System.Globalization;
using System.Security;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using MeelanoExecutiveConsole;
using MeelanoExecutiveConsole.Models;
using MeelanoExecutiveConsole.Services;
using MeelanoExecutiveConsole.ViewModels;
using Microsoft.Win32;

namespace MeelanoExecutiveConsole.Views;

public partial class MainWindow : Window
{
    private MainViewModel vm = new();
    private IReadOnlyList<BankReconciliationService.Entry>? bankEntries;
    private IReadOnlyList<BankReconciliationService.Entry>? ledgerEntries;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = vm;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        SectionTabs.SelectedIndex = 0;
        UpdateThemeToggle();
    }

    // Used by the Windows installer smoke test; no data is loaded or modified.
    internal void VerifyUiLayout()
    {
        UpdateLayout();
        static (double Left, double Right) HorizontalBounds(FrameworkElement element)
        {
            if (!element.IsVisible || element.ActualWidth <= 0 || element.ActualHeight <= 0)
                throw new InvalidOperationException("A required layout element is not visible.");
            var first = element.PointToScreen(new Point(0, 0)).X;
            var second = element.PointToScreen(new Point(element.ActualWidth, 0)).X;
            return (Math.Min(first, second), Math.Max(first, second));
        }
        var sidebar = HorizontalBounds(SidebarPanel);
        var content = HorizontalBounds(ContentArea);
        if (sidebar.Left < content.Right - 2)
            throw new InvalidOperationException("Sidebar is not to the right of the content area.");
        var title = HorizontalBounds(TitleBar);
        foreach (var control in new FrameworkElement[] { SqlConnectionButton, ThemeToggleButton, WindowControls })
        {
            var bounds = HorizontalBounds(control);
            if (bounds.Left < title.Left - 2 || bounds.Right > title.Right + 2)
                throw new InvalidOperationException("A title-bar control is outside the title bar.");
        }
        var family = (FontFamily)Application.Current.FindResource("PersianFont");
        var typeface = new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        if (!typeface.TryGetGlyphTypeface(out var glyph) ||
            !glyph.FamilyNames.Values.Any(name => name.Contains("Vazirmatn", StringComparison.OrdinalIgnoreCase)) ||
            !glyph.CharacterToGlyphMap.ContainsKey('ک') || !glyph.CharacterToGlyphMap.ContainsKey('ی'))
            throw new InvalidOperationException("The embedded Persian font could not be loaded.");
        if (Application.GetResourceStream(new Uri("pack://application:,,,/MeelanoExecutiveConsole;component/Resources/Fonts/OFL.txt")) is not { } license)
            throw new InvalidOperationException("Font license resource is missing.");
        license.Stream.Dispose();
    }

    private void QuickHelp_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(this,
            "راهنمای سریع میلانو\n\n" +
            "• نمای کلی: فروش و چک‌های در انتظارِ منبع فعلی.\n" +
            "• مرکز چک‌ها: جست‌وجو، فیلتر، رأس‌گیری، CSV و چاپ. تغییر وضعیت SQL غیرفعال است.\n" +
            "• تحلیل خزانه و هشدار سررسید: محاسبه فقط از چک‌های موجود؛ ماندهٔ بانک نیست.\n" +
            "• بانک و مغایرت: مقایسهٔ آزمایشی دو فایل CSV، بدون ثبت سند و نیازمند بررسی.\n" +
            "• گزارش طرف حساب: گروه‌بندی نام دقیق؛ اشخاص هم‌نام تفکیک نمی‌شوند.\n\n" +
            "اتصال SQL نسخهٔ آزمایشی اعتبار گواهی سرور را بررسی نمی‌کند؛ فقط از شبکه و حساب آزمایشی استفاده کنید. برای خروج از تمام‌صفحه F11 یا Esc را بزنید.",
            "راهنمای سریع", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void FontLicense_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var resource = Application.GetResourceStream(new Uri("pack://application:,,,/MeelanoExecutiveConsole;component/Resources/Fonts/OFL.txt"));
            if (resource is null) throw new IOException("Font license not found.");
            using var reader = new StreamReader(resource.Stream, Encoding.UTF8);
            MessageBox.Show(this, reader.ReadToEnd(), "Vazirmatn · SIL Open Font License 1.1", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (IOException)
        {
            MessageBox.Show(this, "متن مجوز در مخزن برنامه و فایل OFL.txt موجود است.", "مجوز فونت", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void ThemeToggle_Click(object sender, RoutedEventArgs e)
    {
        ThemeManager.Toggle();
        UpdateThemeToggle();
    }

    private void UpdateThemeToggle()
    {
        ThemeToggleButton.Content = ThemeManager.ToggleLabel;
        ThemeToggleButton.ToolTip = ThemeManager.ToggleToolTip;
    }

    private void ConnectSql_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SqlConnectionWindow { Owner = this };
        var result = dialog.ShowDialog();
        UpdateThemeToggle();
        if (result != true || dialog.ImportedRepository is not { } repository)
            return;

        vm = new MainViewModel(repository);
        AlertDaysBox.Text = "7";
        DataContext = vm;
        ChecksGrid.UnselectAll();
        SectionTabs.SelectedIndex = 0;
        MessageBox.Show(
            $"داده‌ها با موفقیت اعتبارسنجی و بارگذاری شدند.\n{repository.DataSourceDescription}\n{PersianFormatting.Number(repository.GetChecks().Count)} چک · {PersianFormatting.Number(repository.GetSales().Count)} فروش\n\nاتصال در این نسخه فقط‌خواندنی است؛ تغییرات مالی به SQL Server ارسال نمی‌شوند.",
            "بارگذاری موفق",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void Navigation_Checked(object sender, RoutedEventArgs e)
    {
        if (SectionTabs is null || sender is not RadioButton navigation
            || !int.TryParse(navigation.Tag?.ToString(), out var index))
        {
            return;
        }

        if (index >= 0 && index < SectionTabs.Items.Count)
            SectionTabs.SelectedIndex = index;
    }

    private void ChecksGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        vm.UpdateSelection(ChecksGrid.SelectedItems.Cast<CheckItem>());
    }

    private void ApplyStatus(string targetStatus)
    {
        if (vm.IsReadOnlyData)
        {
            MessageBox.Show("منبع SQL در این نسخه فقط‌خواندنی است؛ برای ثبت تغییر، API یا مجوز نوشتنِ تأییدشده لازم است.",
                "عملیات غیرفعال", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (ChecksGrid.SelectedItems.Count == 0)
        {
            MessageBox.Show("ابتدا یک یا چند چک را انتخاب کنید.", "انتخاب چک", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var result = vm.ApplySelectedStatus(targetStatus);
        var message = result.Updated == 0
            ? "برای این عملیات، وضعیت یا نوع چک انتخاب‌شده معتبر نیست."
            : $"وضعیت {PersianFormatting.Number(result.Updated)} چک در نسخهٔ نمایشی تغییر کرد."
              + (result.Skipped > 0 ? $"\n{PersianFormatting.Number(result.Skipped)} مورد به‌دلیل ناسازگاری وضعیت نادیده گرفته شد." : "");
        MessageBox.Show(message, "به‌روزرسانی نمونه", MessageBoxButton.OK,
            result.Updated == 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
    }

    private void Collect_Click(object sender, RoutedEventArgs e) => ApplyStatus(CheckStatuses.Collected);
    private void Pay_Click(object sender, RoutedEventArgs e) => ApplyStatus(CheckStatuses.Paid);
    private void Return_Click(object sender, RoutedEventArgs e) => ApplyStatus(CheckStatuses.Returned);
    private void Suspend_Click(object sender, RoutedEventArgs e) => ApplyStatus(CheckStatuses.Suspended);
    private void Resume_Click(object sender, RoutedEventArgs e) => ApplyStatus(CheckStatuses.Pending);

    private void RefreshAlerts_Click(object sender, RoutedEventArgs e)
    {
        var value = PersianText.NormalizeForSearch(AlertDaysBox.Text).Trim();
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var days) || days is < 0 or > 90)
        {
            MessageBox.Show("افق هشدار باید عددی بین ۰ و ۹۰ روز باشد.", "هشدار سررسید", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        vm.SetAlertDays(days);
    }

    private void LoadBankCsv_Click(object sender, RoutedEventArgs e) => LoadEntries(isBank: true);
    private void LoadLedgerCsv_Click(object sender, RoutedEventArgs e) => LoadEntries(isBank: false);

    private void LoadEntries(bool isBank)
    {
        var dialog = new OpenFileDialog { Filter = "CSV UTF-8 (*.csv)|*.csv", CheckFileExists = true, Multiselect = false };
        if (dialog.ShowDialog() != true) return;
        // Invalidate the replaced source and old results even if the new file is invalid.
        if (isBank) { bankEntries = null; BankEntriesGrid.ItemsSource = null; InvalidateBalance(); }
        else ledgerEntries = null;
        ReconcileGrid.ItemsSource = null;
        UpdateBankStatus();
        try
        {
            using var stream = new FileStream(dialog.FileName, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > 4 * 1024 * 1024) throw new InvalidOperationException("حجم فایل نباید بیش از ۴ مگابایت باشد.");
            using var reader = new StreamReader(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: false);
            var parsed = BankReconciliationService.Parse(reader);
            if (isBank) { bankEntries = parsed; BankEntriesGrid.ItemsSource = parsed; InvalidateBalance(); }
            else ledgerEntries = parsed;
            ReconcileGrid.ItemsSource = null; // Never retain results from previous inputs.
            UpdateBankStatus();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or DecoderFallbackException or ArgumentException)
        {
            var message = exception is InvalidOperationException ? exception.Message : "خواندن فایل ناموفق بود؛ دسترسی و UTF-8 بودن فایل را بررسی کنید.";
            MessageBox.Show(message, "بارگذاری CSV", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void UpdateBankStatus() => BankLoadStatus.Text =
        $"بانک: {(bankEntries is null ? "بارگذاری نشده" : PersianFormatting.Number(bankEntries.Count) + " ردیف")} · دفتر: {(ledgerEntries is null ? "بارگذاری نشده" : PersianFormatting.Number(ledgerEntries.Count) + " ردیف")}";

    private void Reconcile_Click(object sender, RoutedEventArgs e)
    {
        ReconcileGrid.ItemsSource = null;
        if (bankEntries is null || ledgerEntries is null)
        {
            MessageBox.Show("ابتدا هر دو فایل بانک و دفتر را بارگذاری کنید.", "مغایرت‌گیری", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        try { ReconcileGrid.ItemsSource = BankReconciliationService.Compare(bankEntries, ledgerEntries); }
        catch (InvalidOperationException exception) { MessageBox.Show(exception.Message, "مغایرت‌گیری", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void CalculateBalance_Click(object sender, RoutedEventArgs e)
    {
        InvalidateBalance();
        if (bankEntries is null) { MessageBox.Show("ابتدا CSV بانک را بارگذاری کنید."); return; }
        var amount = PersianText.NormalizeForSearch(OpeningBalanceBox.Text).Trim();
        var date = PersianText.NormalizeForSearch(OpeningDateBox.Text).Trim();
        if (!BankReconciliationService.TryParseMoney(amount, allowSigned: true, out var opening)
            || !DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start))
        {
            MessageBox.Show("ماندهٔ آغازین را بدون جداکنندهٔ هزارگان و تاریخ را به شکل YYYY-MM-DD میلادی وارد کنید.");
            return;
        }
        try
        {
            var result = BankReconciliationService.CalculateBalance(bankEntries, opening, start);
            var last = bankEntries.Max(x => x.Date);
            CalculatedBalanceText.Text = $"ماندهٔ محاسبه‌شده تا آخرین تاریخ فایل ({PersianFormatting.PersianDate(last)}): {PersianFormatting.MoneyExact(result)} · مشروط به کامل‌بودن فایل";
        }
        catch (Exception exception) when (exception is InvalidOperationException or OverflowException)
        {
            MessageBox.Show(exception is OverflowException ? "مجموع مبالغ از محدودهٔ محاسبه خارج است." : exception.Message, "محاسبهٔ مانده", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BalanceInput_Changed(object sender, TextChangedEventArgs e) => InvalidateBalance();
    private void InvalidateBalance()
    {
        if (CalculatedBalanceText is not null) CalculatedBalanceText.Text = "مانده محاسبه نشده است.";
    }
    private void ClearBank_Click(object sender, RoutedEventArgs e)
    {
        bankEntries = null; ledgerEntries = null;
        BankEntriesGrid.ItemsSource = null; ReconcileGrid.ItemsSource = null;
        OpeningBalanceBox.Clear(); OpeningDateBox.Clear(); InvalidateBalance(); UpdateBankStatus();
    }

    private void RefreshCounterparties_Click(object sender, RoutedEventArgs e) => vm.RefreshCounterparties();

    private void CounterpartyCsv_Click(object sender, RoutedEventArgs e)
    {
        if (vm.Counterparties.Count == 0) { MessageBox.Show("نتیجه‌ای برای خروجی وجود ندارد."); return; }
        var dialog = CreateCsvSaveDialog("meelano-counterparty-checks.csv");
        if (dialog.ShowDialog() != true) return;
        var rows = vm.Counterparties.Select(row => new[]
        {
            CsvCell.Text(vm.CounterpartyAsOf), CsvCell.Text(vm.DataSourceDescription), CsvCell.Text(row.PartyDisplay),
            CsvCell.Number(row.IncomingCount), CsvCell.Number(row.IncomingPending), CsvCell.Number(row.IncomingOverdue),
            CsvCell.Number(row.OutgoingCount), CsvCell.Number(row.OutgoingPending), CsvCell.Number(row.OutgoingOverdue),
            CsvCell.Number(row.NetPending), CsvCell.Number(row.IncomingReturned), CsvCell.Number(row.OutgoingReturned)
        });
        ExportCsv(dialog.FileName, CsvFormatter.Build(["تاریخ مبنا", "منبع", "طرف حساب", "تعداد دریافتی در انتظار", "دریافتی در انتظار (تومان)",
            "دریافتی سررسیدگذشته (تومان)", "تعداد پرداختی در انتظار", "پرداختی در انتظار (تومان)", "پرداختی سررسیدگذشته (تومان)",
            "خالص چک در انتظار (تومان)", "دریافتی برگشتی (تومان)", "پرداختی برگشتی (تومان)"], rows));
    }

    private void AlertsCsv_Click(object sender, RoutedEventArgs e)
    {
        if (vm.CheckAlerts.Count == 0) { MessageBox.Show("هشداری برای خروجی وجود ندارد."); return; }
        var dialog = CreateCsvSaveDialog("meelano-check-alerts.csv");
        if (dialog.ShowDialog() != true) return;
        var rows = vm.CheckAlerts.Select(alert => new[]
        {
            CsvCell.Text(vm.AlertsDescription), CsvCell.Text(vm.DataSourceDescription), CsvCell.Text(alert.Number),
            CsvCell.Text(alert.Kind), CsvCell.Text(alert.Party), CsvCell.Text(alert.DueDate),
            CsvCell.Text(alert.Status), CsvCell.Number(alert.Check.Amount)
        });
        ExportCsv(dialog.FileName, CsvFormatter.Build(["مبنای هشدار", "منبع", "شماره", "نوع", "طرف حساب", "سررسید شمسی", "زمان", "مبلغ (تومان)"], rows));
    }

    private void ReconcileCsv_Click(object sender, RoutedEventArgs e)
    {
        if (ReconcileGrid.ItemsSource is not IEnumerable<BankReconciliationService.Comparison> comparisons || !comparisons.Any())
        { MessageBox.Show("ابتدا دو فایل را مقایسه کنید."); return; }
        var dialog = CreateCsvSaveDialog("meelano-bank-comparison.csv");
        if (dialog.ShowDialog() != true) return;
        var rows = comparisons.Select(row => new[]
        {
            CsvCell.Text(row.Status), CsvCell.Text(row.Bank?.Account ?? row.Ledger?.Account), CsvCell.Text(row.Reference),
            CsvCell.Text(row.Bank?.Id), CsvCell.Text(row.Ledger?.Id),
            CsvCell.Text(row.Bank is null ? "" : row.Bank.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            CsvCell.Text(row.Ledger is null ? "" : row.Ledger.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            CsvCell.Text(row.Bank?.Direction), CsvCell.Text(row.Ledger?.Direction),
            row.Bank is null ? CsvCell.Text("") : CsvCell.Number(row.Bank.Amount),
            row.Ledger is null ? CsvCell.Text("") : CsvCell.Number(row.Ledger.Amount)
        });
        ExportCsv(dialog.FileName, CsvFormatter.Build(["نتیجه نیازمند بررسی", "حساب", "ارجاع", "شناسه بانک", "شناسه دفتر", "تاریخ بانک میلادی",
            "تاریخ دفتر میلادی", "جهت بانک", "جهت دفتر", "مبلغ بانک (تومان)", "مبلغ دفتر (تومان)"], rows));
    }

    private void ChecksCsv_Click(object sender, RoutedEventArgs e)
    {
        var dialog = CreateCsvSaveDialog("meelano-checks.csv");
        if (dialog.ShowDialog() != true)
            return;

        var rows = vm.FilteredChecks.Select(check => new[]
        {
            CsvCell.Text(check.Number),
            CsvCell.Text(check.Kind.ToString()),
            CsvCell.Text(check.Party),
            CsvCell.Text(check.Bank),
            CsvCell.Number(check.Amount),
            CsvCell.Text(PersianFormatting.PersianDate(check.DueDate)),
            CsvCell.Text(check.Status),
            check.HasRiskScore ? CsvCell.Number(check.Risk) : CsvCell.Text("")
        });
        var content = CsvFormatter.Build(
            ["شماره", "نوع", "طرف حساب", "بانک", "مبلغ (تومان)", "سررسید شمسی", "وضعیت", "امتیاز ریسک"],
            rows);
        ExportCsv(dialog.FileName, content);
    }

    private void TreasuryCsv_Click(object sender, RoutedEventArgs e)
    {
        var dialog = CreateCsvSaveDialog("meelano-treasury-maturity.csv");
        if (dialog.ShowDialog() != true) return;
        var rows = vm.TreasuryBuckets.Select(bucket => new[]
        {
            CsvCell.Text(vm.TreasuryAsOf), CsvCell.Text(vm.DataSourceDescription), CsvCell.Text(bucket.Label),
            CsvCell.Number(bucket.IncomingCount), CsvCell.Number(bucket.Incoming),
            CsvCell.Number(bucket.OutgoingCount), CsvCell.Number(bucket.Outgoing), CsvCell.Number(bucket.Net)
        });
        ExportCsv(dialog.FileName, CsvFormatter.Build(
            ["تاریخ مبنا", "منبع", "بازه سررسید چک در انتظار", "تعداد دریافتی", "دریافتی (تومان)", "تعداد پرداختی", "پرداختی (تومان)", "خالص (تومان)"], rows));
    }

    private void SalesCsv_Click(object sender, RoutedEventArgs e)
    {
        var dialog = CreateCsvSaveDialog("meelano-sales.csv");
        if (dialog.ShowDialog() != true)
            return;

        var rows = vm.Sales.Select(sale => new[]
        {
            CsvCell.Text(PersianFormatting.PersianDate(sale.Date)),
            CsvCell.Text(sale.Customer),
            CsvCell.Text(sale.Seller),
            CsvCell.Number(sale.Amount),
            CsvCell.Number(sale.Profit),
            CsvCell.Text(sale.CollectionStatus)
        });
        var content = CsvFormatter.Build(
            ["تاریخ شمسی", "مشتری", "فروشنده", "مبلغ (تومان)", "سود (تومان)", "وضعیت وصول"],
            rows);
        ExportCsv(dialog.FileName, content);
    }

    private static SaveFileDialog CreateCsvSaveDialog(string fileName) => new()
    {
        Filter = "CSV UTF-8 (*.csv)|*.csv",
        FileName = fileName,
        AddExtension = true,
        DefaultExt = ".csv",
        CheckPathExists = true,
        OverwritePrompt = true
    };

    private static void ExportCsv(string path, string content)
    {
        try
        {
            // A single UTF-8 BOM helps common spreadsheet applications detect Persian text.
            File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            MessageBox.Show($"فایل CSV ذخیره شد:\n{path}", "خروجی موفق", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                           or ArgumentException or SecurityException or NotSupportedException)
        {
            MessageBox.Show($"ذخیرهٔ فایل انجام نشد.\n{exception.Message}", "خطا در خروجی", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Print_Click(object sender, RoutedEventArgs e)
    {
        var items = ChecksGrid.SelectedItems.Cast<CheckItem>().ToList();
        if (items.Count == 0)
            items = vm.FilteredChecks.ToList();
        if (items.Count == 0)
        {
            MessageBox.Show("موردی برای چاپ وجود ندارد.", "گزارش چک‌ها", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var dialog = new PrintDialog();
            if (dialog.ShowDialog() != true)
                return;

            var sourceNote = vm.IsReadOnlyData
                ? $"گزارش فقط‌خواندنی از {vm.DataSourceDescription}"
                : "داده‌های این گزارش متعلق به نسخهٔ نمایشی و غیرعملیاتی هستند.";
            var document = BuildCheckReport(items, dialog.PrintableAreaWidth, dialog.PrintableAreaHeight, sourceNote);
            dialog.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator, "گزارش چک‌های میلانو");
        }
        catch (Exception exception)
        {
            MessageBox.Show($"چاپ گزارش انجام نشد.\n{exception.Message}", "خطا در چاپ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static FlowDocument BuildCheckReport(IReadOnlyList<CheckItem> items, double pageWidth, double pageHeight, string sourceNote)
    {
        var document = new FlowDocument
        {
            FlowDirection = FlowDirection.RightToLeft,
            PagePadding = new Thickness(34),
            FontFamily = new FontFamily("Segoe UI"),
            PageWidth = pageWidth,
            PageHeight = pageHeight,
            ColumnWidth = Math.Max(100, pageWidth - 68)
        };
        document.Blocks.Add(new Paragraph(new Run("میلانو | گزارش مرکز چک‌ها"))
        {
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 8)
        });
        document.Blocks.Add(new Paragraph(new Run(
            $"تاریخ تهیه: {PersianFormatting.PersianDate(DateTime.Today)} · شمسی     |     تعداد: {PersianFormatting.Number(items.Count)}"))
        {
            FontSize = 10,
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 18)
        });

        var table = new Table { CellSpacing = 0 };
        var headers = new[] { "شماره", "نوع", "طرف حساب", "بانک", "مبلغ (تومان)", "سررسید · شمسی", "وضعیت" };
        foreach (var _ in headers)
            table.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });

        var group = new TableRowGroup();
        table.RowGroups.Add(group);
        var headingRow = new TableRow();
        var headerBackground = new SolidColorBrush(Color.FromRgb(225, 214, 184));
        foreach (var header in headers)
        {
            var cell = CreatePrintCell(header, isHeader: true);
            cell.Background = headerBackground;
            headingRow.Cells.Add(cell);
        }
        group.Rows.Add(headingRow);

        foreach (var check in items)
        {
            var row = new TableRow();
            foreach (var value in new[]
                     {
                         check.Number,
                         check.Kind.ToString(),
                         check.Party,
                         check.Bank,
                         check.AmountText,
                         check.DueDateText,
                         check.Status
                     })
            {
                row.Cells.Add(CreatePrintCell(value));
            }
            group.Rows.Add(row);
        }

        document.Blocks.Add(table);
        document.Blocks.Add(new Paragraph(new Run(sourceNote))
        {
            FontSize = 9,
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 16, 0, 0)
        });
        return document;
    }

    private static TableCell CreatePrintCell(string text, bool isHeader = false)
    {
        var paragraph = new Paragraph(new Run(text))
        {
            Margin = new Thickness(0),
            TextAlignment = TextAlignment.Right
        };
        return new TableCell(paragraph)
        {
            Padding = new Thickness(7, 5, 7, 5),
            BorderBrush = Brushes.LightGray,
            BorderThickness = new Thickness(0, 0, 0, 0.5),
            FontSize = isHeader ? 10 : 9,
            FontWeight = isHeader ? FontWeights.Bold : FontWeights.Normal
        };
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;
        if (e.ClickCount == 2)
        {
            ToggleWindowState();
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // A window can leave its drag loop while the user is releasing the mouse.
        }
    }

    private void ToggleFullScreen_Click(object sender, RoutedEventArgs e) => ToggleWindowState();
    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleWindowState();
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11 || (e.Key == Key.Escape && WindowState == WindowState.Maximized))
        {
            ToggleWindowState();
            e.Handled = true;
        }
    }

    private void ToggleWindowState()
    {
        // Let WPF restore the original bounds so maximizing on a secondary monitor is safe.
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }
}
