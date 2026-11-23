// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Management;
using System.Net.NetworkInformation;
using NAudio.CoreAudioApi;

namespace Shima;

/// <summary>
/// 盯著電腦的狀態：音量、亮度、電量、網路、播放裝置（耳機接上或拔掉）。有變化就回報一行字給島顯示。
/// 桌上型電腦沒有電池、外接螢幕也多半不回報亮度，那兩項就不會出現。
/// </summary>
public sealed class SystemWatch : IDisposable
{
    private MMDeviceEnumerator? _devices;
    private MMDevice? _output;
    private int _volume = -1, _brightness = -1, _battery = -1;
    private string _outputId = "";
    private bool _muted, _plugged, _online, _hasBrightness = true, _started;
    private long _quietUntil;

    /// <summary>要顯示在島上的一行字。</summary>
    public event Action<string>? Changed;

    public void Start()
    {
        if (_started) return;
        _started = true;
        try
        {
            _devices = new MMDeviceEnumerator();
            Bind();
        }
        catch (Exception e) { Log.Write("system (audio): " + e.Message); }

        _online = NetworkInterface.GetIsNetworkAvailable();
        NetworkChange.NetworkAvailabilityChanged += (_, e) =>
        {
            if (e.IsAvailable == _online) return;
            _online = e.IsAvailable;
            Changed?.Invoke(_online ? Line("online", NetworkName()) : Line("offline", ""));
        };

        var power = System.Windows.Forms.SystemInformation.PowerStatus;
        _plugged = power.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online;
        _battery = Percent(power);
        _brightness = Brightness();
        // 剛啟動時裝置會自己回報一輪，那不是使用者調的
        _quietUntil = Environment.TickCount64 + 3000;
    }

    /// <summary>約每兩秒呼叫：電量和亮度沒有事件可以等，只能自己看。</summary>
    public void Poll()
    {
        if (!_started) return;
        var power = System.Windows.Forms.SystemInformation.PowerStatus;
        int battery = Percent(power);
        if (battery >= 0)
        {
            bool plugged = power.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online;
            if (plugged != _plugged) Changed?.Invoke(Line(plugged ? "charging" : "battery", battery.ToString()));
            else if (!plugged && battery <= 20 && _battery > 20) Changed?.Invoke(Line("low", battery.ToString()));
            _plugged = plugged;
            _battery = battery;
        }

        // 預設的播放裝置換了（接上或拔掉耳機、藍牙連上）：改盯新的那個
        try
        {
            using var now = _devices?.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            if (now != null && now.ID != _outputId)
            {
                bool first = _outputId.Length == 0;
                var name = now.FriendlyName;
                Bind();
                if (!first && name.Length > 0) Changed?.Invoke(Line("device", name));
            }
        }
        catch { /* 沒有任何播放裝置 */ }

        int brightness = Brightness();
        if (brightness >= 0 && brightness != _brightness && _brightness >= 0) Changed?.Invoke(Line("brightness", brightness.ToString()));
        if (brightness >= 0) _brightness = brightness;
    }

    /// <summary>系統音量加減幾個百分點。回傳調整後的音量；沒有播放裝置就是 null。</summary>
    public int? Nudge(int percent)
    {
        try
        {
            if (_output == null) return null;
            var volume = _output.AudioEndpointVolume;
            volume.MasterVolumeLevelScalar = Math.Clamp(volume.MasterVolumeLevelScalar + percent / 100f, 0f, 1f);
            return (int)Math.Round(volume.MasterVolumeLevelScalar * 100);
        }
        catch (Exception e) { Log.Write("system (set volume): " + e.Message); return null; }
    }

    /// <summary>一種狀態 → 給人看的一行字。</summary>
    public static string Line(string kind, string value) => kind switch
    {
        "volume" => L.F("音量 {0}", value),
        "muted" => L.T("靜音"),
        "brightness" => L.F("亮度 {0}", value),
        "charging" => L.F("充電中 {0}%", value),
        "battery" => L.F("使用電池 {0}%", value),
        "low" => L.F("電量不足 {0}%", value),
        "online" => value.Length > 0 ? L.F("已連上 {0}", value) : L.T("網路已連線"),
        "offline" => L.T("網路已中斷"),
        "device" => L.F("輸出：{0}", value),
        _ => value,
    };

    private static int Percent(System.Windows.Forms.PowerStatus power) =>
        power.BatteryChargeStatus.HasFlag(System.Windows.Forms.BatteryChargeStatus.NoSystemBattery) || power.BatteryLifePercent > 1 ? -1 : (int)Math.Round(power.BatteryLifePercent * 100);

    private int Brightness()
    {
        if (!_hasBrightness) return -1;
        try
        {
            using var search = new ManagementObjectSearcher(@"root\wmi", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
            foreach (var o in search.Get()) return Convert.ToInt32(o["CurrentBrightness"]);
        }
        catch { /* 這台螢幕不回報亮度 */ }
        _hasBrightness = false;
        return -1;
    }

    private static string NetworkName()
    {
        try { return Windows.Networking.Connectivity.NetworkInformation.GetInternetConnectionProfile()?.ProfileName ?? ""; }
        catch { return ""; }
    }

    private void Bind()
    {
        try
        {
            if (_output != null) _output.AudioEndpointVolume.OnVolumeNotification -= OnVolume;
            _output = _devices?.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            if (_output == null) return;
            _outputId = _output.ID;
            _volume = (int)Math.Round(_output.AudioEndpointVolume.MasterVolumeLevelScalar * 100);
            _muted = _output.AudioEndpointVolume.Mute;
            _output.AudioEndpointVolume.OnVolumeNotification += OnVolume;
        }
        catch (Exception e) { Log.Write("system (volume): " + e.Message); }
    }

    private void OnVolume(AudioVolumeNotificationData data)
    {
        int volume = (int)Math.Round(data.MasterVolume * 100);
        if (volume == _volume && data.Muted == _muted) return;
        _volume = volume;
        _muted = data.Muted;
        if (Environment.TickCount64 < _quietUntil) return;
        Changed?.Invoke(data.Muted ? Line("muted", "") : Line("volume", volume.ToString()));
    }

    public void Dispose()
    {
        try
        {
            if (_output != null) _output.AudioEndpointVolume.OnVolumeNotification -= OnVolume;
            _devices?.Dispose();
        }
        catch { }
    }
}
