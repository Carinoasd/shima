// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Shima.Agents;

namespace Shima;

/// <summary>工具分頁：番茄鐘、暫存架（檔案、文字、圖片）、提醒、待辦清單。</summary>
public partial class IslandWindow
{
    private Pomodoro _pomodoro = new()
    {
        Focus = TimeSpan.FromMinutes(Math.Clamp(AppSettings.Current.FocusMinutes, 1, 180)),
        Break = TimeSpan.FromMinutes(Math.Clamp(AppSettings.Current.BreakMinutes, 1, 60)),
    };

    private void InitTools()
    {
        Host.AllowDrop = true;
        Host.DragEnter += (_, e) =>
        {
            if (!Shelfable(e.Data)) return;
            _close.Stop();
            TabTools.IsChecked = true;
            _expanded = true;
            ShowTab(Tab.Tools);
        };
        Host.DragOver += (_, e) =>
        {
            e.Effects = !Shelfable(e.Data) ? DragDropEffects.None
                : e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Link
                : e.AllowedEffects.HasFlag(DragDropEffects.Copy) ? DragDropEffects.Copy : DragDropEffects.Link;
            e.Handled = true;
        };
        Host.Drop += (_, e) => AddToShelf(e.Data);
        RefreshTools();
    }

    private void RefreshTools()
    {
        // 暫存架：檔案放的是位置；文字與圖片是 shelf 資料夾裡的複本
        ShelfBox.Children.Clear();
        foreach (var path in AppSettings.Current.Shelf.ToList())
        {
            var kind = Shelf.Classify(path, Shelf.Folder);
            var thumb = kind == ShelfKind.Image ? Thumbnail(path) : null;
            var chip = new Button { Style = (Style)FindResource("Pill"), Height = 28, Margin = new Thickness(0, 0, 6, 6), MaxWidth = 190 };
            if (thumb != null) chip.Content = new Image { Source = thumb, Height = 20, MaxWidth = 64, Stretch = Stretch.Uniform };
            else chip.Content = Shelf.Label(path, kind) is { Length: > 0 } label ? label : Path.GetFileName(path);
            chip.ToolTip = kind == ShelfKind.File ? path : null;
            Point? pressed = null;
            chip.Click += (_, _) => ShelfClick(path, kind);
            chip.MouseRightButtonUp += (_, e) => { e.Handled = true; RemoveFromShelf(path); };
            // 拖出去：把檔案交給別的程式；文字另外附上文字格式
            chip.PreviewMouseLeftButtonDown += (_, e) => pressed = e.GetPosition(this);
            chip.PreviewMouseMove += (_, e) =>
            {
                if (pressed is not { } from || e.LeftButton != MouseButtonState.Pressed) return;
                if ((e.GetPosition(this) - from).Length < 8) return;
                pressed = null;
                if (!File.Exists(path) && !Directory.Exists(path)) return;
                var data = new DataObject(DataFormats.FileDrop, new[] { path });
                if (kind == ShelfKind.Text && Shelf.ReadText(path) is { } text) data.SetText(text);
                DragDrop.DoDragDrop(chip, data, DragDropEffects.Copy | DragDropEffects.Link | DragDropEffects.Move);
            };
            ShelfBox.Children.Add(chip);
        }
        ShelfHint.Visibility = AppSettings.Current.Shelf.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        TodoBox.Children.Clear();
        foreach (var todo in AppSettings.Current.Todos.ToList())
        {
            var row = new Grid { Height = 28 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            var check = new Button { Content = todo.Done ? "" : "", Style = (Style)FindResource("Key"), Width = 28, Height = 28, FontSize = 14 };
            check.Click += (_, _) => { todo.Done = !todo.Done; AppSettings.Current.Save(); RefreshTools(); };
            var text = new TextBlock
            {
                Text = todo.Text, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(6, 0, 0, 0),
                Foreground = todo.Done ? new SolidColorBrush(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF)) : Brushes.White,
                TextDecorations = todo.Done ? TextDecorations.Strikethrough : null,
            };
            Grid.SetColumn(text, 1);
            row.Children.Add(check);
            row.Children.Add(text);
            row.MouseRightButtonUp += (_, e) => { e.Handled = true; AppSettings.Current.Todos.Remove(todo); AppSettings.Current.Save(); RefreshTools(); };
            row.Background = Brushes.Transparent;
            TodoBox.Children.Add(row);
        }
        RefreshAlarms();
        UpdateTimer();
        Month.Today = DateTime.Today;
        MonthTitle.Text = DateTime.Today.ToString("Y", Loc.Culture);
        Month.InvalidateVisual();
        if (_tab == Tab.Tools && _expanded) Morph();
    }

