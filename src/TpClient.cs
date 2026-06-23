using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace TimeAgent;

/// TargetProcess REST client — faithful port of the Electron tpclient.js:
/// noon-anchored writes, offset-aware day bucketing, manual %20 query encoding,
/// skip-based pagination (TP caps a page at 1000).
public class TpClient
{
    private readonly string _baseUrl;
    private readonly string _token;
    public long MyUserId;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(25) };

    public TpClient(string baseUrl, string token, long myUserId)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _token = token;
        MyUserId = myUserId;
    }

    // %20 for spaces (TP rejects '+').
    private static string Enc(string s)
    {
        var sb = new StringBuilder();
        foreach (var b in Encoding.UTF8.GetBytes(s))
        {
            if (b is (>= (byte)'A' and <= (byte)'Z') or (>= (byte)'a' and <= (byte)'z')
                or (>= (byte)'0' and <= (byte)'9') or (byte)'-' or (byte)'_' or (byte)'.' or (byte)'~')
                sb.Append((char)b);
            else sb.Append('%').Append(b.ToString("X2"));
        }
        return sb.ToString();
    }

    private string Url(string path, IEnumerable<(string, string)> query)
    {
        var ps = new List<(string, string)> { ("format", "json"), ("access_token", _token) };
        ps.AddRange(query);
        var qs = string.Join("&", ps.Select(p => $"{Enc(p.Item1)}={Enc(p.Item2)}"));
        return $"{_baseUrl}/api/v1/{path}?{qs}";
    }

    private async Task<JsonElement> Get(string path, params (string, string)[] query)
    {
        using var resp = await Http.GetAsync(Url(path, query));
        return await Parse(resp);
    }
    private async Task<JsonElement> Post(string path, object body)
    {
        var json = JsonSerializer.Serialize(body);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var resp = await Http.PostAsync(Url(path, Array.Empty<(string, string)>()), content);
        return await Parse(resp);
    }
    private async Task Delete(string path)
    {
        using var resp = await Http.DeleteAsync(Url(path, Array.Empty<(string, string)>()));
        if (!resp.IsSuccessStatusCode) throw new Exception($"HTTP {(int)resp.StatusCode}");
    }

    private static async Task<JsonElement> Parse(HttpResponseMessage resp)
    {
        var text = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
            throw new Exception($"HTTP {(int)resp.StatusCode}: {text[..Math.Min(200, text.Length)]}");
        if (string.IsNullOrEmpty(text)) return JsonDocument.Parse("{}").RootElement;
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private async Task<List<JsonElement>> GetAll(string path, params (string, string)[] query)
    {
        const int take = 1000;
        var items = new List<JsonElement>();
        int skip = 0;
        while (true)
        {
            var q = query.Concat(new[] { ("take", "1000"), ("skip", skip.ToString()) }).ToArray();
            var obj = await Get(path, q);
            var batch = obj.TryGetProperty("Items", out var arr) && arr.ValueKind == JsonValueKind.Array
                ? arr.EnumerateArray().Select(e => e.Clone()).ToList()
                : new List<JsonElement>();
            items.AddRange(batch);
            if (batch.Count < take || batch.Count == 0) break;
            skip += take;
        }
        return items;
    }

    // ---- identity ----
    public async Task<(long id, string name, string email)> WhoAmI()
    {
        var obj = await Get("Context");
        var u = obj.GetProperty("LoggedUser");
        var id = u.GetProperty("Id").GetInt64();
        var name = $"{Str(u, "FirstName")} {Str(u, "LastName")}".Trim();
        return (id, name, Str(u, "Email"));
    }

    public async Task<List<WorkItem>> FetchAllAssigned(bool currentSprintOnly)
    {
        var tasks = FetchCollection("Tasks", currentSprintOnly);
        var bugs = FetchCollection("Bugs", currentSprintOnly);
        await Task.WhenAll(tasks, bugs);
        var all = tasks.Result.Concat(bugs.Result).ToList();
        all.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return all;
    }

    private async Task<List<WorkItem>> FetchCollection(string collection, bool currentSprintOnly)
    {
        var where = $"AssignedUser.Id eq {MyUserId}";
        if (currentSprintOnly) where += " and (TeamIteration.IsCurrent eq 'true')";
        var items = await GetAll(collection,
            ("where", where),
            ("include", "[Id,Name,EntityState[Id,Name,IsFinal],Project[Name,Process[Id]],TeamIteration[Name],UserStory[Id,Name]]"));
        var outv = new List<WorkItem>();
        foreach (var it in items)
        {
            if (!it.TryGetProperty("Id", out var idEl)) continue;
            var es = Obj(it, "EntityState");
            var project = Obj(it, "Project");
            var process = Obj(project, "Process");
            var us = Obj(it, "UserStory");
            outv.Add(new WorkItem
            {
                Id = idEl.GetInt64(),
                Name = Str(it, "Name"),
                EntityType = collection,
                DisplayType = collection == "Bugs" ? "Bug" : "Task",
                StateId = Long(es, "Id"),
                StateName = Str(es, "Name", "?"),
                IsFinal = Bool(es, "IsFinal"),
                ProjectName = Str(project, "Name"),
                ProcessId = Long(process, "Id"),
                Sprint = Str(Obj(it, "TeamIteration"), "Name"),
                UsId = Long(us, "Id"),
                UsName = Str(us, "Name"),
            });
        }
        return outv;
    }

    public async Task<Dictionary<string, List<WorkflowState>>> FetchProcessStates(long processId)
    {
        var outv = new Dictionary<string, List<WorkflowState>> { ["Task"] = new(), ["Bug"] = new() };
        if (processId == 0) return outv;
        foreach (var etype in new[] { "Task", "Bug" })
        {
            JsonElement obj;
            try
            {
                obj = await Get("EntityStates",
                    ("where", $"(Process.Id eq {processId}) and (EntityType.Name eq '{etype}')"),
                    ("include", "[Id,Name,NumericPriority,IsFinal]"), ("take", "200"));
            }
            catch { continue; }
            var list = new List<WorkflowState>();
            if (obj.TryGetProperty("Items", out var arr))
                foreach (var s in arr.EnumerateArray())
                    if (s.TryGetProperty("Id", out var sid))
                        list.Add(new WorkflowState { Id = sid.GetInt64(), Name = Str(s, "Name"), IsFinal = Bool(s, "IsFinal"), Priority = Dbl(s, "NumericPriority") });
            list.Sort((a, b) => a.Priority != b.Priority ? a.Priority.CompareTo(b.Priority) : string.Compare(a.Name, b.Name, StringComparison.Ordinal));
            outv[etype] = list;
        }
        return outv;
    }

    public async Task<(string name, bool isFinal)> SetEntityState(string entityType, long entityId, long stateId)
    {
        var resp = await Post($"{entityType}/{entityId}", new { EntityState = new { Id = stateId } });
        var es = Obj(resp, "EntityState");
        return (Str(es, "Name", "?"), Bool(es, "IsFinal"));
    }

    public async Task<List<TimeEntry>> FetchMyTimes()
    {
        var items = await GetAll("Times", ("where", $"User.Id eq {MyUserId}"), ("include", "[Id,Spent,Date,Description,Assignable[Id]]"));
        var outv = new List<TimeEntry>();
        foreach (var t in items)
        {
            if (!t.TryGetProperty("Id", out var idEl)) continue;
            var day = TpDay(Str(t, "Date"));
            if (day == null) continue;
            outv.Add(new TimeEntry
            {
                Id = idEl.GetInt64(),
                ItemId = Long(Obj(t, "Assignable"), "Id"),
                Hours = Dbl(t, "Spent"),
                Day = day,
                Description = Str(t, "Description"),
            });
        }
        return outv;
    }

    public async Task<long> LogTime(long entityId, double hours, string description, DateOnly date, int tzOffMin)
    {
        var body = new Dictionary<string, object>
        {
            ["Spent"] = hours,
            ["Remain"] = 0,
            ["Date"] = DateString(date, tzOffMin),
            ["Assignable"] = new { Id = entityId },
        };
        var d = description.Trim();
        if (d.Length > 0) body["Description"] = d;
        var resp = await Post("Times", body);
        return resp.TryGetProperty("Id", out var id) ? id.GetInt64() : throw new Exception("Time entry not created");
    }

    public async Task UpdateTime(long timeId, double? hours, string? description, DateOnly? date, int tzOffMin)
    {
        var body = new Dictionary<string, object>();
        if (hours.HasValue) body["Spent"] = hours.Value;
        if (description != null) body["Description"] = description;
        if (date.HasValue) body["Date"] = DateString(date.Value, tzOffMin);
        await Post($"Times/{timeId}", body);
    }

    public Task DeleteTime(long timeId) => Delete($"Times/{timeId}");

    // ---- date helpers (must match the Electron noon-anchor logic) ----

    /// "/Date(ms±HHMM)/" anchored to noon on the target day at the given offset.
    public static string DateString(DateOnly date, int offMin)
    {
        // noon local at the offset, as a UTC instant
        var noonUtc = new DateTime(date.Year, date.Month, date.Day, 12, 0, 0, DateTimeKind.Utc);
        var ms = (long)(noonUtc - DateTime.UnixEpoch).TotalMilliseconds - (long)offMin * 60_000;
        var sign = offMin >= 0 ? "+" : "-";
        var a = Math.Abs(offMin);
        return $"/Date({ms}{sign}{a / 60:D2}{a % 60:D2})/";
    }

    /// Parse "/Date(ms±HHMM)/" → "YYYY-MM-DD" using the embedded offset.
    public static string? TpDay(string? s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        var m = System.Text.RegularExpressions.Regex.Match(s, "-?\\d{10,}");
        if (!m.Success || !long.TryParse(m.Value, out var ms)) return null;
        long offSec = 0;
        var om = System.Text.RegularExpressions.Regex.Match(s, "[+-]\\d{4}");
        if (om.Success)
        {
            var o = om.Value;
            var sign = o[0] == '-' ? -1 : 1;
            offSec = sign * (long.Parse(o.Substring(1, 2)) * 3600 + long.Parse(o.Substring(3, 2)) * 60);
        }
        var dt = DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime.AddSeconds(offSec);
        return dt.ToString("yyyy-MM-dd");
    }

    // ---- JSON helpers ----
    private static JsonElement Obj(JsonElement e, string k) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Object ? v : default;
    private static string Str(JsonElement e, string k, string fallback = "") =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? fallback : fallback;
    private static long Long(JsonElement e, string k) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0;
    private static double Dbl(JsonElement e, string k) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
    private static bool Bool(JsonElement e, string k) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(k, out var v) && (v.ValueKind == JsonValueKind.True);
}
