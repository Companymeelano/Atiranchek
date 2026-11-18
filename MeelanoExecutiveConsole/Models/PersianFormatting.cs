using System.Globalization;

namespace MeelanoExecutiveConsole.Models;

/// <summary>
/// Persian-digit display helpers. Dates deliberately remain Gregorian until a tested
/// Persian-calendar requirement and conversion library are selected.
/// </summary>
public static class PersianFormatting
{
    public static string Number(decimal value) => LocalizeDigits(value.ToString("N0", CultureInfo.InvariantCulture).Replace(',', '٬'));

    public static string Number(int value) => LocalizeDigits(value.ToString("N0", CultureInfo.InvariantCulture).Replace(',', '٬'));

    public static string Money(decimal value) => $"{Number(value)} ریال";

    public static string GregorianDate(DateTime value) => LocalizeDigits(value.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture));

    public static string GregorianShortDate(DateTime value) => LocalizeDigits(value.ToString("MM/dd", CultureInfo.InvariantCulture));

    public static string CompactMillions(decimal value)
    {
        var millions = value / 1_000_000m;
        return $"{Number(decimal.Round(millions, 0, MidpointRounding.AwayFromZero))} م";
    }

    private static string LocalizeDigits(string value)
    {
        return string.Concat(value.Select(character => character is >= '0' and <= '9'
            ? (char)('۰' + character - '0')
            : character));
    }
}
