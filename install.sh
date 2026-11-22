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

powershell.exe -NoProfile -Command "Get-Process Shima -ErrorAction SilentlyContinue | Stop-Process -Force; Start-Sleep -Milliseconds 800"
mkdir -p "$DEST"
cp "$OUT/Shima.exe" "$DEST/Shima.exe"

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
