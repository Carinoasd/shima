// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Shima.Lyrics;

/// <summary>
/// 找不到翻譯的歌，請 AI 逐句翻成介面的語言。送出去的每一句都帶編號，回來的照編號對回每一句；
/// 對不上（少一句、多一句、拒絕回答）就整份不用，錯位的翻譯比沒有翻譯更糟。
/// 翻好的存在 %APPDATA%\Shima\translations，跟哪個來源無關，只要歌詞的句子一樣就用得上。
/// </summary>
public static partial class AiTranslation
{
    /// <summary>給 AI 看的語言名稱。</summary>
    public static string LanguageName(string lang) => lang switch
    {
        "zh-TW" => "臺灣的繁體中文",
        "zh-CN" => "简体中文",
        _ => Loc.Languages.FirstOrDefault(l => l.Code == lang).Name ?? "English",
    };

    /// <summary>要送出去的系統說明與歌詞：只送有字的句子，從 1 開始編號。</summary>
    public static (string System, string Text) Request(LyricSheet sheet, string title, string artist, string language)
    {
        var song = artist.Length > 0 ? $"{artist} 的〈{title}〉" : $"〈{title}〉";
        var system =
            $"你是歌詞翻譯。使用者會給你 {song} 的歌詞，一行一句，每行開頭有編號。請把每一句翻成{language}。" +
            "一句對一句：輸出的行數必須跟輸入完全相同，每行開頭保留同樣的編號，格式是「編號. 翻譯」；不要合併或拆開句子，不要跳過重複的句子。" +
            $"只輸出翻譯：不要附上原文、標題、說明或註解。已經是{language}的句子照抄。";
        var text = new StringBuilder();
        int n = 0;
        foreach (var line in sheet.Lines)
            if (line.Text.Length > 0) text.Append(++n).Append(". ").Append(line.Text).Append('\n');
        return (system, text.ToString().TrimEnd('\n'));
    }

    /// <summary>
    /// 解析 AI 的回答，對到每一句歌詞（間奏的空行是空字串）。對不上就是 null：
    /// 有編號的話 1…N 每個都要剛好出現一次（其他沒編號的行當成說明略過）；
    /// 沒有編號的話，去掉開頭「以下是翻譯：」這種行之後要剛好 N 行。全部照抄原文也不算翻譯。
    /// </summary>
    public static IReadOnlyList<string>? Align(LyricSheet sheet, string reply)
    {
        var originals = sheet.Lines.Where(l => l.Text.Length > 0).Select(l => l.Text).ToList();
        if (originals.Count == 0) return null;
        var found = Parse(reply, originals.Count);
        if (found == null || found.All(t => t.Length == 0)) return null;
        if (found.Zip(originals).All(p => p.First == p.Second.Trim())) return null;

        var result = new string[sheet.Lines.Count];
        int k = 0;
        for (int i = 0; i < sheet.Lines.Count; i++) result[i] = sheet.Lines[i].Text.Length > 0 ? found[k++] : "";
        return result;
    }

    /// <summary>回答 → 剛好 count 句翻譯，對不上是 null。</summary>
    public static List<string>? Parse(string reply, int count)
    {
        // 會思考的模型（DeepSeek、Qwen）可能把思考過程放在 <think> 裡；也有的會包在 ``` 裡
        reply = Think().Replace(reply.Replace("\r", ""), "");
        var lines = reply.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("```", StringComparison.Ordinal)).ToList();

        var numbered = new string?[count];
        bool any = false;
        foreach (var line in lines)
        {
            var m = Numbered().Match(line);
            var digits = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
            if (!m.Success || !int.TryParse(digits, out int n) || n < 1 || n > count) continue;
            if (numbered[n - 1] != null) return null;   // 同一個編號兩次：分不出哪一個才是翻譯
            numbered[n - 1] = Unquote(m.Groups[3].Value);
            any = true;
        }
        if (any) return numbered.All(t => t != null) ? numbered.Select(t => t!).ToList() : null;

        // 沒有編號：開頭的「以下是翻譯：」拿掉，剩下的要剛好一句對一句。只有一句的歌分不出翻譯跟拒絕回答，不用
        while (lines.Count > count && (lines[0].EndsWith(':') || lines[0].EndsWith('：'))) lines.RemoveAt(0);
        return count >= 2 && lines.Count == count ? lines.Select(Unquote).ToList() : null;
    }

    private static string Unquote(string s)
    {
        s = s.Trim();
        return s.Length >= 2 && (s[0], s[^1]) is ('「', '」') or ('"', '"') or ('“', '”') ? s[1..^1].Trim() : s;
    }

    // ---------------------------------------------------------------- 存起來

    public static string Folder => Path.Combine(AppSettings.Folder, "translations");

    private sealed record Stored(string Lang, List<string> Lines);

    /// <summary>這首歌、這個語言、這幾句歌詞（只看字，不看時間）一份翻譯。</summary>
    private static string FileFor(string folder, string id, LyricSheet sheet, string lang)
    {
        var key = id + "\n" + lang + "\n" + string.Join("\n", sheet.Lines.Select(l => l.Text));
        return Path.Combine(folder, Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(key)))[..20] + ".json");
    }

    /// <summary>之前翻過的話拿出來；句數對不上（檔案壞了）就當沒有。</summary>
    public static IReadOnlyList<string>? Load(string folder, string id, LyricSheet sheet, string lang)
    {
        try
        {
            var path = FileFor(folder, id, sheet, lang);
            if (!File.Exists(path)) return null;
            var stored = JsonSerializer.Deserialize<Stored>(File.ReadAllText(path));
            return stored?.Lang == lang && stored.Lines.Count == sheet.Lines.Count ? stored.Lines : null;
        }
        catch (Exception e) { Log.Write("AI 翻譯讀取: " + e.Message); return null; }
    }

    public static void Save(string folder, string id, LyricSheet sheet, string lang, IReadOnlyList<string> lines)
    {
        try
        {
            Directory.CreateDirectory(folder);
            var path = FileFor(folder, id, sheet, lang);
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(new Stored(lang, lines.ToList())));
            File.Move(path + ".tmp", path, overwrite: true);
        }
        catch (Exception e) { Log.Write("AI 翻譯寫入: " + e.Message); }
    }

    [GeneratedRegex(@"<think>.*?</think>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex Think();

    /// <summary>「1. 」「1) 」「1、」「1：」「(1) 」「[1] 」「#1 」：編號後面一定要有標點或括號，免得「1999年」被當成編號。</summary>
    [GeneratedRegex(@"^(?:[\(\[（【#]\s*(\d{1,4})\s*[\)\]）】.:：]?|(\d{1,4})\s*[.．、:：\)）])\s*(.*)$")]
    private static partial Regex Numbered();
}
