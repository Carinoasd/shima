#!/usr/bin/env bash
# 建置後複製到 %LOCALAPPDATA%\Shima 並啟動（已在執行的會先關掉）。
# ./run.sh --smoke：不顯示畫面，啟動幾秒確認沒有錯誤就結束，結果寫在紀錄檔。
set -euo pipefail
SRC="$(cd "$(dirname "$0")" && pwd)"
DOTNET="/mnt/c/Program Files/dotnet/dotnet.exe"
LA="$(powershell.exe -NoProfile -Command '[Environment]::GetFolderPath("LocalApplicationData")' | tr -d '\r')"
DEST="$(wslpath "$LA")/Shima"
(cd /mnt/c && "$DOTNET" build "$(wslpath -w "$SRC")\\src\\Shima\\Shima.csproj" -c Release -nologo -v q)
if [ "${1:-}" != "--smoke" ]; then
  powershell.exe -NoProfile -Command "Get-Process Shima -ErrorAction SilentlyContinue | Stop-Process -Force; Start-Sleep -Milliseconds 600"
fi
mkdir -p "$DEST/app${1:+-smoke}"
cp -r "$SRC"/src/Shima/bin/Release/net10.0-windows*/. "$DEST/app${1:+-smoke}/"
EXE="$LA\\Shima\\app${1:+-smoke}\\Shima.exe"
if [ -n "${1:-}" ]; then
  powershell.exe -NoProfile -Command "Start-Process -FilePath '$EXE' -ArgumentList '$1'"
else
  powershell.exe -NoProfile -Command "Start-Process -FilePath '$EXE'"
fi
