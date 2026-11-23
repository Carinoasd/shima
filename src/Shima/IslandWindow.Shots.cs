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
            await Task.Delay(Demo.On ? 1500 : 7000);   // 等 Roon 連上、歌詞查到、封面載入（示範模式都是現成的）
            Refresh();

            _expanded = false;
            Snap();
            await Save(folder, "1-pill");
            if (_track is { } playing) File.Move(LyricCard.Save(MakeCard(playing), folder, "card"), Path.Combine(folder, "7-card.png"), true);
            await Zoom(folder, "0-mascot", Buddy, 12);

            foreach (var (tab, name) in new[] { (Tab.Music, "2-music"), (Tab.Tools, "3-tools"), (Tab.Ai, "4-ai") })
            {
                _expanded = true;
                (tab switch { Tab.Tools => TabTools, Tab.Ai => TabAi, _ => TabMusic }).IsChecked = true;
                _tab = tab;
                if (tab == Tab.Ai) RefreshAi();
                if (tab == Tab.Tools) RefreshTools();
                Snap();
                UpdateTexts();
                await Save(folder, name);
            }

            // 設定頁：四個子分頁各一張
            _expanded = true;
            TabSettings.IsChecked = true;
            _tab = Tab.Settings;
            foreach (var (section, name) in new[]
            {
                (SettingsSection.General, "5-settings-general"), (SettingsSection.Look, "5-settings-look"),
                (SettingsSection.Music, "5-settings-music"), (SettingsSection.Ai, "5-settings-ai"),
            })
            {
                _settingsSection = section;
                RefreshSettings();
                // 截圖不帶個人的資料：Roon 位址、ListenBrainz token、Discord ID。只改畫面上的字，沒有存檔
                foreach (var box in InputsIn<System.Windows.Controls.TextBox>(_settingsPages[SettingsSection.Music])) box.Text = "";
                // 密碼框也清：圓點的數量看得出 secret 有多長
                foreach (var box in InputsIn<System.Windows.Controls.PasswordBox>(_settingsPages[SettingsSection.Music])) box.Password = "";
                SettingsScroll.ScrollToVerticalOffset(0);
                Snap();
                UpdateTexts();
                await Save(folder, name);
            }
            _settingsSection = SettingsSection.General;

            // 聆聽放最後：後面的引導卡片與收藏截圖都接著這一頁
            TabLibrary.IsChecked = true;
            _tab = Tab.Library;
            RefreshLibrary();
            Snap();
            UpdateTexts();
            await Save(folder, "8-library");

            // 聆聽回顧卡片：本月的
            if (_stats != null)
            {
                _stats.Period = Listening.StatsPeriod.Month;
                if (_stats.Summary is { } summary && RecapCard.Render(summary, _stats.Period, _stats.Now, _track?.Cover) is { } recap)
                    File.Move(LyricCard.Save(recap, folder, "recap"), Path.Combine(folder, "b-recap.png"), true);
                _stats.Period = Listening.StatsPeriod.Week;
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

    private static IEnumerable<T> InputsIn<T>(DependencyObject root) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is T box) yield return box;
            foreach (var inner in InputsIn<T>(child)) yield return inner;
        }
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
        // 截圖模式的視窗不算看得見，頻譜的計時器不會自己走：直接推進到穩定的高度（沒在動的分頁則讓它沉到底）
        for (int i = 0; i < (Spectrum.Active ? 12 : 80); i++) Spectrum.Step();
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
