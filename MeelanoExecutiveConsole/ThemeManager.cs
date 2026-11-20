using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;

namespace MeelanoExecutiveConsole;

public enum ConsoleTheme
{
    Dark,
    Light
}

/// <summary>Applies a persistent, application-wide MEELANO palette through dynamic WPF resources.</summary>
public static class ThemeManager
{
    private static readonly string PreferencePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MEELANO",
        "theme.txt");

    private static bool initialized;

    private static readonly IReadOnlyDictionary<string, string> DarkPalette = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["CanvasBrush"] = "#081113",
        ["SidebarBrush"] = "#0B1719",
        ["PanelBrush"] = "#101D20",
        ["PanelRaisedBrush"] = "#152528",
        ["PanelHoverBrush"] = "#1B3032",
        ["StrokeBrush"] = "#263B3D",
        ["TextPrimaryBrush"] = "#F0F1EA",
        ["TextSecondaryBrush"] = "#C4D1CB",
        ["TextMutedBrush"] = "#A6B9B1",
        ["GoldBrush"] = "#D8B66C",
        ["GoldLightBrush"] = "#F1D995",
        ["GreenBrush"] = "#68C9A2",
        ["RedBrush"] = "#E48379",
        ["BlueBrush"] = "#86B9D2",
        ["GoldButtonBrush"] = "#D8B66C",
        ["GoldButtonTextBrush"] = "#14201E",
        ["HeaderSurfaceBrush"] = "#0D191B",
        ["SurfaceAltBrush"] = "#111F20",
        ["InsetSurfaceBrush"] = "#0B1719",
        ["InsetBorderBrush"] = "#304648",
        ["BadgeSurfaceBrush"] = "#172624",
        ["AccentBorderBrush"] = "#425543",
        ["BrandSurfaceBrush"] = "#20352E",
        ["BrandBorderBrush"] = "#536A4F",
        ["NoticeSurfaceBrush"] = "#152320",
        ["NoticeBorderBrush"] = "#574C31",
        ["NavigationHoverBrush"] = "#142427",
        ["SelectionSurfaceBrush"] = "#1A2C2B",
        ["SelectionBorderBrush"] = "#39534A",
        ["TableHeaderBrush"] = "#1A2D30",
        ["TableAlternateBrush"] = "#132225",
        ["GridLineBrush"] = "#304648",
        ["DataGridSelectionBrush"] = "#2C5046",
        ["InputBackgroundBrush"] = "#0B1719",
        ["InputSelectionBrush"] = "#526D57",
        ["NotificationBrush"] = "#202D27",
        ["LoadingOverlayBrush"] = "#E9081214",
        ["LoadingPanelBrush"] = "#101D20",
        ["GlowBrush"] = "#193F3830",
        ["SoftBorderBrush"] = "#344B41",
        ["ChartTrackBrush"] = "#0B1719",
        ["StatusBorderBrush"] = "#314943",
        ["DialogBorderBrush"] = "#56634A"
    };

    private static readonly IReadOnlyDictionary<string, string> LightPalette = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["CanvasBrush"] = "#F6F2E9",
        ["SidebarBrush"] = "#EEE8DC",
        ["PanelBrush"] = "#FFFCF7",
        ["PanelRaisedBrush"] = "#FFFFFF",
        ["PanelHoverBrush"] = "#F1E9D9",
        ["StrokeBrush"] = "#DDD4C4",
        ["TextPrimaryBrush"] = "#2D2A25",
        ["TextSecondaryBrush"] = "#645D52",
        ["TextMutedBrush"] = "#6D6356",
        ["GoldBrush"] = "#A77B2F",
        ["GoldLightBrush"] = "#89621F",
        ["GreenBrush"] = "#28785A",
        ["RedBrush"] = "#B64D43",
        ["BlueBrush"] = "#3F718E",
        ["GoldButtonBrush"] = "#D4B66F",
        ["GoldButtonTextBrush"] = "#30250F",
        ["HeaderSurfaceBrush"] = "#F0EADB",
        ["SurfaceAltBrush"] = "#F5F0E7",
        ["InsetSurfaceBrush"] = "#F1EDE5",
        ["InsetBorderBrush"] = "#E2DACC",
        ["BadgeSurfaceBrush"] = "#EEE5D2",
        ["AccentBorderBrush"] = "#D1C095",
        ["BrandSurfaceBrush"] = "#ECE5D8",
        ["BrandBorderBrush"] = "#D3C5A5",
        ["NoticeSurfaceBrush"] = "#FBF5E8",
        ["NoticeBorderBrush"] = "#D8C99F",
        ["NavigationHoverBrush"] = "#F1E9DB",
        ["SelectionSurfaceBrush"] = "#E9E0CE",
        ["SelectionBorderBrush"] = "#C8A75D",
        ["TableHeaderBrush"] = "#EEE8DC",
        ["TableAlternateBrush"] = "#F7F3EB",
        ["GridLineBrush"] = "#E4DDCF",
        ["DataGridSelectionBrush"] = "#E9DFC9",
        ["InputBackgroundBrush"] = "#FFFCF7",
        ["InputSelectionBrush"] = "#E2D3AE",
        ["NotificationBrush"] = "#F1E9D9",
        ["LoadingOverlayBrush"] = "#E8F6F1E7",
        ["LoadingPanelBrush"] = "#FFFCF7",
        ["GlowBrush"] = "#24C7A55B",
        ["SoftBorderBrush"] = "#D8CFBE",
        ["ChartTrackBrush"] = "#EEE8DC",
        ["StatusBorderBrush"] = "#D3C5A5",
        ["DialogBorderBrush"] = "#C7B47D"
    };

    public static ConsoleTheme CurrentTheme { get; private set; } = ConsoleTheme.Dark;
    public static bool IsLight => CurrentTheme == ConsoleTheme.Light;
    public static string ToggleLabel => IsLight ? "☾ تم تیره" : "☼ تم روشن";
    public static string ToggleToolTip => IsLight ? "تغییر به تم تیره" : "تغییر به تم روشن";

    public static void Initialize()
    {
        if (initialized)
            return;

        var theme = ConsoleTheme.Dark;
        try
        {
            if (string.Equals(File.ReadAllText(PreferencePath).Trim(), nameof(ConsoleTheme.Light), StringComparison.OrdinalIgnoreCase))
                theme = ConsoleTheme.Light;
        }
        catch (IOException)
        {
            // A missing preference file is the normal first-run case.
        }
        catch (UnauthorizedAccessException)
        {
            // Theme selection still works in memory if the preference cannot be read.
        }

        Apply(theme, persist: false);
        initialized = true;
    }

    public static void Toggle()
    {
        Initialize();
        Apply(IsLight ? ConsoleTheme.Dark : ConsoleTheme.Light, persist: true);
    }

    private static void Apply(ConsoleTheme theme, bool persist)
    {
        if (Application.Current is null)
            return;

        var palette = theme == ConsoleTheme.Light ? LightPalette : DarkPalette;
        var resources = Application.Current.Resources;
        foreach (var (key, hex) in palette)
            resources[key] = new SolidColorBrush(ParseColor(hex));

        resources["WindowBackgroundBrush"] = theme == ConsoleTheme.Light
            ? CreateGradient("#FAF7F0", "#F1EBDD", "#ECE4D7")
            : CreateGradient("#0A1416", "#081012", "#101B1B");
        resources["GoldGradientBrush"] = theme == ConsoleTheme.Light
            ? CreateGradient("#F4DEA2", "#C6A052")
            : CreateGradient("#F0D58B", "#C99F4E");

        CurrentTheme = theme;
        if (persist)
            SavePreference(theme);
    }

    private static void SavePreference(ConsoleTheme theme)
    {
        try
        {
            var directory = Path.GetDirectoryName(PreferencePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(PreferencePath, theme.ToString());
        }
        catch (IOException)
        {
            // The current session still changes even if persistence fails.
        }
        catch (UnauthorizedAccessException)
        {
            // The current session still changes even if persistence fails.
        }
    }

    private static Color ParseColor(string value)
    {
        var hex = value.AsSpan(1);
        if (hex.Length == 8)
        {
            return Color.FromArgb(
                ParseByte(hex[..2]),
                ParseByte(hex.Slice(2, 2)),
                ParseByte(hex.Slice(4, 2)),
                ParseByte(hex.Slice(6, 2)));
        }

        return Color.FromRgb(ParseByte(hex[..2]), ParseByte(hex.Slice(2, 2)), ParseByte(hex.Slice(4, 2)));
    }

    private static byte ParseByte(ReadOnlySpan<char> hex) =>
        byte.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    private static LinearGradientBrush CreateGradient(params string[] colors)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1)
        };
        for (var index = 0; index < colors.Length; index++)
            brush.GradientStops.Add(new GradientStop(ParseColor(colors[index]), colors.Length == 1 ? 0 : (double)index / (colors.Length - 1)));
        return brush;
    }
}
