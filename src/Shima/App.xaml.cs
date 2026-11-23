// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Windows;

namespace Shima;

public partial class App : Application
{
    private Mutex? _single;

    /// <summary>換語言要重新啟動：先放掉「只開一個」的鎖，新的那個才起得來。</summary>
    public void Restart()
    {
        try
        {
            _single?.ReleaseMutex();
            _single?.Dispose();
            _single = null;
            if (Environment.ProcessPath is { } exe) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = false });
        }
        catch (Exception e) { Log.Write("restart: " + e.Message); }
        MainWindow?.Close();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // 示範模式要在讀設定之前就把資料夾換掉
        if (e.Args.Contains("--demo"))
        {
            try { Demo.Prepare(); }
            catch (Exception x) { Log.Write("demo: " + x); Shutdown(); return; }
        }
        int shot = Array.IndexOf(e.Args, "--shot");
        string? shotFolder = shot >= 0 && shot + 1 < e.Args.Length ? e.Args[shot + 1] : null;
        bool smoke = e.Args.Contains("--smoke") || shotFolder != null || e.Args.Contains("--hear-test");

        // 示範模式不佔「只能開一份」的名額：資料各自獨立，可以同時開好幾份
        _single = new Mutex(true, Demo.On ? "Shima.Demo." + Environment.ProcessId : smoke ? "Shima.Smoke" : "Shima.Carinoasd", out bool first);
        if (!first) { Shutdown(); return; }

        DispatcherUnhandledException += (_, x) => { Log.Write("ui: " + x.Exception); x.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, x) => Log.Write("fatal: " + x.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, x) => { Log.Write("task: " + x.Exception.Message); x.SetObserved(); };

        int lang = Array.IndexOf(e.Args, "--lang");
        Loc.Init(lang >= 0 && lang + 1 < e.Args.Length ? e.Args[lang + 1] : AppSettings.Current.Language);
        if (e.Args.Contains("--hear-test"))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = Task.Run(async () =>
            {
                foreach (var (language, sentence) in new[] { ("zh-TW", "現在播放的是哪一首歌"), ("ja-JP", "次の曲にしてください") })
                {
                    try { Log.Write("hear-test: " + await Agents.Ears.SelfTestAsync(language, sentence)); }
                    catch (Exception x) { Log.Write("hear-test 失敗: " + x); }
                }
                Dispatcher.Invoke(Shutdown);
            });
            return;
        }
        // --ask 問題：不顯示畫面，等音樂與歌詞就緒後實際問一次 AI，把問答寫進紀錄檔（確認工具真的有被用到）
        var asks = e.Args.Select((a, i) => (a, i)).Where(x => x.a == "--ask" && x.i + 1 < e.Args.Length).Select(x => e.Args[x.i + 1]).ToList();
        MainWindow = new IslandWindow(smoke || asks.Count > 0, shotFolder, asks);
        MainWindow.Show();
    }
}
