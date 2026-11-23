// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.IO;
using System.Runtime.CompilerServices;
using Xunit;

namespace Shima.Tests;

/// <summary>
/// 所有測試都在一個全新的資料夾裡跑，跟第一次安裝的人一樣：沒有 settings.json。
/// 這樣測試不會讀到（或寫壞）自己電腦上的設定，也能抓到「只有全新安裝才會壞」的問題。
/// </summary>
public static class FreshFolder
{
    public static readonly string Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Shima-tests-" + Guid.NewGuid().ToString("N"));

    [ModuleInitializer]
    public static void Use()
    {
        Directory.CreateDirectory(Path);
        Environment.SetEnvironmentVariable(AppSettings.FolderVariable, Path);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => { try { Directory.Delete(Path, true); } catch { } };
    }
}

public class FirstRunTests
{
    [Fact]
    public void 沒有設定檔時讀得到預設值_而且會寫出一份()
    {
        // 0.6.1 到 0.7.0：第一次啟動時存檔用的鎖還沒初始化，AppSettings 整個類別初始化失敗，程式一開就當掉
        var settings = AppSettings.Current;
        Assert.Equal(2, settings.Version);
        Assert.Equal(FreshFolder.Path, AppSettings.Folder);
        Assert.True(File.Exists(System.IO.Path.Combine(FreshFolder.Path, "settings.json")));
        settings.Save();   // 之後再存也不會出錯
    }
}
