// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.IO;
using System.Text;
using System.Text.Json;

namespace Shima.Agents;

/// <summary>四種 token 的數量。CacheWrite 是 5 分鐘與 1 小時快取寫入的合計。</summary>
public readonly record struct TokenCount(long Input, long Output, long CacheWrite, long CacheRead)
{
    public long Total => Input + Output + CacheWrite + CacheRead;
    public static TokenCount operator +(TokenCount a, TokenCount b) =>
        new(a.Input + b.Input, a.Output + b.Output, a.CacheWrite + b.CacheWrite, a.CacheRead + b.CacheRead);
}

/// <summary>某個模型今天用了多少；Cost 是 null 表示價目表裡沒有這個模型。</summary>
public sealed record ModelUsage(string Model, TokenCount Tokens, decimal? Cost);

/// <summary>
/// 一個工作階段的小計：Today 只算今天的，Tokens 是這個工作階段讀得到的全部（跨日也算）。
/// CostKnown 是 false 表示裡面有認不得的模型，花費只算了認得的部分。
/// </summary>
public sealed record SessionUsage(string SessionId, string Project, TokenCount Today, decimal TodayCost, TokenCount Tokens, decimal Cost, bool CostKnown, DateTime LastUsed);

/// <summary>今天的用量：各模型、各工作階段（今天有用到的，最近的在前面）。</summary>
public sealed record UsageSummary(DateOnly Day, IReadOnlyList<ModelUsage> Models, IReadOnlyList<SessionUsage> Sessions, TokenCount Tokens, decimal Cost, bool CostKnown);

/// <summary>某一天（本地時間）的用量；CostKnown 是 false 表示當天有價目表裡沒有的模型，Cost 只算了認得的部分。</summary>
public sealed record DayUsage(DateOnly Day, TokenCount Tokens, decimal Cost, bool CostKnown);

/// <summary>最近幾天的趨勢：Days 由舊到新、每天一筆（沒用的日子補 0），Models 是這段期間各模型的合計（token 多的在前面）。</summary>
public sealed record UsageTrend(IReadOnlyList<DayUsage> Days, IReadOnlyList<ModelUsage> Models, TokenCount Tokens, decimal Cost, bool CostKnown)
{
    public static readonly UsageTrend Empty = new([], [], default, 0, true);
}

/// <summary>每百萬 token 的美元價格。Long：提示超過 LongAbove 個 token 的請求改用的價格（Haiku 5.5）。</summary>
public sealed record TokenPrice(decimal Input, decimal Write5m, decimal Write1h, decimal Read, decimal Output, long LongAbove = 0, TokenPrice? Long = null);

/// <summary>
/// 估計花費用的價目表。數字抄自 Anthropic 官方定價頁 https://platform.claude.com/docs/en/about-claude/pricing
/// 與模型清單 https://platform.claude.com/docs/en/about-claude/models/overview ，查證日期 2026-11-23。
/// 價格變了就改這張表；表裡沒有的模型只顯示 token、不估價。
/// 不含：Batch 折扣、伺服器端工具（網頁搜尋等）的另計費用、各雲端平台自己的價格。
/// Fast mode（usage.speed = "fast"）是兩倍；只在美國推論（inference_geo = "us"）是 1.1 倍。
/// </summary>
public static class AgentPrices
{
    private static TokenPrice P(decimal input, decimal write5m, decimal write1h, decimal read, decimal output) => new(input, write5m, write1h, read, output);

