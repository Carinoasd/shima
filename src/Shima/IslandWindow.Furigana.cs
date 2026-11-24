// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using Shima.Lyrics;

namespace Shima;

/// <summary>
/// 「歌詞第二行」選「讀音」：日文歌詞的漢字標上平假名。讀音在背景算（<see cref="Furigana"/>），
/// 算好之前展開的歌詞照讀音模式留好空間、桌面歌詞的第二行照舊顯示下一句。
/// 沒有日文輸入法的電腦上選單裡沒有這一項，設定檔裡已經是 furigana 也當成「不顯示」。
/// </summary>
public partial class IslandWindow
{
    private LyricSheet? _rubyFor;
    private RubySheet? _ruby;
    private Task<RubySheet?>? _rubyTask;

    private static bool FuriganaOn => AppSettings.Current.LyricsSecondLine == "furigana" && Furigana.Available;

    /// <summary>
    /// 這份歌詞的讀音：不是讀音模式、或不是日文是 null；還在算是 <see cref="RubySheet.Pending"/>。
    /// 每 0.1 秒都會被問，所以同一份歌詞一定回傳同一個物件，展開的歌詞才不會一直重排。
    /// </summary>
    private RubySheet? RubyFor(LyricSheet? shown)
    {
        if (!FuriganaOn || shown == null || !Furigana.Wanted(shown))
        {
            _rubyFor = null;
            _ruby = null;
            return null;
        }
        if (!ReferenceEquals(shown, _rubyFor))
        {
            _rubyFor = shown;
            var task = _rubyTask = Furigana.ForAsync(shown);
            _ruby = task.IsCompleted ? task.Result : RubySheet.Pending;
            if (!task.IsCompleted) Arrive(shown, task);
        }
        return _ruby;
    }

    /// <summary>算好了：還是同一份歌詞才套上去（換歌了就丟掉，新的歌有自己的那一份）。</summary>
    private async void Arrive(LyricSheet sheet, Task<RubySheet?> task)
    {
        var ruby = await task;   // 不會失敗；回到畫面的執行緒
        if (!ReferenceEquals(sheet, _rubyFor)) return;
        _ruby = ruby;
        UpdateTexts();
    }

    /// <summary>桌面歌詞的第二行：讀音模式是整句的平假名，其他照原本的第二行。</summary>
    private IReadOnlyList<string>? DesktopSecond(LyricSheet? shown, IReadOnlyList<string>? second) =>
        FuriganaOn ? RubyFor(shown)?.Readings is { Count: > 0 } readings ? readings : null : second;

    /// <summary>截圖用：等這份歌詞的讀音算完。</summary>
    private Task RubyReady() => _rubyTask ?? Task.CompletedTask;

    /// <summary>截圖用：正在唱的那句有讀音可以畫了。</summary>
    private bool RubyOnCurrentLine() =>
        _track is { } track && _sheet is { Synced: true } sheet && _ruby?.Lines is { } lines
        && Lrc.LineAt(sheet.Lines, track.Now + LyricLead + _lyricShift) is >= 0 and var i && i < lines.Count
        && lines[i]?.Any(r => r.Reading != null) == true;
}
