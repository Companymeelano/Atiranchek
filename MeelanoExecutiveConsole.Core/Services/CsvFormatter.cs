using System.Text;

namespace MeelanoExecutiveConsole.Services;

/// <summary>RFC 4180-style CSV formatting with spreadsheet-formula protection for text cells.</summary>
public static class CsvFormatter
{
    public static string Build(IEnumerable<string?> headers, IEnumerable<IEnumerable<CsvCell>> rows)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(rows);

        var builder = new StringBuilder();
        AppendRow(builder, headers.Select(header => new CsvCell(header ?? string.Empty)));
        foreach (var row in rows)
        {
            ArgumentNullException.ThrowIfNull(row);
            AppendRow(builder, row);
        }

        return builder.ToString();
    }

    private static void AppendRow(StringBuilder builder, IEnumerable<CsvCell> cells)
    {
        var first = true;
        foreach (var cell in cells)
        {
            if (!first)
                builder.Append(',');
            first = false;
            builder.Append(Escape(cell.Value ?? string.Empty, cell.IsNumeric));
        }

        builder.Append("\r\n");
    }

    private static string Escape(string value, bool isNumeric)
    {
        if (!isNumeric && StartsWithSpreadsheetFormula(value))
            value = $"'{value}";

        var needsQuotes = value.IndexOfAny([',', '"', '\r', '\n']) >= 0
                          || (value.Length > 0 && (char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[^1])));
        return needsQuotes ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }

    private static bool StartsWithSpreadsheetFormula(string value)
    {
        var index = 0;
        while (index < value.Length && char.IsWhiteSpace(value[index]))
            index++;

        return index < value.Length && value[index] is '=' or '+' or '-' or '@';
    }
}
