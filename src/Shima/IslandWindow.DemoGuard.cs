// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

namespace Shima;

public partial class IslandWindow
{
    /// <summary>
    /// 示範模式（--demo）下，會改到這份資料夾以外的動作（登錄、別的程式的設定檔、外掛）一律不做：
    /// 暫存資料夾裡的金鑰與啟動路徑寫出去，平常那一份 Shima 就失效了。只看 Demo.On，
    /// 單純用 SHIMA_DATA 換資料夾的人是自己要放在別處，按下去的連接與開機啟動照做。回傳 true 表示已擋下，呼叫端要把開關還原並直接結束。
    /// </summary>
    private bool RefuseInDemo()
    {
        if (!Demo.On) return false;
        Toast(L.T("示範模式下不能更改這項"), 4, false);
        return true;
    }
}
