// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using Shima.Agents;
using Xunit;

namespace Shima.Tests;

/// <summary>自訂 AI 位址的 http://：區網的主機名稱、Tailscale、IPv6 的本機與區網位址照常可以用；公開網路上的照舊要 https。</summary>
public class CustomUrlLanTests
{
    [Theory]
    [InlineData("http://nas:11434/v1")]
    [InlineData("http://NAS/v1")]
    [InlineData("http://ollama./v1")]
    [InlineData("http://ollama.local:11434/v1")]
    [InlineData("http://gpu-box.lan/v1")]
    [InlineData("http://pc.home.arpa:8080/v1")]
    [InlineData("http://llm.internal/v1")]
    [InlineData("http://100.64.0.1/v1")]
    [InlineData("http://100.101.102.103:11434/v1")]
    [InlineData("http://100.127.255.254/v1")]
    [InlineData("http://[::1]:11434/v1")]
    [InlineData("http://[fd7a:115c:a1e0::1]:11434/v1")]   // Tailscale 的 IPv6 是 ULA
    [InlineData("http://[fc00::1]/v1")]
    [InlineData("http://[fe80::1]/v1")]
    [InlineData("http://[::ffff:192.168.1.5]/v1")]
    public void 區網與本機的寫法照常送(string url) => Assert.True(AiService.Sendable(url));

    [Theory]
    [InlineData("http://100.63.255.255/v1")]
    [InlineData("http://100.128.0.1/v1")]
    [InlineData("http://nas.example.com/v1")]
    [InlineData("http://evil.com/local")]
    [InlineData("http://local.evil.com/v1")]
    [InlineData("http://lan.evil.example/v1")]
    [InlineData("http://evil-home.arpa/v1")]
    [InlineData("http://home.arpa/v1")]
    [InlineData("http://[2001:db8::1]/v1")]
    [InlineData("http://[2606:4700::1111]/v1")]
    [InlineData("http://[::ffff:8.8.8.8]/v1")]
    [InlineData("http://my.localhost/v1")]
    public void 公開網路上的照舊不送(string url) => Assert.False(AiService.Sendable(url));
}