    public static readonly Dictionary<string, TokenPrice> Table = new(StringComparer.OrdinalIgnoreCase)
    {
        ["claude-fable-5-2"] = P(8, 10, 16, 0.20m, 40),
        ["claude-mythos-5-2"] = P(8, 10, 16, 0.20m, 40),
        ["claude-fable-5-1"] = P(10, 12.50m, 20, 0.25m, 50),
        ["claude-mythos-5-1"] = P(10, 12.50m, 20, 0.25m, 50),
        ["claude-fable-5"] = P(10, 12.50m, 20, 1, 50),
        ["claude-mythos-5"] = P(10, 12.50m, 20, 1, 50),
        ["claude-opus-5-5"] = P(4, 5, 8, 0.20m, 20),
        ["claude-opus-5"] = P(5, 6.25m, 10, 0.50m, 25),
        ["claude-opus-4-8"] = P(5, 6.25m, 10, 0.50m, 25),
        ["claude-opus-4-7"] = P(5, 6.25m, 10, 0.50m, 25),
        ["claude-opus-4-6"] = P(5, 6.25m, 10, 0.50m, 25),
        ["claude-opus-4-5"] = P(5, 6.25m, 10, 0.50m, 25),
        ["claude-opus-4-1"] = P(15, 18.75m, 30, 1.50m, 75),
        ["claude-opus-4"] = P(15, 18.75m, 30, 1.50m, 75),
        ["claude-sonnet-5-5"] = P(2, 2.50m, 4, 0.10m, 10),
        ["claude-sonnet-5"] = P(2, 2.50m, 4, 0.20m, 10),
        ["claude-sonnet-4-6"] = P(3, 3.75m, 6, 0.30m, 15),
        ["claude-sonnet-4-5"] = P(3, 3.75m, 6, 0.30m, 15),
        ["claude-sonnet-4"] = P(3, 3.75m, 6, 0.30m, 15),
        ["claude-haiku-5-5"] = new(0.10m, 0.125m, 0.20m, 0.01m, 0.50m, 100_000, P(0.50m, 0.625m, 1, 0.05m, 2.50m)),
        ["claude-haiku-4-5"] = P(1, 1.25m, 2, 0.10m, 5),
    };

    /// <summary>
    /// 模型 id 換成價目表的名字：去掉雲端平台的前綴（anthropic.、us.anthropic.…）、日期（-20251101、@20251101）、
    /// Bedrock 的版本（-v1:0）、Claude Code 的 [1m] 之類的標記。
    /// </summary>
    public static string Canonical(string model)
    {
        var m = model.Trim().ToLowerInvariant();
        int bracket = m.IndexOf('[');
        if (bracket >= 0) m = m[..bracket];
        int at = m.IndexOf('@');
        if (at >= 0) m = m[..at];
        int claude = m.IndexOf("claude-", StringComparison.Ordinal);
        if (claude > 0) m = m[claude..];
        int colon = m.IndexOf(':');
        if (colon >= 0) m = m[..colon];
        if (System.Text.RegularExpressions.Regex.Match(m, @"-v\d+$") is { Success: true } v) m = m[..v.Index];
        if (System.Text.RegularExpressions.Regex.Match(m, @"-\d{8}$") is { Success: true } date) m = m[..date.Index];
        return m;
    }

    public static TokenPrice? Of(string model) => Table.TryGetValue(Canonical(model), out var p) ? p : null;

    /// <summary>一次請求的花費；認不得的模型回傳 null。</summary>
    public static decimal? Cost(string model, long input, long output, long write5m, long write1h, long read, string? speed = null, string? geo = null)
    {
        if (Of(model) is not { } price) return null;
        if (price.Long != null && input + write5m + write1h + read > price.LongAbove) price = price.Long;
        decimal cost = (input * price.Input + write5m * price.Write5m + write1h * price.Write1h + read * price.Read + output * price.Output) / 1_000_000m;
        if (speed == "fast") cost *= 2;
        if (geo == "us") cost *= 1.1m;
        return cost;
    }
}

