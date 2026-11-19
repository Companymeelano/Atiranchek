using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace MeelanoExecutiveConsole;

public partial class App : Application
{
    private const long MaximumLogBytes = 1_048_576;
    private static readonly object ErrorLogLock = new();
    private static readonly string ErrorLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MEELANO",
        "Logs",
        "startup.log");

    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteError(e.Exception);
        try
        {
            MessageBox.Show(
                $"برنامه با خطا متوقف شد.\n\n{e.Exception.Message}\n\nگزارش جزئیات در این مسیر ذخیره شد:\n{ErrorLogPath}",
                "خطای MEELANO",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
            // Keep the original exception from being masked if the message box cannot be shown.
        }

        e.Handled = true;
        Shutdown(-1);
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
            WriteError(exception);
        else
            WriteError(new InvalidOperationException(e.ExceptionObject?.ToString() ?? "Unknown unhandled exception."));
    }

    private static void WriteError(Exception exception)
    {
        try
        {
            var directory = Path.GetDirectoryName(ErrorLogPath);
            if (string.IsNullOrWhiteSpace(directory))
                return;

            lock (ErrorLogLock)
            {
                Directory.CreateDirectory(directory);
                if (File.Exists(ErrorLogPath) && new FileInfo(ErrorLogPath).Length >= MaximumLogBytes)
                    File.Move(ErrorLogPath, ErrorLogPath + ".previous", overwrite: true);

                var entry = $"{DateTimeOffset.Now:O}{Environment.NewLine}{exception}{Environment.NewLine}{new string('-', 72)}{Environment.NewLine}";
                File.AppendAllText(ErrorLogPath, entry, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
        }
        catch
        {
            // Diagnostics must never prevent the original application failure from surfacing.
        }
    }
}