    private void TodoInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || TodoInput.Text.Trim() is not { Length: > 0 } text) return;
        AppSettings.Current.Todos.Add(new TodoItem { Text = text });
        AppSettings.Current.Save();
        TodoInput.Text = "";
        RefreshTools();
    }

    // ---------------------------------------------------------------- 暫存架：檔案、文字、圖片

    private static bool Shelfable(IDataObject data) =>
        data.GetDataPresent(DataFormats.FileDrop) || data.GetDataPresent(DataFormats.Bitmap) || data.GetDataPresent(DataFormats.UnicodeText);

    /// <summary>把拖進來或剪貼簿裡的東西放上暫存架：檔案優先，其次圖片，最後文字。</summary>
    private void AddToShelf(IDataObject data)
    {
        var shelf = AppSettings.Current.Shelf;
        var added = false;
        try
        {
            if (data.GetData(DataFormats.FileDrop) is string[] paths)
            {
                foreach (var path in paths)
                {
                    if (shelf.Contains(path)) continue;
                    if (shelf.Count >= Shelf.MaxItems) { Toast(L.T("暫存架滿了"), 4, false); break; }
                    shelf.Add(path);
                    added = true;
                }
            }
            else if (shelf.Count >= Shelf.MaxItems) Toast(L.T("暫存架滿了"), 4, false);
            else if (ReadImage(data) is { } image)
            {
                if (Shelf.SaveImage(image, Shelf.Folder, DateTime.Now) is { } saved) { shelf.Add(saved); added = true; }
            }
            else if (data.GetData(DataFormats.UnicodeText) as string is { } text)
            {
                if (Shelf.SaveText(text, Shelf.Folder, DateTime.Now) is { } saved) { shelf.Add(saved); added = true; }
            }
        }
        catch (Exception x) { Log.Write("shelf add: " + x.Message); }
        if (!added) return;
        AppSettings.Current.Save();
        RefreshTools();
    }

    /// <summary>取出圖片。有 PNG 格式就用它（保留透明）；否則用一般點陣圖，並去掉可能全為 0 的透明度，免得存出一張看不見的圖。</summary>
    private static BitmapSource? ReadImage(IDataObject data)
    {
        try
        {
            if (data.GetData("PNG") is MemoryStream png)
                return BitmapFrame.Create(png, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            if (data.GetData(DataFormats.Bitmap) is BitmapSource bitmap)
            {
                var flat = new FormatConvertedBitmap(bitmap, PixelFormats.Bgr32, null, 0);
                flat.Freeze();
                return flat;
            }
        }
        catch { /* 剪貼簿被別的程式佔著或格式怪：當作沒有圖 */ }
        return null;
    }

    private void ShelfPaste_Click(object sender, RoutedEventArgs e)
    {
        try { if (Clipboard.GetDataObject() is { } data) AddToShelf(data); }
        catch (Exception x) { Log.Write("shelf paste: " + x.Message); }
    }

    private void RemoveFromShelf(string path)
    {
        AppSettings.Current.Shelf.Remove(path);
        Shelf.DeleteCopy(path, Shelf.Folder);
        AppSettings.Current.Save();
        RefreshTools();
    }

    private static BitmapImage? Thumbnail(string path)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;   // 讀完就放開檔案，之後才刪得掉
            image.DecodePixelHeight = 48;
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch { return null; }
    }

    private void ShelfClick(string path, ShelfKind kind)
    {
        try
        {
            switch (kind)
            {
                case ShelfKind.Text when Shelf.ReadText(path) is { Length: > 0 } text:
                    Clipboard.SetText(text);
                    Toast(L.T("已複製"), 3, false);
                    break;
                case ShelfKind.Image:
                    var image = new BitmapImage();
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.UriSource = new Uri(path);
                    image.EndInit();
                    Clipboard.SetImage(image);
                    Toast(L.T("已複製"), 3, false);
                    break;
                case ShelfKind.File:
                    Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                    break;
            }
        }
        catch (Exception x) { Log.Write("shelf: " + x.Message); }
    }

    // ---------------------------------------------------------------- 提醒

    private Alarms? _alarms;

    private void RefreshAlarms()
    {
        AlarmBox.Children.Clear();
        var now = DateTime.Now;
        var list = _alarms?.List ?? [];
        foreach (var alarm in list)
        {
            var row = new Grid { Height = 26, Background = Brushes.Transparent };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            var time = new TextBlock { Text = Alarms.Short(alarm.At, now, L.T("明天")), FontSize = 12, Foreground = new SolidColorBrush(Color.FromArgb(0x90, 0xFF, 0xFF, 0xFF)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
            var text = new TextBlock { Text = alarm.Text, FontSize = 13, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetColumn(text, 1);
            row.Children.Add(time);
            row.Children.Add(text);
            var id = alarm.Id;
            row.MouseRightButtonUp += (_, e) => { e.Handled = true; _alarms?.Cancel("#" + id); };
            AlarmBox.Children.Add(row);
        }
        AlarmBox.Visibility = list.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (_tab == Tab.Tools && _expanded) Morph();
    }

    private void AlarmInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || _alarms is not { } alarms || AlarmInput.Text.Trim() is not { Length: > 0 } line) return;
        if (Alarms.ParseLine(line, DateTime.Now) is not { } parsed) { Toast(L.T("看不懂這個時間"), 4, false); return; }
        alarms.Add(parsed.At, parsed.Text);
        AlarmInput.Text = "";
    }

    /// <summary>輸入框空的時候顯示圖示當作提示，打字就收起來。</summary>
    private void ToolInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        TodoGlyph.Visibility = TodoInput.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        AlarmGlyph.Visibility = AlarmInput.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Timer_Click(object sender, RoutedEventArgs e) { _pomodoro.Toggle(DateTime.Now); UpdateTimer(); }
    private void TimerReset_Click(object sender, RoutedEventArgs e) { _pomodoro.Reset(); UpdateTimer(); }

    /// <summary>每個 tick 呼叫：時間到了就提醒並換段。</summary>
    private (int, int, bool, bool) _timerShown = (-1, -1, false, false);
    private static readonly Brush RestGreen = Frozen(Color.FromRgb(0x7D, 0xE0, 0xA8));
    private static Brush Frozen(Color color) { var brush = new SolidColorBrush(color); brush.Freeze(); return brush; }

    private void UpdateTimer()
    {
        if (_pomodoro.Tick(DateTime.Now))
        {
            System.Media.SystemSounds.Asterisk.Play();
            Toast(_pomodoro.Resting ? L.T("休息一下") : L.T("回來專注"), 8);
        }
        // 這裡每 0.1 秒跑一次：畫面上的字沒變就不要碰控制項
        var left = _pomodoro.Left(DateTime.Now);
        var shown = ((int)left.TotalMinutes, left.Seconds, _pomodoro.Resting, _pomodoro.Running);
        if (shown == _timerShown) return;
        _timerShown = shown;
        TimerText.Text = $"{(int)left.TotalMinutes}:{left.Seconds:00}";
        TimerText.Foreground = _pomodoro.Resting ? RestGreen : Brushes.White;
        TimerButton.Content = _pomodoro.Running ? "" : "";
    }

    /// <summary>番茄鐘在跑的時候，膠囊閒置時顯示剩餘時間。</summary>
    private string? TimerLine()
    {
        if (!_pomodoro.Running) return null;
        var left = _pomodoro.Left(DateTime.Now);
        return $"{(_pomodoro.Resting ? L.T("休息") : L.T("專注"))} {(int)left.TotalMinutes}:{left.Seconds:00}";
    }
}

public sealed class TodoItem
{
    public string Text { get; set; } = "";
    public bool Done { get; set; }
}