/// <summary>
/// 對話紀錄（JSONL）裡的用量帳本。每一行丟進來，依 message id 去重（同一則訊息串流時會寫好幾行），
/// 同一則取每個欄位最大的那次。純計算，不碰檔案。
/// </summary>
public sealed class UsageLedger
{
    private sealed class Entry
    {
        public string Session = "", Project = "", Model = "";
        private static readonly List<(string Model, long Input, long Output, long Write5m, long Write1h, long Read)> NoExtra = [];
        public string? Speed, Geo;
        /// <summary>UTC。紀錄裡沒有 timestamp（或看不懂）時維持 default，見 Summarize 的說明。</summary>
        public DateTime Time;
        public long Input, Output, Write5m, Write1h, Read;
        /// <summary>模型備援時，先失敗的那個模型另外算的部分（usage.iterations 裡別的模型）。</summary>
        public List<(string Model, long Input, long Output, long Write5m, long Write1h, long Read)> Extra = NoExtra;   // 共用的空清單：絕不就地修改，只整個換掉
    }

    private readonly Dictionary<string, Entry> _entries = [];

    public int Count => _entries.Count;

    /// <summary>早於這個時間（UTC）的紀錄不收：趨勢只看最近幾天，更早的留著只是佔記憶體。沒有時間的紀錄照收。</summary>
    public DateTime? Oldest { get; set; }

    /// <summary>快速判斷這一行值不值得解析：一定要有 "assistant" 和 "usage"。</summary>
    public static bool Worth(ReadOnlySpan<byte> line) =>
        line.IndexOf("\"usage\""u8) >= 0 && line.IndexOf("\"assistant\""u8) >= 0;

