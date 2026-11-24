// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.IO;
using System.Windows.Media.Imaging;

namespace Shima;

public enum ShelfKind { File, Text, Image }

/// <summary>
/// 暫存架的項目。使用者拖進來的檔案只記位置；文字與圖片存成 shelf 資料夾裡的 .txt／.png，
/// 那是 Shima 自己的複本，移除時要刪掉。判斷「是不是自己的複本」只看路徑，所以集中在這裡、不依賴畫面，方便測試。
/// </summary>
public static class Shelf
{
    /// <summary>文字最多收這麼多字；再長的截掉（設定檔與磁碟不能被塞爆）。</summary>
    public const int MaxText = 20_000;
    /// <summary>圖片長邊最多這麼多像素；再大的縮小後才存。</summary>
    public const int MaxImageSide = 2048;
    /// <summary>暫存架最多幾個項目。</summary>
    public const int MaxItems = 30;
    /// <summary>標籤最多顯示幾個字。</summary>
    public const int LabelLength = 18;

    public static string Folder => Path.Combine(AppSettings.Folder, "shelf");

    /// <summary>path 是不是直接放在 folder 底下的檔案（路徑穿越、子資料夾、資料夾本身都不算）。</summary>
    public static bool IsOwned(string? path, string folder)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(folder)) return false;
        try
        {
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
            // 磁碟機或網路主機不同的一定不在裡面：先用字串擋掉。GetFullPath 遇到含 ~ 的路徑會去問檔案系統（展開 8.3 短檔名），
            // 放在不通的網路位置（\\主機\共用\SHELF~1.TXT）要等好幾秒，而這個函式在畫面的執行緒上對每個項目都跑一次
            if (!string.Equals(RootOf(path), RootOf(root), StringComparison.OrdinalIgnoreCase)) return false;
            var full = Path.GetFullPath(path);
            var parent = Path.GetDirectoryName(full);
            return parent != null && string.Equals(parent, root, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(full).Length > 0;
        }
        catch { return false; }
    }

    private static string RootOf(string path) => (Path.GetPathRoot(path) ?? "").Replace('/', '\\');

    public static ShelfKind Classify(string path, string folder)
    {
        if (!IsOwned(path, folder)) return ShelfKind.File;
        return Path.GetExtension(path).ToLowerInvariant() switch { ".txt" => ShelfKind.Text, ".png" => ShelfKind.Image, _ => ShelfKind.File };
    }

    /// <summary>文字項目的標籤：第一行有字的部分，空白縮成一格，太長加「…」。</summary>
    public static string TextLabel(string? text)
    {
        var line = (text ?? "").Split('\n').Select(l => string.Join(' ', l.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))).FirstOrDefault(l => l.Length > 0) ?? "";
        return line.Length <= LabelLength ? line : line[..LabelLength].TrimEnd() + "…";
    }

    /// <summary>項目要顯示的字（圖片不用字，回空字串）。</summary>
    public static string Label(string path, ShelfKind kind) => kind switch
    {
        ShelfKind.File => Path.GetFileName(path.TrimEnd('\\', '/')),
        ShelfKind.Text => TextLabel(ReadText(path)),
        _ => "",
    };

    /// <summary>讀回文字項目的內容；讀不到是 null。</summary>
    public static string? ReadText(string path)
    {
        try { return File.ReadAllText(path); } catch { return null; }
    }

    /// <summary>文字截到上限；空白的不收（null）。</summary>
    public static string? Trim(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Length > MaxText ? text[..MaxText] : text;

    /// <summary>shelf 裡一個沒用過的檔名。</summary>
    public static string NewPath(string folder, string extension, DateTime now)
    {
        var stem = $"shelf-{now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}";
        var path = Path.Combine(folder, stem + extension);
        for (var i = 2; File.Exists(path); i++) path = Path.Combine(folder, $"{stem}-{i}{extension}");
        return path;
    }

    /// <summary>把文字存成 .txt，回傳路徑；空白的或寫不進去是 null。</summary>
    public static string? SaveText(string? text, string folder, DateTime now)
    {
        if (Trim(text) is not { } kept) return null;
        try
        {
            Directory.CreateDirectory(folder);
            var path = NewPath(folder, ".txt", now);
            File.WriteAllText(path, kept);
            return path;
        }
        catch { return null; }
    }

    /// <summary>把圖片存成 .png（長邊超過上限先縮小），回傳路徑；寫不進去是 null。</summary>
    public static string? SaveImage(BitmapSource image, string folder, DateTime now)
    {
        try
        {
            var longest = Math.Max(image.PixelWidth, image.PixelHeight);
            if (longest > MaxImageSide)
            {
                var scale = (double)MaxImageSide / longest;
                image = new TransformedBitmap(image, new System.Windows.Media.ScaleTransform(scale, scale));
            }
            Directory.CreateDirectory(folder);
            var path = NewPath(folder, ".png", now);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using (var stream = File.Create(path)) encoder.Save(stream);
            return path;
        }
        catch { return null; }
    }

    /// <summary>
    /// 移除項目時呼叫：只有確實位於 shelf 資料夾裡的複本才刪檔；使用者原本的檔案（任何其他位置）一律不碰。
    /// 回傳有沒有刪掉。
    /// </summary>
    public static bool DeleteCopy(string path, string folder)
    {
        if (!IsOwned(path, folder)) return false;
        try { if (!File.Exists(path)) return false; File.Delete(path); return true; }
        catch { return false; }
    }
}
