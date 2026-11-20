using System.Globalization;
using System.Text;
using MeelanoExecutiveConsole.Models;
using MeelanoExecutiveConsole.Services;
using MeelanoExecutiveConsole.ViewModels;
using Microsoft.Maui.Controls.Shapes;

namespace MeelanoMobile;

public sealed class MainPage : ContentPage
{
    private static readonly Color Canvas = Color.FromArgb("#081315");
    private static readonly Color Surface = Color.FromArgb("#142629");
    private static readonly Color BorderColor = Color.FromArgb("#314A48");
    private static readonly Color Gold = Color.FromArgb("#E7C77E");
    private static readonly Color Text = Color.FromArgb("#F2F2EA");
    private static readonly Color Muted = Color.FromArgb("#B8CAC3");
    private readonly Grid body = new();
    private readonly Label sectionLabel = Caption("نمای کلی", 20, Gold, true);
    private readonly Label sourceLabel = Caption("", 12, Muted);
    private readonly Button menu = ButtonOf("☰  منو");
    private MainViewModel vm = new();
    private IReadOnlyList<BankReconciliationService.Entry>? bank, ledger;
    private string section = "نمای کلی";
    private bool busy;
    private int alertHorizon = 7;
    private static readonly string[] Sections = ["نمای کلی", "مرکز چک‌ها", "فروش", "رادار نقدینگی", "تحلیل خزانه", "هشدار سررسید", "طرف حساب", "بانک و مغایرت", "اتصال SQL", "راهنما"];
    private sealed record CardRow(string Title, string Detail);