    /// <summary>一行紀錄。fallbackSession：紀錄裡沒有 sessionId 時用（檔名）。回傳 true 表示有記到。</summary>
    public bool Feed(ReadOnlySpan<byte> line, string fallbackSession = "")
    {
        if (!Worth(line)) return false;
        try
        {
            using var doc = JsonDocument.Parse(line.ToArray());
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || Str(root, "type") != "assistant") return false;
            if (root.TryGetProperty("isApiErrorMessage", out var error) && error.ValueKind == JsonValueKind.True) return false;
            if (!root.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object) return false;
            if (!message.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object) return false;
            var model = Str(message, "model") ?? "";
            if (model.Length == 0 || model.StartsWith('<')) return false;   // <synthetic>：Claude Code 自己產生的，不是真的呼叫

            var id = Str(message, "id") ?? Str(root, "requestId") ?? Str(root, "uuid");
            if (id == null) return false;
            DateTime? stamp = null;
            if (root.TryGetProperty("timestamp", out var ts) && ts.ValueKind == JsonValueKind.String && ts.TryGetDateTime(out var time))
                stamp = time.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(time, DateTimeKind.Utc) : time.ToUniversalTime();
            if (stamp < Oldest) return false;
            if (!_entries.TryGetValue(id, out var e)) _entries[id] = e = new Entry();
            // 字串共用：同一個模型、工作階段、專案的幾千則不用各配一份
            e.Session = string.Intern(Str(root, "sessionId") is { Length: > 0 } s ? s : e.Session.Length > 0 ? e.Session : fallbackSession);
            if (Str(root, "cwd") is { Length: > 0 } cwd) e.Project = string.Intern(AgentSessions.ProjectOf(cwd));
            e.Model = string.Intern(model);
            if (stamp != null) e.Time = stamp.Value;
            e.Speed = Str(usage, "speed") ?? e.Speed;
            e.Geo = Str(usage, "inference_geo") ?? e.Geo;

            var (input, output, w5, w1, read) = Counts(usage);
            e.Input = Math.Max(e.Input, input);
            e.Output = Math.Max(e.Output, output);
            e.Write5m = Math.Max(e.Write5m, w5);
            e.Write1h = Math.Max(e.Write1h, w1);
            e.Read = Math.Max(e.Read, read);

            // 模型備援：usage 本身是最後成功的那個模型；先前用別的模型跑過的那一段在 iterations 裡
            if (usage.TryGetProperty("iterations", out var iterations) && iterations.ValueKind == JsonValueKind.Array)
            {
                var extra = new List<(string, long, long, long, long, long)>();
                foreach (var it in iterations.EnumerateArray())
                    if (it.ValueKind == JsonValueKind.Object && Str(it, "model") is { Length: > 0 } other && other != model)
                    {
                        var c = Counts(it);
                        extra.Add((other, c.Input, c.Output, c.Write5m, c.Write1h, c.Read));
                    }
                if (extra.Count >= e.Extra.Count) e.Extra = extra;
            }
            return true;
        }
        catch (JsonException) { return false; }
    }

    public bool Feed(string line, string fallbackSession = "") => Feed(Encoding.UTF8.GetBytes(line), fallbackSession);

    private static (long Input, long Output, long Write5m, long Write1h, long Read) Counts(JsonElement usage)
    {
        long input = Num(usage, "input_tokens"), output = Num(usage, "output_tokens"), read = Num(usage, "cache_read_input_tokens");
        long write = Num(usage, "cache_creation_input_tokens"), w5 = write, w1 = 0;
        if (usage.TryGetProperty("cache_creation", out var split) && split.ValueKind == JsonValueKind.Object)
        {
            long a = Num(split, "ephemeral_5m_input_tokens"), b = Num(split, "ephemeral_1h_input_tokens");
            if (a + b > 0) { w5 = a; w1 = b; w5 += Math.Max(0, write - a - b); }
        }
        return (input, output, w5, w1, read);
    }

    private static string? Str(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static long Num(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out long n) && n > 0 ? n : 0;

    /// <summary>拿掉最後一筆早於 before 的工作階段（跨好幾天前的舊帳，免得越記越多）。</summary>
    public void Forget(DateTime before)
    {
        var last = _entries.Values.GroupBy(e => e.Session).ToDictionary(g => g.Key, g => g.Max(e => e.Time));
        foreach (var key in _entries.Where(p => last[p.Value.Session] < before).Select(p => p.Key).ToList()) _entries.Remove(key);
    }

    public void Clear() => _entries.Clear();

    /// <summary>拿掉有時間、而且早於 before 的紀錄（已經收進來、後來超出範圍的）。</summary>
    public void Trim(DateTime before)
    {
        foreach (var key in _entries.Where(p => p.Value.Time != default && p.Value.Time < before).Select(p => p.Key).ToList()) _entries.Remove(key);
    }

    /// <summary>
    /// UTC 時間換成 zone 的日期。沒有時間（default）或貼著 DateTime 上下限的值一律當成「不知道是哪一天」。
    /// .NET 10 的 ConvertTimeFromUtc 遇到超出範圍會夾在 MinValue／MaxValue 而不丟例外（實測過），
    /// 但夾出來的 0001-01-01 只是碰巧不等於今天；這裡明確說「不知道」，不靠這個巧合，也擋住其他平台或版本可能的例外。
    /// </summary>
    public static DateOnly? DayOf(DateTime utc, TimeZoneInfo zone)
    {
        if (utc <= DateTime.MinValue.AddDays(2) || utc >= DateTime.MaxValue.AddDays(-2)) return null;
        try { return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone)); }
        catch (ArgumentException) { return null; }
    }

    /// <summary>
    /// 到 last 為止的 days 天（本地時間）各自的用量，以及這段期間各模型的合計。
    /// 去重、備援模型、價目表都與 Summarize 一樣；沒有時間的紀錄說不出是哪一天，不算；價目表裡沒有的模型花費是「不知道」，不當成 0。
    /// </summary>
    public UsageTrend Trend(DateOnly last, int days, TimeZoneInfo? zone = null)
    {
        zone ??= TimeZoneInfo.Local;
        var first = last.AddDays(1 - days);
        var perDay = new (TokenCount Tokens, decimal Cost, bool Known)[days];
        for (int i = 0; i < days; i++) perDay[i] = (default, 0m, true);
        var models = new Dictionary<string, (TokenCount Tokens, decimal Cost, bool Known)>();
        foreach (var e in _entries.Values)
        {
            if (DayOf(e.Time, zone) is not { } day || day < first || day > last) continue;
            int index = day.DayNumber - first.DayNumber;
            var parts = new List<(string Model, long Input, long Output, long Write5m, long Write1h, long Read)> { (e.Model, e.Input, e.Output, e.Write5m, e.Write1h, e.Read) };
            parts.AddRange(e.Extra);
            foreach (var p in parts)
            {
                var tokens = new TokenCount(p.Input, p.Output, p.Write5m + p.Write1h, p.Read);
                var cost = AgentPrices.Cost(p.Model, p.Input, p.Output, p.Write5m, p.Write1h, p.Read, e.Speed, e.Geo);
                var d = perDay[index];
                perDay[index] = (d.Tokens + tokens, d.Cost + (cost ?? 0), d.Known && cost != null);
                var name = AgentPrices.Of(p.Model) != null ? AgentPrices.Canonical(p.Model) : p.Model;
                var m = models.GetValueOrDefault(name, (default, 0m, true));
                models[name] = (m.Tokens + tokens, m.Cost + (cost ?? 0), m.Known && cost != null);
            }
        }
        var dayList = perDay.Select((d, i) => new DayUsage(first.AddDays(i), d.Tokens, d.Cost, d.Known)).ToList();
        var modelList = models.OrderByDescending(m => m.Value.Tokens.Total).Select(m => new ModelUsage(m.Key, m.Value.Tokens, m.Value.Known ? m.Value.Cost : null)).ToList();
        return new UsageTrend(dayList, modelList, dayList.Aggregate(default(TokenCount), (sum, d) => sum + d.Tokens), dayList.Sum(d => d.Cost), dayList.All(d => d.CostKnown));
    }

    /// <summary>
    /// 某一天（本地時間）的用量。zone 預設是這台電腦的時區。
    /// 不知道時間的紀錄（沒有 timestamp）確實花了 token，所以照樣算進工作階段的總量（SessionUsage.Tokens／Cost）；
    /// 但說不出是不是今天，就不算進「今天」，也不拿來當工作階段最後使用的時間。Forget 也因此會把只剩這種紀錄的工作階段清掉。
    /// </summary>
    public UsageSummary Summarize(DateOnly day, TimeZoneInfo? zone = null)
    {
        zone ??= TimeZoneInfo.Local;
        var models = new Dictionary<string, (TokenCount Tokens, decimal Cost, bool Known)>();
        var sessions = new Dictionary<string, (string Project, TokenCount Today, decimal TodayCost, TokenCount All, decimal AllCost, bool Known, bool Used, DateTime Last)>();
        foreach (var e in _entries.Values)
        {
            var known = DayOf(e.Time, zone);
            bool today = known == day;
            var parts = new List<(string Model, long Input, long Output, long Write5m, long Write1h, long Read)> { (e.Model, e.Input, e.Output, e.Write5m, e.Write1h, e.Read) };
            parts.AddRange(e.Extra);
            var s = sessions.GetValueOrDefault(e.Session, ("", default, 0m, default, 0m, true, false, DateTime.MinValue));
            if (e.Project.Length > 0) s.Project = e.Project;
            if (known != null && e.Time > s.Last) s.Last = e.Time;
            foreach (var p in parts)
            {
                var tokens = new TokenCount(p.Input, p.Output, p.Write5m + p.Write1h, p.Read);
                var cost = AgentPrices.Cost(p.Model, p.Input, p.Output, p.Write5m, p.Write1h, p.Read, e.Speed, e.Geo);
                s.All += tokens;
                s.AllCost += cost ?? 0;
                s.Known &= cost != null;
                if (!today) continue;
                s.Used = true;
                s.Today += tokens;
                s.TodayCost += cost ?? 0;
                var name = AgentPrices.Of(p.Model) != null ? AgentPrices.Canonical(p.Model) : p.Model;
                var m = models.GetValueOrDefault(name, (default, 0m, true));
                models[name] = (m.Tokens + tokens, m.Cost + (cost ?? 0), m.Known && cost != null);
            }
            sessions[e.Session] = s;
        }
        var modelList = models.OrderByDescending(m => m.Value.Tokens.Total).Select(m => new ModelUsage(m.Key, m.Value.Tokens, m.Value.Known ? m.Value.Cost : null)).ToList();
        var sessionList = sessions.Where(s => s.Value.Used).OrderByDescending(s => s.Value.Last)
            .Select(s => new SessionUsage(s.Key, s.Value.Project, s.Value.Today, s.Value.TodayCost, s.Value.All, s.Value.AllCost, s.Value.Known, s.Value.Last)).ToList();
        var total = modelList.Aggregate(default(TokenCount), (sum, m) => sum + m.Tokens);
        return new UsageSummary(day, modelList, sessionList, total, modelList.Sum(m => m.Cost ?? 0), modelList.All(m => m.Cost != null));
    }
}

