namespace TimeAgent;

/// Morocco civil holidays + the day-off check used to skip recurring auto-log.
public static class Holidays
{
    private static readonly (int m, int d)[] FixedCivil =
    {
        (1, 1), (1, 11), (5, 1), (7, 30), (8, 14), (8, 20), (8, 21), (11, 6), (11, 18),
    };

    public static HashSet<string> DaysOffForYear(Settings s, int year)
    {
        var set = new HashSet<string>(s.DaysOff);
        if (s.Region == "morocco")
            foreach (var (m, d) in FixedCivil)
                set.Add(new DateOnly(year, m, d).ToString("yyyy-MM-dd"));
        return set;
    }

    public static bool IsDayOff(Settings s, DateOnly date)
    {
        // weeklyOff: 0=Sun … 6=Sat
        int dow = (int)date.DayOfWeek; // Sunday = 0 in .NET, matches
        if (s.WeeklyOff.Contains(dow)) return true;
        return DaysOffForYear(s, date.Year).Contains(date.ToString("yyyy-MM-dd"));
    }
}
