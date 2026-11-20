using System.Globalization;

namespace MeelanoExecutiveConsole.Services;

/// <summary>A value in a CSV row; numeric values are not escaped as spreadsheet formulas.</summary>
public readonly record struct CsvCell(string Value, bool IsNumeric = false)
{
    public static CsvCell Text(string? value) => new(value ?? string.Empty);

    public static CsvCell Number(decimal value) => new(value.ToString(CultureInfo.InvariantCulture), IsNumeric: true);

    public static CsvCell Number(int value) => new(value.ToString(CultureInfo.InvariantCulture), IsNumeric: true);
}
