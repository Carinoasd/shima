// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Windows;

namespace Shima;

public partial class App : Application
{
    private Mutex? _single;

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

        MainWindow = new IslandWindow(smoke, shotFolder);
        MainWindow.Show();
    }
}
