using MeelanoExecutiveConsole.Models;

namespace MeelanoExecutiveConsole.Services;

public static class CheckAlertService
{
    public sealed record Alert(CheckItem Check, int Days)
    {
        public string Number => Check.Number;
        public string Party => Check.Party;
        public string Kind => Check.Kind.ToString();
        public string DueDate => Check.DueDateText;
        public string Amount => Check.AmountText;
        public string Status => Days < 0 ? $"{PersianFormatting.Number(-Days)} روز گذشته" : Days == 0 ? "امروز" : $"{PersianFormatting.Number(Days)} روز آینده";
    }

    public static IReadOnlyList<Alert> Create(IEnumerable<CheckItem> checks, DateTime asOf, int horizonDays)
    {
        ArgumentNullException.ThrowIfNull(checks);
        if (horizonDays is < 0 or > 90) throw new ArgumentOutOfRangeException(nameof(horizonDays));
        asOf = asOf.Date;
        return checks.Where(c => c.Status == CheckStatuses.Pending)
            .Select(c => new Alert(c, (c.DueDate.Date - asOf).Days))
            .Where(a => a.Days <= horizonDays)
            .OrderBy(a => a.Days).ThenBy(a => a.Number, StringComparer.Ordinal).ToArray();
    }
}
