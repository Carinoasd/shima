// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Windows;
using System.Windows.Controls;
using Shima.Lyrics;
using Shima.Playback;

namespace Shima;

// 右鍵「這首歌的歌詞」：存成 .lrc、複製全文、用 AI 翻譯。
public partial class IslandWindow
{
    /// <summary>子選單打開時才決定哪幾項能按：要看當下這首歌有沒有同步歌詞。</summary>
    private void LyricsMenu_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        LyricsSaveItem.IsEnabled = _sheet is { Synced: true };
        LyricsCopyItem.IsEnabled = _sheet is { Lines.Count: > 0 };
        // AI 翻的可以再翻一次（翻壞了、錯位了）；歌詞來源自己帶的翻譯就不用
        LyricsTranslateItem.IsEnabled = !_translating.Contains(_trackKey) && _sheet is { Synced: true } current && (current.Translation == null || _aiTranslated);
    }

    /// <summary>
    /// 現在這份同步歌詞存到自己的歌詞資料夾，之後這首歌優先用它。調過的時間直接寫進檔案，
    /// 所以記住的偏移要歸零（不然會算兩次）；換過來源的選擇也拿掉，不然下次還是先問那個來源。
    /// </summary>
    private void LyricsSave_Click(object sender, RoutedEventArgs e)
    {
        if (_track is not { } track || _sheet is not { Synced: true } sheet) { Toast(L.T("現在沒有歌詞"), 3, false); return; }
        if (LyricsFinder.QueryOf(track) is not { } q || LocalProvider.FileName(q) is not { } name) { Toast(L.T("無法儲存歌詞"), 3, false); return; }
        try
        {
            var lrc = Lrc.Write(sheet, _lyricShift, track.Title, track.Artist);
            LocalProvider.Save(LocalProvider.Folder, name, lrc);
            var id = LyricsFinder.IdOf(track);
            AppSettings.Current.LyricsShift.Remove(id);
            AppSettings.Current.LyricsPick.Remove(id);
            AppSettings.Current.Save();
            _lyrics.Forget(track);
            // 畫面改用存出去的那份：時間已經含偏移，偏移歸零後才不會跳一下
            _lyricShift = TimeSpan.Zero;
            _sheet = Lrc.ParseSheet(lrc);
            _aiTranslated = false;   // 翻譯已經寫進檔案，之後是檔案自己的
            _lyricsFrom = "local";
            _lyricIndex = int.MinValue;
            Toast(L.F("歌詞已存成 {0}", name), 4);
            Morph();
        }
        catch (Exception x) { Log.Write("存歌詞: " + x.Message); Toast(L.T("無法儲存歌詞"), 3, false); }
    }

    /// <summary>整份歌詞（有翻譯的話每句下面一行翻譯）放進剪貼簿。</summary>
    private void LyricsCopy_Click(object sender, RoutedEventArgs e)
    {
        if (_sheet is not { Lines.Count: > 0 } sheet || Lrc.PlainText(sheet) is not { Length: > 0 } text) { Toast(L.T("現在沒有歌詞"), 3, false); return; }
        // Windows 的文字用 CRLF 換行，舊程式才不會貼成一整行；別的程式正開著剪貼簿時會丟例外
        try { Clipboard.SetText(text.Replace("\n", "\r\n")); Toast(L.T("已複製"), 2); }
        catch (Exception x) { Log.Write("複製歌詞: " + x.Message); }
    }

    // ---------------------------------------------------------------- AI 翻譯

    /// <summary>
    /// 正在翻的歌（_trackKey）。翻到一半換了歌，上一首的照樣翻完存起來（之後播到用得上，也不會套到現在這首），
    /// 不擋著現在這首也能請 AI 翻；只有同一首不能同時翻兩次。
    /// </summary>
    private readonly HashSet<string> _translating = [];
    /// <summary>現在顯示的翻譯是 AI 翻的（不是歌詞來源帶的）。</summary>
    private bool _aiTranslated;

    /// <summary>這份歌詞沒有翻譯、之前請 AI 翻過的話，把那份翻譯掛上去。</summary>
    private LyricSheet? WithAiTranslation(Track track, LyricSheet? sheet)
    {
        _aiTranslated = false;
        if (Demo.On || sheet is not { Synced: true, Translation: null }) return sheet;
        if (AiTranslation.Load(AiTranslation.Folder, LyricsFinder.IdOf(track), sheet, Loc.Lang) is not { } lines) return sheet;
        _aiTranslated = true;
        return sheet with { Translation = lines };
    }

    /// <summary>
    /// 整份歌詞交給 AI 分頁設定的那一家，逐句翻成介面的語言，當成這首歌的翻譯。
    /// 不經過語音助手（那邊會帶工具、記對話），只問一次。翻好就存起來，下次播這首直接用。
    /// </summary>
    private async void LyricsTranslate_Click(object sender, RoutedEventArgs e)
    {
        if (_translating.Contains(_trackKey)) return;
        if (_track is not { } track || _sheet is not { Synced: true } sheet) { Toast(L.T("現在沒有歌詞"), 3, false); return; }
        if (sheet.Translation != null && !_aiTranslated) return;
        var shown = sheet;                          // 現在畫面上那一份（可能帶著上一次 AI 翻的）
        sheet = sheet with { Translation = null };  // 送出去與存檔用的鍵都只看原文
        var brain = Agents.Brain.Create((_, _) => "", Loc.Speech, out var problem);
        if (brain == null) { Say(problem); Toast(L.T("先到 AI 分頁設定好 AI 服務"), 4); return; }

        string key = _trackKey, id = LyricsFinder.IdOf(track), lang = Loc.Lang;
        var (system, text) = AiTranslation.Request(sheet, track.Title, track.Artist, AiTranslation.LanguageName(lang));
        var waiting = L.T("翻譯中…");
        _translating.Add(key);
        Toast(waiting, 15, false);
        var waitingUntil = _toastUntil;   // 換歌後另一首也在翻的話，那邊的「翻譯中…」不是這一次的
        // 沒翻成：還在這首才說；已經換歌的話不能蓋掉現在那首的提示（看起來像是它失敗了），只收掉自己的「翻譯中…」
        void Failed(string message)
        {
            if (key == _trackKey) Toast(message, 4, false);
            else if (_toast == waiting && _toastUntil == waitingUntil) { _toast = ""; UpdateTexts(); }
        }
        try
        {
            // 最多等三分鐘：網路卡住時不要讓這一項永遠按不了
            using var limit = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            var reply = await Task.Run(() => brain.CompleteAsync(system, text, limit.Token));
            if (AiTranslation.Align(sheet, reply) is not { } lines)
            {
                Log.Write($"AI 翻譯對不上: {track.Title}（{reply.Split('\n').Length} 行）");
                Failed(L.T("AI 的翻譯跟歌詞對不上"));
                return;
            }
            // 換歌了也先存著（只看歌詞的句子），下次播到就用得上；但不能套到現在這首上
            AiTranslation.Save(AiTranslation.Folder, id, sheet, lang, lines);
            if (key != _trackKey || !ReferenceEquals(_sheet, shown))
            {
                if (_toast == waiting && _toastUntil == waitingUntil) { _toast = ""; UpdateTexts(); }
                return;
            }
            _sheet = sheet with { Translation = lines };
            _aiTranslated = true;
            _lyricIndex = int.MinValue;
            // 第二行設成不顯示、讀音或羅馬拼音（歌詞有拼音的話翻譯排在後面）的話，翻好了也看不到：改成顯示翻譯
            if (AppSettings.Current.LyricsSecondLine != "translation")
            {
                AppSettings.Current.LyricsSecondLine = "translation";
                AppSettings.Current.Save();
                foreach (MenuItem item in SecondLineMenu.Items) item.IsChecked = (string)item.Tag == "translation";
            }
            Toast(L.T("翻譯好了"), 3);
            Morph();
        }
        catch (Exception x) { Log.Write("AI 翻譯: " + x.Message); Failed(brain.Explain(x)); }
        finally { _translating.Remove(key); }
    }
}
