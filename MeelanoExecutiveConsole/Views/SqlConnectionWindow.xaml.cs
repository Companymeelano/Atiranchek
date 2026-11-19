using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Media3D;
using MeelanoExecutiveConsole;
using MeelanoExecutiveConsole.Models;
using MeelanoExecutiveConsole.Services;

namespace MeelanoExecutiveConsole.Views;

public partial class SqlConnectionWindow : Window
{
    private readonly SqlServerDataService dataService = new();
    private readonly List<(AxisAngleRotation3D Rotation, DoubleAnimation Animation)> loadingAnimations = [];
    private IReadOnlyList<SqlTableMetadata> schema = Array.Empty<SqlTableMetadata>();
    private CancellationTokenSource? activeOperation;
    private bool initialized;
    private bool suppressMappingEvents;
    private bool updatingDatabaseList;

    public SqlConnectionWindow()
    {
        InitializeComponent();
        AuthenticationCombo.SelectedIndex = 0;
        ConfigureMappingGrid(KindMappingsGrid, [CheckKind.دریافتی.ToString(), CheckKind.پرداختی.ToString()]);
        ConfigureMappingGrid(StatusMappingsGrid, CheckStatuses.All);
        InitializeLoadingScene();
        Closing += Window_Closing;
    }

    public SqlServerRepository? ImportedRepository { get; private set; }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var workArea = SystemParameters.WorkArea;
        Width = Math.Min(Width, Math.Max(MinWidth, workArea.Width - 32));
        Height = Math.Min(Height, Math.Max(MinHeight, workArea.Height - 32));
        initialized = true;
        UpdateAuthenticationControls();
        UpdateThemeToggle();
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

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        SqlServerConnectionSettings settings;
        try
        {
            settings = CreateSettings();
        }
        catch (Exception exception) when (exception is ArgumentException)
        {
            ShowError(SqlServerErrorMessages.ToPersian(exception));
            return;
        }

        ClearDiscoveredSchema();
        ResetDatabaseList();
        ConnectionStatusText.Text = "در حال اتصال…";
        IReadOnlyList<string>? databases = null;
        var succeeded = await RunWithOverlayAsync(async (progress, cancellationToken) =>
        {
            databases = await dataService.ListDatabasesAsync(settings, progress, cancellationToken);
        });
        if (!succeeded || databases is null)
            return;

        updatingDatabaseList = true;
        DatabaseCombo.ItemsSource = databases;
        DatabaseCombo.SelectedIndex = databases.Count > 0 ? 0 : -1;
        DatabaseCombo.IsEnabled = databases.Count > 0;
        DiscoverButton.IsEnabled = databases.Count > 0;
        updatingDatabaseList = false;
        ConnectionStatusText.Text = databases.Count == 0
            ? "اتصال برقرار شد، اما دیتابیس کاربریِ قابل‌دسترسی پیدا نشد."
            : $"اتصال برقرار شد · {PersianFormatting.Number(databases.Count)} دیتابیس";

