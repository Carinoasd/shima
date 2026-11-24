// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Text.Json;
using Xunit;

namespace Shima.Tests;

/// <summary>ListenBrainz token 跟 AI 金鑰一樣用 DPAPI 加密保存。測試用的是新建的設定物件，不碰 Current。</summary>
public class ListenBrainzTokenTests
{
    private const string Token = "b0c1d2e3-0000-4abc-9def-123456789abc";

    [Fact]
    public void 新設定存成加密的_讀得回明文()
    {
        var s = new AppSettings();
        s.SetListenBrainzToken(Token);
        Assert.StartsWith("dpapi:", s.ListenBrainzToken);
        Assert.DoesNotContain(Token, s.ListenBrainzToken);
        Assert.Equal(Token, s.ListenBrainzTokenText());
        // 寫進設定檔的 JSON 也看不到明文
        Assert.DoesNotContain(Token, JsonSerializer.Serialize(s));
    }

    [Fact]
    public void 清空就是沒有_也不留下加密的空殼()
    {
        var s = new AppSettings();
        s.SetListenBrainzToken(Token);
        s.SetListenBrainzToken("");
        Assert.Equal("", s.ListenBrainzToken);
        Assert.Equal("", s.ListenBrainzTokenText());
    }

    [Fact]
    public void 舊版的明文被轉成加密的_只轉一次()
    {
        var s = new AppSettings { ListenBrainzToken = Token };
        Assert.True(s.EncryptListenBrainzToken());
        Assert.StartsWith("dpapi:", s.ListenBrainzToken);
        Assert.Equal(Token, s.ListenBrainzTokenText());
        var stored = s.ListenBrainzToken;
        Assert.False(s.EncryptListenBrainzToken());      // 已經是加密的：不會再加密一次
        Assert.Equal(stored, s.ListenBrainzToken);
        Assert.Equal(Token, s.ListenBrainzTokenText());
    }

    [Fact]
    public void 沒有token的設定不用轉()
    {
        Assert.False(new AppSettings().EncryptListenBrainzToken());
    }

    [Fact]
    public void 解不開_換了電腦或帳號_當成沒有token而不是丟例外()
    {
        // 格式對、內容不是這個帳號的 DPAPI 加的
        var s = new AppSettings { ListenBrainzToken = "dpapi:" + Convert.ToBase64String(new byte[64]) };
        Assert.Equal("", s.ListenBrainzTokenText());
        s.ListenBrainzToken = "dpapi:!!!不是base64";
        Assert.Equal("", s.ListenBrainzTokenText());
        // 還沒轉換的明文不能被當成 token 用（Load 會先轉換，這裡確認讀取端不會直接吐出來）
        s.ListenBrainzToken = Token;
        Assert.Equal("", s.ListenBrainzTokenText());
    }

    [Fact]
    public void 存的值變了_快取跟著重算()
    {
        var s = new AppSettings();
        s.SetListenBrainzToken("first");
        Assert.Equal("first", s.ListenBrainzTokenText());
        Assert.Equal("first", s.ListenBrainzTokenText());
        s.SetListenBrainzToken("second");
        Assert.Equal("second", s.ListenBrainzTokenText());
        s.ListenBrainzToken = "";   // 還原備份、或直接改掉
        Assert.Equal("", s.ListenBrainzTokenText());
    }

    [Fact]
    public void 備份把這個欄位當機密()
    {
        Assert.Contains("ListenBrainzToken", Backup.SecretFields);
    }
}
