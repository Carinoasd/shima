// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using Xunit;

namespace Shima.Tests;

public class ChatLinksTests
{
    [Theory]
    [InlineData("https://claude.ai/new")]
    [InlineData("https://claude.ai")]
    [InlineData("https://CLAUDE.AI/chat/1")]
    [InlineData("https://console.anthropic.com/login")]
    [InlineData("https://anthropic.com/")]
    [InlineData("https://chatgpt.com/auth/login")]
    [InlineData("https://auth.openai.com/authorize?x=1")]
    [InlineData("https://auth0.openai.com/u/login")]
    [InlineData("https://gemini.google.com/app")]
    [InlineData("https://accounts.google.com/o/oauth2/v2/auth?client_id=1")]
    [InlineData("https://accounts.youtube.com/accounts/SetSID")]
    [InlineData("https://appleid.apple.com/auth/authorize")]
    [InlineData("https://login.microsoftonline.com/common/oauth2/v2.0/authorize")]
    [InlineData("https://login.live.com/oauth20_authorize.srf")]
    [InlineData("https://github.com/login/oauth/authorize")]
    [InlineData("https://challenges.cloudflare.com/cdn-cgi/challenge-platform/h/b")]
    [InlineData("https://claude.ai./new")]            // 結尾的點
    [InlineData("https://claude.ai:443/new")]
    public void 聊天網站與登入網域留在島裡(string url) => Assert.Equal(ChatLinks.Where.Island, ChatLinks.Decide(url));

    [Theory]
    [InlineData("https://claude.ai.evil.example/login")]     // 長得像的網域
    [InlineData("https://evilclaude.ai/login")]
    [InlineData("https://claude.ai-login.com/")]
    [InlineData("https://notanthropic.com/")]
    [InlineData("https://evil.example/claude.ai")]
    [InlineData("https://evil.example/?u=https://claude.ai")]
    [InlineData("https://www.google.com/")]                 // google.com 本身不在清單
    [InlineData("https://gemini.google.com.evil.example/")]
    [InlineData("https://claude.ai@evil.example/login")]    // userinfo：實際的主機是 evil.example
    [InlineData("https://user:pw@claude.ai/new")]           // 主機是對的，但帶帳號密碼也不留
    [InlineData("http://claude.ai/new")]                    // 不是 https 不留在島裡
    [InlineData("http://evil.example/")]
    [InlineData("https://example.com/")]
    public void 其他網址交給預設瀏覽器(string url) => Assert.Equal(ChatLinks.Where.Browser, ChatLinks.Decide(url));

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("ms-settings:")]
    [InlineData("data:text/html,<b>x</b>")]
    [InlineData("ftp://claude.ai/x")]
    [InlineData("claude://open")]
    [InlineData("about:blank")]
    [InlineData("//claude.ai/new")]
    [InlineData("claude.ai/new")]
    [InlineData("")]
    [InlineData(null)]
    public void 不是_http_也不是網址_什麼都不做(string? url) => Assert.Equal(ChatLinks.Where.Nowhere, ChatLinks.Decide(url));
}