/// <summary>
/// 讀 Claude Code 的對話紀錄算用量：Windows 的 ~/.claude/projects 與 WSL 的（從 \\wsl.localhost 讀）。
/// 每個檔案記住讀到哪裡，之後只讀新增的部分；只看今天有改過的檔案。全部在背景執行緒做。
/// </summary>
public sealed class AgentUsage
{
    private readonly Func<IReadOnlyList<string>> _folders;
    private readonly Func<DateTime> _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly UsageLedger _ledger = new();
    private readonly Dictionary<string, long> _read = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _watched = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<string>? _resolved;
    private DateOnly _forgotOn;

    // 趨勢另外用一本帳：「今天」那本只留兩天、更新頻繁，不能讓它跟著變大或變慢
    private readonly SemaphoreSlim _trendGate = new(1, 1);
    private readonly UsageLedger _trendLedger = new();
    private readonly Dictionary<string, long> _trendRead = new(StringComparer.OrdinalIgnoreCase);
    private DateOnly _trendForgotOn;
    private UsageTrend? _trend;
    private DateTime _trendAt;

    // 兩本帳在同一次刷新裡各列舉一次的話很浪費（WSL 那邊走網路）：幾秒內共用同一份清單
    private readonly object _listLock = new();
    private List<(string Path, DateTime Written)>? _listing;
    private DateTime _listingSince;
    private long _listingTick;

