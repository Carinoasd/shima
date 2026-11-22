// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Shima;

/// <summary>工具分頁：番茄鐘、檔案暫存架、待辦清單。</summary>
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
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            _close.Stop();
            TabTools.IsChecked = true;
            _expanded = true;
            ShowTab(Tab.Tools);
        };
        Host.DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Link : DragDropEffects.None; e.Handled = true; };
        Host.Drop += (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
            var shelf = AppSettings.Current.Shelf;
            foreach (var path in paths) if (!shelf.Contains(path)) shelf.Add(path);
            AppSettings.Current.Save();
            RefreshTools();
        };
        RefreshTools();
    }

    private void RefreshTools()
    {
        // 暫存架：放的是檔案的位置，不是複本
        ShelfBox.Children.Clear();
        foreach (var path in AppSettings.Current.Shelf.ToList())
        {
            var chip = new Button { Content = Path.GetFileName(path.TrimEnd('\\')), ToolTip = path, Style = (Style)FindResource("Pill"), Height = 28, Margin = new Thickness(0, 0, 6, 6), MaxWidth = 190 };
            Point? pressed = null;
            chip.Click += (_, _) => { try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch (Exception x) { Log.Write("shelf: " + x.Message); } };
            chip.MouseRightButtonUp += (_, e) => { e.Handled = true; AppSettings.Current.Shelf.Remove(path); AppSettings.Current.Save(); RefreshTools(); };
            // 拖出去：把檔案交給別的程式
            chip.PreviewMouseLeftButtonDown += (_, e) => pressed = e.GetPosition(this);
            chip.PreviewMouseMove += (_, e) =>
            {
                if (pressed is not { } from || e.LeftButton != MouseButtonState.Pressed) return;
                if ((e.GetPosition(this) - from).Length < 8) return;
                pressed = null;
                if (File.Exists(path) || Directory.Exists(path))
                    DragDrop.DoDragDrop(chip, new DataObject(DataFormats.FileDrop, new[] { path }), DragDropEffects.Copy | DragDropEffects.Link | DragDropEffects.Move);
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
        UpdateTimer();
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

    private void Timer_Click(object sender, RoutedEventArgs e) { _pomodoro.Toggle(DateTime.Now); UpdateTimer(); }
    private void TimerReset_Click(object sender, RoutedEventArgs e) { _pomodoro.Reset(); UpdateTimer(); }

    /// <summary>每個 tick 呼叫：時間到了就提醒並換段。</summary>
    private void UpdateTimer()
    {
        if (_pomodoro.Tick(DateTime.Now))
        {
            System.Media.SystemSounds.Asterisk.Play();
            Toast(_pomodoro.Resting ? "休息一下" : "回來專注", 8);
        }
        var left = _pomodoro.Left(DateTime.Now);
        TimerText.Text = $"{(int)left.TotalMinutes}:{left.Seconds:00}";
        TimerText.Foreground = _pomodoro.Resting ? new SolidColorBrush(Color.FromRgb(0x7D, 0xE0, 0xA8)) : Brushes.White;
        TimerButton.Content = _pomodoro.Running ? "" : "";
    }

    /// <summary>番茄鐘在跑的時候，膠囊閒置時顯示剩餘時間。</summary>
    private string? TimerLine()
    {
        if (!_pomodoro.Running) return null;
        var left = _pomodoro.Left(DateTime.Now);
        return $"{(_pomodoro.Resting ? "休息" : "專注")} {(int)left.TotalMinutes}:{left.Seconds:00}";
    }
}

public sealed class TodoItem
{
    public string Text { get; set; } = "";
    public bool Done { get; set; }
}
