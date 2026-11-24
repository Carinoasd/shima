// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.IO;

namespace Shima;

public static class Log
{
    /// <summary>
    /// 紀錄檔的位置：資料夾被 SHIMA_DATA 換掉（示範模式、使用者自己指定）就放在那裡面，不然是 %LOCALAPPDATA%\Shima\log.txt。
    /// 這裡不可以去碰 AppSettings（連 AppSettings.Folder 都不行）：AppSettings 初始化讀設定檔出錯時會呼叫 Log.Write，
    /// 兩邊互相等對方初始化就是循環，全新安裝的電腦一啟動就會出錯。所以直接讀環境變數（名稱要跟 AppSettings.FolderVariable 一致，有測試守著）。
    /// 每次寫的時候才決定：示範模式是啟動後才設環境變數的。
    /// </summary>
    public static string PathFor(string? custom, string localAppData) =>
        custom is { Length: > 0 } ? Path.Combine(custom, "log.txt") : Path.Combine(localAppData, "Shima", "log.txt");

    private static string FilePath() => PathFor(Environment.GetEnvironmentVariable("SHIMA_DATA"),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
    private static readonly object Gate = new();

    /// <summary>跑測試時不寫檔：測試會故意餵壞資料，那些訊息不該混進使用者真正的記錄檔。</summary>
    private static readonly bool Testing = AppDomain.CurrentDomain.FriendlyName.StartsWith("testhost", StringComparison.OrdinalIgnoreCase);

    public static void Write(string message)
    {
        if (Testing) return;
        try
        {
            lock (Gate)
            {
                var file = FilePath();
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                if (File.Exists(file) && new FileInfo(file).Length > 512 * 1024) File.Delete(file);
                File.AppendAllText(file, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}");
            }
        }
        catch { /* 紀錄寫不進去不影響使用 */ }
    }
}
