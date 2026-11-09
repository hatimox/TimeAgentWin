using System.Globalization;

namespace TimeAgent;

/// Day / week helpers for the steppers. Offsets are 0 = current period, -1 =
/// previous, ...; "today" is taken in the configured timezone so it lines up
/// with the offset-aware day strings on time entries.
public static class Totals
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static DateOnly Today(int tzOffMin) => DateOnly.FromDateTime(DateTime.UtcNow.AddMinutes(tzOffMin));
    public static DateOnly DayAt(int dayOffset, int tzOffMin) => Today(tzOffMin).AddDays(dayOffset);

    /// Monday-start week, shifted by whole weeks.
    public static DateOnly WeekStart(int weekOffset, int tzOffMin)
    {
        var t = Today(tzOffMin);
        return t.AddDays(-(((int)t.DayOfWeek + 6) % 7) + 7 * weekOffset);
    }

    public static string Key(DateOnly d) => d.ToString("yyyy-MM-dd", Inv);

    public static double HoursOn(IEnumerable<TimeEntry> times, DateOnly day)
    {
        var k = Key(day);
        return times.Where(t => t.Day == k).Sum(t => t.Hours);
    }

    /// Closed range start..start+6.
    public static double HoursInWeek(IEnumerable<TimeEntry> times, DateOnly start)
    {
        var a = Key(start); var b = Key(start.AddDays(6));
        return times.Where(t => string.CompareOrdinal(t.Day, a) >= 0 && string.CompareOrdinal(t.Day, b) <= 0).Sum(t => t.Hours);
    }

    public static string DayLabel(int offset, int tzOffMin) =>
        offset == 0 ? "Today" : offset == -1 ? "Yesterday" : DayAt(offset, tzOffMin).ToString("ddd d MMM", Inv);

    public static string WeekLabel(int offset, int tzOffMin)
    {
        if (offset == 0) return "This week";
        if (offset == -1) return "Last week";
        var s = WeekStart(offset, tzOffMin);
        return $"{s.ToString("d MMM", Inv)} – {s.AddDays(6).ToString("d MMM", Inv)}";
    }

    /// e.g. "Today", "Yesterday", "Friday 6 November 2026".
    public static string LongDayLabel(int offset, int tzOffMin) =>
        offset == 0 ? "Today" : offset == -1 ? "Yesterday" : DayAt(offset, tzOffMin).ToString("dddd d MMMM yyyy", Inv);
}
