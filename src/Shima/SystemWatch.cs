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
/// Changed 不保證在哪條執行緒觸發：音量來自音訊裝置的回呼執行緒、網路來自執行緒集區、亮度在背景查，
/// 只有電量與播放裝置是在呼叫 Poll() 的執行緒。訂閱的人要自己切回畫面的執行緒（島是用 Dispatcher.BeginInvoke）。
/// </summary>
public sealed class SystemWatch : IDisposable
{
    private MMDeviceEnumerator? _devices;
    private MMDevice? _output;
    private int _volume = -1, _brightness = -1, _battery = -1;
    private string _outputId = "";
    private volatile string _expected = "";
    private bool _muted, _plugged, _online, _started;
    private volatile bool _hasBrightness = true, _disposed;
    private long _quietUntil;
    /// <summary>亮度查詢還在跑（1）就不再發起新的，WMI 卡住時也不會越堆越多。</summary>
    private int _askingBrightness;
    private NetworkAvailabilityChangedEventHandler? _network;

    /// <summary>要顯示在島上的一行字。</summary>
    public event Action<string>? Changed;

    /// <summary>目前預設播放裝置的 Id（Poll 時更新）；還沒查到是空字串。</summary>
    public string OutputId => _outputId;
    /// <summary>啟用中的播放裝置有幾個（Poll 時更新）；還沒啟動是 -1。</summary>
    public int Outputs { get; private set; } = -1;

    /// <summary>只數個數，不讀每個裝置的名稱（那要逐一開屬性庫）。</summary>
    private void CountOutputs()
    {
        try { if (_devices != null) Outputs = _devices.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).Count; }
        catch { /* 列不出來就維持上一次的數字 */ }
    }

    public void Start()
    {
        if (_started) return;
        _started = true;
        try
        {
            _devices = new MMDeviceEnumerator();
            Bind();
            CountOutputs();
        }
        catch (Exception e) { Log.Write("system (audio): " + e.Message); }

        _online = NetworkInterface.GetIsNetworkAvailable();
        // 靜態事件：不退訂的話 NetworkChange 會一直抓著這個物件，Dispose 之後還會觸發
        _network = (_, e) =>
        {
            if (_disposed || e.IsAvailable == _online) return;
            _online = e.IsAvailable;
            Changed?.Invoke(_online ? Line("online", NetworkName()) : Line("offline", ""));
        };
        NetworkChange.NetworkAvailabilityChanged += _network;

        var power = System.Windows.Forms.SystemInformation.PowerStatus;
        _plugged = power.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online;
        _battery = Percent(power);
        // 第一次查到的亮度只當基準（_brightness 還是 -1，不會觸發 Changed）
        AskBrightness();
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
                // 島自己切的：切換的地方已經顯示過了，這裡不重複
                bool mine = now.ID == _expected;
                _expected = "";
                Bind();
                if (!first && !mine && name.Length > 0) Changed?.Invoke(Line("device", name));
            }
        }
        catch { /* 沒有任何播放裝置 */ }
        CountOutputs();

        AskBrightness();
    }

    /// <summary>
    /// 亮度要問 WMI，同步查詢可能卡上好一陣子；Poll() 是在畫面的執行緒上呼叫的，所以丟到背景查。
    /// 上一次還沒回來就不再發起；查到了才跟上次比較，變了就在背景執行緒觸發 Changed（跟音量、網路一樣）。
    /// _brightness 只在這個背景工作裡讀寫，而同一時間只會有一個在跑。
    /// </summary>
    private void AskBrightness()
    {
        if (!_hasBrightness || _disposed || Interlocked.CompareExchange(ref _askingBrightness, 1, 0) != 0) return;
        _ = Task.Run(() =>
        {
            try
            {
                int brightness = Brightness();
                if (_disposed) return;
                if (brightness >= 0 && brightness != _brightness && _brightness >= 0) Changed?.Invoke(Line("brightness", brightness.ToString()));
                if (brightness >= 0) _brightness = brightness;
            }
            catch (Exception e) { Log.Write("system (brightness): " + e.Message); }
            finally { Volatile.Write(ref _askingBrightness, 0); }
        });
    }

    /// <summary>島即將把預設播放裝置切到這個 Id：之後 Poll() 發現換成它時不再重複提示。</summary>
    public void Expect(string deviceId) => _expected = deviceId;

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

    /// <summary>電腦現在的音量（0–100）與是否靜音；沒有播放裝置時音量是 -1。</summary>
    public (int Volume, bool Muted) Sound => (_volume, _muted);

    /// <summary>系統靜音開關。回傳現在是不是靜音；沒有播放裝置就是 null。</summary>
    public bool? ToggleMute()
    {
        try
        {
            if (_output == null) return null;
            var volume = _output.AudioEndpointVolume;
            volume.Mute = !volume.Mute;
            return volume.Mute;
        }
        catch (Exception e) { Log.Write("system (mute): " + e.Message); return null; }
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
        int found = -1;
        try
        {
            // 搜尋器、結果集合、每一筆都是 COM 物件，全部要釋放；多螢幕時只取第一台，其他的也要釋放
            using var search = new ManagementObjectSearcher(@"root\wmi", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
            using var results = search.Get();
            foreach (var o in results)
                using (o)
                    if (found < 0) found = Convert.ToInt32(o["CurrentBrightness"]);
        }
        catch { /* 這台螢幕不回報亮度 */ }
        if (found < 0) _hasBrightness = false;
        return found;
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
            // 換裝置時舊的 MMDevice 也是 COM 物件，先退訂再釋放
            var old = _output;
            _output = null;
            if (old != null)
            {
                try { old.AudioEndpointVolume.OnVolumeNotification -= OnVolume; } catch { }
                try { old.Dispose(); } catch { }
            }
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
        if (_disposed) return;
        _disposed = true;
        if (_network != null) NetworkChange.NetworkAvailabilityChanged -= _network;
        _network = null;
        var output = _output;
        _output = null;
        if (output != null)
        {
            try { output.AudioEndpointVolume.OnVolumeNotification -= OnVolume; } catch { }
            try { output.Dispose(); } catch { }
        }
        try { _devices?.Dispose(); } catch { }
        _devices = null;
    }
}
