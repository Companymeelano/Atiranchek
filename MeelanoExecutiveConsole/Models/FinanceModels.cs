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
}

public sealed class CheckItem
{
    public string Number { get; set; } = "";
    public CheckKind Kind { get; set; }
    public string Party { get; set; } = "";
    public decimal Amount { get; set; }
    public DateTime DueDate { get; set; }
    public string Status { get; set; } = CheckStatuses.Pending;
    public int Risk { get; set; }
    public string Bank { get; set; } = "";

    public string AmountText => PersianFormatting.Money(Amount);
    public string DueDateText => PersianFormatting.GregorianDate(DueDate);
    public string RiskText => PersianFormatting.Number(Risk);
}

public sealed class SaleItem
{
    public DateTime Date { get; set; }
    public string Customer { get; set; } = "";
    public decimal Amount { get; set; }
    public decimal Profit { get; set; }
    public string CollectionStatus { get; set; } = "";
    public string Seller { get; set; } = "";
    public double ChartBarHeight { get; set; }

    public string AmountText => PersianFormatting.Money(Amount);
    public string ProfitText => PersianFormatting.Money(Profit);
    public string DateText => PersianFormatting.GregorianDate(Date);
    public string ShortDateText => PersianFormatting.GregorianShortDate(Date);
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
    public string DayText => PersianFormatting.GregorianDate(Day);
    public string IncomingText => PersianFormatting.Money(Incoming);
    public string OutgoingText => PersianFormatting.Money(Outgoing);
    public string BalanceText => PersianFormatting.Money(Balance);
}
