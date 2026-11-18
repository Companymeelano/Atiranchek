using MeelanoExecutiveConsole.Models;

namespace MeelanoExecutiveConsole.Services;

public interface IRepository
{
    IReadOnlyList<CheckItem> GetChecks();
    IReadOnlyList<SaleItem> GetSales();
}
