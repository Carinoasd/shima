# 把打包好的 Shima.exe 像使用者一樣直接開起來，確認在一台全新的電腦上（沒有任何設定檔）島真的有出現。
# 給 CI 用。0.6.1～0.7.0 在全新的電腦上一啟動就出錯，卻留在背景不顯示也不結束；
# 開發用的電腦一定有設定檔，單元測試也碰不到「整個程式啟動」這一段，所以要實際開一次才看得出來。
param([Parameter(Mandatory)][string]$Exe, [int]$Timeout = 60)
$ErrorActionPreference = 'Stop'

Add-Type @"
using System; using System.Text; using System.Collections.Generic; using System.Runtime.InteropServices;
public static class Windows {
  delegate bool Each(IntPtr window, IntPtr unused);
  [DllImport("user32.dll")] static extern bool EnumWindows(Each each, IntPtr unused);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr window, StringBuilder text, int max);
  // 某個程序看得見的視窗的標題
  public static List<string> Visible(uint process) {
    var titles = new List<string>();
    EnumWindows((window, unused) => {
      uint owner; GetWindowThreadProcessId(window, out owner);
      if (owner == process && IsWindowVisible(window)) { var text = new StringBuilder(256); GetWindowText(window, text, 256); titles.Add(text.ToString()); }
      return true;
    }, IntPtr.Zero);
    return titles;
  }
}
"@

$data = Join-Path $env:APPDATA 'Shima'
if (Test-Path $data) { Write-Host "::error::這台機器已經有 $data，測不到第一次啟動"; exit 1 }
$log = Join-Path $env:LOCALAPPDATA 'Shima\log.txt'

$process = Start-Process -FilePath (Resolve-Path $Exe) -PassThru
$null = $process.Handle   # 先拿住，之後才讀得到 ExitCode
$deadline = (Get-Date).AddSeconds($Timeout)
$shown = $false
while ((Get-Date) -lt $deadline -and -not $process.HasExited) {
  if ([Windows]::Visible([uint32]$process.Id) -contains 'Shima') { $shown = $true; break }
  Start-Sleep -Milliseconds 500
}
Start-Sleep -Seconds 5   # 出現之後再等一下：啟動後才做的事有沒有出錯

$alive = -not $process.HasExited
$lines = if (Test-Path $log) { @(Get-Content $log) } else { @() }
# 沒人接住的例外會記成「ui: …」或「fatal: …」
$errors = @($lines | Where-Object { $_ -match '^\d{4}-\d\d-\d\d \d\d:\d\d:\d\d\s+(ui|fatal): ' })

Write-Host "程序還在：$alive　島有出現：$shown"
Write-Host "--- log.txt"
$lines | Select-Object -First 80 | ForEach-Object { Write-Host $_ }
Get-Process Shima -ErrorAction SilentlyContinue | Stop-Process -Force

if (-not $alive) { Write-Host "::error::Shima 自己結束了（exit code $($process.ExitCode)）"; exit 1 }
if (-not $shown) { Write-Host "::error::$Timeout 秒內沒有出現看得見的視窗（程序還在，但使用者什麼都看不到）"; exit 1 }
if ($errors.Count -gt 0) { Write-Host "::error::啟動時有沒人接住的例外，見上面的 log.txt"; exit 1 }
Write-Host "第一次啟動正常"
