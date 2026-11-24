// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.IO;
using System.Net.Http;

namespace Shima.Lyrics;

/// <summary>歌詞來源的回應大小上限：來源或中間人送一個超大的回應，不能把記憶體撐爆。</summary>
public static class HttpLimits
{
    /// <summary>一個回應最多收這麼多（HttpClient 的緩衝上限）。</summary>
    public const int MaxBody = 4 * 1024 * 1024;

    /// <summary>單一份歌詞文字（解碼之後）最長這麼多字元，超過當成沒找到。</summary>
    public const int MaxText = 1024 * 1024;

    public static HttpClient Create(TimeSpan timeout, HttpMessageHandler? handler = null)
    {
        var http = handler == null ? new HttpClient() : new HttpClient(handler);
        http.Timeout = timeout;
        http.MaxResponseContentBufferSize = MaxBody;
        return http;
    }

    /// <summary>
    /// 取回文字。回應超過上限（不管有沒有宣告長度）回 null，呼叫端當成沒找到；
    /// 網路錯誤與非 2xx 照舊丟例外，由 LyricsFinder 記下。
    /// </summary>
    public static async Task<string?> GetStringAsync(HttpClient http, string url)
    {
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        return await ReadAsync(response);
    }

    /// <summary>讀出回應內容，超過 <see cref="MaxBody"/> 位元組回 null。</summary>
    public static async Task<string?> ReadAsync(HttpResponseMessage response)
    {
        if (response.Content.Headers.ContentLength > MaxBody) return null;
        using var stream = await response.Content.ReadAsStreamAsync();
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int n;
        while ((n = await stream.ReadAsync(chunk)) > 0)
        {
            if (buffer.Length + n > MaxBody) return null;
            buffer.Write(chunk, 0, n);
        }
        var charset = response.Content.Headers.ContentType?.CharSet;
        System.Text.Encoding encoding;
        try { encoding = charset == null ? System.Text.Encoding.UTF8 : System.Text.Encoding.GetEncoding(charset.Trim('"')); }
        catch (ArgumentException) { encoding = System.Text.Encoding.UTF8; }
        return encoding.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }
}
