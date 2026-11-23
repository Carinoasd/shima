// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace Shima.Playback;

/// <summary>一個可用的播放裝置。</summary>
public sealed record AudioOutput(string Id, string Name, bool IsDefault);

/// <summary>
/// Windows 的播放裝置：列出可用的、切換預設的。
/// 列舉用 NAudio；切換沒有公開 API，只能用未公開的 IPolicyConfig（跟音量混音器、各種切換工具同一招）。
/// 選單排序與「下一個」的計算是純函式，不碰 COM，可以測試。
/// </summary>
public static class AudioOutputs
{
    /// <summary>目前啟用中的播放裝置（已整理名稱、排好序）。沒有裝置或查詢失敗就是空的。</summary>
    public static List<AudioOutput> List()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            string defaultId = "";
            try { using var d = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia); defaultId = d.ID; }
            catch { /* 沒有預設裝置 */ }

            var found = new List<AudioOutput>();
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                using (device)
                    found.Add(new AudioOutput(device.ID, Tidy(device.FriendlyName), device.ID == defaultId));
            return Order(found);
        }
        catch (Exception e) { Log.Write("audio outputs: " + e.Message); return []; }
    }

    /// <summary>啟用中的播放裝置有幾個。只數個數，不讀名稱。</summary>
    public static int Count()
    {
        try { using var enumerator = new MMDeviceEnumerator(); return enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).Count; }
        catch { return 0; }
    }

    /// <summary>名稱去頭尾空白、連續空白併成一個；空的名稱給個占位，免得選單出現空白一列。</summary>
    public static string Tidy(string? name)
    {
        var text = string.Join(' ', (name ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return text.Length > 0 ? text : "?";
    }

    /// <summary>依名稱排序（不分大小寫、位置固定），重複的 Id 只留一個。排序固定，選單才不會因為預設裝置換了而跳動。</summary>
    public static List<AudioOutput> Order(IEnumerable<AudioOutput> outputs) =>
        outputs.GroupBy(o => o.Id).Select(g => g.First())
               .OrderBy(o => o.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(o => o.Id, StringComparer.Ordinal).ToList();

    /// <summary>目前預設之後的下一個裝置（排好序的清單、到尾巴就繞回頭）；少於兩個是 null，找不到目前的就從第一個開始。</summary>
    public static AudioOutput? Next(IReadOnlyList<AudioOutput> ordered, string currentId)
    {
        if (ordered.Count < 2) return null;
        int at = -1;
        for (int i = 0; i < ordered.Count; i++) if (ordered[i].Id == currentId) { at = i; break; }
        return ordered[(at + 1) % ordered.Count];
    }

    /// <summary>
    /// 把預設播放裝置換掉。三個角色（Console、Multimedia、Communications）一起換：
    /// 使用者要的是「聲音從這裡出來」，只換其中一個的話通話軟體或系統音效還留在舊裝置上，反而像是沒切成功。
    /// 成功回傳 true。
    /// </summary>
    public static bool SetDefault(string deviceId)
    {
        object? com = null;
        try
        {
            var type = Type.GetTypeFromCLSID(PolicyConfigClient);
            if (type == null) return false;
            com = Activator.CreateInstance(type);
            var policy = (IPolicyConfig)com!;
            foreach (var role in new[] { Role.Console, Role.Multimedia, Role.Communications })
            {
                int hr = policy.SetDefaultEndpoint(deviceId, (int)role);
                if (hr < 0) { Log.Write($"audio outputs: SetDefaultEndpoint {role} 0x{hr:X8}"); return false; }
            }
            return true;
        }
        catch (Exception e) { Log.Write("audio outputs (switch): " + e.Message); return false; }
        finally { if (com != null) Marshal.ReleaseComObject(com); }
    }

    // CLSID_PolicyConfigClient（Windows 7 之後；舊的 294935CE… 是 Vista 版，IID 也不同）。
    private static readonly Guid PolicyConfigClient = new("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9");

    /// <summary>
    /// 未公開的 IPolicyConfig（Windows SDK 內部標頭 PolicyConfig.h，IID F8679F50-…）。
    /// vtable 順序：IUnknown 三個，接著 GetMixFormat、GetDeviceFormat、ResetDeviceFormat、SetDeviceFormat、
    /// GetProcessingPeriod、SetProcessingPeriod、GetShareMode、SetShareMode（共 8 個），
    /// GetPropertyValue、SetPropertyValue，然後才是 SetDefaultEndpoint、SetEndpointVisibility。
    /// 所以 SetDefaultEndpoint 前面要墊 10 個方法；沒用到的用 IntPtr 參數佔位，只要位置對就行。
    /// </summary>
    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat(IntPtr a, IntPtr b);
        [PreserveSig] int GetDeviceFormat(IntPtr a, IntPtr b, IntPtr c);
        [PreserveSig] int ResetDeviceFormat(IntPtr a);
        [PreserveSig] int SetDeviceFormat(IntPtr a, IntPtr b, IntPtr c);
        [PreserveSig] int GetProcessingPeriod(IntPtr a, IntPtr b, IntPtr c, IntPtr d);
        [PreserveSig] int SetProcessingPeriod(IntPtr a, IntPtr b);
        [PreserveSig] int GetShareMode(IntPtr a, IntPtr b);
        [PreserveSig] int SetShareMode(IntPtr a, IntPtr b);
        [PreserveSig] int GetPropertyValue(IntPtr a, IntPtr b, IntPtr c);
        [PreserveSig] int SetPropertyValue(IntPtr a, IntPtr b, IntPtr c);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int role);
        [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int visible);
    }
}
