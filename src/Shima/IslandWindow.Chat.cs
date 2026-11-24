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
    private string? _chatAway;   // 上方那一列顯示的是哪一頁（離開聊天網站時）

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
                ShowChatAway(_web?.CoreWebView2?.Source);   // 換了網站，「自己的網域」也換了
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

    /// <summary>聊天分頁上方那一列：不在聊天網站自己的網域時顯示主機名稱，回到自己的網域就收起來。聊天分頁的高度是固定的，不用重新變形。</summary>
    private void ShowChatAway(string? url)
    {
        var host = ChatLinks.Away(url, Site().Url);
        _chatAway = host == null ? null : url;
        ChatAwayHost.Text = host ?? "";
        ChatAway.Visibility = host == null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ChatAwayOpen_Click(object sender, RoutedEventArgs e)
    {
        if (_chatAway != null) OpenOutside(_chatAway);
    }

    /// <summary>交給預設的瀏覽器開。只開 http／https。</summary>
    private static void OpenOutside(string url)
    {
        if (ChatLinks.Decide(url) == ChatLinks.Where.Nowhere) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(new Uri(url).AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Write("chat: " + ex.Message); }
    }

    /// <summary>第一次打開聊天分頁時才啟動瀏覽器（它要用不少記憶體）。</summary>
    private async Task EnsureChatAsync()
    {
        if (_web != null || _webStarting) return;
        // 截圖與啟動檢查不載入聊天網站：不該對外發請求，載入的進度也會讓每次拍出來的圖不一樣
        if (_smoke) return;
        _webStarting = true;
        WebView2CompositionControl? created = null;
        try
        {
            // 示範模式不能用真正的瀏覽器設定檔：裡面有登入的 cookie，兩份 Shima 同時開還會搶同一個設定檔的鎖
            var data = Demo.On ? Path.Combine(AppSettings.Folder, "WebView2")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Shima", "WebView2");
            var environment = await CoreWebView2Environment.CreateAsync(null, data);
            var web = created = new WebView2CompositionControl();
            _chatFrame.Children.Add(web);   // 隔一層反向縮放，島的大小不是 100% 時瀏覽器才清楚（IslandWindow.Scale.cs）
            ChatHost.Child = _chatFrame;
            await web.EnsureCoreWebView2Async(environment);
            // 登入流程常會開新視窗：聊天網站與登入用的網域留在同一頁裡，其他的交給預設瀏覽器（這個瀏覽器沒有網址列，分辨不出假的登入頁）。
            // 只管「新視窗」；同一個視窗裡的導覽（登入時的轉址）不限制
            web.CoreWebView2.NewWindowRequested += (_, e) =>
            {
                e.Handled = true;
                switch (ChatLinks.Decide(e.Uri))
                {
                    case ChatLinks.Where.Island: web.CoreWebView2.Navigate(e.Uri); break;
                    case ChatLinks.Where.Browser: OpenOutside(e.Uri); break;
                }
            };
            // 同一個視窗裡的導覽不限制（企業的單一登入會經過清單以外的網域），但最上層的網頁離開聊天網站自己的網域時，上方顯示現在的主機。
            // NavigationStarting 連轉址也會來（OAuth 出錯轉回 redirect_uri）；沒走完的導覽由 NavigationCompleted 換回實際的那一頁
            web.CoreWebView2.NavigationStarting += (_, e) => ShowChatAway(e.Uri);
            web.CoreWebView2.SourceChanged += (_, _) => ShowChatAway(web.CoreWebView2.Source);
            web.CoreWebView2.NavigationCompleted += (_, _) => ShowChatAway(web.CoreWebView2.Source);
            web.CoreWebView2.Navigate(Site().Url);
            _web = web;
            ScaleChat();
        }
        catch (Exception e)
        {
            Log.Write("chat: " + e.Message);
            // 已經加進去的控制項要拿掉：下次打開聊天分頁會重試，不拿掉就一次疊一個
            if (created != null) { _chatFrame.Children.Remove(created); if (ReferenceEquals(_web, created)) _web = null; try { created.Dispose(); } catch { } }
            ChatHost.Child = new TextBlock
            {
                Text = L.T("無法啟動內嵌瀏覽器（需要 Microsoft Edge WebView2）"), Foreground = Brushes.White, FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
        }
        finally { _webStarting = false; }
    }
}
