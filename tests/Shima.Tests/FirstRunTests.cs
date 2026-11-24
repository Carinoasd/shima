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

    // 下面的都用自己的暫存資料夾與 LoadFrom，不碰 AppSettings.Current 那一份
    private static string NewFolder()
    {
        var folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Shima-load-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static readonly DateTime Now = new(2026, 11, 24, 10, 0, 0);

    [Theory]
    [InlineData("{ \"Language\": \"ja\", ")]
    [InlineData("null")]
    [InlineData("{ \"Language\": 5 }")]
    public void 壞掉的設定檔會被留下一份_程式照常啟動(string broken)
    {
        var folder = NewFolder();
        try
        {
            var path = System.IO.Path.Combine(folder, "settings.json");
            File.WriteAllText(path, broken);
            var settings = AppSettings.LoadFrom(path, Now);
            Assert.Equal(2, settings.Version);
            Assert.Equal(broken, File.ReadAllText(path + ".broken-20261124-100000"));
            Assert.True(File.Exists(path));
            Assert.NotEqual(broken, File.ReadAllText(path));

            // 同一秒又壞一次：加序號，前一份不會被蓋掉
            File.WriteAllText(path, "{ again");
            AppSettings.LoadFrom(path, Now);
            Assert.Equal(broken, File.ReadAllText(path + ".broken-20261124-100000"));
            Assert.Equal("{ again", File.ReadAllText(path + ".broken-20261124-100000-2"));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void 設定檔被鎖住讀不了也改不了名時_不蓋掉也不丟例外()
    {
        var folder = NewFolder();
        try
        {
            var path = System.IO.Path.Combine(folder, "settings.json");
            const string mine = "{ \"Language\": \"ja\" }";
            File.WriteAllText(path, mine);
            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var settings = AppSettings.LoadFrom(path, Now);
                Assert.Equal(2, settings.Version);
            }
            Assert.Equal(mine, File.ReadAllText(path));
            Assert.Empty(Directory.GetFiles(folder, "settings.json.broken-*"));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void 轉換步驟丟例外時_原本的設定不變()
    {
        var folder = NewFolder();
        try
        {
            var path = System.IO.Path.Combine(folder, "settings.json");
            const string mine = "{ \"Language\": \"ja\", \"Version\": 2, \"Opacity\": 0.8, \"ListenBrainzToken\": \"plain-token\" }";
            File.WriteAllText(path, mine);
            Func<AppSettings, bool> fails = _ => throw new System.Security.Cryptography.CryptographicException("DPAPI 壞了");

            var settings = AppSettings.LoadFrom(path, Now, [fails]);
            Assert.Equal("ja", settings.Language);
            Assert.Equal(0.8, settings.Opacity);
            Assert.Equal("plain-token", settings.ListenBrainzToken);   // 轉不了就維持原樣，下次再試
            Assert.Equal(mine, File.ReadAllText(path));
            Assert.Empty(Directory.GetFiles(folder, "settings.json.broken-*"));

            // 一步出錯，其他步驟照做、照存
            settings = AppSettings.LoadFrom(path, Now, [fails, s => { s.Water = true; return true; }]);
            Assert.True(settings.Water);
            var saved = AppSettings.LoadFrom(path, Now, []);
            Assert.True(saved.Water);
            Assert.Equal("ja", saved.Language);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void 沒有設定檔就是全新安裝_不留壞檔()
    {
        var folder = NewFolder();
        try
        {
            var path = System.IO.Path.Combine(folder, "settings.json");
            Assert.Equal(2, AppSettings.LoadFrom(path, Now).Version);
            Assert.True(File.Exists(path));
            Assert.Empty(Directory.GetFiles(folder, "settings.json.broken-*"));
        }
        finally { Directory.Delete(folder, true); }
    }
}
