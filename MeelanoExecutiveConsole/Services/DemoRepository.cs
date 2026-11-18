using MeelanoExecutiveConsole.Models;

namespace MeelanoExecutiveConsole.Services;

/// <summary>
/// In-memory sample data. The same objects are returned for the life of the app so
/// demo status changes are reflected consistently across the screens.
/// </summary>
public sealed class DemoRepository : IRepository
{
    private readonly List<CheckItem> checks;
    private readonly List<SaleItem> sales;

    public DemoRepository()
    {
        var today = DateTime.Today;
        checks =
        [
            new() { Number = "RC-2401", Kind = CheckKind.دریافتی, Party = "بازرگانی سپهر", Amount = 185_000_000, DueDate = today, Status = CheckStatuses.Pending, Risk = 18, Bank = "ملت" },
            new() { Number = "RC-2402", Kind = CheckKind.دریافتی, Party = "پخش آریا", Amount = 92_500_000, DueDate = today.AddDays(2), Status = CheckStatuses.Pending, Risk = 74, Bank = "تجارت" },
            new() { Number = "RC-2403", Kind = CheckKind.دریافتی, Party = "فروشگاه هفت‌اقلیم", Amount = 240_000_000, DueDate = today.AddDays(8), Status = CheckStatuses.Pending, Risk = 42, Bank = "سامان" },
            new() { Number = "PC-1108", Kind = CheckKind.پرداختی, Party = "تأمین کالای پارس", Amount = 135_000_000, DueDate = today.AddDays(1), Status = CheckStatuses.Pending, Risk = 20, Bank = "ملت" },
            new() { Number = "PC-1112", Kind = CheckKind.پرداختی, Party = "حمل‌ونقل باران", Amount = 78_000_000, DueDate = today.AddDays(4), Status = CheckStatuses.Pending, Risk = 55, Bank = "صادرات" },
            new() { Number = "RC-2410", Kind = CheckKind.دریافتی, Party = "صنایع نوآور", Amount = 64_000_000, DueDate = today.AddDays(13), Status = CheckStatuses.Collected, Risk = 12, Bank = "سامان" },
            new() { Number = "PC-1120", Kind = CheckKind.پرداختی, Party = "خدمات رایان", Amount = 46_000_000, DueDate = today.AddDays(10), Status = CheckStatuses.Pending, Risk = 81, Bank = "رفاه" },
            new() { Number = "RC-2414", Kind = CheckKind.دریافتی, Party = "بازارچه نارون", Amount = 121_000_000, DueDate = today.AddDays(20), Status = CheckStatuses.Pending, Risk = 31, Bank = "ملت" }
        ];

        sales =
        [
            new() { Date = today, Customer = "بازرگانی سپهر", Amount = 148_000_000, Profit = 26_000_000, CollectionStatus = "نقدی", Seller = "نرگس احمدی" },
            new() { Date = today, Customer = "فروشگاه هفت‌اقلیم", Amount = 96_000_000, Profit = 18_500_000, CollectionStatus = "چکی", Seller = "آرمان رضایی" },
            new() { Date = today.AddDays(-1), Customer = "پخش آریا", Amount = 73_500_000, Profit = 11_000_000, CollectionStatus = "بخشی وصول", Seller = "نرگس احمدی" },
            new() { Date = today.AddDays(-2), Customer = "صنایع نوآور", Amount = 214_000_000, Profit = 42_000_000, CollectionStatus = "چکی", Seller = "کیان مرادی" },
            new() { Date = today.AddDays(-3), Customer = "بازارچه نارون", Amount = 58_000_000, Profit = 9_500_000, CollectionStatus = "نقدی", Seller = "آرمان رضایی" },
            new() { Date = today.AddDays(-4), Customer = "تجهیزات هامون", Amount = 131_000_000, Profit = 24_000_000, CollectionStatus = "وصول‌شده", Seller = "کیان مرادی" }
        ];
    }

    public IReadOnlyList<CheckItem> GetChecks() => checks;

    public IReadOnlyList<SaleItem> GetSales() => sales;
}
