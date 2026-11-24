// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using Microsoft.Win32;

namespace Shima;

/// <summary>開機自動啟動：寫在目前使用者的登錄檔 Run 機碼，不需要系統管理員權限。</summary>
public static class Startup
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run", Name = "Shima";

    public static bool Enabled
    {
        get
        {
            if (Demo.On) return false;   // 示範模式看不到、也不改真正的登錄值
            try
            {
                using var run = Registry.CurrentUser.OpenSubKey(Key);
                return run?.GetValue(Name) is string command && command.Contains(Environment.ProcessPath ?? "?", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }
        set
        {
            if (Demo.On) return;
            try
            {
                using var run = Registry.CurrentUser.CreateSubKey(Key);
                if (value && Environment.ProcessPath is { } exe) run.SetValue(Name, $"\"{exe}\"");
                else run.DeleteValue(Name, throwOnMissingValue: false);
            }
            catch (Exception e) { Log.Write("startup: " + e.Message); }
        }
    }
}