        if (databases.Count == 0)
            ShowError("فهرست پایگاه داده خالی است. مجوز اتصال به master و دسترسی دیتابیس را بررسی کنید.");
    }

    private async void DiscoverSchema_Click(object sender, RoutedEventArgs e)
    {
        if (DatabaseCombo.SelectedItem is not string database)
        {
            ShowError("ابتدا یک پایگاه داده انتخاب کنید.");
            return;
        }

        SqlServerConnectionSettings settings;
        try
        {
            settings = CreateSettings();
        }
        catch (Exception exception) when (exception is ArgumentException)
        {
            ShowError(SqlServerErrorMessages.ToPersian(exception));
            return;
        }

        IReadOnlyList<SqlTableMetadata>? discovered = null;
        var succeeded = await RunWithOverlayAsync(async (progress, cancellationToken) =>
        {
            discovered = await dataService.DiscoverSchemaAsync(settings, database, progress, cancellationToken);
        });
        if (!succeeded || discovered is null)
            return;

        schema = discovered;
        SchemaGrid.ItemsSource = schema;
        SchemaCountText.Text = $"{PersianFormatting.Number(schema.Count)} شیء";
        PopulateTablePickers();
        MappingTab.IsEnabled = schema.Count > 0;
        ImportButton.IsEnabled = schema.Count > 0;

        if (schema.Count == 0)
        {
            ShowError("ساختار قابل‌دسترسی پیدا نشد. مجوزهای metadata و SELECT روی جدول‌ها یا Viewهای گزارش را بررسی کنید.");
            return;
        }

        WizardTabs.SelectedIndex = 1;
    }

    private void CheckTable_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (suppressMappingEvents)
            return;
        SetColumnOptions(
            CheckTableCombo.SelectedItem as SqlTableMetadata,
            CheckNumberField, CheckKindField, CheckPartyField, CheckAmountField,
            CheckDueDateField, CheckStatusField, CheckBankField, CheckRiskField);
        CheckAmountUnitCombo.SelectedIndex = 0;
        ClearValueMapping(KindMappingsGrid);
        ClearValueMapping(StatusMappingsGrid);
    }

    private void SalesTable_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (suppressMappingEvents)
            return;
        SetColumnOptions(
            SalesTableCombo.SelectedItem as SqlTableMetadata,
            SalesDateField, SalesCustomerField, SalesSellerField, SalesAmountField,
            SalesProfitField, SalesCollectionStatusField);
        SalesAmountUnitCombo.SelectedIndex = 0;
    }

    private void CheckAmountField_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!suppressMappingEvents)
            CheckAmountUnitCombo.SelectedIndex = 0;
    }

    private void SalesUnitField_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!suppressMappingEvents)
            SalesAmountUnitCombo.SelectedIndex = 0;
    }

    private async void CheckKindField_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!suppressMappingEvents)
            await LoadValueMappingsAsync(CheckKindField, CheckTableCombo, KindMappingsGrid, isStatus: false);
    }

    private async void CheckStatusField_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!suppressMappingEvents)
            await LoadValueMappingsAsync(CheckStatusField, CheckTableCombo, StatusMappingsGrid, isStatus: true);
    }

    private async Task LoadValueMappingsAsync(
        ComboBox fieldPicker,
        ComboBox tablePicker,
        DataGrid grid,
        bool isStatus)
    {
        if (tablePicker.SelectedItem is not SqlTableMetadata table
            || GetOptionalColumn(fieldPicker) is not { } column)
        {
            ClearValueMapping(grid);
            return;
        }

        ClearValueMapping(grid);
        SqlServerConnectionSettings settings;
        try
        {
            settings = CreateSettings();
        }
        catch (Exception exception) when (exception is ArgumentException)
        {
            ShowError(SqlServerErrorMessages.ToPersian(exception));
            return;
        }

        IReadOnlyList<SqlValueOption>? values = null;
        var succeeded = await RunWithOverlayAsync(async (progress, cancellationToken) =>
        {
            values = await dataService.GetDistinctValuesAsync(
                settings,
                GetSelectedDatabase(),
                table,
                column,
                progress,
                cancellationToken);
        });
        if (!succeeded || values is null)
            return;

        var source = new ObservableCollection<SqlValueMappingChoice>(values.Select(value => new SqlValueMappingChoice
        {
            DisplayValue = value.DisplayValue,
            LookupKey = value.LookupKey,
            TargetValue = value.IsNull ? null : SuggestMapping(value.RawValue, isStatus)
        }));
        grid.ItemsSource = source;
    }

    private static string? SuggestMapping(string rawValue, bool isStatus)
    {
        var normalized = PersianText.NormalizeForSearch(rawValue);
        if (!isStatus)
        {
            if (normalized is "پرداختی" or "چک پرداختی" or "outgoing" or "payable" or "out")
                return CheckKind.پرداختی.ToString();
            if (normalized is "دریافتی" or "چک دریافتی" or "incoming" or "receivable" or "in")
                return CheckKind.دریافتی.ToString();
            return null;
        }

        // Suggestions are deliberately conservative and always remain editable by the user.
        if (normalized.Contains("در انتظار", StringComparison.Ordinal)
            || normalized.Contains("پرداخت نشده", StringComparison.Ordinal)
            || normalized.Contains("وصول نشده", StringComparison.Ordinal)
            || normalized is "pending" or "open")
        {
            return CheckStatuses.Pending;
        }
        if (normalized.Contains("تعلیق", StringComparison.Ordinal)
            || normalized.Contains("معلق", StringComparison.Ordinal)
            || normalized is "suspended" or "suspend" or "hold")
        {
            return CheckStatuses.Suspended;
        }
        if (normalized.Contains("برگشت", StringComparison.Ordinal)
            || normalized.Contains("عودت", StringComparison.Ordinal)
            || normalized is "returned" or "return" or "bounced")
        {
            return CheckStatuses.Returned;
        }
        if (normalized is "وصول شده" or "وصول شده است" or "collected" or "received" or "cleared")
            return CheckStatuses.Collected;
        if (normalized is "پرداخت شده" or "پرداخت شده است" or "paid")
            return CheckStatuses.Paid;
        return null;
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        SqlImportMapping mapping;
        SqlServerConnectionSettings settings;
        string database;
        try
        {
            settings = CreateSettings();
            database = GetSelectedDatabase();
            mapping = BuildImportMapping();
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            ShowError(SqlServerErrorMessages.ToPersian(exception));
            return;
        }

        SqlServerRepository? repository = null;
        var succeeded = await RunWithOverlayAsync(async (progress, cancellationToken) =>
        {
            repository = await dataService.ImportAsync(settings, database, mapping, progress, cancellationToken);
        });
        if (!succeeded || repository is null)
            return;

        ImportedRepository = repository;
        PasswordInput.Clear();
        DialogResult = true;
        Close();
    }

    private SqlImportMapping BuildImportMapping()
    {
        var checkTable = CheckTableCombo.SelectedItem as SqlTableMetadata
            ?? throw new InvalidOperationException("منبع جدول چک انتخاب نشده است.");
        var salesTable = SalesTableCombo.SelectedItem as SqlTableMetadata
            ?? throw new InvalidOperationException("منبع جدول فروش انتخاب نشده است.");

        var kindValues = ReadMappingGrid(KindMappingsGrid);
        var statusValues = ReadMappingGrid(StatusMappingsGrid);
        if (kindValues.Count == 0 || statusValues.Count == 0)
            throw new InvalidOperationException("ابتدا ستون نوع و وضعیت چک را انتخاب و تمام مقدارهای متمایز را نگاشت کنید.");

        var checkMapping = new SqlCheckImportMapping(
            checkTable,
            GetSelectedColumn(CheckNumberField),
            GetSelectedColumn(CheckKindField),
            GetSelectedColumn(CheckPartyField),
            GetSelectedColumn(CheckAmountField),
            GetSelectedColumn(CheckDueDateField),
            GetSelectedColumn(CheckStatusField),
            GetOptionalColumn(CheckBankField),
            GetOptionalColumn(CheckRiskField),
            kindValues.ToDictionary(item => item.LookupKey, item => Enum.Parse<CheckKind>(item.TargetValue!, ignoreCase: false), StringComparer.Ordinal),
            statusValues.ToDictionary(item => item.LookupKey, item => item.TargetValue!, StringComparer.Ordinal),
            GetMultiplier(CheckAmountUnitCombo));

        var salesMapping = new SqlSalesImportMapping(
            salesTable,
            GetSelectedColumn(SalesDateField),
            GetSelectedColumn(SalesCustomerField),
            GetSelectedColumn(SalesSellerField),
            GetSelectedColumn(SalesAmountField),
            GetSelectedColumn(SalesProfitField),
            GetOptionalColumn(SalesCollectionStatusField),
            GetMultiplier(SalesAmountUnitCombo));
        return new SqlImportMapping(checkMapping, salesMapping);
    }

    private static List<SqlValueMappingChoice> ReadMappingGrid(DataGrid grid)
    {
        grid.CommitEdit(DataGridEditingUnit.Cell, exitEditingMode: true);
        grid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);
        if (grid.ItemsSource is not IEnumerable<SqlValueMappingChoice> values)
            return [];

        var list = values.ToList();
        var invalid = list.FirstOrDefault(item => string.IsNullOrWhiteSpace(item.TargetValue));
        if (invalid is not null)
            throw new InvalidOperationException($"برای مقدار منبع «{invalid.DisplayValue}» یک مقصد نگاشت انتخاب کنید.");
        return list;
    }

    private static void ConfigureMappingGrid(DataGrid grid, IReadOnlyList<string> targets)
    {
        grid.AutoGenerateColumns = false;
        grid.IsReadOnly = false;
        grid.CanUserAddRows = false;
        grid.CanUserDeleteRows = false;
        grid.CanUserReorderColumns = false;
        grid.CanUserResizeRows = false;
        grid.SelectionUnit = DataGridSelectionUnit.FullRow;
        grid.SelectionMode = DataGridSelectionMode.Single;
        grid.HeadersVisibility = DataGridHeadersVisibility.Column;
        grid.RowHeight = 34;
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "مقدار کشف‌شده در SQL",
            Binding = new Binding(nameof(SqlValueMappingChoice.DisplayValue)),
            IsReadOnly = true,
            Width = new DataGridLength(1, DataGridLengthUnitType.Star)
        });
        grid.Columns.Add(new DataGridComboBoxColumn
        {
            Header = "معادل در برنامه",
            ItemsSource = targets,
            SelectedItemBinding = new Binding(nameof(SqlValueMappingChoice.TargetValue))
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            },
            Width = new DataGridLength(1, DataGridLengthUnitType.Star)
        });
    }

    private static void ClearValueMapping(DataGrid grid) => grid.ItemsSource = null;

    private void PopulateTablePickers()
    {
        suppressMappingEvents = true;
        CheckTableCombo.ItemsSource = schema;
        CheckTableCombo.DisplayMemberPath = nameof(SqlTableMetadata.DisplayName);
        CheckTableCombo.SelectedIndex = -1;
        SalesTableCombo.ItemsSource = schema;
        SalesTableCombo.DisplayMemberPath = nameof(SqlTableMetadata.DisplayName);
        SalesTableCombo.SelectedIndex = -1;
        ResetColumnPickers();
        CheckAmountUnitCombo.SelectedIndex = 0;
        SalesAmountUnitCombo.SelectedIndex = 0;
        ClearValueMapping(KindMappingsGrid);
        ClearValueMapping(StatusMappingsGrid);
        suppressMappingEvents = false;
    }

    private void SetColumnOptions(SqlTableMetadata? table, params ComboBox[] pickers)
    {
        suppressMappingEvents = true;
        foreach (var picker in pickers)
        {
            var choices = new List<ColumnChoice> { new(null, "— انتخاب ستون —") };
            if (picker == CheckBankField || picker == CheckRiskField || picker == SalesCollectionStatusField)
                choices[0] = new ColumnChoice(null, "— بدون نگاشت —");
            if (table is not null)
                choices.AddRange(table.Columns.Select(column => new ColumnChoice(column.Name, $"{column.Name}  ·  {column.DataType}")));
            picker.ItemsSource = choices;
            picker.DisplayMemberPath = nameof(ColumnChoice.Display);
            picker.SelectedIndex = 0;
        }
        suppressMappingEvents = false;
    }

    private void ResetColumnPickers()
    {
        SetColumnOptions(null,
            CheckNumberField, CheckKindField, CheckPartyField, CheckAmountField,
            CheckDueDateField, CheckStatusField, CheckBankField, CheckRiskField,
            SalesDateField, SalesCustomerField, SalesSellerField, SalesAmountField,
            SalesProfitField, SalesCollectionStatusField);
    }

    private static string GetSelectedColumn(ComboBox picker)
    {
        var column = (picker.SelectedItem as ColumnChoice)?.Name;
        if (!string.IsNullOrWhiteSpace(column))
            return column;
        throw new InvalidOperationException("یکی از فیلدهای ضروری نگاشت انتخاب نشده است.");
    }

    private static string? GetOptionalColumn(ComboBox picker)
    {
        var column = (picker.SelectedItem as ColumnChoice)?.Name;
        return string.IsNullOrWhiteSpace(column) ? null : column;
    }

    private static decimal GetMultiplier(ComboBox picker)
    {
        if (picker.SelectedItem is ComboBoxItem item
            && decimal.TryParse(item.Tag?.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var multiplier)
            && multiplier > 0)
        {
            return multiplier;
        }
        throw new InvalidOperationException("واحد منبع را صریحاً برای چک و مبلغ/سود فروش انتخاب کنید؛ تبدیل پیش‌فرض انجام نمی‌شود.");
    }

    private SqlServerConnectionSettings CreateSettings()
    {
        var normalizedPort = PersianText.NormalizeForSearch(PortBox.Text).Trim();
        if (!int.TryParse(normalizedPort, NumberStyles.None, CultureInfo.InvariantCulture, out var port))
            throw new ArgumentOutOfRangeException(nameof(PortBox), "پورت باید عددی بین ۱ تا ۶۵۵۳۵ باشد.");

        var useWindowsAuthentication = (AuthenticationCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "windows";
        return new SqlServerConnectionSettings(
            ServerHostBox.Text,
            port,
            useWindowsAuthentication,
            UserNameBox.Text,
            PasswordInput.Password);
    }

    private string GetSelectedDatabase() => DatabaseCombo.SelectedItem as string
        ?? throw new InvalidOperationException("پایگاه داده انتخاب نشده است.");

    private void Authentication_Changed(object sender, SelectionChangedEventArgs e)
    {
        UpdateAuthenticationControls();
        if (initialized)
            ConnectionField_Changed(sender, e);
    }

    private void UpdateAuthenticationControls()
    {
        var windowsAuth = (AuthenticationCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "windows";
        PasswordPanel.Visibility = windowsAuth ? Visibility.Collapsed : Visibility.Visible;
        UserNameBox.IsEnabled = !windowsAuth;
        if (windowsAuth)
        {
            UserNameBox.Clear();
            PasswordInput.Clear();
        }
    }

    private void ConnectionField_Changed(object sender, RoutedEventArgs e)
    {
        if (!initialized)
            return;
        ClearDiscoveredSchema();
        ResetDatabaseList();
        ConnectionStatusText.Text = "تنظیم اتصال تغییر کرده است؛ دوباره اتصال را بیازمایید.";
    }

    private void ResetDatabaseList()
    {
        updatingDatabaseList = true;
        DatabaseCombo.ItemsSource = null;
        DatabaseCombo.SelectedIndex = -1;
        DatabaseCombo.IsEnabled = false;
        DiscoverButton.IsEnabled = false;
        updatingDatabaseList = false;
    }

    private void Database_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (initialized && !updatingDatabaseList)
            ClearDiscoveredSchema();
    }

    private void ClearDiscoveredSchema()
    {
        schema = Array.Empty<SqlTableMetadata>();
        SchemaGrid.ItemsSource = null;
        SchemaCountText.Text = string.Empty;
        MappingTab.IsEnabled = false;
        ImportButton.IsEnabled = false;
        suppressMappingEvents = true;
        CheckTableCombo.ItemsSource = null;
        SalesTableCombo.ItemsSource = null;
        ResetColumnPickers();
        CheckAmountUnitCombo.SelectedIndex = 0;
        SalesAmountUnitCombo.SelectedIndex = 0;
        ClearValueMapping(KindMappingsGrid);
        ClearValueMapping(StatusMappingsGrid);
        suppressMappingEvents = false;
    }

    private async Task<bool> RunWithOverlayAsync(
        Func<IProgress<SqlServerProgress>, CancellationToken, Task> action)
    {
        if (activeOperation is not null)
            return false;

        var operation = new CancellationTokenSource();
        activeOperation = operation;
        ShowLoading();
        var progress = new Progress<SqlServerProgress>(UpdateLoading);
        var succeeded = false;
        string? errorMessage = null;
        try
        {
            await action(progress, operation.Token);
            succeeded = true;
        }
        catch (OperationCanceledException)
        {
            ConnectionStatusText.Text = "عملیات لغو شد.";
        }
        catch (Exception exception)
        {
            errorMessage = SqlServerErrorMessages.ToPersian(exception);
            ConnectionStatusText.Text = errorMessage;
        }
        finally
        {
            // Close the progress overlay before opening a modal error dialog; otherwise
            // the dimmed loading screen remains visible behind the MessageBox.
            HideLoading();
            if (ReferenceEquals(activeOperation, operation))
                activeOperation = null;
            operation.Dispose();
        }

        if (errorMessage is not null)
            ShowError(errorMessage);

        return succeeded;
    }

    private void ShowLoading()
    {
        LoadingProgressBar.Value = 0;
        LoadingPercentText.Text = "۰٪";
        LoadingStageText.Text = "آماده‌سازی اتصال مستقیم…";
        foreach (var (rotation, animation) in loadingAnimations)
            rotation.BeginAnimation(AxisAngleRotation3D.AngleProperty, animation, HandoffBehavior.SnapshotAndReplace);
        LoadingOverlay.Visibility = Visibility.Visible;
        LoadingOverlay.UpdateLayout();
    }

    private void UpdateLoading(SqlServerProgress update)
    {
        var percent = Math.Clamp(update.Percentage, 0, 100);
        LoadingProgressBar.Value = percent;
        LoadingPercentText.Text = $"{PersianFormatting.Number(percent)}٪";
        LoadingStageText.Text = update.TotalRows > 0
            ? $"{update.Stage}\n{PersianFormatting.Number(update.RowsRead)} از {PersianFormatting.Number(update.TotalRows)} ردیف"
            : update.Stage;
    }

    private void HideLoading()
    {
        foreach (var (rotation, _) in loadingAnimations)
            rotation.BeginAnimation(AxisAngleRotation3D.AngleProperty, null);
        LoadingOverlay.Visibility = Visibility.Collapsed;
    }

    private void CancelOperation_Click(object sender, RoutedEventArgs e) => activeOperation?.Cancel();

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (activeOperation is not null)
        {
            activeOperation.Cancel();
            return;
        }
        PasswordInput.Clear();
        Close();
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (activeOperation is not null)
        {
            activeOperation.Cancel();
            e.Cancel = true;
        }
        PasswordInput.Clear();
    }

    private void ShowError(string message) =>
        MessageBox.Show(this, message, "اتصال SQL Server", MessageBoxButton.OK, MessageBoxImage.Warning);

    private void InitializeLoadingScene()
    {
        LoadingViewport.Camera = new PerspectiveCamera
        {
            Position = new Point3D(0, 0, 3.5),
            LookDirection = new Vector3D(0, 0, -3.5),
            UpDirection = new Vector3D(0, 1, 0),
            FieldOfView = 45
        };

        var scene = new Model3DGroup();
        scene.Children.Add(new AmbientLight(Color.FromRgb(105, 114, 104)));
        scene.Children.Add(new DirectionalLight(Color.FromRgb(255, 248, 219), new Vector3D(-1, -1, -2)));
        scene.Children.Add(new DirectionalLight(Color.FromRgb(99, 210, 173), new Vector3D(1, 0.5, 1)));

        var sphereMaterial = CreateMaterial(Color.FromRgb(42, 97, 78), Color.FromRgb(196, 229, 190));
        var sphere = CreateSphere(0.46, 28, 18, sphereMaterial);
        var sphereRotation = new AxisAngleRotation3D(new Vector3D(0.4, 1, 0.25), 0);
        sphere.Transform = new RotateTransform3D(sphereRotation);
        loadingAnimations.Add((sphereRotation, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(12))
        {
            RepeatBehavior = RepeatBehavior.Forever
        }));
        scene.Children.Add(sphere);

        var goldMaterial = CreateMaterial(Color.FromRgb(207, 170, 93), Color.FromRgb(255, 235, 179));
        var ringRotation = new AxisAngleRotation3D(new Vector3D(0, 1, 0), 0);
        var ringTransform = new Transform3DGroup();
        ringTransform.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), 63)));
        ringTransform.Children.Add(new RotateTransform3D(ringRotation));
        var ring = CreateTorus(0.75, 0.035, 72, 12, goldMaterial);
        ring.Transform = ringTransform;
        loadingAnimations.Add((ringRotation, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(6))
        {
            RepeatBehavior = RepeatBehavior.Forever
        }));
        scene.Children.Add(ring);

        var secondRotation = new AxisAngleRotation3D(new Vector3D(1, 0.2, 0), 0);
        var secondRingTransform = new Transform3DGroup();
        secondRingTransform.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 0, 1), -48)));
        secondRingTransform.Children.Add(new RotateTransform3D(secondRotation));
        var secondRing = CreateTorus(0.91, 0.018, 72, 8, CreateMaterial(Color.FromRgb(82, 153, 131), Color.FromRgb(176, 224, 202)));
        secondRing.Transform = secondRingTransform;
        loadingAnimations.Add((secondRotation, new DoubleAnimation(360, 0, TimeSpan.FromSeconds(9))
        {
            RepeatBehavior = RepeatBehavior.Forever
        }));
        scene.Children.Add(secondRing);

        LoadingViewport.Children.Add(new ModelVisual3D { Content = scene });
    }

    private static Material CreateMaterial(Color diffuseColor, Color specularColor)
    {
        var material = new MaterialGroup();
        material.Children.Add(new DiffuseMaterial(new SolidColorBrush(diffuseColor)));
        material.Children.Add(new SpecularMaterial(new SolidColorBrush(specularColor), 72));
        return material;
    }

    private static GeometryModel3D CreateSphere(double radius, int slices, int stacks, Material material)
    {
        var mesh = new MeshGeometry3D();
        for (var stack = 0; stack <= stacks; stack++)
        {
            var latitude = Math.PI * stack / stacks;
            var y = Math.Cos(latitude);
            var ringRadius = Math.Sin(latitude);
            for (var slice = 0; slice <= slices; slice++)
            {
                var longitude = 2 * Math.PI * slice / slices;
                var x = ringRadius * Math.Cos(longitude);
                var z = ringRadius * Math.Sin(longitude);
                mesh.Positions.Add(new Point3D(radius * x, radius * y, radius * z));
                mesh.Normals.Add(new Vector3D(x, y, z));
            }
        }

        for (var stack = 0; stack < stacks; stack++)
        {
            for (var slice = 0; slice < slices; slice++)
            {
                var first = stack * (slices + 1) + slice;
                var second = first + slices + 1;
                mesh.TriangleIndices.Add(first);
                mesh.TriangleIndices.Add(second);
                mesh.TriangleIndices.Add(first + 1);
                mesh.TriangleIndices.Add(second);
                mesh.TriangleIndices.Add(second + 1);
                mesh.TriangleIndices.Add(first + 1);
            }
        }
        return new GeometryModel3D(mesh, material) { BackMaterial = material };
    }

    private static GeometryModel3D CreateTorus(double majorRadius, double tubeRadius, int majorSegments, int tubeSegments, Material material)
    {
        var mesh = new MeshGeometry3D();
        for (var major = 0; major <= majorSegments; major++)
        {
            var theta = 2 * Math.PI * major / majorSegments;
            var cosTheta = Math.Cos(theta);
            var sinTheta = Math.Sin(theta);
            for (var tube = 0; tube <= tubeSegments; tube++)
            {
                var phi = 2 * Math.PI * tube / tubeSegments;
                var cosPhi = Math.Cos(phi);
                var sinPhi = Math.Sin(phi);
                var radial = majorRadius + tubeRadius * cosPhi;
                mesh.Positions.Add(new Point3D(radial * cosTheta, tubeRadius * sinPhi, radial * sinTheta));
                mesh.Normals.Add(new Vector3D(cosPhi * cosTheta, sinPhi, cosPhi * sinTheta));
            }
        }

        for (var major = 0; major < majorSegments; major++)
        {
            for (var tube = 0; tube < tubeSegments; tube++)
            {
                var first = major * (tubeSegments + 1) + tube;
                var second = first + tubeSegments + 1;
                mesh.TriangleIndices.Add(first);
                mesh.TriangleIndices.Add(first + 1);
                mesh.TriangleIndices.Add(second);
                mesh.TriangleIndices.Add(first + 1);
                mesh.TriangleIndices.Add(second + 1);
                mesh.TriangleIndices.Add(second);
            }
        }
        return new GeometryModel3D(mesh, material) { BackMaterial = material };
    }

    private sealed record ColumnChoice(string? Name, string Display);

    private sealed class SqlValueMappingChoice
    {
        public string DisplayValue { get; init; } = string.Empty;
        public string LookupKey { get; init; } = string.Empty;
        public string? TargetValue { get; set; }
    }
}
