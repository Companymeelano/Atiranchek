using Microsoft.Extensions.Logging;

namespace MeelanoMobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>().ConfigureFonts(fonts =>
        {
            fonts.AddFont("Vazirmatn-Regular.ttf", "Vazir");
            fonts.AddFont("Vazirmatn-Bold.ttf", "VazirBold");
        });
#if DEBUG
        builder.Logging.AddDebug();
#endif
        return builder.Build();
    }
}
