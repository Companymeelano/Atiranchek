using System.Globalization;
using Microsoft.VisualBasic.FileIO;
using MeelanoExecutiveConsole.Models;

namespace MeelanoExecutiveConsole.Services;

/// <summary>Strict, test-only, in-memory CSV comparison. Does not authenticate a bank or post entries.</summary>
public static class BankReconciliationService
{
    public const int MaximumRows = 10_000;
    private static readonly string[] Header = ["id", "account", "date", "reference", "direction", "amount"];
    public sealed record Entry(string Id, string Account, DateTime Date, string Reference, string Direction, decimal Amount)
    {
        public string DateText => PersianFormatting.PersianDate(Date);
        public string AmountText => PersianFormatting.MoneyExact(Amount);
        public string DirectionText => Direction == "credit" ? "واریز" : "برداشت";
    }
    public sealed record Comparison(Entry? Bank, Entry? Ledger, string Status)
    {
        public string BankId => Bank?.Id ?? "—";
        public string LedgerId => Ledger?.Id ?? "—";
        public string Reference => Bank?.Reference ?? Ledger?.Reference ?? "—";
        public string BankDate => Bank?.DateText ?? "—";
        public string LedgerDate => Ledger?.DateText ?? "—";
        public string BankAmount => Bank?.AmountText ?? "—";
        public string LedgerAmount => Ledger?.AmountText ?? "—";
        public string BankDirection => Bank?.DirectionText ?? "—";
        public string LedgerDirection => Ledger?.DirectionText ?? "—";
    }

