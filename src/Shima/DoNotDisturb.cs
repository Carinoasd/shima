// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

namespace Shima;

/// <summary>勿擾模式：結束時間存在設定裡，「現在是不是勿擾中」只看這個純函式。</summary>
public static class DoNotDisturb
{
    public static readonly TimeSpan Length = TimeSpan.FromHours(1);

    public static DateTime EndFrom(DateTime now) => now + Length;

    /// <summary>
    /// 還沒到結束時間才算。剩餘時間比一次勿擾的長度還久，表示系統時鐘被往回調過（或設定檔被改過），
    /// 這時寧可當作已結束，免得勿擾卡住很久。
    /// </summary>
    public static bool IsActive(DateTime? until, DateTime now) =>
        until is { } end && end > now && end - now <= Length + TimeSpan.FromMinutes(1);
}
