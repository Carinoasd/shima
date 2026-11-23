// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.IO;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Windows.Media.Imaging;
using Shima.Lyrics;
using Shima.Playback;

namespace Shima.Agents;

/// <summary>島上此刻的歌與歌詞，給 Stream Dock 外掛讀。整個物件不會再變，換歌或換歌詞時換一個新的。</summary>
public sealed record DeckState(Track? Track, LyricSheet? Sheet, IReadOnlyList<string>? Second, TimeSpan Shift)
{
    /// <summary>歌與歌詞的身分；外掛手上的跟這個一樣就不用再傳一次歌詞。</summary>
    public string Key => Track == null ? "" : $"{Track.Key}\n{Sheet?.Lines.Count ?? 0}\n{Second?.Count ?? 0}\n{Shift.TotalMilliseconds}";

    public JsonObject ToJson(string have)
    {
        var json = new JsonObject { ["key"] = Key };
        if (Track is not { } t) return json;
        json["title"] = t.Title;
        json["artist"] = t.Artist;
        json["playing"] = t.Playing;
        json["position"] = (t.Now + Shift).TotalSeconds;
        json["duration"] = t.Duration.TotalSeconds;
        json["cover"] = t.Cover != null;
        if (have == Key) return json;
        var lines = new JsonArray();
        if (Sheet is { Synced: true } sheet)
            for (int i = 0; i < sheet.Lines.Count; i++)
                lines.Add(new JsonArray(sheet.Lines[i].Time.TotalSeconds, sheet.Lines[i].Text, Second != null && i < Second.Count ? Second[i] : ""));
        json["lines"] = lines;
        return json;
    }

    public byte[]? CoverJpeg()
    {
        if (Track?.Cover is not BitmapSource { IsFrozen: true } image) return null;
        var encoder = new JpegBitmapEncoder { QualityLevel = 85 };
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}

/// <summary>島上音樂以外的事，Stream Dock 的長條跟著插播。Kind：ask（Claude Code 要權限）、voice（語音助手）、toast（一句提示），或空字串。</summary>
public sealed record DeckNotice(string Kind, string Title, string Text, string Timer, string Plan, bool Listening)
{
    public static readonly DeckNotice None = new("", "", "", "", "", false);
}

/// <summary>Stream Dock（HotSpot／Mirabox）外掛：把外掛檔案放進它的 plugins 資料夾。</summary>
public static class StreamDock
{
    public const string PluginFolder = "com.carinoasd.shima.sdPlugin";
    private const string Prefix = "Shima.StreamDock.";

    public static string PluginsFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HotSpot", "StreamDock", "plugins");
    public static bool Available => Directory.Exists(PluginsFolder);
    public static bool Installed => File.Exists(Path.Combine(PluginsFolder, PluginFolder, "manifest.json"));

    /// <summary>寫入（或更新）外掛檔案，連同這台電腦的橋接 token。回傳 false 表示沒裝 Stream Dock 或寫不進去。</summary>
    public static bool Install()
    {
        if (!Available) return false;
        try
        {
            var folder = Path.Combine(PluginsFolder, PluginFolder);
            Directory.CreateDirectory(folder);
            var assembly = Assembly.GetExecutingAssembly();
            foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith(Prefix)))
            {
                using var from = assembly.GetManifestResourceStream(name)!;
                using var to = File.Create(Path.Combine(folder, name[Prefix.Length..]));
                from.CopyTo(to);
            }
            File.WriteAllText(Path.Combine(folder, "bridge.js"), $"window.SHIMA = {{ port: {AgentBridge.Port}, token: \"{AgentBridge.Token()}\" }};\n");
            return true;
        }
        catch (Exception e) { Log.Write("stream dock: " + e.Message); return false; }
    }

    public static void Remove()
    {
        try { Directory.Delete(Path.Combine(PluginsFolder, PluginFolder), true); }
        catch (Exception e) { Log.Write("stream dock: " + e.Message); }
    }
}