    /// <param name="claudeFolders">Claude Code 的設定資料夾（裡面有 projects）；null 就用 AgentHooks.ClaudeFolders()（會問一次 WSL）。</param>
    /// <param name="clock">本地時間，測試用。</param>
    public AgentUsage(Func<IReadOnlyList<string>>? claudeFolders = null, Func<DateTime>? clock = null)
    {
        _folders = claudeFolders ?? (() => AgentHooks.ClaudeFolders());
        _clock = clock ?? (() => DateTime.Now);
    }

    private IReadOnlyList<string> Folders() => _resolved ??= _folders();

    /// <summary>
    /// hook 給的 transcript_path：之後一定會讀這個檔（就算它不在掃描的資料夾裡）。
    /// 只記下來、不碰檔案也不問 WSL，所以可以在 UI 執行緒呼叫；路徑在背景讀的時候才換成 Windows 讀得到的。
    /// </summary>
    public void Watch(string transcriptPath)
    {
        if (transcriptPath.Length == 0) return;
        lock (_watched) _watched.Add(transcriptPath);
    }

    /// <summary>圖表收起一陣子之後釋放趨勢帳（再展開時重讀）。</summary>
    public async Task ReleaseTrendAsync()
    {
        await _trendGate.WaitAsync();
        try
        {
            _trendLedger.Clear();
            _trendRead.Clear();
            _trend = null;
            _trendForgotOn = default;
        }
        finally { _trendGate.Release(); }
    }

