#!/usr/bin/env bash
# 用示範模式把膠囊與各分頁存成 PNG（不會動到正在用的 Shima，也不讀自己的設定）。
#   tools/shot.sh <輸出資料夾> [--lang en …]
# 先建置（Release），再把建置結果複製到暫存位置執行；可以好幾份原始碼同時跑。
set -euo pipefail
SRC="$(cd "$(dirname "$0")/.." && pwd)"
OUT="${1:?用法：tools/shot.sh <輸出資料夾> [其他參數]}"; shift || true
DOTNET="/mnt/c/Program Files/dotnet/dotnet.exe"
(cd /mnt/c && "$DOTNET" build "$(wslpath -w "$SRC")\\src\\Shima\\Shima.csproj" -c Release -nologo -v q | grep -E 'error|個錯誤' || true)
TEMP_WIN="$(powershell.exe -NoProfile -Command '$env:TEMP' | tr -d '\r')"
RUN="shima-shot-$$"
APP="$(wslpath "$TEMP_WIN")/$RUN"
rm -rf "$APP"; mkdir -p "$APP/app"
cp -r "$SRC"/src/Shima/bin/Release/net10.0-windows*/. "$APP/app/"
ARGS="'--demo','--shot','$TEMP_WIN\\$RUN\\out'"
for a in "$@"; do ARGS="$ARGS,'$a'"; done
powershell.exe -NoProfile -Command "\$p = Start-Process -FilePath '$TEMP_WIN\\$RUN\\app\\Shima.exe' -ArgumentList $ARGS -PassThru; if (-not \$p.WaitForExit(120000)) { 'TIMEOUT'; \$p | Stop-Process -Force }" | tr -d '\r'
mkdir -p "$OUT"; rm -f "$OUT"/*.png
cp "$APP/out/"*.png "$OUT/"
rm -rf "$APP"
ls "$OUT"
