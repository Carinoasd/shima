// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.IO;
using System.Text;
using Shima.Agents;
using Xunit;

namespace Shima.Tests;

public class BridgeGateTests
{
    private const string Hook = "hook-token-0123456789abcdef0123456789abcdef", Deck = "deck-token-0123456789abcdef0123456789abcdef";

    private static AgentBridge.Door Gate(string method, string path, string? header = null, string? query = null, bool origin = false) =>
        AgentBridge.Gate(method, path, header, query, origin, Hook, Deck);

    [Fact]
    public void Hook要用自己的金鑰_POST_而且不能是網頁送的()
    {
        Assert.Equal(AgentBridge.Door.Hook, Gate("POST", "/permission", header: Hook));
        Assert.Equal(AgentBridge.Door.Refuse, Gate("GET", "/permission", header: Hook));
        Assert.Equal(AgentBridge.Door.Refuse, Gate("POST", "/permission", header: Hook, origin: true));
        Assert.Equal(AgentBridge.Door.Refuse, Gate("POST", "/permission", header: "wrong"));
        Assert.Equal(AgentBridge.Door.Refuse, Gate("POST", "/permission"));
    }

    [Fact]
    public void 外掛的金鑰不能拿來當Hook_Hook的金鑰也進不了外掛那邊()
    {
        Assert.Equal(AgentBridge.Door.Refuse, Gate("POST", "/permission", header: Deck));
        Assert.Equal(AgentBridge.Door.Refuse, Gate("POST", "/", header: Deck, query: Deck));
        Assert.Equal(AgentBridge.Door.Refuse, Gate("GET", "/deck/now", query: Hook));
        Assert.Equal(AgentBridge.Door.Refuse, Gate("POST", "/deck/press", header: Hook, query: Hook));
    }

    [Fact]
    public void 會改變狀態的外掛請求只收POST()
    {
        Assert.Equal(AgentBridge.Door.Deck, Gate("GET", "/deck/now", query: Deck));
        Assert.Equal(AgentBridge.Door.Deck, Gate("GET", "/deck/cover", query: Deck, origin: true));
        Assert.Equal(AgentBridge.Door.Deck, Gate("POST", "/deck/press", query: Deck, origin: true));
        Assert.Equal(AgentBridge.Door.Deck, Gate("POST", "/deck/turn", query: Deck));
        // 一張 <img> 或一個連結發出的是 GET：不能替人按允許
        Assert.Equal(AgentBridge.Door.Refuse, Gate("GET", "/deck/press", query: Deck));
        Assert.Equal(AgentBridge.Door.Refuse, Gate("GET", "/deck/turn", query: Deck));
        Assert.Equal(AgentBridge.Door.Refuse, Gate("POST", "/deck/now", query: Deck));
    }

    [Fact]
    public void 金鑰是空的就誰都進不來()
    {
        Assert.Equal(AgentBridge.Door.Refuse, AgentBridge.Gate("GET", "/deck/now", null, "", false, "", ""));
        Assert.Equal(AgentBridge.Door.Refuse, AgentBridge.Gate("POST", "/permission", "", null, false, "", ""));
    }

    /// <summary>一次只吐一小塊、而且不說總共多長的串流，模擬分塊傳輸。</summary>
    private sealed class Drip(byte[] data) : MemoryStream(data)
    {
        public override bool CanSeek => false;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancel = default) => base.ReadAsync(buffer[..Math.Min(buffer.Length, 1000)], cancel);
    }

    [Fact]
    public async Task 內容超過上限就不收_就算沒有先說有多長()
    {
        Assert.Equal("{\"a\":1}", await AgentBridge.ReadLimitedAsync(new Drip(Encoding.UTF8.GetBytes("{\"a\":1}")), 100));
        Assert.Equal(new string('x', 5000), await AgentBridge.ReadLimitedAsync(new Drip(Encoding.UTF8.GetBytes(new string('x', 5000))), 5000));
        Assert.Null(await AgentBridge.ReadLimitedAsync(new Drip(new byte[5001]), 5000));
        Assert.Null(await AgentBridge.ReadLimitedAsync(new Drip(new byte[AgentBridge.MaxBody + 1]), AgentBridge.MaxBody));
    }
}

public class OpenerSafetyTests
{
    [Theory]
    [InlineData("http://localhost:47813/deck/press")]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://127.1.2.3:8080/x")]
    [InlineData("http://[::1]/")]
    [InlineData("http://0.0.0.0/")]
    [InlineData("http://192.168.1.1/")]
    [InlineData("http://10.0.0.5/admin")]
    [InlineData("http://172.16.0.1/")]
    [InlineData("http://172.31.255.254/")]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    [InlineData("http://[fe80::1]/")]
    [InlineData("http://[fd00::1]/")]
    [InlineData("http://[::ffff:127.0.0.1]/")]
    [InlineData("http://router.local/")]
    [InlineData("http://app.localhost/")]
    [InlineData("http://nas/")]
    public void 不開這台電腦或區域網路裡的網址(string url) => Assert.Equal(OpenKind.Refused, Opener.Classify(url).Kind);

