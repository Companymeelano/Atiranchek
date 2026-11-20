using System.Globalization;
using System.Text;

namespace MeelanoExecutiveConsole.Models;

/// <summary>
/// Normalizes common Persian/Arabic keyboard variants so equivalent text remains searchable.
/// </summary>
public static class PersianText
{
    public static string NormalizeForSearch(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var result = new StringBuilder(value.Length);
        var pendingSpace = false;
        foreach (var character in value)
        {
            if (character == '\u0640' || char.GetUnicodeCategory(character) is UnicodeCategory.NonSpacingMark
                or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark)
            {
                continue;
            }

            if (char.IsWhiteSpace(character) || character == '\u200c')
            {
                pendingSpace = result.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                result.Append(' ');
                pendingSpace = false;
            }

            result.Append(character switch
            {
                'ي' or 'ى' => 'ی',
                'ك' => 'ک',
                >= '۰' and <= '۹' => (char)('0' + character - '۰'),
                >= '٠' and <= '٩' => (char)('0' + character - '٠'),
                _ => char.ToLowerInvariant(character)
            });
        }

        return result.ToString();
    }
}
