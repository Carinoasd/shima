#!/usr/bin/env bash
# 由 src/Shima/Assets/shima.svg 產生 shima.ico（16 到 256 像素）。
# 用 Edge 無頭模式把 SVG 畫成 1024 像素的透明底 PNG，再用 Windows 內建的繪圖功能縮成各個尺寸。
set -euo pipefail
SRC="$(cd "$(dirname "$0")/.." && pwd)"
LA="$(powershell.exe -NoProfile -Command '[Environment]::GetFolderPath("LocalApplicationData")' | tr -d '\r')"
WORK="$(wslpath "$LA")/Shima/icon-build"
rm -rf "$WORK"; mkdir -p "$WORK"
cp "$SRC/src/Shima/Assets/shima.svg" "$WORK/icon.svg"
cat > "$WORK/icon.html" <<'HTML'
<!doctype html><html><body style="margin:0;background:transparent"><img src="icon.svg" style="display:block;width:1024px;height:1024px"></body></html>
HTML
powershell.exe -NoProfile -Command "
  \$w = Join-Path \$env:LOCALAPPDATA 'Shima\\icon-build'
  \$edge = @(\"\${env:ProgramFiles(x86)}\\Microsoft\\Edge\\Application\\msedge.exe\", \"\$env:ProgramFiles\\Microsoft\\Edge\\Application\\msedge.exe\") | Where-Object { Test-Path \$_ } | Select-Object -First 1
  & \$edge --headless=new --disable-gpu --hide-scrollbars --default-background-color=00000000 --window-size=1024,1024 \"--screenshot=\$w\\1024.png\" ('file:///' + (\$w -replace '\\\\','/') + '/icon.html') 2>\$null | Out-Null
  Start-Sleep -Milliseconds 500
  Add-Type -AssemblyName System.Drawing
  \$big = [System.Drawing.Image]::FromFile(\"\$w\\1024.png\")
  foreach (\$s in 16,20,24,32,40,48,64,128,256) {
    \$bmp = New-Object System.Drawing.Bitmap \$s, \$s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    \$g = [System.Drawing.Graphics]::FromImage(\$bmp)
    \$g.InterpolationMode = 'HighQualityBicubic'; \$g.PixelOffsetMode = 'HighQuality'; \$g.CompositingQuality = 'HighQuality'
    \$g.DrawImage(\$big, 0, 0, \$s, \$s); \$g.Dispose()
    \$bmp.Save(\"\$w\\\$s.png\", [System.Drawing.Imaging.ImageFormat]::Png); \$bmp.Dispose()
  }
  \$big.Dispose()
"
python3 - "$WORK" "$SRC/src/Shima/Assets/shima.ico" <<'PY'
import struct, sys, os
work, out = sys.argv[1], sys.argv[2]
sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
pngs = [open(os.path.join(work, f"{s}.png"), "rb").read() for s in sizes]
head = struct.pack("<HHH", 0, 1, len(sizes)); offset = 6 + 16 * len(sizes); entries = b""
for s, p in zip(sizes, pngs):
    entries += struct.pack("<BBBBHHII", s % 256, s % 256, 0, 0, 1, 32, len(p), offset); offset += len(p)
open(out, "wb").write(head + entries + b"".join(pngs))
print("shima.ico", offset, "bytes")
PY
