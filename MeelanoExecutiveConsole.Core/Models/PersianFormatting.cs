using System.Globalization;

namespace MeelanoExecutiveConsole.Models;

/// <summary>Persian-digit, Toman and Solar Hijri display helpers.</summary>
public static class PersianFormatting
{
    private static readonly PersianCalendar Calendar = new();

    public static string Number(decimal value) =>
        LocalizeDigits(value.ToString("N0", CultureInfo.InvariantCulture).Replace(',', '٬'));

    public static string Number(int value) =>
        LocalizeDigits(value.ToString("N0", CultureInfo.InvariantCulture).Replace(',', '٬'));

    public static string Money(decimal value) => $"{Number(value)} تومان";

    /// <summary>Displays the full decimal value without rounding fractional tomans.</summary>
    public static string MoneyExact(decimal value)
    {
        var text = value.ToString("0.############################", CultureInfo.InvariantCulture).Replace('.', '٫');
        return $"{LocalizeDigits(text)} تومان";
    }

    public static string PersianDate(DateTime value)
    {
        if (value < Calendar.MinSupportedDateTime || value > Calendar.MaxSupportedDateTime)
            return "—";

        return LocalizeDigits(string.Create(CultureInfo.InvariantCulture,
            $"{Calendar.GetYear(value):0000}/{Calendar.GetMonth(value):00}/{Calendar.GetDayOfMonth(value):00}"));
    }

    public static string PersianShortDate(DateTime value)
    {
        if (value < Calendar.MinSupportedDateTime || value > Calendar.MaxSupportedDateTime)
            return "—";

        return LocalizeDigits(string.Create(CultureInfo.InvariantCulture,
            $"{Calendar.GetMonth(value):00}/{Calendar.GetDayOfMonth(value):00}"));
    }

    public static string CompactMillions(decimal value)
    {
        var rounded = decimal.Round(value / 1_000_000m, 1, MidpointRounding.AwayFromZero);
        var format = rounded == decimal.Truncate(rounded) ? "0" : "0.0";
        var localized = rounded.ToString(format, CultureInfo.InvariantCulture).Replace('.', '٫');
        return $"{LocalizeDigits(localized)} م";
    }

    private static string LocalizeDigits(string value) =>
        string.Concat(value.Select(character => character is >= '0' and <= '9'
            ? (char)('۰' + character - '0')
            : character));
}
