#!/usr/bin/env bash
# 用示範模式把膠囊與各分頁存成 PNG（不會動到正在用的 Shima，也不讀自己的設定）。
#   tools/shot.sh <輸出資料夾> [--lang en …]
# 先建置（Release），再把建置結果複製到暫存位置執行；可以好幾份原始碼同時跑。
set -euo pipefail
SRC="$(cd "$(dirname "$0")/.." && pwd)"
OUT="${1:?用法：tools/shot.sh <輸出資料夾> [其他參數]}"; shift || true
DOTNET="/mnt/c/Program Files/dotnet/dotnet.exe"
# 建置失敗要停下來：不然會拿舊的 bin/Release 去執行，改了程式卻看到舊畫面
if ! BUILD_LOG="$(cd /mnt/c && "$DOTNET" build "$(wslpath -w "$SRC")\\src\\Shima\\Shima.csproj" -c Release -nologo -v q 2>&1)"; then
  echo "$BUILD_LOG" | grep -E 'error' || echo "$BUILD_LOG"
  echo "建置失敗" >&2; exit 1
fi
TEMP_WIN="$(powershell.exe -NoProfile -Command '$env:TEMP' | tr -d '\r')"
RUN="shima-shot-$$"
APP="$(wslpath "$TEMP_WIN")/$RUN"
rm -rf "$APP"; mkdir -p "$APP/app"
trap 'rm -rf "$APP"' EXIT   # 逾時、出錯、被中斷時暫存資料夾也要清掉
cp -r "$SRC"/src/Shima/bin/Release/net10.0-windows*/. "$APP/app/"
ARGS="'--demo','--shot','$TEMP_WIN\\$RUN\\out'"
for a in "$@"; do ARGS="$ARGS,'${a//\'/\'\'}'"; done   # PowerShell 的單引號字串裡，單引號要寫兩個
mkdir -p "$OUT"; rm -f "$OUT"/*.png   # 先清掉舊圖，這次沒產出時就不會誤看到上一輪的
SHOT_LOG="$(powershell.exe -NoProfile -Command "\$p = Start-Process -FilePath '$TEMP_WIN\\$RUN\\app\\Shima.exe' -ArgumentList $ARGS -PassThru; if (-not \$p.WaitForExit(120000)) { 'TIMEOUT'; \$p | Stop-Process -Force }" | tr -d '\r')"
[ -z "$SHOT_LOG" ] || echo "$SHOT_LOG"
if [[ "$SHOT_LOG" == *TIMEOUT* ]] || ! compgen -G "$APP/out/*.png" >/dev/null; then echo "沒有產出截圖" >&2; exit 1; fi
cp "$APP/out/"*.png "$OUT/"
ls "$OUT"
