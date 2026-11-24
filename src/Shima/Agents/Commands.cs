// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Shima.Agents;

public enum CommandKind { Play, Pause, Next, Prev, Volume, Sleep, Remind, Todo, Focus, Dnd, Lyrics, Earlier, Later, Settings, Help }

/// <summary>指令後面要不要接參數：補上名稱時，必填的要等使用者打完，其餘直接執行。</summary>
public enum ArgNeed { None, Optional, Required }

/// <summary>一個指令的定義：名稱、中文別名、用法與一句說明（說明要能翻譯，所以用函式延後到顯示時才取）。</summary>
public sealed record CommandSpec(CommandKind Kind, string Name, string[] Aliases, string Usage, ArgNeed Arg, Func<string> Help);

public enum CommandState { NotCommand, Ok, Unknown, BadArgs }

/// <summary>
/// 解析的結果。Action 與 Number 的意思看指令：
/// 音量 set（Number 是 0–100）／up／down；睡眠 minutes（Number）／track／off；專注 start（Number 是分鐘，0 是用設定的）／stop／reset；勿擾 on／off。
/// 提醒與待辦的內容放在 Text。
/// </summary>
public sealed record Command(CommandState State, CommandSpec? Spec = null, string Action = "", int Number = 0, string Text = "", string Name = "");

/// <summary>輸入框裡以 / 開頭的快速指令：在本機直接執行，不經過 AI。這裡只負責解析，不碰畫面。</summary>
public static class Commands
{
    public const int MaxHints = 6;
    public const int MaxSleepMinutes = 600, MaxFocusMinutes = 180;

    /// <summary>所有指令，順序就是提示清單與說明的順序。中文別名只給中文使用者打，不翻譯。</summary>
    public static readonly IReadOnlyList<CommandSpec> All =
    [
        new(CommandKind.Play, "play", ["播放"], "/play", ArgNeed.None, () => L.T("播放")),
        new(CommandKind.Pause, "pause", ["暫停", "暂停"], "/pause", ArgNeed.None, () => L.T("暫停")),
        new(CommandKind.Next, "next", ["下一首"], "/next", ArgNeed.None, () => L.T("下一首")),
        new(CommandKind.Prev, "prev", ["上一首", "previous"], "/prev", ArgNeed.None, () => L.T("上一首")),
        new(CommandKind.Volume, "vol", ["音量", "volume"], "/vol 0-100 | + | -", ArgNeed.Required, () => L.T("音量")),
        new(CommandKind.Sleep, "sleep", ["睡眠"], "/sleep 1-600 | track | off", ArgNeed.Required, () => L.T("睡眠定時")),
        new(CommandKind.Remind, "remind", ["提醒"], "/remind 15:30 …", ArgNeed.Required, () => L.T("新增提醒")),
        new(CommandKind.Todo, "todo", ["待辦", "待办"], "/todo …", ArgNeed.Required, () => L.T("新增待辦")),
        new(CommandKind.Focus, "focus", ["專注", "专注"], "/focus [1-180 | stop | reset]", ArgNeed.Optional, () => L.T("番茄鐘")),
        new(CommandKind.Dnd, "dnd", ["勿擾", "勿扰"], "/dnd [off]", ArgNeed.Optional, () => L.T("勿擾（1 小時）")),
        new(CommandKind.Lyrics, "lyrics", ["歌詞", "歌词"], "/lyrics", ArgNeed.None, () => L.T("桌面歌詞")),
        new(CommandKind.Earlier, "earlier", ["提早"], "/earlier", ArgNeed.None, () => L.T("歌詞提早 0.1 秒")),
        new(CommandKind.Later, "later", ["延後", "延后"], "/later", ArgNeed.None, () => L.T("歌詞延後 0.1 秒")),
        new(CommandKind.Settings, "settings", ["設定", "设置"], "/settings", ArgNeed.None, () => L.T("設定")),
        new(CommandKind.Help, "help", ["說明", "说明", "幫助", "帮助", "?"], "/help", ArgNeed.None, () => L.T("所有指令")),
    ];

    // 只認 0-9：\d 也會比對到阿拉伯-印度數字（٥٠）這些 int.Parse 轉不了的字。全形數字在 Fold 時已經變成半形
    private static readonly Regex Minutes = new(@"^([0-9]{1,6})\s*(?:m|min|mins|minutes?|分|分鐘|分钟)?$", RegexOptions.CultureInvariant);
    private static readonly Regex Percent = new(@"^([0-9]{1,6})\s*%?$", RegexOptions.CultureInvariant);

    /// <summary>全形轉半形（／、全形空白、全形數字）、轉小寫。中文輸入法下很容易打出全形。孤立的代理字元先換掉，不然 Normalize 會丟例外。</summary>
    private static string Fold(string text) => Shima.Lyrics.Titles.Wellformed(text).Normalize(NormalizationForm.FormKC).ToLowerInvariant();

    private static bool Slash(char c) => c is '/' or '／';

