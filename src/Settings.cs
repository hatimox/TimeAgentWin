using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TimeAgent;

/// Settings persisted as JSON under %APPDATA%\TimeAgent\settings.json (the same
/// place the Electron app used on Windows). The token is NOT in the JSON; it's
/// stored separately, encrypted per-user with DPAPI.
public class Settings
{
    [JsonPropertyName("tpURL")] public string TpUrl { get; set; } = "";
    [JsonPropertyName("myUserId")] public long MyUserId { get; set; }
    [JsonPropertyName("myUserName")] public string MyUserName { get; set; } = "";
    [JsonPropertyName("myUserEmail")] public string MyUserEmail { get; set; } = "";
    [JsonPropertyName("timezone")] public string Timezone { get; set; } = TimeZoneInfo.Local.Id;
    [JsonPropertyName("dailyTaskId")] public long DailyTaskId { get; set; }
    [JsonPropertyName("meetingsTaskId")] public long MeetingsTaskId { get; set; }
    [JsonPropertyName("meetingMinMinutes")] public int MeetingMinMinutes { get; set; } = 30;
    [JsonPropertyName("meetingStepMinutes")] public int MeetingStepMinutes { get; set; } = 15;
    [JsonPropertyName("recurring")] public List<RecurringEntry> Recurring { get; set; } = new();
    [JsonPropertyName("dynamicMeetings")] public List<DynamicMeeting> DynamicMeetings { get; set; } = new();
    [JsonPropertyName("daysOff")] public List<string> DaysOff { get; set; } = new();
    [JsonPropertyName("weeklyOff")] public List<int> WeeklyOff { get; set; } = new() { 0, 6 };
    [JsonPropertyName("region")] public string Region { get; set; } = "none";

    [JsonIgnore] public string Token { get; set; } = "";

    public static string Dir
    {
        get
        {
            var d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TimeAgent");
            Directory.CreateDirectory(d);
            return d;
        }
    }
    private static string File => Path.Combine(Dir, "settings.json");
    private static string TokenFile => Path.Combine(Dir, "token.bin");

    [JsonIgnore] public bool IsConfigured => !string.IsNullOrEmpty(Token) && TpUrl.StartsWith("http");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static Settings Load()
    {
        Settings s = new();
        try
        {
            if (System.IO.File.Exists(File))
                s = JsonSerializer.Deserialize<Settings>(System.IO.File.ReadAllText(File)) ?? new();
        }
        catch { /* keep defaults */ }
        s.Token = ReadToken();
        return s;
    }

    public void Save()
    {
        WriteToken(Token);
        try { System.IO.File.WriteAllText(File, JsonSerializer.Serialize(this, JsonOpts)); }
        catch { }
    }

    // DPAPI per-user encryption for the token at rest.
    private static string ReadToken()
    {
        try
        {
            if (!System.IO.File.Exists(TokenFile)) return "";
            var enc = System.IO.File.ReadAllBytes(TokenFile);
            var raw = ProtectedData.Unprotect(enc, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(raw).Trim();
        }
        catch { return ""; }
    }
    private static void WriteToken(string token)
    {
        try
        {
            if (string.IsNullOrEmpty(token))
            {
                if (System.IO.File.Exists(TokenFile)) System.IO.File.Delete(TokenFile);
                return;
            }
            var enc = ProtectedData.Protect(Encoding.UTF8.GetBytes(token), null, DataProtectionScope.CurrentUser);
            System.IO.File.WriteAllBytes(TokenFile, enc);
        }
        catch { }
    }

    /// Minutes east of UTC for the configured timezone (TP date anchoring).
    public int TzOffsetMinutes()
    {
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(Timezone);
            return (int)tz.GetUtcOffset(DateTimeOffset.UtcNow).TotalMinutes;
        }
        catch { return (int)TimeZoneInfo.Local.GetUtcOffset(DateTimeOffset.UtcNow).TotalMinutes; }
    }
}
