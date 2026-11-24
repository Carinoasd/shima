// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using Xunit;

namespace Shima.Tests;

/// <summary>隨機輸入測試找到的問題：路徑含 ~ 時 Path.GetFullPath 會去問檔案系統，放在不通的網路位置要等好幾秒（畫面執行緒上每個暫存項目都跑一次）。</summary>
public class ShelfForeignRootTests
{
    private const string Folder = @"C:\Users\u\AppData\Roaming\Shima\shelf";

    [Theory]
    [InlineData(@"\\srv\share\SHELF~1.TXT")]
    [InlineData(@"\\no-such-host.invalid\share\A~1.PNG")]
    [InlineData(@"D:\x\SHELF~1.TXT")]
    [InlineData("~/a.txt")]
    [InlineData(@"\\?\UNC\srv\share\A~1.TXT")]
    public void IsOwned_ForAnotherRoot_IsFalse_AndReturnsAtOnce(string path)
    {
        var watch = Stopwatch.StartNew();
        Assert.False(Shelf.IsOwned(path, Folder));
        Assert.Equal(ShelfKind.File, Shelf.Classify(path, Folder));
        Assert.False(Shelf.DeleteCopy(path, Folder));
        Assert.True(watch.ElapsedMilliseconds < 1000, $"花了 {watch.ElapsedMilliseconds} 毫秒");
    }

    [Theory]
    [InlineData(@"C:\Users\u\AppData\Roaming\Shima\shelf\a.txt")]
    [InlineData("C:/Users/u/AppData/Roaming/Shima/shelf/a.txt")]
    [InlineData(@"c:\USERS\u\appdata\roaming\shima\SHELF\a.PNG")]
    [InlineData(@"C:\Users\u\AppData\Roaming\Shima\shelf\sub\..\a.txt")]
    public void IsOwned_ForTheSameRoot_StillWorks(string path)
    {
        Assert.True(Shelf.IsOwned(path, Folder));
    }
}
