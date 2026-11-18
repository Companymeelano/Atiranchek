using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using MeelanoExecutiveConsole.Models;
using MeelanoExecutiveConsole.ViewModels;
using Microsoft.Win32;

namespace MeelanoExecutiveConsole.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel vm = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = vm;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        SectionTabs.SelectedIndex = 0;
    }

    private void Navigation_Checked(object sender, RoutedEventArgs e)
    {
        if (SectionTabs is null || sender is not RadioButton navigation
            || !int.TryParse(navigation.Tag?.ToString(), out var index))
        {
            return;
        }

        SectionTabs.SelectedIndex = index;
    }

    private void ChecksGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        vm.UpdateSelection(ChecksGrid.SelectedItems.Cast<CheckItem>());
    }

    private void ApplyStatus(string targetStatus)
    {
        if (ChecksGrid.SelectedItems.Count == 0)
        {
            MessageBox.Show("ابتدا یک یا چند چک را انتخاب کنید.", "انتخاب چک", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var result = vm.ApplySelectedStatus(targetStatus);
        ChecksGrid.Items.Refresh();
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

    private void ChecksCsv_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "CSV UTF-8 (*.csv)|*.csv",
            FileName = "meelano-checks.csv",
            AddExtension = true,
            DefaultExt = ".csv"
        };

        if (dialog.ShowDialog() != true)
            return;

        var rows = vm.FilteredChecks.Select(check => new[]
        {
            check.Number,
            check.Kind.ToString(),
            check.Party,
            check.Bank,
            check.Amount.ToString(CultureInfo.InvariantCulture),
            check.DueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            check.Status,
            check.Risk.ToString(CultureInfo.InvariantCulture)
        });
        ExportCsv(dialog.FileName,
            BuildCsv(["شماره", "نوع", "طرف حساب", "بانک", "مبلغ (ریال)", "سررسید میلادی", "وضعیت", "ریسک نمونه"], rows));
    }

    private void SalesCsv_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "CSV UTF-8 (*.csv)|*.csv",
            FileName = "meelano-sales.csv",
            AddExtension = true,
            DefaultExt = ".csv"
        };

        if (dialog.ShowDialog() != true)
            return;

        var rows = vm.Sales.Select(sale => new[]
        {
            sale.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            sale.Customer,
            sale.Seller,
            sale.Amount.ToString(CultureInfo.InvariantCulture),
            sale.Profit.ToString(CultureInfo.InvariantCulture),
            sale.CollectionStatus
        });
        ExportCsv(dialog.FileName,
            BuildCsv(["تاریخ میلادی", "مشتری", "فروشنده", "مبلغ (ریال)", "سود (ریال)", "وضعیت وصول"], rows));
    }

    private static string BuildCsv(IEnumerable<string> headers, IEnumerable<string[]> rows)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(",", headers.Select(EscapeCsv)));
        foreach (var row in rows)
            builder.AppendLine(string.Join(",", row.Select(EscapeCsv)));
        return builder.ToString();
    }

    private static string EscapeCsv(string value)
    {
        var needsQuotes = value.IndexOfAny([',', '"', '\r', '\n']) >= 0
                          || value.StartsWith(' ') || value.EndsWith(' ');
        return needsQuotes ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }

    private static void ExportCsv(string path, string content)
    {
        try
        {
            // The encoding writes one UTF-8 BOM, which helps spreadsheet applications detect Persian text.
            File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            MessageBox.Show($"فایل CSV ذخیره شد:\n{path}", "خروجی موفق", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
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

        var document = new FlowDocument
        {
            FlowDirection = FlowDirection.RightToLeft,
            PagePadding = new Thickness(38),
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI")
        };
        document.Blocks.Add(new Paragraph(new Run("میلانو | گزارش مرکز چک‌ها"))
        {
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 8)
        });
        document.Blocks.Add(new Paragraph(new Run($"تاریخ تهیه: {PersianFormatting.GregorianDate(DateTime.Today)} · میلادی"))
        {
            FontSize = 10,
            Foreground = System.Windows.Media.Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 18)
        });

        foreach (var check in items)
        {
            var line = $"{check.Number}   |   {check.Kind}   |   {check.Party}   |   {check.AmountText}   |   {check.DueDateText}   |   {check.Status}";
            document.Blocks.Add(new Paragraph(new Run(line))
            {
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 8)
            });
        }

        var dialog = new PrintDialog();
        if (dialog.ShowDialog() == true)
            dialog.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator, "گزارش چک‌های میلانو");
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
        if (WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;
            var workArea = SystemParameters.WorkArea;
            Width = Math.Min(1380, Math.Max(MinWidth, workArea.Width - 32));
            Height = Math.Min(820, Math.Max(MinHeight, workArea.Height - 32));
            Left = workArea.Left + Math.Max(0, (workArea.Width - Width) / 2);
            Top = workArea.Top + Math.Max(0, (workArea.Height - Height) / 2);
        }
        else
        {
            WindowState = WindowState.Maximized;
        }
    }
}