    /// <summary>一行字是不是指令。不是 / 開頭的一律不是指令（照舊交給 AI）。</summary>
    public static Command Parse(string? line)
    {
        var text = line?.Trim() ?? "";
        if (text.Length == 0 || !Slash(text[0])) return new(CommandState.NotCommand);

        var body = text[1..].TrimStart();
        if (body.Length == 0) return new(CommandState.Ok, Find(CommandKind.Help), Name: "help");
        int cut = body.IndexOfAny([' ', '\t', '　']);
        var name = Fold(cut < 0 ? body : body[..cut]);
        var arg = cut < 0 ? "" : body[cut..].Trim();

        var spec = All.FirstOrDefault(s => s.Name == name || s.Aliases.Contains(name));
        if (spec == null) return new(CommandState.Unknown, Name: name);
        return Args(spec, arg) with { Name = name };
    }

    private static CommandSpec Find(CommandKind kind) => All.First(s => s.Kind == kind);

    private static Command Ok(CommandSpec spec, string action = "", int number = 0, string text = "") => new(CommandState.Ok, spec, action, number, text);
    private static Command Bad(CommandSpec spec) => new(CommandState.BadArgs, spec);

    private static Command Args(CommandSpec spec, string raw)
    {
        var arg = Fold(raw);
        switch (spec.Kind)
        {
            case CommandKind.Volume:
                if (arg is "+" or "up" or "加") return Ok(spec, "up");
                if (arg is "-" or "down" or "減" or "减") return Ok(spec, "down");
                return Percent.Match(arg) is { Success: true } v && int.Parse(v.Groups[1].Value, CultureInfo.InvariantCulture) is <= 100 and var level ? Ok(spec, "set", level) : Bad(spec);

            case CommandKind.Sleep:
                if (arg is "track" or "end" or "這首" or "这首" or "這首播完" or "这首播完") return Ok(spec, "track");
                if (arg is "off" or "cancel" or "stop" or "關" or "关" or "關閉" or "关闭" or "取消") return Ok(spec, "off");
                return Minutes.Match(arg) is { Success: true } m && int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) is >= 1 and <= MaxSleepMinutes and var span ? Ok(spec, "minutes", span) : Bad(spec);

            case CommandKind.Focus:
                if (arg is "" or "start" or "開始" or "开始") return Ok(spec, "start");
                if (arg is "stop" or "pause" or "停" or "停止" or "暫停" or "暂停") return Ok(spec, "stop");
                if (arg is "reset" or "重來" or "重来" or "重置" or "歸零" or "归零") return Ok(spec, "reset");
                return Minutes.Match(arg) is { Success: true } f && int.Parse(f.Groups[1].Value, CultureInfo.InvariantCulture) is >= 1 and <= MaxFocusMinutes and var focus ? Ok(spec, "start", focus) : Bad(spec);

            case CommandKind.Dnd:
                if (arg is "" or "on" or "開" or "开") return Ok(spec, "on");
                if (arg is "off" or "關" or "关" or "關閉" or "关闭" or "結束" or "结束" or "取消") return Ok(spec, "off");
                return Bad(spec);

            // 內容照原樣交給後面的解析：提醒的時間格式、待辦的標點都不能被轉掉
            case CommandKind.Remind:
            case CommandKind.Todo:
                return raw.Length == 0 ? Bad(spec) : Ok(spec, text: raw);

            default:
                return raw.Length == 0 ? Ok(spec) : Bad(spec);
        }
    }

    /// <summary>輸入框打到一半（只有「/」與名稱、還沒有空白）時符合的指令；名稱或別名開頭相符就算。Shown 是使用者打的那一種寫法。</summary>
    public static IReadOnlyList<(CommandSpec Spec, string Shown)> Suggest(string? input)
    {
        var text = input?.TrimStart() ?? "";
        if (text.Length == 0 || !Slash(text[0])) return [];
        var typed = Fold(text[1..]);
        if (typed.Any(char.IsWhiteSpace)) return [];

        var found = new List<(CommandSpec, string)>();
        foreach (var spec in All)
        {
            var shown = typed.Length == 0 ? spec.Name : new[] { spec.Name }.Concat(spec.Aliases).FirstOrDefault(n => n.StartsWith(typed, StringComparison.Ordinal));
            if (shown != null) found.Add((spec, shown));
            if (found.Count == MaxHints) break;
        }
        return found;
    }

    /// <summary>/help 的內容（Markdown 清單）。中文使用者多列中文別名，其他語言只列英文名稱。</summary>
    public static string HelpText(bool withAliases)
    {
        var text = new StringBuilder();
        foreach (var spec in All)
        {
            var help = spec.Help();
            text.Append("- `").Append(spec.Usage).Append('`');
            // 說明本身就是別名（「/play」「播放」）的話，再列一次只是重複
            if (withAliases && spec.Aliases[0].Any(c => c > 0x7F) && !help.StartsWith(spec.Aliases[0], StringComparison.Ordinal)) text.Append(" `/").Append(spec.Aliases[0]).Append('`');
            text.Append("　").Append(help).Append('\n');
        }
        return text.ToString().TrimEnd();
    }
}
