#!/usr/bin/env bash
# 打包成單一執行檔（不用另外裝 .NET），安裝到 %LOCALAPPDATA%\Shima\Shima.exe，
# 在桌面和開始功能表建立捷徑，然後啟動。之後直接雙擊捷徑就能開。
set -euo pipefail
SRC="$(cd "$(dirname "$0")" && pwd)"
DOTNET="/mnt/c/Program Files/dotnet/dotnet.exe"
LA="$(powershell.exe -NoProfile -Command '[Environment]::GetFolderPath("LocalApplicationData")' | tr -d '\r')"
DEST="$(wslpath "$LA")/Shima"
OUT="$SRC/dist"

(cd /mnt/c && "$DOTNET" publish "$(wslpath -w "$SRC")\\src\\Shima\\Shima.csproj" -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none \
  -o "$(wslpath -w "$OUT")" -nologo -v q)

# 先關掉執行中的 Shima，等它真的結束（檔案解除鎖定）再覆蓋；還鎖著就多等幾次，
# 不然會留下「舊的關了、新的沒裝上」的狀態
powershell.exe -NoProfile -Command "Get-Process Shima -ErrorAction SilentlyContinue | Stop-Process -Force; for (\$i = 0; \$i -lt 40 -and (Get-Process Shima -ErrorAction SilentlyContinue); \$i++) { Start-Sleep -Milliseconds 250 }"
mkdir -p "$DEST"
for attempt in 1 2 3 4 5 6; do
  if cp "$OUT/Shima.exe" "$DEST/Shima.exe" 2>/dev/null; then break; fi
  if [ "$attempt" = 6 ]; then echo "無法覆蓋 $DEST/Shima.exe（還被占用），改為重新啟動原本的版本" >&2; powershell.exe -NoProfile -Command "Start-Process -FilePath (Join-Path \$env:LOCALAPPDATA 'Shima\\Shima.exe')"; exit 1; fi
  sleep 1
done

powershell.exe -NoProfile -Command "
  \$exe = Join-Path \$env:LOCALAPPDATA 'Shima\\Shima.exe'
  \$shell = New-Object -ComObject WScript.Shell
  foreach (\$folder in @([Environment]::GetFolderPath('Desktop'), [Environment]::GetFolderPath('Programs'))) {
    \$link = \$shell.CreateShortcut((Join-Path \$folder 'Shima.lnk'))
    \$link.TargetPath = \$exe
    \$link.WorkingDirectory = Split-Path \$exe
    \$link.IconLocation = \$exe
    \$link.Save()
  }
  Start-Process -FilePath \$exe
"
echo "已安裝到 $LA\\Shima\\Shima.exe，桌面與開始功能表有捷徑"
