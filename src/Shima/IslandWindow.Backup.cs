// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Windows;
using System.Windows.Controls;

namespace Shima;

/// <summary>設定 → 一般的「備份」：匯出成 zip、從 zip 還原。打包與驗證的邏輯在 <see cref="Backup"/>。</summary>
public partial class IslandWindow
{
    private bool _backupBusy;

    private DockPanel BackupRow()
    {
        Button Make(string text, RoutedEventHandler click)
        {
            var button = new Button { Style = (Style)FindResource("Pill"), Height = 26, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(6, 0, 0, 0), Content = text };
            button.Click += click;
            return button;
        }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        buttons.Children.Add(Make(L.T("匯出"), Export_Click));
        buttons.Children.Add(Make(L.T("還原備份"), Restore_Click));
        return SettingsRow("備份", buttons);
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_backupBusy) return;
        var dialog = new Microsoft.Win32.SaveFileDialog { FileName = $"Shima-backup-{DateTime.Now:yyyyMMdd}.zip", DefaultExt = ".zip", Filter = "ZIP|*.zip" };
        if (dialog.ShowDialog(this) != true) return;
        _backupBusy = true;
        try
        {
            var folder = AppSettings.Folder;
            var result = await Task.Run(() => Backup.Export(folder, dialog.FileName));
            Toast(result.Status == BackupStatus.Ok ? L.T("已匯出備份") : BackupMessage(result.Status, L.T("匯出失敗")), 5, false);
        }
        finally { _backupBusy = false; }
    }

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (_backupBusy) return;
        var dialog = new Microsoft.Win32.OpenFileDialog { DefaultExt = ".zip", Filter = "ZIP|*.zip" };
        if (dialog.ShowDialog(this) != true) return;
        _backupBusy = true;
        try
        {
            var folder = AppSettings.Folder;
            // 驗證都通過、確定要換上去的那一刻才結算目前這首（被拒絕的 zip 不能讓這次播放提早記一筆），
            // 關閉時才寫的話會接在還原好的紀錄後面。之後到重新啟動前不再存設定、提醒、收藏。
            void Committing() => Dispatcher.Invoke(() => { _listening.Flush(); AppSettings.Frozen = true; });
            var result = await Task.Run(() => Backup.Restore(dialog.FileName, folder, DateTime.Now, Committing));
            if (result.Status != BackupStatus.Ok)
            {
                AppSettings.Frozen = false;
                // 換到一半失敗又沒能全部換回來（很少見）：原本的資料在還原前複製的那一份裡，要說在哪
                if (result.Saved is { } kept) Toast(L.F("還原失敗，原本的資料留在 {0}", kept), 20, false);
                else Toast(BackupMessage(result.Status, L.T("還原失敗，資料沒有變動")), 6, false);
                return;
            }
            Toast(L.T("已還原，重新啟動中"), 10, false);
            ((App)Application.Current).Restart();
            if (Demo.On) AppSettings.Frozen = false;   // 示範模式不會真的重啟
            else Close();
        }
        finally { _backupBusy = false; }
    }

    private static string BackupMessage(BackupStatus status, string fallback) => status switch
    {
        BackupStatus.Unreadable or BackupStatus.Empty => L.T("這不是有效的備份檔"),
        BackupStatus.Unsafe => L.T("備份檔含有不安全的路徑，已拒絕"),
        BackupStatus.TooBig => L.T("備份檔太大，已拒絕"),
        BackupStatus.BadSettings => L.T("備份裡的設定檔壞了，已拒絕"),
        _ => fallback,
    };
}
