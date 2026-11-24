// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

namespace Shima;

/// <summary>
/// 聊天分頁的內嵌瀏覽器沒有網址列：聊天內容裡的連結如果能在島裡開出一個長得像登入頁的地方，使用者分辨不出來。
/// 所以「要開新視窗」的網址只有聊天網站自己與登入流程會用到的網域才留在島裡，其他交給預設瀏覽器。
/// </summary>
public static class ChatLinks
{
    public enum Where
    {
        /// <summary>留在島裡的瀏覽器開。</summary>
        Island,
        /// <summary>交給使用者預設的瀏覽器。</summary>
        Browser,
        /// <summary>不是 http／https 或根本不是網址：什麼都不做。</summary>
        Nowhere,
    }

    /// <summary>這些網域與它們的所有子網域（Sites 的三個網站各自的登入流程會經過的地方）。</summary>
    private static readonly string[] Trusted =
    [
        // Claude
        "claude.ai", "claude.com", "anthropic.com",
        // ChatGPT（auth.openai.com、auth0.openai.com 是 openai.com 的子網域）
        "chatgpt.com", "openai.com",
        // Gemini 與 Google 登入
        "gemini.google.com", "accounts.google.com", "accounts.youtube.com",
        // 第三方登入
        "appleid.apple.com", "idmsa.apple.com", "login.microsoftonline.com", "login.live.com", "github.com",
        // 登入頁的人機驗證
        "challenges.cloudflare.com",
    ];

    public static Where Decide(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.Host.Length == 0) return Where.Nowhere;
        // 帶帳號密碼的網址（https://claude.ai@evil.example）看起來像一個網站、實際是另一個：一律交給外面
        if (uri.Scheme == Uri.UriSchemeHttps && uri.UserInfo.Length == 0 && Trusted.Any(d => InDomain(uri.IdnHost, d))) return Where.Island;
        return Where.Browser;
    }

    /// <summary>
    /// 聊天分頁最上層的網頁現在在哪裡。在目前選的聊天網站自己的網域（<paramref name="site"/> 的主機與它的子網域、https）是 null，不用顯示；
    /// 不在的話是要顯示的主機名稱（IDN 用 punycode：長得像的網域才分得出來）。
    /// 留在島裡的登入網域可以轉到任何地方（OAuth 出錯時轉回 redirect_uri），所以不看「留在島裡」的清單，只看是不是聊天網站自己。
    /// 不是 http／https 的：about:blank 不顯示；blob: 看它裡面的來源；其他的（data:、file:…）顯示協定。
    /// </summary>
    public static string? Away(string? url, string site)
    {
        if (string.IsNullOrEmpty(url) || !Uri.TryCreate(site, UriKind.Absolute, out var home)) return null;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme == "about") return null;
        if (uri.Scheme == "blob")
            return Uri.TryCreate(url[(url.IndexOf(':') + 1)..], UriKind.Absolute, out var inner) && inner.Scheme is "http" or "https" ? Away(inner.AbsoluteUri, site) : "blob:";
        if (uri.Scheme is not ("http" or "https")) return uri.Scheme + ":";
        var host = uri.IdnHost.TrimEnd('.').ToLowerInvariant();
        return uri.Scheme == Uri.UriSchemeHttps && InDomain(host, home.IdnHost) ? null : host;
    }

    /// <summary>host 就是 domain，或是 domain 的子網域（要有那個點：evilclaude.ai 不算）。</summary>
    private static bool InDomain(string host, string domain)
    {
        host = host.TrimEnd('.');
        return host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase);
    }
}