    public MainPage()
    {
        FlowDirection = FlowDirection.RightToLeft;
        BackgroundColor = Canvas;
        var brand = Caption("MEELANO  |  کنسول مدیران", 17, Text, true);
        menu.Clicked += async (_, _) =>
        {
            if (busy) return;
            var choice = await DisplayActionSheetAsync("انتخاب بخش", "انصراف", null, Sections);
            if (Sections.Contains(choice)) { section = choice; Render(); }
        };
        var header = new Grid { Padding = new Thickness(16, 12), ColumnSpacing = 10,
            ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star)] };
        header.Add(menu, 0); header.Add(brand, 1);
        var status = new VerticalStackLayout { Padding = new Thickness(18, 0, 18, 8), Spacing = 2, Children = { sectionLabel, sourceLabel } };
        var root = new Grid { RowDefinitions = [new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star)] };
        root.Add(header, 0, 0); root.Add(status, 0, 1); root.Add(body, 0, 2);
        Content = root;
        Render();
    }

    private static Label Caption(string text, double size = 14, Color? color = null, bool bold = false) => new()
    {
        Text = text, FontFamily = bold ? "VazirBold" : "Vazir", FontSize = size,
        TextColor = color ?? Text, HorizontalTextAlignment = TextAlignment.Start, LineBreakMode = LineBreakMode.WordWrap
    };
    private static Button ButtonOf(string text) => new()
    {
        Text = text, FontFamily = "VazirBold", FontSize = 13, TextColor = Canvas,
        BackgroundColor = Gold, CornerRadius = 12, MinimumHeightRequest = 46, Padding = new Thickness(14, 8)
    };
    private static Entry Field(string placeholder, string? value = null, bool secret = false) => new()
    {
        Placeholder = placeholder, Text = value, IsPassword = secret, FontFamily = "Vazir", FontSize = 15,
        TextColor = Text, PlaceholderColor = Muted, BackgroundColor = Surface, Margin = new Thickness(0, 3),
        MinimumHeightRequest = 50, FlowDirection = FlowDirection.LeftToRight, HorizontalTextAlignment = TextAlignment.Start
    };
    private static Border Card(View content) => new()
    {
        BackgroundColor = Surface, Stroke = BorderColor, StrokeThickness = 1,
        StrokeShape = new RoundRectangle { CornerRadius = 14 }, Padding = 14, Margin = new Thickness(0, 4), Content = content
    };
    private static VerticalStackLayout Column(params View[] children)
    {
        var stack = new VerticalStackLayout { Spacing = 8 };
        foreach (var child in children) stack.Children.Add(child);
        return stack;
    }
    private static Border Metric(string title, string value, string suffix = "تومان") =>
        Card(Column(Caption(title, 13, Muted), Caption(value, 21, Gold, true), Caption(suffix, 12, Muted)));
    private void SetScroll(params View[] children)
    {
        var stack = Column(children); stack.Padding = new Thickness(16, 4, 16, 28);
        body.Children.Clear(); body.Add(new ScrollView { Content = stack });
    }
    private void SetList(IEnumerable<CardRow> rows, string note, View? extra = null)
    {
        var collection = new CollectionView
        {
            ItemsSource = rows, SelectionMode = SelectionMode.None, Margin = new Thickness(0, 4),
            EmptyView = "موردی برای نمایش وجود ندارد.",
            ItemTemplate = new DataTemplate(() =>
            {
                var title = Caption("", 15, Gold, true); title.SetBinding(Label.TextProperty, nameof(CardRow.Title));
                var detail = Caption("", 13, Text); detail.SetBinding(Label.TextProperty, nameof(CardRow.Detail));
                return Card(Column(title, detail));
            })
        };
        var top = Column(Caption(note, 12, Muted)); if (extra is not null) top.Children.Add(extra);
        var layout = new Grid { Padding = new Thickness(16, 4, 16, 10), RowDefinitions = [new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star)] };
        layout.Add(top, 0, 0); layout.Add(collection, 0, 1);
        body.Children.Clear(); body.Add(layout);
    }
    private void Render()
    {
        sectionLabel.Text = section;
        sourceLabel.Text = $"{vm.DataModeLabel}  ·  {vm.TodayDateText}";
        switch (section)
        {
            case "نمای کلی": SetScroll(
                Card(Column(Caption("نمای مدیریتی میلانو", 20, Gold, true), Caption(vm.DataSourceDescription, 13, Muted), Caption("داده‌ها snapshot هستند و خودکار همگام نمی‌شوند.", 12, Muted))),
                Metric("فروش امروز", vm.TodaySales), Metric("چک دریافتی · امروز تا ۶ روز آینده", vm.Incoming7),
                Metric("چک پرداختی · امروز تا ۶ روز آینده", vm.Outgoing7), Metric("خالص چک‌های هفت‌روزه", vm.Net7),
                Metric("چک‌های هشدار", vm.Warnings, "مورد")); break;
            case "مرکز چک‌ها": ChecksPage(); break;
            case "فروش": SetList(vm.Sales.Select(s => new CardRow($"{s.Customer} · {s.DateText}", $"فروشنده: {s.Seller}\nفروش: {s.AmountText} تومان\nسود: {s.ProfitText} تومان · {s.CollectionStatus}")), $"جمع فروش: {vm.SalesTotal} تومان  ·  جمع سود: {vm.SalesProfit} تومان\nنمایش کل فروش snapshot؛ هیچ سندی ثبت نمی‌شود."); break;
            case "رادار نقدینگی": SetList(vm.Radar.Select(x => new CardRow(x.DayText, $"دریافتی: {x.IncomingText}\nپرداختی: {x.OutgoingText}\nماندهٔ تجمعی از صفر: {x.BalanceText} تومان")), "فقط چک‌های در انتظار؛ ماندهٔ آغازین صفر است و این اعداد موجودی بانکی واقعی نیستند."); break;
            case "تحلیل خزانه": SetList(vm.TreasuryBuckets.Select(x => new CardRow(x.Label, $"دریافتی: {x.IncomingText} · تعداد {x.IncomingCountText}\nپرداختی: {x.OutgoingText} · تعداد {x.OutgoingCountText}\nخالص: {x.NetText} تومان")), $"{vm.TreasuryAsOf}\nنسبت پوشش ۳۰ روزه: {vm.TreasuryCoverage} · صرفاً چک‌های موجود، نه ماندهٔ حساب."); break;
            case "هشدار سررسید": AlertsPage(); break;
            case "طرف حساب": CounterpartiesPage(); break;
            case "بانک و مغایرت": BankPage(); break;
            case "اتصال SQL": ConnectionPage(); break;
            case "راهنما": SetScroll(Card(Column(Caption("راهنمای نسخهٔ اندروید", 19, Gold, true), Caption("ابتدا داده‌ها ساختگی هستند. برای دادهٔ واقعی از اتصال SQL استفاده کنید. دو نمای MEELANO_Checks و MEELANO_Sales مطابق قرارداد نسخهٔ ویندوز لازم‌اند. اتصال و بارگذاری فقط‌خواندنی است و اطلاعات صرفاً در حافظه نگهداری می‌شود."), Caption("هشدار امنیتی: در اتصال آزمایشی اعتبار گواهی سرور بررسی نمی‌شود. فقط در شبکهٔ آزمایشی ایزوله و با حساب و دادهٔ آزمایشی استفاده کنید؛ در اینترنت یا محیط عملیاتی استفاده نکنید.", 13, Gold), Caption("این نسخه اعلان پس‌زمینه، ثبت سند، تغییر وضعیت و خروجی CSV ندارد. مقایسهٔ بانک صرفاً پیشنهادی است. گزارش طرف حساب بر اساس نام دقیق و چک‌هاست، نه ماندهٔ حساب."), Caption("فونت Vazirmatn، تحت مجوز SIL OFL 1.1؛ متن مجوز همراه برنامه و در مخزن موجود است.", 12, Muted)))); break;
        }
    }
    private void ChecksPage()
    {
        var search = Field("جست‌وجوی شماره، نام یا بانک", vm.Query);
        search.FlowDirection = FlowDirection.RightToLeft;
        var type = new Picker { Title = "نوع چک", ItemsSource = vm.CheckTypeFilters, SelectedItem = vm.SelectedCheckType, TextColor = Text, TitleColor = Muted, FontFamily = "Vazir", BackgroundColor = Surface };
        var status = new Picker { Title = "وضعیت چک", ItemsSource = vm.StatusFilters, SelectedItem = vm.SelectedStatus, TextColor = Text, TitleColor = Muted, FontFamily = "Vazir", BackgroundColor = Surface };
        var list = new CollectionView { ItemsSource = vm.FilteredChecks, SelectionMode = SelectionMode.None, EmptyView = "چکی پیدا نشد.", ItemTemplate = new DataTemplate(() => {
            var title = Caption("", 15, Gold, true); title.SetBinding(Label.TextProperty, "Number");
            var party = Caption("", 14, Text); party.SetBinding(Label.TextProperty, "Party");
            var detail = Caption("", 13, Muted); detail.BindingContextChanged += (_, _) => { if (detail.BindingContext is CheckItem c) detail.Text = $"{c.Kind} · {c.Status} · {c.Bank}\nسررسید: {c.DueDateText}  ·  مبلغ: {c.AmountText} تومان\nریسک: {c.RiskText}"; };
            return Card(Column(title, party, detail)); }) };
        search.TextChanged += (_, e) => vm.Query = e.NewTextValue ?? "";
        type.SelectedIndexChanged += (_, _) => { if (type.SelectedItem is string x) vm.SelectedCheckType = x; };
        status.SelectedIndexChanged += (_, _) => { if (status.SelectedItem is string x) vm.SelectedStatus = x; };
        var top = Column(Caption("جست‌وجو و فیلتر چک‌ها؛ اطلاعات SQL فقط‌خواندنی است.", 12, Muted), search, type, status);
        var layout = new Grid { Padding = new Thickness(16, 0, 16, 10), RowDefinitions = [new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star)] };
        layout.Add(top, 0, 0); layout.Add(list, 0, 1); body.Children.Clear(); body.Add(layout);
    }
    private void AlertsPage()
    {
        var days = Field("افق روز (۰ تا ۹۰)", alertHorizon.ToString(CultureInfo.InvariantCulture)); days.Keyboard = Keyboard.Numeric;
        var refresh = ButtonOf("محاسبهٔ هشدارها");
        var controls = Column(days, refresh);
        refresh.Clicked += async (_, _) => {
            var normalized = PersianText.NormalizeForSearch(days.Text ?? "");
            if (!int.TryParse(normalized, NumberStyles.None, CultureInfo.InvariantCulture, out var n) || n < 0 || n > 90) { await DisplayAlertAsync("خطا", "عدد ۰ تا ۹۰ وارد کنید.", "باشه"); return; }
            alertHorizon = n; vm.SetAlertDays(n); Render();
        };
        SetList(vm.CheckAlerts.Select(x => new CardRow($"{x.Number} · {x.Party}", $"{x.Kind} · {x.Status}\nسررسید: {x.DueDate}  ·  {x.Amount} تومان")), vm.AlertsDescription + "\nاعلان پس‌زمینه وجود ندارد.", controls);
    }
    private void CounterpartiesPage()
    {
        var search = Field("جست‌وجوی نام طرف حساب", vm.CounterpartyQuery); search.FlowDirection = FlowDirection.RightToLeft;
        var list = new CollectionView { SelectionMode = SelectionMode.None, ItemsSource = vm.Counterparties, EmptyView = "طرف حسابی یافت نشد.", ItemTemplate = new DataTemplate(() => {
            var title = Caption("", 15, Gold, true); title.SetBinding(Label.TextProperty, "PartyDisplay");
            var detail = Caption("", 13, Text); detail.BindingContextChanged += (_, _) => { if (detail.BindingContext is CounterpartyReportService.Row r) detail.Text = $"دریافتی در انتظار ({r.IncomingCountText}): {r.IncomingText}\nپرداختی در انتظار ({r.OutgoingCountText}): {r.OutgoingText}\nدریافتی سررسیدگذشته: {r.IncomingOverdueText}\nپرداختی سررسیدگذشته: {r.OutgoingOverdueText}\nخالص چک: {r.NetText}\nبرگشتی دریافتی: {r.IncomingReturnedText} · پرداختی: {r.OutgoingReturnedText} تومان"; };
            return Card(Column(title, detail)); }) };
        search.TextChanged += (_, e) => { vm.CounterpartyQuery = e.NewTextValue ?? ""; list.ItemsSource = vm.Counterparties; };
        var layout = new Grid { Padding = new Thickness(16, 0, 16, 10), RowDefinitions = [new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star)] };
        layout.Add(Column(Caption($"{vm.CounterpartyAsOf} · فقط چک‌ها؛ نه ماندهٔ حساب. اشخاص هم‌نام ممکن است ادغام شوند.", 12, Muted), search), 0, 0); layout.Add(list, 0, 1); body.Children.Clear(); body.Add(layout);
    }
    private void BankPage()
    {
        var openBank = ButtonOf("انتخاب CSV بانک"); var openLedger = ButtonOf("انتخاب CSV دفتر"); var compare = ButtonOf("مقایسهٔ پیشنهادی");
        var status = Caption($"بانک: {(bank is null ? "انتخاب نشده" : PersianFormatting.Number(bank.Count) + " ردیف")} · دفتر: {(ledger is null ? "انتخاب نشده" : PersianFormatting.Number(ledger.Count) + " ردیف")}", 13, Muted);
        async Task Load(bool isBank)
        {
            try
            {
                var file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = isBank ? "انتخاب فایل بانک" : "انتخاب فایل دفتر" });
                if (file is null) return;
                await using var stream = await file.OpenReadAsync();
                using var buffer = new MemoryStream();
                var chunk = new byte[16384]; int n;
                while ((n = await stream.ReadAsync(chunk)) > 0)
                {
                    if (buffer.Length + n > 4 * 1024 * 1024) throw new InvalidOperationException("حجم فایل نباید بیش از ۴ مگابایت باشد.");
                    await buffer.WriteAsync(chunk.AsMemory(0, n));
                }
                buffer.Position = 0;
                using var reader = new StreamReader(buffer, new UTF8Encoding(false, true), true);
                var entries = BankReconciliationService.Parse(reader);
                if (isBank) bank = entries; else ledger = entries;
                BankPage();
            }
            catch (Exception e) when (e is InvalidOperationException or IOException or DecoderFallbackException or UnauthorizedAccessException)
            { await DisplayAlertAsync("فایل معتبر نیست", e is InvalidOperationException ? e.Message : "فایل قابل خواندن نیست یا UTF-8 معتبر ندارد.", "باشه"); }
        }
        openBank.Clicked += async (_, _) => await Load(true); openLedger.Clicked += async (_, _) => await Load(false);
        compare.Clicked += async (_, _) => {
            if (bank is null || ledger is null) { await DisplayAlertAsync("مقایسه", "ابتدا دو فایل بانک و دفتر را انتخاب کنید.", "باشه"); return; }
            try
            {
                var result = BankReconciliationService.Compare(bank, ledger);
                var back = ButtonOf("بازگشت به فایل‌ها"); back.Clicked += (_, _) => BankPage();
                SetList(result.Select(x => new CardRow(x.Status, $"ارجاع: {x.Reference}\nبانک: {x.BankId} · {x.BankDate} · {x.BankDirection} · {x.BankAmount}\nدفتر: {x.LedgerId} · {x.LedgerDate} · {x.LedgerDirection} · {x.LedgerAmount} تومان")), "نتیجه پیشنهادی و نیازمند بررسی است؛ تطبیق رسمی یا ثبت سند نیست.", back);
            }
            catch (InvalidOperationException e) { await DisplayAlertAsync("مقایسه ممکن نیست", e.Message, "باشه"); }
        };
        SetScroll(Card(Column(Caption("مقایسهٔ آزمایشی دو فایل", 18, Gold, true), Caption("هیچ اتصال بانکی، ذخیره‌سازی یا ثبت سند انجام نمی‌شود. نتیجه صرفاً پیشنهادی است."), status)),
            Caption("هر دو فایل UTF-8 با سرستون id,account,date,reference,direction,amount؛ تاریخ میلادی YYYY-MM-DD و مبلغ مثبت تومان است. هر فایل حداکثر ۴ مگابایت و ۱۰٬۰۰۰ ردیف.", 13, Muted), openBank, openLedger, compare);
    }
    private void ConnectionPage()
    {
        var host = Field("IP یا نام میزبان SQL"); var port = Field("پورت TCP", "1433"); port.Keyboard = Keyboard.Numeric;
        var database = Field("نام پایگاه داده"); var user = Field("نام کاربری SQL"); var password = Field("رمز SQL", secret: true);
        var progress = Caption("", 13, Gold);
        var connect = ButtonOf("بارگذاری فقط‌خواندنی");
        connect.Clicked += async (_, _) => {
            if (busy) return;
            if (!int.TryParse(PersianText.NormalizeForSearch(port.Text ?? ""), NumberStyles.None, CultureInfo.InvariantCulture, out var portNumber) || portNumber is < 1 or > 65535 || string.IsNullOrWhiteSpace(database.Text))
            { await DisplayAlertAsync("اطلاعات ناقص", "پورت معتبر و نام پایگاه داده را وارد کنید.", "باشه"); return; }
            if (!await DisplayAlertAsync("هشدار امنیتی", "در این اتصال آزمایشی هویت سرور و اعتبار گواهی بررسی نمی‌شود و امکان سرقت داده یا رمز وجود دارد. فقط در شبکهٔ آزمایشی ایزوله با حساب و دادهٔ آزمایشی ادامه دهید.", "ادامه", "انصراف")) return;
            SqlServerConnectionSettings settings;
            try { settings = new SqlServerConnectionSettings(host.Text ?? "", portNumber, false, user.Text, password.Text); }
            catch (ArgumentException ex) { password.Text = ""; await DisplayAlertAsync("اطلاعات اتصال", ex.Message, "باشه"); return; }
            var db = database.Text.Trim(); password.Text = ""; busy = true; menu.IsEnabled = false; connect.IsEnabled = false;
            progress.Text = "در حال اتصال و اعتبارسنجی نماهای گزارش…";
            try
            {
                var updates = new Progress<SqlServerProgress>(p => progress.Text = p.Stage);
                var next = await Task.Run(async () => {
                    var service = new SqlServerDataService();
                    var schema = await service.DiscoverSchemaAsync(settings, db, updates);
                    var plan = SqlAutomaticMapping.Analyze(schema);
                    var repo = await service.ImportAsync(settings, db, SqlAutomaticMapping.CreateMapping(plan), updates, maximumRowsPerTable: 25000);
                    return new MainViewModel(repo);
                });
                vm = next; alertHorizon = 7; section = "نمای کلی";
                await DisplayAlertAsync("بارگذاری موفق", "نماها اعتبارسنجی شدند. داده‌ها snapshot فقط‌خواندنی هستند؛ برای تازه‌سازی دوباره اتصال بگیرید.", "باشه");
                Render();
            }
            catch (Exception ex)
            {
                var message = ex is Microsoft.Data.SqlClient.SqlException sqlError ? SqlServerErrorMessages.ToPersian(sqlError) : ex is InvalidOperationException && (ex.Message.Contains("نمای گزارش", StringComparison.Ordinal) || ex.Message.Contains("ستون", StringComparison.Ordinal) || ex.Message.Contains("واحد", StringComparison.Ordinal) || ex.Message.Contains("سقف محافظتی", StringComparison.Ordinal))
                    ? ex.Message : "اتصال یا بارگذاری انجام نشد. IP، پورت، نام پایگاه داده، دسترسی حساب فقط‌خواندنی و قرارداد نماهای گزارش را بررسی کنید. دادهٔ قبلی تغییر نکرد.";
                progress.Text = "بارگذاری ناموفق؛ دادهٔ قبلی حفظ شد.";
                await DisplayAlertAsync("اتصال ناموفق", message, "باشه");
            }
            finally { busy = false; menu.IsEnabled = true; connect.IsEnabled = true; }
        };
        SetScroll(Card(Column(Caption("SQL Server · فقط برای آزمایش", 18, Gold, true), Caption("فقط شبکه و حساب آزمایشی؛ اعتبار گواهی و هویت سرور بررسی نمی‌شود.", 13, Gold))),
            Caption("SQL Login و نام دیتابیس را وارد کنید؛ مشخصات ذخیره نمی‌شوند.", 13, Muted),
            host, port, database, user, password, progress,
            Caption("دو نمای MEELANO_Checks و MEELANO_Sales با قرارداد مستند لازم است. سقف موبایل ۲۵٬۰۰۰ ردیف در هر نماست؛ دادهٔ اضافه به‌جای برش پنهان رد می‌شود. Windows Authentication و جست‌وجوی شبکه در این نسخه موجود نیست.", 12, Muted));
        // Keep the primary action accessible while the form itself scrolls on short screens.
        var scroll = body.Children[0];
        body.Children.Clear();
        var form = new Grid { RowDefinitions = [new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto)] };
        form.Add(scroll, 0, 0);
        connect.Margin = new Thickness(16, 4, 16, 12);
        form.Add(connect, 0, 1);
        body.Add(form);
    }
}
