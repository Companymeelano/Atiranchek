using System.Globalization;
using System.IO;
using System.Security;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using MeelanoExecutiveConsole.Models;
using MeelanoExecutiveConsole.Services;
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

        if (index >= 0 && index < SectionTabs.Items.Count)
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
            CsvCell.Text(check.DueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            CsvCell.Text(check.Status),
            CsvCell.Number(check.Risk)
        });
        var content = CsvFormatter.Build(
            ["شماره", "نوع", "طرف حساب", "بانک", "مبلغ (ریال)", "سررسید میلادی", "وضعیت", "ریسک نمونه"],
            rows);
        ExportCsv(dialog.FileName, content);
    }

    private void SalesCsv_Click(object sender, RoutedEventArgs e)
    {
        var dialog = CreateCsvSaveDialog("meelano-sales.csv");
        if (dialog.ShowDialog() != true)
            return;

        var rows = vm.Sales.Select(sale => new[]
        {
            CsvCell.Text(sale.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            CsvCell.Text(sale.Customer),
            CsvCell.Text(sale.Seller),
            CsvCell.Number(sale.Amount),
            CsvCell.Number(sale.Profit),
            CsvCell.Text(sale.CollectionStatus)
        });
        var content = CsvFormatter.Build(
            ["تاریخ میلادی", "مشتری", "فروشنده", "مبلغ (ریال)", "سود (ریال)", "وضعیت وصول"],
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

            var document = BuildCheckReport(items, dialog.PrintableAreaWidth, dialog.PrintableAreaHeight);
            dialog.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator, "گزارش چک‌های میلانو");
        }
        catch (Exception exception)
        {
            MessageBox.Show($"چاپ گزارش انجام نشد.\n{exception.Message}", "خطا در چاپ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static FlowDocument BuildCheckReport(IReadOnlyList<CheckItem> items, double pageWidth, double pageHeight)
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
            $"تاریخ تهیه: {PersianFormatting.GregorianDate(DateTime.Today)} · میلادی     |     تعداد: {PersianFormatting.Number(items.Count)}"))
        {
            FontSize = 10,
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 18)
        });

        var table = new Table { CellSpacing = 0 };
        var headers = new[] { "شماره", "نوع", "طرف حساب", "بانک", "مبلغ (ریال)", "سررسید · میلادی", "وضعیت" };
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
        document.Blocks.Add(new Paragraph(new Run("داده‌های این گزارش متعلق به نسخهٔ نمایشی و غیرعملیاتی هستند."))
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