    public async Task<UsageSummary> TodayAsync(CancellationToken cancel = default)
    {
        await _gate.WaitAsync(cancel);
        try
        {
            return await Task.Run(() =>
            {
                var now = _clock();
                var today = DateOnly.FromDateTime(now);
                if (_forgotOn != today) { _ledger.Forget(now.ToUniversalTime().AddDays(-2)); _forgotOn = today; }
                foreach (var file in Files(now.Date, now))
                {
                    cancel.ThrowIfCancellationRequested();
                    ReadNew(file, _ledger, _read, cancel);
                }
                return _ledger.Summarize(today);
            }, cancel);
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// 最近 days 天的趨勢。只讀這段期間內有改過的檔案、每個檔案只讀新增的部分；一分鐘內再問直接給上次的結果。
    /// 第一次可能要讀很久（紀錄有幾百 MB），全部在背景執行緒。
    /// </summary>
    public async Task<UsageTrend> TrendAsync(int days = 14, CancellationToken cancel = default)
    {
        await _trendGate.WaitAsync(cancel);
        try
        {
            var now = _clock();
            if (_trend != null && _trend.Days.Count == days && now >= _trendAt && now - _trendAt < TimeSpan.FromSeconds(60)
                && _trend.Days[^1].Day == DateOnly.FromDateTime(now)) return _trend;   // 剛過午夜時快取的最後一根其實是昨天
            return _trend = await Task.Run(() =>
            {
                var today = DateOnly.FromDateTime(now);
                var oldest = now.Date.AddDays(1 - days).ToUniversalTime().AddDays(-1);   // 多留一天給時區
                _trendLedger.Oldest = oldest;
                if (_trendForgotOn != today) { _trendLedger.Trim(oldest); _trendForgotOn = today; }
                foreach (var file in Files(now.Date.AddDays(1 - days), now, onlyRecent: true))
                {
                    cancel.ThrowIfCancellationRequested();
                    ReadNew(file, _trendLedger, _trendRead, cancel);
                }
                _trendAt = now;
                return _trendLedger.Trend(today, days);
            }, cancel);
        }
        finally { _trendGate.Release(); }
    }

    /// <summary>某個工作階段的小計（只算今天有用到的工作階段；沒有就是 null）。</summary>
    public async Task<SessionUsage?> SessionAsync(string sessionId, CancellationToken cancel = default) =>
        (await TodayAsync(cancel)).Sessions.FirstOrDefault(s => s.SessionId == sessionId);

    /// <summary>今天（since 之後）改過的紀錄檔，以及 hook 告訴我們的那幾個。子代理的紀錄在 &lt;工作階段&gt;/subagents/ 底下，一起算。</summary>
    private List<string> Files(DateTime since, DateTime now, bool onlyRecent = false)
    {
        var files = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, written) in Listing(since))
            if (written >= since && seen.Add(path)) files.Add(path);
        List<string> watched;
        lock (_watched) watched = [.. _watched];
        var stale = now.AddDays(-16);   // 超過趨勢範圍還沒動過的不必再盯著
        foreach (var w in watched)
        {
            if (Local(w, Folders()) is not { } local) continue;
            DateTime written;
            try { written = File.Exists(local) ? File.GetLastWriteTime(local) : DateTime.MinValue; }
            catch (Exception) { continue; }
            if (written < stale) { lock (_watched) _watched.Remove(w); continue; }
            if (!seen.Add(local) || (onlyRecent && written < since)) continue;
            files.Add(local);
        }
        return files;
    }

    /// <summary>列舉 projects 底下的紀錄檔（路徑與修改時間）。幾秒內、範圍夠大的上一份清單直接沿用。</summary>
    private List<(string Path, DateTime Written)> Listing(DateTime since)
    {
        lock (_listLock)
        {
            if (_listing != null && since >= _listingSince && Environment.TickCount64 - _listingTick < 5000) return _listing;
            var list = new List<(string, DateTime)>();
            foreach (var folder in Folders())
            {
                var projects = Path.Combine(folder, "projects");
                try
                {
                    if (!Directory.Exists(projects)) continue;
                    var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
                    foreach (var file in new DirectoryInfo(projects).EnumerateFiles("*.jsonl", options))
                        if (file.LastWriteTime >= since) list.Add((file.FullName, file.LastWriteTime));
                }
                catch (Exception e) { Log.Write("usage: " + e.GetType().Name); }   // 訊息裡有含專案名稱的完整路徑，不寫進紀錄檔
            }
            _listing = list; _listingSince = since; _listingTick = Environment.TickCount64;
            return list;
        }
    }

