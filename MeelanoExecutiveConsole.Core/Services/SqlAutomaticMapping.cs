using MeelanoExecutiveConsole.Models;

namespace MeelanoExecutiveConsole.Services;

/// <summary>
/// Automatic import contract. View/column names and units are explicit; arbitrary business
/// schemas cannot be safely inferred from names alone. Unknown or ambiguous data is rejected.
/// </summary>
public static class SqlAutomaticMapping
{
    public sealed record Plan(SqlTableMetadata Checks, SqlTableMetadata Sales,
        string CheckNumber, string CheckKind, string CheckParty, string CheckAmount, string CheckDate,
        string CheckStatus, string? Bank, string? Risk, decimal CheckMultiplier,
        string SaleDate, string Customer, string Seller, string SaleAmount, string Profit,
        string? CollectionStatus, decimal SaleMultiplier);

    private static readonly string[] TextTypes = ["varchar", "nvarchar", "char", "nchar"];
    private static readonly string[] NumericTypes = ["decimal", "numeric", "money", "smallmoney", "int", "bigint", "smallint", "tinyint"];
    private static readonly string[] DateTypes = ["date", "datetime", "datetime2", "smalldatetime"];

    public static Plan Analyze(IReadOnlyList<SqlTableMetadata> schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        var checks = FindView(schema, "MEELANO_Checks");
        var sales = FindView(schema, "MEELANO_Sales");
        var (checkAmount, checkMultiplier) = Amount(checks, "Amount");
        var (saleAmount, saleMultiplier) = Amount(sales, "Amount");
        var profitName = saleMultiplier == 1m ? "ProfitToman" : "ProfitRial";
        // Both units present on one view are ambiguous even if one pair is complete.
        if (FindColumn(sales, saleMultiplier == 1m ? "ProfitRial" : "ProfitToman") is not null)
            throw new InvalidOperationException("واحد سود فروش مبهم است؛ فقط یک واحد در نمای گزارش مجاز است.");
        return new Plan(checks, sales,
            Required(checks, "CheckNumber", TextTypes), Required(checks, "CheckKind", TextTypes),
            Required(checks, "PartyName", TextTypes), checkAmount, Required(checks, "DueDate", DateTypes),
            Required(checks, "CheckStatus", TextTypes), Optional(checks, "BankName", TextTypes), Optional(checks, "RiskScore", NumericTypes), checkMultiplier,
            Required(sales, "SaleDate", DateTypes), Required(sales, "CustomerName", TextTypes),
            Required(sales, "SellerName", TextTypes), saleAmount, Required(sales, profitName, NumericTypes),
            Optional(sales, "CollectionStatus", TextTypes), saleMultiplier);
    }

    private static SqlTableMetadata FindView(IReadOnlyList<SqlTableMetadata> schema, string name)
    {
        var matches = schema.Where(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1 || !matches[0].ObjectType.Equals("VIEW", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"یک نمای گزارش یکتا به نام {name} لازم است؛ نام یافت نشد، تکراری است یا View نیست. قرارداد خودکار را با مدیر پایگاه داده بررسی کنید.");
        return matches[0];
    }

    private static SqlColumnMetadata? FindColumn(SqlTableMetadata table, string name)
    {
        var matches = table.Columns.Where(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length > 1)
            throw new InvalidOperationException($"ستون {name} در {table.DisplayName} مبهم است.");
        return matches.SingleOrDefault();
    }
    private static string Required(SqlTableMetadata table, string name, string[] types) =>
        Optional(table, name, types) ?? throw new InvalidOperationException($"ستون ضروری {name} در {table.DisplayName} وجود ندارد.");
    private static string? Optional(SqlTableMetadata table, string name, string[] types)
    {
        var column = FindColumn(table, name);
        if (column is null) return null;
        if (!types.Contains(column.DataType, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException($"نوع ستون {name} در {table.DisplayName} با قرارداد گزارش سازگار نیست.");
        return column.Name;
    }
    private static (string Name, decimal Multiplier) Amount(SqlTableMetadata table, string stem)
    {
        var toman = Optional(table, stem + "Toman", NumericTypes);
        var rial = Optional(table, stem + "Rial", NumericTypes);
        if ((toman is null) == (rial is null))
            throw new InvalidOperationException($"واحد {stem} در {table.DisplayName} مشخص و یکتا نیست؛ دقیقاً یکی از ستون‌های {stem}Toman یا {stem}Rial لازم است.");
        return toman is not null ? (toman, 1m) : (rial!, 0.1m);
    }

    public static SqlImportMapping CreateMapping(Plan plan) => new(
        new SqlCheckImportMapping(plan.Checks, plan.CheckNumber, plan.CheckKind, plan.CheckParty,
            plan.CheckAmount, plan.CheckDate, plan.CheckStatus, plan.Bank, plan.Risk,
            new Dictionary<string, CheckKind>(), new Dictionary<string, string>(), plan.CheckMultiplier, AutomaticValues: true),
        new SqlSalesImportMapping(plan.Sales, plan.SaleDate, plan.Customer, plan.Seller,
            plan.SaleAmount, plan.Profit, plan.CollectionStatus, plan.SaleMultiplier));

    public static bool TryKind(string value, out CheckKind kind)
    {
        switch (PersianText.NormalizeForSearch(value).Trim())
        {
            case "دریافتی": case "چک دریافتی": case "incoming": case "receivable":
                kind = CheckKind.دریافتی; return true;
            case "پرداختی": case "چک پرداختی": case "outgoing": case "payable":
                kind = CheckKind.پرداختی; return true;
            default: kind = default; return false;
        }
    }
    public static string? Status(string value) => PersianText.NormalizeForSearch(value).Trim() switch
    {
        "در انتظار" or "pending" => CheckStatuses.Pending,
        "وصول شده" or "collected" => CheckStatuses.Collected,
        "پرداخت شده" or "paid" => CheckStatuses.Paid,
        "برگشتی" or "returned" => CheckStatuses.Returned,
        "معلق" or "suspended" => CheckStatuses.Suspended,
        _ => null
    };
}
