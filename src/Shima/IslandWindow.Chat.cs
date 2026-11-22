// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Shima;

/// <summary>聊天分頁：把 Claude、Gemini、ChatGPT 的網頁版放進島裡。登入一次就好，不需要 API 金鑰。</summary>
public partial class IslandWindow
{
    private static readonly (string Id, string Name, string Url)[] Sites =
    [
        ("claude", "Claude", "https://claude.ai/new"),
        ("gemini", "Gemini", "https://gemini.google.com/app"),
        ("chatgpt", "ChatGPT", "https://chatgpt.com/"),
    ];

    private WebView2CompositionControl? _web;
    private bool _webStarting;

    private void InitChat()
    {
        foreach (var (id, name, _) in Sites)
        {
            var button = new Button { Content = name, Tag = id, Style = (Style)FindResource("Pill"), Height = 28, Margin = new Thickness(3, 0, 3, 0) };
            button.Click += (_, _) =>
            {
                AppSettings.Current.ChatSite = id;
                AppSettings.Current.Save();
                MarkChatSite();
                _web?.CoreWebView2?.Navigate(Site().Url);
            };
            ChatSites.Children.Add(button);
        }
        MarkChatSite();
        ChatHost.SizeChanged += (_, _) =>
            ChatHost.Clip = new RectangleGeometry(new Rect(0, 0, ChatHost.ActualWidth, ChatHost.ActualHeight), 12, 12);
    }

    private static (string Id, string Name, string Url) Site() =>
        Sites.FirstOrDefault(s => s.Id == AppSettings.Current.ChatSite) is { Id: not null } site ? site : Sites[0];

    private void MarkChatSite()
    {
        foreach (Button b in ChatSites.Children)
        {
            bool on = (string)b.Tag == Site().Id;
            b.Background = on ? Brushes.White : new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF));
            b.Foreground = on ? Brushes.Black : Brushes.White;
        }
    }

    /// <summary>第一次打開聊天分頁時才啟動瀏覽器（它要用不少記憶體）。</summary>
    private async Task EnsureChatAsync()
    {
        if (_web != null || _webStarting) return;
        _webStarting = true;
        try
        {
            var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Shima", "WebView2");
            var environment = await CoreWebView2Environment.CreateAsync(null, data);
            var web = new WebView2CompositionControl();
            ChatHost.Child = web;
            await web.EnsureCoreWebView2Async(environment);
            // 登入流程常會開新視窗：留在同一頁裡
            web.CoreWebView2.NewWindowRequested += (_, e) => { e.Handled = true; web.CoreWebView2.Navigate(e.Uri); };
            web.CoreWebView2.Navigate(Site().Url);
            _web = web;
        }
        catch (Exception e)
        {
            Log.Write("chat: " + e.Message);
            ChatHost.Child = new TextBlock
            {
                Text = L.T("無法啟動內嵌瀏覽器（需要 Microsoft Edge WebView2）"), Foreground = Brushes.White, FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
        }
        finally { _webStarting = false; }
    }
}
