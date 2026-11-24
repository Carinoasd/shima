// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using Xunit;

namespace Shima.Tests;

/// <summary>示範模式（--demo）不能碰到使用者真正的環境：紀錄檔、agent 的設定。</summary>
public class DemoIsolationTests
{
    [Fact]
    public void 沒有換資料夾時紀錄檔照舊在LocalAppData()
    {
        // CI 的 first-run.ps1 與使用者平常都讀這個位置
        Assert.Equal(@"C:\Users\x\AppData\Local\Shima\log.txt", Log.PathFor(null, @"C:\Users\x\AppData\Local"), ignoreCase: true);
        Assert.Equal(@"C:\Users\x\AppData\Local\Shima\log.txt", Log.PathFor("", @"C:\Users\x\AppData\Local"), ignoreCase: true);
    }

    [Fact]
    public void 資料夾被換掉時紀錄檔跟著走()
    {
        Assert.Equal(@"D:\demo\log.txt", Log.PathFor(@"D:\demo", @"C:\Users\x\AppData\Local"), ignoreCase: true);
    }

    [Fact]
    public void Log讀的環境變數名稱跟AppSettings一致()
    {
        // Log 不能引用 AppSettings（static 初始化會循環），名稱是寫死的字串：改了一邊要有人提醒
        Assert.Equal("SHIMA_DATA", AppSettings.FolderVariable);
    }

    [Fact]
    public void 測試期間Log不寫檔()
    {
        // 測試專案把 SHIMA_DATA 設成暫存資料夾；跑測試時 Log 本來就不寫，確認不會在那裡留下 log.txt
        Log.Write("測試用的訊息");
        Assert.False(System.IO.File.Exists(Log.PathFor(AppSettings.Folder, "")));
    }

    [Fact]
    public void 示範模式的agent狀態是固定的假結果()
    {
        // 跟截圖 4-ai.png 一致：Claude Code 已連接，Codex 與 Gemini CLI 找得到但還沒連接
        var status = Shima.Agents.AgentInstaller.DemoStatuses();
        Assert.Equal(3, status.Count);
        Assert.All(status, s => Assert.True(s.Detected));
        Assert.All(status, s => Assert.False(s.Outdated));
        Assert.True(status.Single(s => s.Agent == Shima.Agents.AgentKind.Claude).Installed);
        Assert.False(status.Single(s => s.Agent == Shima.Agents.AgentKind.Codex).Installed);
        Assert.False(status.Single(s => s.Agent == Shima.Agents.AgentKind.Gemini).Installed);
    }

    [Fact]
    public void 不是示範模式時照舊掃這台電腦()
    {
        // Demo.On 在測試裡是 false：Scan(home) 讀的是給定的資料夾（這裡是空的暫存資料夾，不碰真實的設定）
        var home = System.IO.Path.Combine(FreshFolder.Path, "scan-home");
        var status = Shima.Agents.AgentInstaller.Scan(new Shima.Agents.AgentHooks.Home(home, null));
        Assert.False(Demo.On);
        Assert.All(status, s => Assert.False(s.Installed));
    }
}
