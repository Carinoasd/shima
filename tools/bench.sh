#!/usr/bin/env bash
# 量測閒置與播歌時的成本（示範模式，不會動到正在用的 Shima）。
#   tools/bench.sh [秒數=20] [模式…=idle playing expanded]
# 每個模式跑一次，印出這段時間用了多少 CPU、配置了多少記憶體。機器上別的程式在忙時數字會飄，要比較請各跑幾次。
set -euo pipefail
SRC="$(cd "$(dirname "$0")/.." && pwd)"
SECONDS_EACH="${1:-20}"; shift || true
MODES=("$@"); [ ${#MODES[@]} -gt 0 ] || MODES=(idle playing expanded)
DOTNET="/mnt/c/Program Files/dotnet/dotnet.exe"
(cd /mnt/c && "$DOTNET" build "$(wslpath -w "$SRC")\\src\\Shima\\Shima.csproj" -c Release -nologo -v q | grep -E 'error|個錯誤' || true)
TEMP_WIN="$(powershell.exe -NoProfile -Command '$env:TEMP' | tr -d '\r')"
RUN="shima-bench-$$"
APP="$(wslpath "$TEMP_WIN")/$RUN"
rm -rf "$APP"; mkdir -p "$APP/app"
cp -r "$SRC"/src/Shima/bin/Release/net10.0-windows*/. "$APP/app/"
for mode in "${MODES[@]}"; do
  powershell.exe -NoProfile -Command "\$p = Start-Process -FilePath '$TEMP_WIN\\$RUN\\app\\Shima.exe' -ArgumentList '--demo','--bench','$SECONDS_EACH','$mode','$TEMP_WIN\\$RUN\\result.txt' -PassThru; if (-not \$p.WaitForExit(($SECONDS_EACH + 60) * 1000)) { 'TIMEOUT'; \$p | Stop-Process -Force }" | tr -d '\r'
done
cat "$APP/result.txt" 2>/dev/null | tr -d '\r'
rm -rf "$APP"
