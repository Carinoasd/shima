// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace Shima;

/// <summary>
/// 量測模式（--demo --bench 秒數 [idle|playing|expanded] [結果檔]）：視窗放在螢幕外、不註冊快速鍵與系統匣，
/// 但照常排版與重畫。暖機幾秒之後量一段時間裡這個程序用了多少 CPU、配置了多少記憶體，寫進紀錄檔（與結果檔）就結束。
/// 用來比較改動前後：島大部分的時間都是收著的膠囊，閒置與播歌時的成本最要緊。
/// </summary>
public partial class IslandWindow
{
    public sealed record BenchPlan(int Seconds, string Mode, string? Output);

    public static BenchPlan? Bench { get; set; }

    private async Task BenchAsync(BenchPlan plan)
    {
        try
        {
            switch (plan.Mode)
            {
                case "idle": _media.Demo = null; break;                                   // 沒有歌：只有時鐘與角色
                case "expanded": TabMusic.IsChecked = true; SetExpanded(true); break;      // 音樂分頁開著：整份歌詞與頻譜
            }
            Refresh();
            await Task.Delay(4000);   // 暖機：JIT、第一次排版、展開的動畫

            var process = Process.GetCurrentProcess();
            process.Refresh();
            var cpu = process.TotalProcessorTime;
            long allocated = GC.GetTotalAllocatedBytes();
            int gen0 = GC.CollectionCount(0);
            var clock = Stopwatch.StartNew();
            await Task.Delay(TimeSpan.FromSeconds(plan.Seconds));
            clock.Stop();
            process.Refresh();

            double used = (process.TotalProcessorTime - cpu).TotalMilliseconds;
            double percent = used / clock.Elapsed.TotalMilliseconds * 100;
            double megabytes = (GC.GetTotalAllocatedBytes() - allocated) / 1048576.0;
            string line = string.Format(CultureInfo.InvariantCulture,
                "bench {0}: cpu {1:0} ms / {2:0.0} s = {3:0.00}% of one core, allocated {4:0.0} MB ({5:0.00} MB/s), gen0 {6}, working set {7:0} MB",
                plan.Mode, used, clock.Elapsed.TotalSeconds, percent, megabytes, megabytes / clock.Elapsed.TotalSeconds,
                GC.CollectionCount(0) - gen0, process.WorkingSet64 / 1048576.0);
            Log.Write(line);
            if (plan.Output is { Length: > 0 } path) File.AppendAllText(path, line + Environment.NewLine);
        }
        catch (Exception e) { Log.Write("bench: " + e); }
        Close();
    }
}
