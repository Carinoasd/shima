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
                if (tab == Tab.Ai) { RefreshAi(); if (Demo.On) Say(Demo.Answer); }
                if (tab == Tab.Tools) RefreshTools();
                Snap();
                UpdateTexts();
                await Save(folder, name);
            }

            // 用量圖：AI 分頁點開那一行，拍完收起來
            if (Demo.On)
            {
                _expanded = true;
                TabAi.IsChecked = true;
                _tab = Tab.Ai;
                RefreshAi();
                ToggleUsage(true);
                Snap();
                UpdateTexts();
                await Save(folder, "c-usage");
                ToggleUsage(false);
            }

            // 長的回答：一次到齊時要停在開頭、底部淡出
            if (Demo.On)
            {
                Say(Demo.LongAnswer);
                Snap();
                UpdateTexts();
                await Save(folder, "c-ai-long");
                Say(Demo.Answer);
            }

            // 最近的通知：打開清單拍一張，拍完關掉
            if (Demo.On)
            {
                _expanded = true;
                TabTools.IsChecked = true;
                _tab = Tab.Tools;
                SetRecentOpen(true);
                Snap();
                UpdateTexts();
                await Save(folder, "c-recent");
                SetRecentOpen(false);
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

            // 外觀的三個選項：只改記憶體裡的設定值，拍完改回來，不存檔
            {
                var look = AppSettings.Current;
                var (mode, custom, lines, progress) = (look.AccentMode, look.AccentCustom, look.LyricsLines, look.PillProgress);

                // 自訂顏色選中時的「外觀」子分頁：色碼輸入框與那一排圓點
                look.AccentMode = "custom"; look.AccentCustom = "#4FD1C5";
                ApplyTheme();
                _settingsSection = SettingsSection.Look;
                RefreshSettings();
                Snap(); UpdateTexts();
                await Save(folder, "c-settings-accent-custom");
                _settingsSection = SettingsSection.General;

                TabMusic.IsChecked = true; _tab = Tab.Music;
                look.AccentMode = "sakura";
                ApplyTheme();
                Snap(); UpdateTexts();
                await Save(folder, "c-accent");
                (look.AccentMode, look.AccentCustom) = (mode, custom);
                ApplyTheme();

                foreach (var n in new[] { 7, 3 })
                {
                    look.LyricsLines = n;
                    ApplyLyricsLines();
                    Snap(); UpdateTexts();
                    await Save(folder, $"c-lyrics-{n}");
                }
                look.LyricsLines = lines;
                ApplyLyricsLines();

                look.PillProgress = true;
                _expanded = false;
                Pill.Visibility = Visibility.Visible;   // 前面展開過，膠囊可能被收起來了
                _pillProgressShown = -1;
                Snap(); UpdatePillProgress(); UpdateTexts();
                await Save(folder, "c-pill-progress");
                await Zoom(folder, "c-pill-progress-zoom", Host, 6);
                look.PillProgress = progress;
                UpdatePillProgress();
                _expanded = true;
            }

            // 島的大小：只改記憶體裡的值，拍完改回 1，不存檔
            {
                var look = AppSettings.Current;
                look.Scale = 1.3;
                _expanded = false;
                Pill.Visibility = Visibility.Visible;
                ApplyScale();
                Snap(); UpdateTexts();
                await Save(folder, "c-scale-130-pill");

                _expanded = true;
                TabMusic.IsChecked = true; _tab = Tab.Music;
                Snap(); UpdateTexts();
                await Save(folder, "c-scale-130-music");

                TabSettings.IsChecked = true; _tab = Tab.Settings;
                _settingsSection = SettingsSection.Look;
                RefreshSettings();
                SettingsScroll.ScrollToVerticalOffset(0);
                Snap(); UpdateTexts();
                await Save(folder, "c-scale-130-settings");
                _settingsSection = SettingsSection.General;

                look.Scale = 0.9;
                ApplyScale();
                TabMusic.IsChecked = true; _tab = Tab.Music;
                Snap(); UpdateTexts();
                await Save(folder, "c-scale-90-music");

                look.Scale = 1;
                ApplyScale();
            }

            // 聆聽放最後：後面的引導卡片與收藏截圖都接著這一頁
            TabLibrary.IsChecked = true;
            _tab = Tab.Library;
            RefreshLibrary();
            Snap();
            UpdateTexts();
            await Save(folder, "8-library");

            // 聆聽統計：年的熱力圖、月的一天當中幾點在聽
            if (_stats != null)
            {
                _stats.Period = Listening.StatsPeriod.Year;
                Snap(); UpdateTexts(); await Save(folder, "c-stats-year");
                _stats.Period = Listening.StatsPeriod.Month;
                _stats.ShowHours = true;
                Snap(); UpdateTexts(); await Save(folder, "c-stats-hours");
                _stats.ShowHours = false;
                _stats.Period = Listening.StatsPeriod.Week;
            }

            // 聆聽統計：翻看過去的期間
            if (_stats != null)
            {
                _stats.Period = Listening.StatsPeriod.Month;
                _stats.Back = 1;
                Snap(); UpdateTexts(); await Save(folder, "c-stats-prev-month");
                _stats.Period = Listening.StatsPeriod.Week;
                _stats.Back = 2;
                Snap(); UpdateTexts(); await Save(folder, "c-stats-prev-week");
                _stats.Period = Listening.StatsPeriod.Month;
                _stats.Period = Listening.StatsPeriod.Week;
            }

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

            // 沒在播歌的膠囊：示範模式永遠有歌，暫時拿掉，四種顯示各拍一張；只動記憶體，拍完還原
            if (Demo.On)
            {
                var song = _media.Demo;
                var shows = AppSettings.Current.IdleShows;
                _media.Demo = null;
                _expanded = false;
                foreach (var (mode, name) in new[] { (IdleLine.Clock, "c-idle-clock"), (IdleLine.Date, "c-idle-date"), (IdleLine.Alarm, "c-idle-alarm"), (IdleLine.None, "c-idle-none") })
                {
                    AppSettings.Current.IdleShows = mode;
                    Refresh();
                    Snap();
                    await Task.Delay(500);   // 換字的動畫看不見的視窗不會自己走：等它過了再重畫一次
                    PillLine.InvalidateVisual();
                    await Save(folder, name);
                }
                AppSettings.Current.IdleShows = shows;
                _media.Demo = song;
                Refresh();
                Snap();
            }

            // 月曆：滑鼠停在有提醒的那一天
            if (Demo.On && _alarms?.List.Count > 0)
            {
                var days = Shima.Agents.Alarms.InMonth(_alarms.List, DateTime.Today, DateTime.Now);
                _expanded = true;
                TabTools.IsChecked = true;
                _tab = Tab.Tools;
                RefreshTools();
                Snap();
                if (days.Count > 0) Month.Peek(days.Keys.First());
                await Save(folder, "c-month-tip");
                Month.Peek(0);
            }

            // 歌詞第二行選「讀音」：只改記憶體裡的設定值，拍完改回來，不存檔
            if (Demo.On && Lyrics.Furigana.Available)
            {
                var second = AppSettings.Current.LyricsSecondLine;
                AppSettings.Current.LyricsSecondLine = "furigana";
                _expanded = true;
                TabMusic.IsChecked = true;
                _tab = Tab.Music;
                Snap(); UpdateTexts();
                await RubyReady();
                // 讀音是背景算的；剛好在間奏或沒有漢字的句子就等到下一句有讀音的
                for (int wait = 0; wait < 100 && !RubyOnCurrentLine(); wait++) { await Task.Delay(100); UpdateTexts(); }
                // 截圖的視窗不算看得見，捲動不會自己走：重給一次歌詞，直接跳到正在唱的那句
                LyricsList.Show(null, null);
                Snap(); UpdateTexts();
                await Save(folder, "c-furigana");
                AppSettings.Current.LyricsSecondLine = second;
                Refresh();
            }
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
        Height = WindowHeightFor(_h.Value);
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
        // 島的大小不是 100% 時：VisualBrush 只畫 Host 自己，不含 Root 上的縮放，要自己乘回去（連墊的邊一起，陰影也跟著變大）
        double w = (Host.ActualWidth + pad * 2) * _scale, h = (Host.ActualHeight + pad * 2) * _scale;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            // 墊一塊桌面色，島的陰影和圓角才看得出來
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x3A, 0x48, 0x5C)), null, new Rect(0, 0, w, h));
            if (_scale != 1) dc.PushTransform(new ScaleTransform(_scale, _scale));
            dc.DrawRectangle(new VisualBrush(Host) { Stretch = Stretch.None }, null, new Rect(pad, pad, Host.ActualWidth, Host.ActualHeight));
            if (_scale != 1) dc.Pop();
        }
        var bitmap = new RenderTargetBitmap((int)(w * scale), (int)(h * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(folder, name + ".png"));
        encoder.Save(file);
    }
}