    [Theory]
    [InlineData("https://example.com/")]
    [InlineData("https://172.32.0.1/")]
    [InlineData("https://8.8.8.8/")]
    [InlineData("github.com/Carinoasd/shima")]
    public void 一般的網址照常開(string url) => Assert.Equal(OpenKind.Url, Opener.Classify(url).Kind);

    [Fact]
    public void 白名單的執行檔只在固定的位置找()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "opener-" + Guid.NewGuid().ToString("N"));
        string system = Path.Combine(root, "System32"), windows = Path.Combine(root, "Windows"), local = Path.Combine(root, "Local");
        Directory.CreateDirectory(system);
        Directory.CreateDirectory(windows);
        Directory.CreateDirectory(Path.Combine(local, "Roon", "Application"));
        try
        {
            File.WriteAllText(Path.Combine(system, "notepad.exe"), "");
            File.WriteAllText(Path.Combine(windows, "explorer.exe"), "");
            File.WriteAllText(Path.Combine(local, "Roon", "Application", "roon.exe"), "");
            Assert.Equal(Path.Combine(system, "notepad.exe"), Opener.Locate("notepad.exe", system, windows, local));
            Assert.Equal(Path.Combine(windows, "explorer.exe"), Opener.Locate("explorer.exe", system, windows, local));
            Assert.Equal(Path.Combine(local, "Roon", "Application", "roon.exe"), Opener.Locate("roon.exe", system, windows, local));
            Assert.Null(Opener.Locate("calc.exe", system, windows, local));     // 沒裝：不退回去看目前的資料夾或 PATH
            Assert.Null(Opener.Locate("wt.exe", system, windows, local));
        }
        finally { Directory.Delete(root, true); }
    }
}

public class MascotRedrawTests
{
    private static int Sign(string kind, double now, bool dancing = false, bool happy = false, bool hop = false, double sinceBlink = 5, double gazeX = 0, double gazeY = 0, int frames = 0, int frame = 0) =>
        Mascot.Signature(kind, now, dancing, happy, hop, sinceBlink, gazeX, gazeY, frames, frame);

    [Fact]
    public void 幽靈閒著也在飄_所以每一格都要畫() => Assert.NotEqual(Sign("ghost", 10.00), Sign("ghost", 10.03));

    [Fact]
    public void 小Claude閒著時_十六格裡只有吸氣那兩格不一樣()
    {
        // 第 3 格到第 15 格畫出來一樣（每秒 8 格）
        Assert.Equal(Sign("claude", 3 / 8.0 + 0.01), Sign("claude", 10 / 8.0 + 0.01));
        Assert.NotEqual(Sign("claude", 0.01), Sign("claude", 3 / 8.0 + 0.01));
        // 跳舞時四格一輪
        Assert.NotEqual(Sign("claude", 0.01, dancing: true), Sign("claude", 1 / 8.0 + 0.01, dancing: true));
        Assert.Equal(Sign("claude", 0.01, dancing: true), Sign("claude", 4 / 8.0 + 0.01, dancing: true));
        // 眨眼、跟拍、眼睛移到下一個像素都要重畫
        Assert.NotEqual(Sign("claude", 1, sinceBlink: 5), Sign("claude", 1, sinceBlink: 0.07));
        Assert.NotEqual(Sign("claude", 1), Sign("claude", 1, hop: true));
        Assert.NotEqual(Sign("claude", 1, gazeX: 0.2), Sign("claude", 1, gazeX: 0.8));
        Assert.Equal(Sign("claude", 1, gazeX: 0.1), Sign("claude", 1, gazeX: 0.3));
    }

    [Fact]
    public void 自己的圖_只有一格的平常不重畫_很多格的照格數走()
    {
        Assert.Equal(Sign(Mascot.Custom, 1.0, frames: 1), Sign(Mascot.Custom, 9.0, frames: 1));
        Assert.Equal(Sign(Mascot.Custom, 1.0, frames: 6, frame: 2), Sign(Mascot.Custom, 1.03, frames: 6, frame: 2));
        Assert.NotEqual(Sign(Mascot.Custom, 1.0, frames: 6, frame: 2), Sign(Mascot.Custom, 1.1, frames: 6, frame: 3));
        Assert.NotEqual(Sign(Mascot.Custom, 1.0, dancing: true, frames: 1), Sign(Mascot.Custom, 1.03, dancing: true, frames: 1));
    }

    [Fact]
    public void 其他角色閒著時只有眼睛動才重畫()
    {
        Assert.Equal(Sign("cat", 1.0), Sign("cat", 7.0));
        Assert.NotEqual(Sign("cat", 1.0), Sign("cat", 1.0, gazeX: 0.5));
        Assert.NotEqual(Sign("cat", 1.0, dancing: true), Sign("cat", 1.03, dancing: true));
        Assert.NotEqual(Sign("eyes", 1.0, sinceBlink: 0.05), Sign("eyes", 1.03, sinceBlink: 0.08));
    }
}