    /// <summary>從上次讀到的地方讀到最後一個換行為止（最後一行可能還在寫）。檔案變小了就從頭讀。</summary>
    private static void ReadNew(string path, UsageLedger ledger, Dictionary<string, long> read, CancellationToken cancel)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16);
            long start = read.GetValueOrDefault(path);
            if (stream.Length < start) start = 0;
            if (stream.Length == start) return;
            stream.Position = start;
            var session = Path.GetFileNameWithoutExtension(path);
            read[path] = start + Lines(stream, line => ledger.Feed(line, session), cancel);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Log.Write("usage: " + e.GetType().Name); }   // 訊息有完整路徑（含專案名稱）
    }

    public delegate void LineHandler(ReadOnlySpan<byte> line);

    /// <summary>一行一行交給 handle（不含換行）；回傳用掉的位元組數（到最後一個換行為止，沒寫完的最後一行留到下次）。</summary>
    public static long Lines(Stream stream, LineHandler handle, CancellationToken cancel = default)
    {
        var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(1 << 20);   // 1 MB 會進 LOH，每個檔案配一塊太浪費
        try { return LinesIn(stream, buffer, handle, cancel); }
        finally { System.Buffers.ArrayPool<byte>.Shared.Return(buffer); }
    }

    private static long LinesIn(Stream stream, byte[] buffer, LineHandler handle, CancellationToken cancel)
    {
        var carry = new MemoryStream();   // 跨越兩次讀取的那一行
        long used = 0;
        int n;
        while ((n = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancel.ThrowIfCancellationRequested();
            int from = 0;
            while (from < n)
            {
                int newline = Array.IndexOf(buffer, (byte)'\n', from, n - from);
                if (newline < 0) { carry.Write(buffer, from, n - from); break; }
                if (carry.Length == 0)
                {
                    used += newline - from + 1;
                    if (newline > from) handle(buffer.AsSpan(from, newline - from));
                }
                else
                {
                    carry.Write(buffer, from, newline - from);
                    used += carry.Length + 1;
                    handle(carry.GetBuffer().AsSpan(0, (int)carry.Length));
                    carry.SetLength(0);
                }
                from = newline + 1;
            }
        }
        return used;
    }

    /// <summary>
    /// hook 給的路徑換成 Windows 這邊讀得到的：WSL 的 /home/… 接到 \\wsl.localhost\&lt;發行版&gt; 後面，/mnt/c/… 換成 C:\…。
    /// 發行版從已知的 Claude Code 資料夾（\\wsl.localhost\&lt;發行版&gt;\…）找。Windows 路徑原樣回傳。
    /// </summary>
    public static string? Local(string path, IEnumerable<string> claudeFolders)
    {
        if (path.Length == 0) return null;
        if (!path.StartsWith('/')) return path;
        var mount = System.Text.RegularExpressions.Regex.Match(path, "^/mnt/([a-zA-Z])(/.*)?$");
        if (mount.Success) return char.ToUpperInvariant(mount.Groups[1].Value[0]) + @":\" + mount.Groups[2].Value.TrimStart('/').Replace('/', '\\');
        foreach (var folder in claudeFolders)
        {
            var m = System.Text.RegularExpressions.Regex.Match(folder, @"^(\\\\(?:wsl\.localhost|wsl\$)\\[^\\]+)\\", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (m.Success) return m.Groups[1].Value + path.Replace('/', '\\');
        }
        return null;
    }
}
