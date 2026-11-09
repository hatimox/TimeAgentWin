namespace TimeAgent;

/// Morocco civil holidays + the day-off check used to skip recurring auto-log.
public static class Holidays
{
    private static readonly (int m, int d)[] FixedCivil =
    {
        (1, 1), (1, 11), (5, 1), (7, 30), (8, 14), (8, 20), (8, 21), (11, 6), (11, 18),
    };

    /// Religious holiday ESTIMATES by year (YYYY-MM-DD). Moon-sighting dependent.
    /// Slot keys are "year|name|index", so keep the order stable.
    private static readonly Dictionary<int, (string name, string[] dates)[]> Religious = new()
    {
        [2026] = new[]
        {
            ("Eid al-Fitr", new[] { "2026-03-20", "2026-03-21" }),
            ("Eid al-Adha", new[] { "2026-05-26", "2026-05-27" }),
            ("Islamic New Year", new[] { "2026-06-16" }),
            ("Prophet's Birthday", new[] { "2026-08-25", "2026-08-26" }),
        },
        [2027] = new[]
        {
            ("Eid al-Fitr", new[] { "2027-03-09", "2027-03-10", "2027-03-11" }),
            ("Eid al-Adha", new[] { "2027-05-16", "2027-05-17" }),
            ("Islamic New Year", new[] { "2027-06-06" }),
            ("Prophet's Birthday", new[] { "2027-08-15" }),
        },
        [2028] = new[]
        {
            ("Eid al-Fitr", new[] { "2028-02-26", "2028-02-27" }),
            ("Eid al-Adha", new[] { "2028-05-05", "2028-05-06" }),
            ("Islamic New Year", new[] { "2028-05-25" }),
            ("Prophet's Birthday", new[] { "2028-08-03" }),
        },
    };

    /// Fixed civil holiday dates (YYYY-MM-DD) for a year.
    public static List<string> FixedHolidays(int year) =>
        FixedCivil.Select(x => new DateOnly(year, x.m, x.d).ToString("yyyy-MM-dd")).ToList();

    /// One religious-holiday day: settings slot key ("year|name|idx"), label, estimated date.
    public record ReligiousDay(string Key, string Label, string Estimate);

    /// Every estimated religious-holiday day of a year, flattened in order.
    public static List<ReligiousDay> ReligiousDays(int year)
    {
        var list = new List<ReligiousDay>();
        if (!Religious.TryGetValue(year, out var hs)) return list;
        foreach (var h in hs)
            for (int i = 0; i < h.dates.Length; i++)
                list.Add(new ReligiousDay($"{year}|{h.name}|{i}",
                    h.name + (h.dates.Length > 1 ? $" (day {i + 1})" : ""), h.dates[i]));
        return list;
    }

    public static HashSet<string> DaysOffForYear(Settings s, int year)
    {
        var set = new HashSet<string>(s.DaysOff);
        var slots = new Dictionary<string, ReligiousSlot>();
        foreach (var sl in s.ReligiousSlots) slots.TryAdd(sl.Key, sl);
        if (s.Region == "morocco")
        {
            foreach (var d in FixedHolidays(year)) set.Add(d);
            foreach (var d in ReligiousDays(year))
            {
                var has = slots.TryGetValue(d.Key, out var sl);
                var date = has && !string.IsNullOrEmpty(sl!.Date) ? sl.Date : d.Estimate;
                if (!has || sl!.On) set.Add(date);
            }
        }
        else
        {
            foreach (var sl in s.ReligiousSlots)
                if (sl.On && !string.IsNullOrEmpty(sl.Date)) set.Add(sl.Date);
        }
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
