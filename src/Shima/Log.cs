// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.IO;

namespace Shima;

public static class Log
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Shima", "log.txt");
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
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 512 * 1024) File.Delete(FilePath);
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}");
            }
        }
        catch { /* 紀錄寫不進去不影響使用 */ }
    }
}
