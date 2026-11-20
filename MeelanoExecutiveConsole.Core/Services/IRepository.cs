using MeelanoExecutiveConsole.Models;

namespace MeelanoExecutiveConsole.Services;

public interface IRepository
{
    IReadOnlyList<CheckItem> GetChecks();
    IReadOnlyList<SaleItem> GetSales();

    /// <summary>Live SQL sources are intentionally immutable; demo edits remain in-memory.</summary>
    bool IsReadOnly => false;
    string DataSourceDescription => "نسخهٔ نمایشی · داده‌های ساختگی";
}
