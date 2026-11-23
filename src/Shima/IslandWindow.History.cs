// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using Shima.Listening;

namespace Shima;

/// <summary>聆聽分頁讀紀錄：在背景讀、檔案沒變就不重讀。</summary>
public partial class IslandWindow
{
    private readonly HistoryCache _historyCache = new();
    private int _statsLoad;
    private List<Listen>? _statsShown;
    private DateTime _statsShownDay;

    private void LoadStats()
    {
        var stats = _stats;
        if (stats == null) return;
        // 示範模式的紀錄是幾筆編出來的，同步讀；截圖要在 RefreshLibrary 之後馬上拍
        if (Demo.On) { Show(stats, _historyCache.Read(), DateTime.Now); return; }

        int load = ++_statsLoad;
        // 讀的時候畫面維持上一次的內容（第一次是空的），讀完才換，不會閃
        _ = Task.Run(() => _historyCache.Read()).ContinueWith(done =>
        {
            if (!done.IsCompletedSuccessfully) return;
            Dispatcher.BeginInvoke(() =>
            {
                if (load != _statsLoad) return;   // 這中間又打開過一次，用比較新的那次
                Show(stats, done.Result, DateTime.Now);
                if (_tab == Tab.Library && _expanded) Morph();
            });
        });
    }

    private void Show(StatsView stats, (List<Listen> Listens, bool Changed) read, DateTime now)
    {
        // 檔案沒變、也還是同一天（「本週」「本月」從哪天算只看日期）：畫面上的就是最新的，不必重算
        if (!read.Changed && ReferenceEquals(_statsShown, read.Listens) && _statsShownDay == now.Date) return;
        stats.SetListens(read.Listens, now);
        _statsShown = read.Listens;
        _statsShownDay = now.Date;
    }
}
