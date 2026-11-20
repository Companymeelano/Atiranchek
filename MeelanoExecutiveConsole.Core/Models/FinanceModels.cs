namespace MeelanoExecutiveConsole.Models;

public enum CheckKind
{
    دریافتی,
    پرداختی
}

public static class CheckStatuses
{
    public const string Pending = "در انتظار";
    public const string Collected = "وصول‌شده";
    public const string Paid = "پرداخت‌شده";
    public const string Returned = "برگشتی";
    public const string Suspended = "معلق";

    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(
        new[] { Pending, Collected, Paid, Returned, Suspended });
}

public sealed class CheckItem
{
    public string Number { get; init; } = "";
    public CheckKind Kind { get; init; }
    public string Party { get; init; } = "";
    public decimal Amount { get; init; }
    public DateTime DueDate { get; init; }
    public string Status { get; set; } = CheckStatuses.Pending;
    public int Risk { get; init; }
    public bool HasRiskScore { get; init; } = true;
    public string Bank { get; init; } = "";

    public string AmountText => PersianFormatting.Money(Amount);
    public string DueDateText => PersianFormatting.PersianDate(DueDate);
    public string RiskText => HasRiskScore ? PersianFormatting.Number(Risk) : "—";
    public int? RiskSort => HasRiskScore ? Risk : null;
}

public sealed class SaleItem
{
    public DateTime Date { get; init; }
    public string Customer { get; init; } = "";
    public decimal Amount { get; init; }
    public decimal Profit { get; init; }
    public string CollectionStatus { get; init; } = "";
    public string Seller { get; init; } = "";

    public string AmountText => PersianFormatting.Money(Amount);
    public string ProfitText => PersianFormatting.Money(Profit);
    public string DateText => PersianFormatting.PersianDate(Date);
}

public sealed class SalesDayPoint
{
    public SalesDayPoint(DateTime date, decimal amount)
    {
        Date = date.Date;
        Amount = amount;
    }

    public DateTime Date { get; }
    public decimal Amount { get; }
    public double ChartBarHeight { get; set; }
    public string ShortDateText => PersianFormatting.PersianShortDate(Date);
    public string ChartAmountText => PersianFormatting.CompactMillions(Amount);
}

public sealed class RadarPoint
{
    public RadarPoint(DateTime day, decimal incoming, decimal outgoing, decimal balance)
    {
        Day = day;
        Incoming = incoming;
        Outgoing = outgoing;
        Balance = balance;
    }

    public DateTime Day { get; }
    public decimal Incoming { get; }
    public decimal Outgoing { get; }
    public decimal Balance { get; }
    public bool IsNegative => Balance < 0;
    public string DayText => PersianFormatting.PersianDate(Day);
    public string IncomingText => PersianFormatting.Money(Incoming);
    public string OutgoingText => PersianFormatting.Money(Outgoing);
    public string BalanceText => PersianFormatting.Money(Balance);
}
