// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using System.Globalization;
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

    /// <summary>正在重建工具分頁：這時輸入框失焦是被移走的，不是使用者離開。</summary>
    private bool _rebuildingTools;

    private void RefreshTools()
    {
        _rebuildingTools = true;
        try { RebuildTools(); }
        finally { _rebuildingTools = false; }
    }

    private void RebuildTools()
    {
        // 暫存架：檔案放的是位置；文字與圖片是 shelf 資料夾裡的複本
        ShelfBox.Children.Clear();
        foreach (var path in AppSettings.Current.Shelf.ToList())
        {
            var kind = Shelf.Classify(path, Shelf.Folder);
            var thumb = kind == ShelfKind.Image ? Thumbnail(path) : null;
            var chip = new Button { Style = (Style)FindResource("Pill"), Height = 28, Margin = new Thickness(0, 0, 6, 6), MaxWidth = 190 };
            if (thumb != null) chip.Content = new Image { Source = thumb, Height = 20, MaxWidth = 64, Stretch = Stretch.Uniform };
            // 字太長時在按鈕裡用刪節號收尾（按鈕有寬度上限，直接放字串會被硬切一半）
            else chip.Content = new TextBlock { Text = Shelf.Label(path, kind) is { Length: > 0 } label ? label : Path.GetFileName(path), TextTrimming = TextTrimming.CharacterEllipsis };
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
                // 自己的複本只給複製：拖到同一顆磁碟時檔案總管預設是「移動」，會把 shelf 資料夾裡的檔案搬走
                DragDrop.DoDragDrop(chip, data, kind == ShelfKind.File ? DragDropEffects.Copy | DragDropEffects.Link | DragDropEffects.Move : DragDropEffects.Copy);
                // 使用者自己的檔案被搬走了：架上那一顆已經點不開也拖不動，拿掉
                if (!File.Exists(path) && !Directory.Exists(path)) RemoveFromShelf(path);
            };
            ShelfBox.Children.Add(chip);
        }
        ShelfHint.Visibility = AppSettings.Current.Shelf.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        RefreshTodos();
        RefreshAlarms();
        UpdateTimer();
        SyncMonth();
        Month.InvalidateVisual();
        if (_tab == Tab.Tools && _expanded) Morph();
    }

    /// <summary>月曆的「今天」跟著日期走：島開著跨日、跨月之後，格子與標題才不會還是上個月的。</summary>
    private void SyncMonth()
    {
        var today = DateTime.Today;
        if (Month.Today.Date != today) Month.Today = today;
        MonthTitle.Text = today.ToString("Y", Loc.Culture);
    }

    private static readonly Brush DropLine = Frozen(Color.FromRgb(0xD9, 0x77, 0x57));
    private const double TodoRowHeight = 28;

    private void RefreshTodos()
    {
        TodoBox.Children.Clear();
        var todos = AppSettings.Current.Todos;
        var hasDone = todos.Any(t => t.Done);
        TodoClear.Visibility = hasDone ? Visibility.Visible : Visibility.Collapsed;
        // 清掉做完的按鈕疊在清單右下角：不多佔一列，所以清單右邊讓出它的位置
        TodoBox.Margin = new Thickness(0, 0, hasDone ? 30 : 0, 0);

        // 拖曳時的插入位置提示：第 gap 條線在第 gap 列的上緣，最後一條在最後一列的下緣
        var lines = new List<System.Windows.Shapes.Rectangle>();
        System.Windows.Shapes.Rectangle NewLine(VerticalAlignment where)
        {
            var line = new System.Windows.Shapes.Rectangle { Height = 2, Fill = DropLine, RadiusX = 1, RadiusY = 1, VerticalAlignment = where, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
            System.Windows.Controls.Panel.SetZIndex(line, 5);
            lines.Add(line);
            return line;
        }

        foreach (var todo in todos.ToList())
        {
            var row = new Grid { Height = TodoRowHeight, Background = Brushes.Transparent, RenderTransform = new TranslateTransform() };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            var check = new Button { Content = todo.Done ? "\uE73A" : "\uE739", Style = (Style)FindResource("Key"), Width = 28, Height = 28, FontSize = 14 };
            check.Click += (_, _) => { todo.Done = !todo.Done; AppSettings.Current.Save(); RefreshTools(); };
            var text = new TextBlock
            {
                Text = todo.Text, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = todo.Done ? new SolidColorBrush(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF)) : Brushes.White,
                TextDecorations = todo.Done ? TextDecorations.Strikethrough : null,
            };
            var hit = new Border { Background = Brushes.Transparent, Margin = new Thickness(6, 0, 0, 0), Child = text };
            Grid.SetColumn(hit, 1);
            row.Children.Add(check);
            row.Children.Add(hit);
            row.Children.Add(NewLine(VerticalAlignment.Top));
            if (todo == todos[^1]) row.Children.Add(NewLine(VerticalAlignment.Bottom));
            row.MouseRightButtonUp += (_, e) => { e.Handled = true; todos.Remove(todo); AppSettings.Current.Save(); RefreshTools(); };

            // 按兩下改文字；按住上下拖換順序（自己追蹤滑鼠，不用 DragDrop：外面那層的拖放是給暫存架用的）
            Point start = default;
            var pressed = false;
            var dragging = false;
            var gap = 0;
            hit.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                if (e.ClickCount >= 2) { EditTodo(hit, todo); return; }
                start = e.GetPosition(TodoBox);
                pressed = true;
                dragging = false;
                hit.CaptureMouse();
            };
            hit.MouseMove += (_, e) =>
            {
                if (!pressed) return;
                var at = e.GetPosition(TodoBox);
                if (!dragging && Math.Abs(at.Y - start.Y) < 6) return;
                dragging = true;
                row.Opacity = 0.6;
                ((TranslateTransform)row.RenderTransform).Y = at.Y - start.Y;
                gap = (int)Math.Clamp(Math.Floor(at.Y / TodoRowHeight + 0.5), 0, todos.Count);
                var from = todos.IndexOf(todo);
                for (var i = 0; i < lines.Count; i++)
                    lines[i].Visibility = i == gap && gap != from && gap != from + 1 ? Visibility.Visible : Visibility.Collapsed;
            };
            hit.MouseLeftButtonUp += (_, _) =>
            {
                if (!pressed) return;
                pressed = false;
                hit.ReleaseMouseCapture();
                if (dragging && TodoOps.Move(todos, todos.IndexOf(todo), gap)) AppSettings.Current.Save();
                RefreshTools();
            };
            hit.LostMouseCapture += (_, _) =>
            {
                if (!pressed) return;
                pressed = false;
                RefreshTools();
            };
            TodoBox.Children.Add(row);
        }
    }

    /// <summary>原地改一筆待辦的文字：Enter 或失焦存檔、Esc 取消，改成空的就是刪掉。</summary>
    private void EditTodo(Border host, TodoItem todo)
    {
        var box = new TextBox { Text = todo.Text, Style = (Style)FindResource("Input"), Height = 26, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(-6, 0, 0, 0) };
        host.Child = box;
        var finished = false;
        void Finish(bool save)
        {
            if (finished) return;
            finished = true;
            if (save && TodoOps.Rename(AppSettings.Current.Todos, todo, box.Text)) AppSettings.Current.Save();
            RefreshTools();
        }
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { e.Handled = true; Finish(true); }
            else if (e.Key == Key.Escape) { e.Handled = true; Finish(false); }
        };
        box.LostKeyboardFocus += (_, _) =>
        {
            // 清單被重建（點了別列的勾、語音助手改了待辦）把輸入框移走：當成取消，不要把打到一半的字存下來
            if (_rebuildingTools || PresentationSource.FromVisual(box) == null) { finished = true; return; }
            // 失焦時是空的不算刪除（可能只是全選刪掉準備重打）：要刪請按 Enter
            Finish(box.Text.Trim().Length > 0);
        };
        // 在輸入框裡按右鍵不要冒泡到那一列的「右鍵刪除」；內建的英文選單也不要
        box.ContextMenu = null;
        box.MouseRightButtonUp += (_, e) => e.Handled = true;
        box.Dispatcher.BeginInvoke(() => { box.Focus(); Keyboard.Focus(box); box.SelectAll(); }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private void TodoClear_Click(object sender, RoutedEventArgs e)
    {
        if (TodoOps.ClearDone(AppSettings.Current.Todos) > 0) AppSettings.Current.Save();
        RefreshTools();
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
        data.GetDataPresent(DataFormats.FileDrop) || data.GetDataPresent("PNG") || data.GetDataPresent(DataFormats.Bitmap) || data.GetDataPresent(DataFormats.UnicodeText);

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
                using (png)
                {
                    png.Position = 0;
                    return BitmapFrame.Create(png, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);   // OnLoad：整張讀進來，之後不需要這個串流
                }
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
            // 「平日 09:30」在別的語言常常比 96 長：欄寬跟著最長的那一列，所有列共用同一個寬度才對得齊
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 96, SharedSizeGroup = "AlarmWhen" });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            var dim = new SolidColorBrush(Color.FromArgb(0x90, 0xFF, 0xFF, 0xFF));
            // 重複的：前面一個循環圖示，時間前面寫每天、平日或週幾；單次的照舊
            var when = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 10, 0) };
            if (Alarms.NormalizeRepeat(alarm.Repeat) is { } repeat)
            {
                when.Children.Add(new TextBlock { Text = "\uE8EE", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 10, Foreground = DropLine, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 5, 0) });
                when.Children.Add(new TextBlock { Text = $"{RepeatLabel(repeat)} {alarm.At:HH:mm}", FontSize = 12, Foreground = dim });
            }
            else when.Children.Add(new TextBlock { Text = Alarms.Short(alarm.At, now, L.T("明天")), FontSize = 12, Foreground = dim });
            var text = new TextBlock { Text = alarm.Text, FontSize = 13, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetColumn(text, 1);
            row.Children.Add(when);
            row.Children.Add(text);
            var id = alarm.Id;
            row.MouseRightButtonUp += (_, e) => { e.Handled = true; _alarms?.Cancel("#" + id); };
            AlarmBox.Children.Add(row);
        }
        AlarmBox.Visibility = list.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        SyncMonth();
        Month.AlarmDays = Alarms.InMonth(list, DateTime.Today, now);
        if (_tab == Tab.Tools && _expanded) Morph();
    }

    private static string RepeatLabel(string repeat) => repeat switch
    {
        "daily" => L.T("每天"),
        "weekdays" => L.T("平日"),
        _ => L.T("週" + "日一二三四五六"[Alarms.WeeklyDay(repeat) ?? 0]),
    };

    /// <summary>點月曆上的一天：輸入框填成那天的日期，接著打時間與內容。</summary>
    private void Month_DayPicked(DateTime day)
    {
        AlarmInput.Text = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " ";
        AlarmInput.Focus();
        AlarmInput.CaretIndex = AlarmInput.Text.Length;
    }

    private void AlarmInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || _alarms is not { } alarms || AlarmInput.Text.Trim() is not { Length: > 0 } line) return;
        if (Alarms.ParseLine(line, DateTime.Now) is not { } parsed) { Toast(L.T("看不懂這個時間"), 4, false); return; }
        alarms.Add(parsed.At, parsed.Text, parsed.Repeat);
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
    private (int, int, bool, bool, int) _timerShown = (-1, -1, false, false, -1);
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
        var completed = _pomodoro.CompletedToday(DateTime.Now);
        var shown = ((int)left.TotalMinutes, left.Seconds, _pomodoro.Resting, _pomodoro.Running, completed);
        if (shown == _timerShown) return;
        _timerShown = shown;
        TimerText.Text = $"{(int)left.TotalMinutes}:{left.Seconds:00}";
        TimerDots.Text = Pomodoro.Dots(completed);
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

/// <summary>待辦清單的資料操作，不碰畫面。</summary>
public static class TodoOps
{
    /// <summary>
    /// 把第 from 筆移到第 gap 個空隙（0 是最前面、Count 是最後面，空隙是照移動前的清單算）。
    /// 放回原地（空隙在自己前後）或超出範圍是 false，什麼都沒動。
    /// </summary>
    public static bool Move(List<TodoItem> list, int from, int gap)
    {
        if (from < 0 || from >= list.Count || gap < 0 || gap > list.Count || gap == from || gap == from + 1) return false;
        var item = list[from];
        list.RemoveAt(from);
        list.Insert(gap > from ? gap - 1 : gap, item);
        return true;
    }

    /// <summary>移除所有打勾的，回傳移除了幾筆。</summary>
    public static int ClearDone(List<TodoItem> list) => list.RemoveAll(t => t.Done);

    /// <summary>改文字：前後空白去掉；改成空的就刪掉這一筆。有變動回傳 true。</summary>
    public static bool Rename(List<TodoItem> list, TodoItem item, string text)
    {
        text = text.Trim();
        if (text.Length == 0) return list.Remove(item);
        if (text == item.Text) return false;
        item.Text = text;
        return true;
    }
}
