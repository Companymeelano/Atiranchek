using System.Globalization;
using System.Windows;
using System.Windows.Controls;
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
    private CancellationTokenSource? activeOperation;
    private bool initialized;
    private bool updatingDatabaseList;

    public SqlConnectionWindow()
    {
        InitializeComponent();
        AuthenticationCombo.SelectedIndex = 0;
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
        RefreshNetworks();
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

    private void RefreshNetworks_Click(object sender, RoutedEventArgs e) => RefreshNetworks();

    private void RefreshNetworks()
    {
        NetworkCombo.ItemsSource = SqlServerDiscoveryService.GetLocalNetworks();
        NetworkCombo.SelectedIndex = NetworkCombo.Items.Count > 0 ? 0 : -1;
        ServerResultsCombo.ItemsSource = null;
        ScanStatusText.Text = NetworkCombo.Items.Count == 0 ? "آداپتور IPv4 فعال پیدا نشد؛ سرور را دستی وارد کنید." : "آداپتور را انتخاب کنید.";
    }

    private async void ScanServers_Click(object sender, RoutedEventArgs e)
    {
        if (NetworkCombo.SelectedItem is not SqlServerDiscoveryService.LocalNetwork network)
        {
            ShowError("ابتدا آداپتور شبکه را انتخاب کنید.");
            return;
        }
        var normalizedPort = PersianText.NormalizeForSearch(PortBox.Text).Trim();
        if (!int.TryParse(normalizedPort, NumberStyles.None, CultureInfo.InvariantCulture, out var port) || port is < 1 or > 65535)
        {
            ShowError("پورت باید بین ۱ تا ۶۵۵۳۵ باشد.");
            return;
        }
        ServerResultsCombo.ItemsSource = null;
        IReadOnlyList<SqlServerDiscoveryService.Server>? results = null;
        var succeeded = await RunWithOverlayAsync(async (progress, token) =>
        {
            var scanProgress = new Progress<(int Done, int Total)>(p =>
                progress.Report(new SqlServerProgress(p.Total == 0 ? 0 : p.Done * 100 / p.Total, $"جستجوی شبکه: {p.Done} از {p.Total}")));
            results = await SqlServerDiscoveryService.DiscoverAsync(network, new[] { 1433, 5050, port }, scanProgress, token);
        });
        if (!succeeded || results is null) return;
        ServerResultsCombo.ItemsSource = results;
        ScanStatusText.Text = results.Count == 0
            ? "پاسخ SQL پیدا نشد؛ فایروال، پورت و محدوده را بررسی کنید یا سرور را دستی وارد کنید."
            : $"{PersianFormatting.Number(results.Count)} پاسخ TDS پیدا شد؛ سرور آزمایشی موردنظر را انتخاب کنید.";
    }

    private void ServerResults_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ServerResultsCombo.SelectedItem is not SqlServerDiscoveryService.Server server) return;
        ServerHostBox.Text = server.Address;
        PortBox.Text = server.Port.ToString(CultureInfo.InvariantCulture);
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
        DatabaseCombo.SelectedIndex = -1;
        DatabaseCombo.IsEnabled = databases.Count > 0;
        DiscoverButton.IsEnabled = false;
        updatingDatabaseList = false;
        ConnectionStatusText.Text = databases.Count == 0
            ? "اتصال برقرار شد، اما دیتابیس کاربریِ قابل‌دسترسی پیدا نشد."
            : $"اتصال برقرار شد · {PersianFormatting.Number(databases.Count)} دیتابیس؛ یک دیتابیس را انتخاب کنید.";

        if (databases.Count == 0)
            ShowError("فهرست پایگاه داده خالی است. مجوز اتصال به master و دسترسی دیتابیس را بررسی کنید.");
    }

    private async void DiscoverSchema_Click(object sender, RoutedEventArgs e) => await LoadDatabaseAutomaticallyAsync();

    private async Task LoadDatabaseAutomaticallyAsync()
    {
        if (DatabaseCombo.SelectedItem is not string database) return;
        SqlServerConnectionSettings settings;
        try { settings = CreateSettings(); }
        catch (ArgumentException exception) { ShowError(SqlServerErrorMessages.ToPersian(exception)); return; }

        SchemaGrid.ItemsSource = null;
        SchemaCountText.Text = "تحلیل خودکار…";
        ConnectionStatusText.Text = "در حال تحلیل و بارگذاری…";
        IReadOnlyList<SqlTableMetadata>? discovered = null;
        SqlServerRepository? repository = null;
        var succeeded = await RunWithOverlayAsync(async (progress, token) =>
        {
            discovered = await dataService.DiscoverSchemaAsync(settings, database, progress, token);
            var plan = SqlAutomaticMapping.Analyze(discovered);
            progress.Report(new SqlServerProgress(5, "قرارداد گزارش شناسایی شد؛ خواندن و کنترل داده‌ها…"));
            repository = await dataService.ImportAsync(settings, database, SqlAutomaticMapping.CreateMapping(plan), progress, token);
        });
        if (discovered is not null)
        {
            SchemaGrid.ItemsSource = discovered;
            SchemaCountText.Text = $"{PersianFormatting.Number(discovered.Count)} شیء";
        }
        if (!succeeded || repository is null)
        {
            ConnectionStatusText.Text = "بارگذاری انجام نشد؛ قرارداد گزارش و دسترسی را بررسی کنید.";
            return;
        }
        ImportedRepository = repository;
        PasswordInput.Clear();
        DialogResult = true;
        Close();
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

    private async void Database_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!initialized || updatingDatabaseList) return;
        ClearDiscoveredSchema();
        DiscoverButton.IsEnabled = DatabaseCombo.SelectedItem is string;
        if (DatabaseCombo.SelectedItem is string)
            await LoadDatabaseAutomaticallyAsync();
    }

    private void ClearDiscoveredSchema()
    {
        SchemaGrid.ItemsSource = null;
        SchemaCountText.Text = string.Empty;
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

}
