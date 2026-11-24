// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Diagnostics;

namespace Shima;

/// <summary>更新後第一次啟動：島上提示「已更新到 x」，系統匣多一項「這一版改了什麼」（只在這次執行期間）。</summary>
public partial class IslandWindow
{
    private void AnnounceUpdate()
    {
        if (_smoke || Demo.On) return;
        var s = AppSettings.Current;
        var current = Updater.Current;
        var change = Updater.Compare(s.LastRunVersion, current);
        if (change == Updater.VersionChange.Same) return;
        s.LastRunVersion = current.ToString(3);   // 第一次安裝與降版只記下版本，不提示
        s.Save();
        if (change != Updater.VersionChange.Upgraded) return;
        string version = s.LastRunVersion;
        Loaded += (_, _) => Toast(L.F("已更新到 {0}", version), 12);
        _trayMenu?.Items.Insert(0, new System.Windows.Forms.ToolStripMenuItem(L.T("這一版改了什麼"), null, (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(Updater.NotesUrl(current)) { UseShellExecute = true }); }
            catch (Exception e) { Log.Write("release notes: " + e.Message); }
        }));
    }
}
