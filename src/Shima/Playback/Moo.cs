// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Text;
using System.Text.Json.Nodes;

namespace Shima.Playback;

/// <summary>Roon 擴充功能協定（MOO/1）的一則訊息：第一行、標頭、可有可無的 JSON 內容。</summary>
public sealed record MooMessage(string Verb, string Name, string RequestId, JsonNode? Body);

public static class Moo
{
    public static byte[] Build(string verb, string name, string requestId, JsonNode? body = null)
    {
        var json = body == null ? [] : Encoding.UTF8.GetBytes(body.ToJsonString());
        var head = $"MOO/1 {verb} {name}\nRequest-Id: {requestId}\n";
        if (body != null) head += $"Content-Length: {json.Length}\nContent-Type: application/json\n";
        return [.. Encoding.UTF8.GetBytes(head + "\n"), .. json];
    }

    public static MooMessage? Parse(ReadOnlySpan<byte> data)
    {
        int end = data.IndexOf("\n\n"u8);
        if (end < 0) return null;
        var lines = Encoding.UTF8.GetString(data[..end]).Split('\n');
        var first = lines[0].Split(' ', 3);
        if (first.Length < 3 || !first[0].StartsWith("MOO/", StringComparison.Ordinal)) return null;

        string? id = null, type = null;
        int length = 0;
        foreach (var line in lines.Skip(1))
        {
            int colon = line.IndexOf(':');
            if (colon < 0) return null;
            var value = line[(colon + 1)..].Trim();
            switch (line[..colon])
            {
                case "Request-Id": id = value; break;
                case "Content-Type": type = value; break;
                case "Content-Length": if (!int.TryParse(value, out length)) return null; break;
            }
        }
        if (id == null) return null;

        JsonNode? body = null;
        var rest = data[(end + 2)..];
        if (length > 0 && type == "application/json")
        {
            if (rest.Length < length) return null;
            try { body = JsonNode.Parse(rest[..length]); } catch { return null; }
        }
        return new MooMessage(first[1], first[2], id, body);
    }
}
