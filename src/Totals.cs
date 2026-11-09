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

    /// Hours expected on the single day.
    public static double DayTarget(Settings s, DateOnly day) => Target(s, day, 1);

    /// Hours expected over the whole Monday–Sunday week starting at `start`.
    public static double WeekTarget(Settings s, DateOnly start) => Target(s, start, 7);

    /// Hours expected over the whole calendar month containing `anyDayInMonth`.
    public static double MonthTarget(Settings s, DateOnly anyDayInMonth) =>
        Target(s, new DateOnly(anyDayInMonth.Year, anyDayInMonth.Month, 1), DateTime.DaysInMonth(anyDayInMonth.Year, anyDayInMonth.Month));

    /// hours-per-day × working days in start..start+days-1. A working day is not
    /// a weekly day off (0=Sun…6=Sat), user day off or holiday. The days-off set
    /// is cached per year for the duration of the call.
    private static double Target(Settings s, DateOnly start, int days)
    {
        var perDay = Math.Max(0, s.HoursPerDay);
        var offByYear = new Dictionary<int, HashSet<string>>();
        int working = 0;
        for (int i = 0; i < days; i++)
        {
            var d = start.AddDays(i);
            if (s.WeeklyOff.Contains((int)d.DayOfWeek)) continue;
            if (!offByYear.TryGetValue(d.Year, out var off))
                offByYear[d.Year] = off = Holidays.DaysOffForYear(s, d.Year);
            if (off.Contains(Key(d))) continue;
            working++;
        }
        return perDay * working;
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