    public static IReadOnlyList<Entry> Parse(TextReader input)
    {
        ArgumentNullException.ThrowIfNull(input);
        using var parser = new TextFieldParser(input) { TextFieldType = FieldType.Delimited, HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = true };
        parser.SetDelimiters(",");
        var rows = new List<Entry>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        string? account = null;
        int row = 1;
        try
        {
            var header = parser.ReadFields();
            if (header is null || !header.SequenceEqual(Header, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException("ستون‌های CSV باید دقیقاً id,account,date,reference,direction,amount باشند.");
            while (!parser.EndOfData)
            {
                row++;
                var fields = parser.ReadFields();
                if (fields is null || fields.Length != 6) throw new InvalidOperationException($"تعداد ستون‌های ردیف {row} معتبر نیست.");
                if (rows.Count >= MaximumRows) throw new InvalidOperationException("فایل بیش از ۱۰٬۰۰۰ ردیف دارد.");
                var id = fields[0].Trim(); var rowAccount = fields[1].Trim(); var reference = fields[3].Trim();
                if (id.Length == 0 || id.Length > 128 || rowAccount.Length == 0 || rowAccount.Length > 128 || reference.Length > 128 ||
                    new[] { id, rowAccount, reference }.Any(x => x.Any(char.IsControl)))
                    throw new InvalidOperationException($"شناسه یا حساب در ردیف {row} نامعتبر است.");
                if (!ids.Add(id)) throw new InvalidOperationException($"شناسهٔ تراکنش در ردیف {row} تکراری است.");
                account ??= rowAccount;
                if (!string.Equals(account, rowAccount, StringComparison.Ordinal))
                    throw new InvalidOperationException("هر فایل باید دقیقاً متعلق به یک حساب باشد.");
                if (!DateTime.TryParseExact(fields[2].Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                    throw new InvalidOperationException($"تاریخ ردیف {row} باید میلادی و به شکل YYYY-MM-DD باشد.");
                var direction = fields[4].Trim().ToLowerInvariant();
                if (direction is not ("credit" or "debit"))
                    throw new InvalidOperationException($"جهت ردیف {row} باید credit یا debit باشد.");
                if (!TryParseMoney(fields[5].Trim(), allowSigned: false, out var amount) || amount <= 0)
                    throw new InvalidOperationException($"مبلغ ردیف {row} باید عدد مثبت تومان بدون جداکنندهٔ هزارگان باشد.");
                rows.Add(new Entry(id, rowAccount, date.Date, reference, direction, amount));
            }
        }
        catch (MalformedLineException) { throw new InvalidOperationException($"قالب CSV در ردیف {row} معتبر نیست."); }
        if (rows.Count == 0) throw new InvalidOperationException("فایل هیچ تراکنشی ندارد؛ حساب از فایل خالی قابل تأیید نیست.");
        return rows.AsReadOnly();
    }

    /// <summary>Rejects values that Decimal.TryParse would round beyond its representable precision.</summary>
    public static bool TryParseMoney(string text, bool allowSigned, out decimal amount)
    {
        amount = 0;
        if (string.IsNullOrEmpty(text)) return false;
        bool negative = text[0] == '-';
        if (text[0] is '-' or '+')
        {
            if (!allowSigned) return false;
            text = text[1..];
        }
        if (text.Length == 0 || text.Count(c => c == '.') > 1 || text.Any(c => c != '.' && (c < '0' || c > '9')) || !text.Any(char.IsAsciiDigit)) return false;
        var parts = text.Split('.');
        var whole = parts[0].TrimStart('0');
        if (whole.Length == 0) whole = "0";
        var fraction = parts.Length == 2 ? parts[1].TrimEnd('0') : string.Empty;
        var canonical = whole + (fraction.Length > 0 ? "." + fraction : "");
        if (negative && canonical != "0") canonical = "-" + canonical;
        if (!decimal.TryParse(canonical, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount)) return false;
        return amount.ToString("0.############################", CultureInfo.InvariantCulture) == canonical;
    }

    public static IReadOnlyList<Comparison> Compare(IReadOnlyList<Entry> bank, IReadOnlyList<Entry> ledger)
    {
        ArgumentNullException.ThrowIfNull(bank); ArgumentNullException.ThrowIfNull(ledger);
        if (bank.Count == 0 || ledger.Count == 0 ||
            bank.Any(x => !string.Equals(x.Account, bank[0].Account, StringComparison.Ordinal)) ||
            ledger.Any(x => !string.Equals(x.Account, bank[0].Account, StringComparison.Ordinal)))
            throw new InvalidOperationException("دو فایل غیرخالی باید به یک حساب یکسان تعلق داشته باشند.");
        var banks = bank.Where(x => x.Reference.Length > 0).GroupBy(x => x.Reference, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.ToList(), StringComparer.Ordinal);
        var books = ledger.Where(x => x.Reference.Length > 0).GroupBy(x => x.Reference, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.ToList(), StringComparer.Ordinal);
        var output = new List<Comparison>();
        foreach (var reference in banks.Keys.Concat(books.Keys).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal))
        {
            var b = banks.GetValueOrDefault(reference) ?? [];
            var l = books.GetValueOrDefault(reference) ?? [];
            if (b.Count > 1 || l.Count > 1)
            {
                output.AddRange(b.Select(x => new Comparison(x, null, "شناسهٔ ارجاع تکراری · بررسی")));
                output.AddRange(l.Select(x => new Comparison(null, x, "شناسهٔ ارجاع تکراری · بررسی")));
            }
            else if (b.Count == 1 && l.Count == 1)
            {
                var same = b[0].Date == l[0].Date && b[0].Direction == l[0].Direction && b[0].Amount == l[0].Amount;
                output.Add(new Comparison(b[0], l[0], same ? "پیشنهاد تطبیق · بررسی" : "اختلاف تاریخ/جهت/مبلغ"));
            }
            else if (b.Count == 1) output.Add(new Comparison(b[0], null, "فقط بانک"));
            else if (l.Count == 1) output.Add(new Comparison(null, l[0], "فقط دفتر"));
        }
        output.AddRange(bank.Where(x => x.Reference.Length == 0).Select(x => new Comparison(x, null, "بدون ارجاع · بررسی")));
        output.AddRange(ledger.Where(x => x.Reference.Length == 0).Select(x => new Comparison(null, x, "بدون ارجاع · بررسی")));
        return output.AsReadOnly();
    }

    public static decimal CalculateBalance(IReadOnlyList<Entry> bank, decimal openingBalance, DateTime openingDate)
    {
        if (bank.Count == 0) throw new InvalidOperationException("ابتدا گردش بانکی را بارگذاری کنید.");
        if (bank.Any(x => x.Date < openingDate.Date)) throw new InvalidOperationException("گردش قبل از تاریخ ماندهٔ آغازین در فایل وجود دارد.");
        return checked(openingBalance + bank.Sum(x => x.Direction == "credit" ? x.Amount : -x.Amount));
    }
}
