// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Windows;
using Shima.Lyrics;

namespace Shima;

// 右鍵「這首歌的歌詞」：存成 .lrc。
public partial class IslandWindow
{
    /// <summary>子選單打開時才決定哪幾項能按：要看當下這首歌有沒有同步歌詞。</summary>
    private void LyricsMenu_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        LyricsSaveItem.IsEnabled = _sheet is { Synced: true };
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
            _lyricsFrom = "local";
            _lyricIndex = int.MinValue;
            Toast(L.F("歌詞已存成 {0}", name), 4);
            Morph();
        }
        catch (Exception x) { Log.Write("存歌詞: " + x.Message); Toast(L.T("無法儲存歌詞"), 3, false); }
    }
}
