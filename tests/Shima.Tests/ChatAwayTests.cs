// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using Xunit;

namespace Shima.Tests;

/// <summary>聊天分頁最上層的網頁不在目前那個聊天網站自己的網域時，上方顯示主機名稱（島裡沒有網址列）。</summary>
public class ChatAwayTests
{
    private const string Claude = "https://claude.ai/new", Gemini = "https://gemini.google.com/app", ChatGpt = "https://chatgpt.com/";

    [Theory]
    [InlineData("https://claude.ai/new", Claude)]
    [InlineData("https://claude.ai/chat/123?x=1", Claude)]
    [InlineData("https://CLAUDE.AI/", Claude)]
    [InlineData("https://claude.ai./", Claude)]
    [InlineData("https://gemini.google.com/app/abc", Gemini)]
    [InlineData("https://chatgpt.com/c/1", ChatGpt)]
    [InlineData("https://cdn.chatgpt.com/x", ChatGpt)]   // 子網域也是自己的
    [InlineData("about:blank", Claude)]
    [InlineData("", Claude)]
    [InlineData(null, Claude)]
    [InlineData("blob:https://claude.ai/0f1e2d3c", Claude)]
    public void 在聊天網站自己的網域_不顯示(string? url, string site) => Assert.Null(ChatLinks.Away(url, site));

    [Theory]
    // 留在島裡的登入網域（OAuth 出錯時會轉回 redirect_uri）也要顯示：使用者要看得到自己在哪裡
    [InlineData("https://login.microsoftonline.com/common/oauth2/v2.0/authorize?redirect_uri=https://evil.example/", Claude, "login.microsoftonline.com")]
    [InlineData("https://evil.example/fake-login", Claude, "evil.example")]
    [InlineData("https://accounts.google.com/signin", Gemini, "accounts.google.com")]
    [InlineData("https://sites.google.com/view/fake", Gemini, "sites.google.com")]   // 同一個 google.com 底下，但不是 Gemini
    [InlineData("https://google.com/", Gemini, "google.com")]
    [InlineData("https://auth.openai.com/authorize", ChatGpt, "auth.openai.com")]
    [InlineData("https://claude.ai/new", ChatGpt, "claude.ai")]                  // 換了網站，原本的那個就不是自己的了
    [InlineData("https://evilclaude.ai/", Claude, "evilclaude.ai")]
    [InlineData("https://claude.ai.evil.example/", Claude, "claude.ai.evil.example")]
    [InlineData("http://claude.ai/", Claude, "claude.ai")]                      // 明文的不算自己的
    [InlineData("https://claude.ai@evil.example/", Claude, "evil.example")]
    [InlineData("https://xn--cude-1na.ai/", Claude, "xn--cude-1na.ai")]
    [InlineData("https://сlaude.ai/", Claude, "xn--laude-0ye.ai")]               // 西里爾字母 с：顯示 punycode
    [InlineData("blob:https://evil.example/1", Claude, "evil.example")]
    [InlineData("data:text/html,<h1>Sign in</h1>", Claude, "data:")]
    [InlineData("file:///C:/x.html", Claude, "file:")]
    public void 不在自己的網域_顯示主機名稱(string url, string site, string shown) => Assert.Equal(shown, ChatLinks.Away(url, site));
}
