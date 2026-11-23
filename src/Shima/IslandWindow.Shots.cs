// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Shima;

/// <summary>
/// 「--shot 資料夾」：不顯示在螢幕上，把膠囊和每個分頁各存成一張 PNG 就結束。
/// 給 README 的截圖用，也用來檢查畫面有沒有跑掉。
/// </summary>
public partial class IslandWindow
{
    private async Task ShootAsync(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            await Task.Delay(7000);   // 等 Roon 連上、歌詞查到、封面載入
            Refresh();

            _expanded = false;
            Snap();
            await Save(folder, "1-pill");
            if (_track is { } playing) File.Move(LyricCard.Save(MakeCard(playing), folder, "card"), Path.Combine(folder, "7-card.png"), true);
            await Zoom(folder, "0-mascot", Buddy, 12);

            foreach (var (tab, name) in new[] { (Tab.Music, "2-music"), (Tab.Tools, "3-tools"), (Tab.Ai, "4-ai"), (Tab.Settings, "5-settings"), (Tab.Library, "8-library") })
            {
                _expanded = true;
                (tab switch { Tab.Tools => TabTools, Tab.Ai => TabAi, Tab.Settings => TabSettings, Tab.Library => TabLibrary, _ => TabMusic }).IsChecked = true;
                _tab = tab;
                if (tab == Tab.Ai) RefreshAi();
                if (tab == Tab.Settings) { RefreshSettings(); DiscordBox.Text = ""; }   // 截圖不帶個人的 ID
                if (tab == Tab.Tools) RefreshTools();
                if (tab == Tab.Library) RefreshLibrary();
                Snap();
                UpdateTexts();
                await Save(folder, name);
            }

            WelcomeCard(); Snap(); await Save(folder, "a-welcome");
            WelcomeHost.Children.Clear(); WelcomeHost.Visibility = Visibility.Collapsed;
            _libraryShowsFavorites = true; RefreshLibrary(); Snap(); await Save(folder, "8-favorites");
            TabMusic.IsChecked = true; _tab = Tab.Music;
            Queue_Click(this, new RoutedEventArgs()); Snap(); await Task.Delay(1500); await Save(folder, "9-queue");
            Queue_Click(this, new RoutedEventArgs());

            AskTitle.Text = "Claude Code · shima";
            AskTool.Text = "Bash";
            AskDetails.Text = "dotnet test tests/Shima.Tests/Shima.Tests.csproj";
            _tab = Tab.Ask;
            Snap();
            await Save(folder, "6-ask");
            Log.Write("shots: " + folder);
        }
        catch (Exception e) { Log.Write("shots: " + e); }
        Close();
    }

    /// <summary>不跑動畫，直接變成目標的大小。</summary>
    private void Snap()
    {
        var open = OpenSize();
        _w.Jump(_expanded ? open.Width : _pillWidth);
        _h.Jump(_expanded ? open.Height : PillHeight);
        Height = Math.Max(BaseWindowHeight, 8 + _h.Value + 48);
        Panel.IsHitTestVisible = _expanded;
        Apply();
        LyricsList.Active = _expanded && _tab == Tab.Music;
        UpdateLayout();
    }

    /// <summary>把一個小元件放大存下來，檢查像素有沒有畫對。</summary>
    private async Task Zoom(string folder, string name, FrameworkElement element, double scale)
    {
        await Task.Delay(100);
        var visual = new DrawingVisual();
        RenderOptions.SetEdgeMode(visual, EdgeMode.Aliased);
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, element.ActualWidth, element.ActualHeight));
            dc.DrawRectangle(new VisualBrush(element) { Stretch = Stretch.None }, null, new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        }
        var bitmap = new RenderTargetBitmap((int)(element.ActualWidth * scale), (int)(element.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(folder, name + ".png"));
        encoder.Save(file);
    }

    private async Task Save(string folder, string name)
    {
        await Task.Delay(350);   // 讓版面和自己畫的元件都畫完
        UpdateLayout();
        const double pad = 28, scale = 2;
        double w = Host.ActualWidth + pad * 2, h = Host.ActualHeight + pad * 2;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            // 墊一塊桌面色，島的陰影和圓角才看得出來
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x3A, 0x48, 0x5C)), null, new Rect(0, 0, w, h));
            dc.DrawRectangle(new VisualBrush(Host) { Stretch = Stretch.None }, null, new Rect(pad, pad, Host.ActualWidth, Host.ActualHeight));
        }
        var bitmap = new RenderTargetBitmap((int)(w * scale), (int)(h * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(folder, name + ".png"));
        encoder.Save(file);
    }
}
