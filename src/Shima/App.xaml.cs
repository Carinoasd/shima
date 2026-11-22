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
        int shot = Array.IndexOf(e.Args, "--shot");
        string? shotFolder = shot >= 0 && shot + 1 < e.Args.Length ? e.Args[shot + 1] : null;
        bool smoke = e.Args.Contains("--smoke") || shotFolder != null;

        _single = new Mutex(true, smoke ? "Shima.Smoke" : "Shima.Carinoasd", out bool first);
        if (!first) { Shutdown(); return; }

        DispatcherUnhandledException += (_, x) => { Log.Write("ui: " + x.Exception); x.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, x) => Log.Write("fatal: " + x.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, x) => { Log.Write("task: " + x.Exception.Message); x.SetObserved(); };

        int lang = Array.IndexOf(e.Args, "--lang");
        Loc.Init(lang >= 0 && lang + 1 < e.Args.Length ? e.Args[lang + 1] : AppSettings.Current.Language);
        MainWindow = new IslandWindow(smoke, shotFolder);
        MainWindow.Show();
    }
}
