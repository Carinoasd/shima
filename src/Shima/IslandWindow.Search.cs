// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using System.Windows;
using Shima.Lyrics;
using Shima.Playback;

namespace Shima;

// 音樂分頁的 YouTube / YouTube Music / 關鍵字找歌詞搜尋抽屜。
public partial class IslandWindow
{
    private MusicSearchView? _musicSearch;
    private bool _searchOpen;

    private MusicSearchView EnsureMusicSearch()
    {
        if (_musicSearch != null) return _musicSearch;
        _musicSearch = new MusicSearchView(YouTubeSearch.Shared, () => _lyrics);
        _musicSearch.SetTrack(_track);
        _musicSearch.ItemPicked += item =>
        {
            OpenExternalUrl(item.WatchUrl);
            Toast(L.F("已在瀏覽器開啟：{0}", item.Title), 3);
        };
        _musicSearch.BrowserSearchRequested += (query, music) =>
        {
            OpenExternalUrl(YouTubeSearch.SearchUrl(query, music));
            Toast(L.T("已在瀏覽器開啟搜尋結果"), 3);
        };
        _musicSearch.LyricsPicked += (target, choice, sheet) =>
        {
            if (_track is not { } current || LyricsFinder.RawId(current) != LyricsFinder.RawId(target))
            {
                Toast(L.T("已換成另一首歌，未套用歌詞"), 3, false);
                return;
            }
            _searchOpen = false;
            ApplySearchedLyrics(current, choice, sheet);
        };
        _musicSearch.Notice += msg => Toast(msg, 3, false);
        _musicSearch.CloseRequested += () => { _searchOpen = false; Morph(); };
        MusicSearchHost.Children.Add(_musicSearch);
        return _musicSearch;
    }

    /// <summary>音樂分頁放大鏡按鈕：在歌詞與搜尋抽屜之間切換（跟佇列互斥）。</summary>
    private void MusicSearch_Click(object sender, RoutedEventArgs e)
    {
        _searchOpen = !_searchOpen;
        if (_searchOpen)
        {
            _queueOpen = false;
            var view = EnsureMusicSearch();
            view.SetTrack(_track);
            Morph();
            Dispatcher.BeginInvoke(() => { Activate(); view.FocusInput(); });
        }
        else Morph();
    }

    /// <summary>右鍵「這首歌的歌詞 → 搜尋歌詞…」：打開音樂分頁的找歌詞模式，先帶入整理過的歌名並開始搜尋。</summary>
    private void LyricsSearch_Click(object sender, RoutedEventArgs e)
    {
        if (_track is not { } track) { Toast(L.T("沒有正在播放的音樂"), 3, false); return; }
        _queueOpen = false;
        _searchOpen = true;
        var view = EnsureMusicSearch();
        view.SetTrack(track);
        if (_tab != Tab.Music) { TabMusic.IsChecked = true; ShowTab(Tab.Music); }
        if (!_expanded) SetExpanded(true);
        else Morph();
        var initial = LyricsFinder.QueryOf(track)?.Title is { Length: > 0 } cleaned ? cleaned : track.Title;
        Dispatcher.BeginInvoke(() => { Activate(); view.OpenMode(MusicSearchMode.Lyrics, initial); });
    }

    private static void OpenExternalUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose(); }
        catch (Exception e) { Log.Write("開啟瀏覽器失敗: " + e.Message); }
    }
}
