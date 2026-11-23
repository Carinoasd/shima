// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Windows;
using Xunit;

namespace Shima.Tests;

public class ScaleTests
{
    [Theory]
    [InlineData(0.9)]
    [InlineData(1.0)]
    [InlineData(1.15)]
    [InlineData(1.3)]
    [InlineData(1.2)]   // 手改設定檔、不在選項裡但在範圍內：照用
    public void 範圍內的比例照用(double value) => Assert.Equal(value, IslandScale.Sanitize(value));

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(0.5)]
    [InlineData(0.89)]
    [InlineData(1.31)]
    [InlineData(2)]
    [InlineData(100)]
    public void 不合理的比例當成1(double value) => Assert.Equal(1, IslandScale.Sanitize(value));

    [Theory]
    [InlineData(0.9, 0.9)]
    [InlineData(1.0, 1.0)]
    [InlineData(1.2, 1.15)]
    [InlineData(1.25, 1.3)]
    [InlineData(0.95, 0.9)]
    [InlineData(double.NaN, 1.0)]
    [InlineData(5, 1.0)]
    public void 設定頁亮最接近的那一格(double value, double shown) => Assert.Equal(shown, IslandScale.Nearest(value));

    [Theory]
    [InlineData(1.0, 560)]
    [InlineData(0.9, 504)]
    [InlineData(1.15, 644)]   // 560 × 1.15 有浮點誤差，不能進位成 645
    [InlineData(1.3, 728)]
    public void 視窗寬跟著比例(double scale, double width) => Assert.Equal(width, IslandScale.Window(560, scale));

    [Fact]
    public void 百分之百的視窗大小跟以前一模一樣()
    {
        // 以前：Math.Max(312, 8 + 島高 + 48)，視窗寬 560
        foreach (double island in new[] { 36, 164, 380, 549, 619 })
            Assert.Equal(Math.Max(312, 8 + island + 48), IslandScale.WindowHeight(312, island, 1));
        Assert.Equal(560, IslandScale.Window(560, 1));
    }

    [Theory]
    [InlineData(36, 1.3, 406)]     // 膠囊：平常的 312 放大
    [InlineData(380, 1.3, 567)]    // 音樂分頁：8 + 380 + 48 = 436，乘上 1.3 再進位
    [InlineData(380, 0.9, 393)]
    [InlineData(36, 0.9, 281)]     // 縮小時平常的高度也跟著縮
    public void 視窗高跟著比例(double island, double scale, double height) =>
        Assert.Equal(height, IslandScale.WindowHeight(312, island, scale));

    [Fact]
    public void 放大後視窗放得下整座島()
    {
        // 最寬的聊天分頁 528、展開最高的情況，都要在視窗裡（島離上緣 8，底下留 48 給陰影）
        foreach (double scale in IslandScale.Steps)
        {
            Assert.True(IslandScale.Window(560, scale) >= 528 * scale);
            foreach (double island in new[] { 36, 400, 620 })
                Assert.True(IslandScale.WindowHeight(312, island, scale) >= (8 + island + 48) * scale - 1e-9);
        }
    }

    private static readonly Rect Work = new(0, 0, 1920, 1032);
    private static readonly Rect Screens = new(0, 0, 1920, 1080);

    [Theory]
    [InlineData(1.0, 560)]
    [InlineData(1.3, 728)]
    [InlineData(0.9, 504)]
    public void 沒拖過就在工作區上緣正中間(double scale, double width)
    {
        var at = IslandScale.TopLeft(Work, Screens, null, null, width, 440 * scale);
        Assert.Equal(960, at.X + width / 2);   // 島的中心永遠是螢幕中心，不管多大
        Assert.Equal(0, at.Y);
        // 跟以前的寫法一樣
        Assert.Equal(Work.Left + (Work.Width - width) / 2, at.X);
    }

    [Fact]
    public void 工作區不在原點時也置中()
    {
        // 工作列在左邊與上面
        var work = new Rect(62, 48, 1858, 1032);
        var at = IslandScale.TopLeft(work, Screens, null, null, 728, 572);
        Assert.Equal(62 + 1858 / 2.0, at.X + 728 / 2);
        Assert.Equal(48, at.Y);
    }

    [Theory]
    [InlineData(1.0, 560)]
    [InlineData(1.3, 728)]
    [InlineData(0.9, 504)]
    public void 拖過的位置放大縮小時中心不動(double scale, double width)
    {
        var at = IslandScale.TopLeft(Work, Screens, 700, 120, width, 440 * scale);
        Assert.Equal(700, at.X + width / 2);
        Assert.Equal(120, at.Y);
    }

    [Fact]
    public void 靠邊的位置放大後往內推_面板不出螢幕()
    {
        // 島中心 X = 250：100% 時展開 440 寬剛好放得下，130% 時 572 寬會超出左邊，往內推到 286
        Assert.Equal(250, IslandScale.TopLeft(Work, Screens, 250, 0, 560, 440).X + 280);
        Assert.Equal(286, IslandScale.TopLeft(Work, Screens, 250, 0, 728, 572).X + 364);
        // 右邊也一樣
        Assert.Equal(1920 - 286, IslandScale.TopLeft(Work, Screens, 1900, 0, 728, 572).X + 364);
    }

    [Fact]
    public void 多螢幕_左邊的螢幕是負座標()
    {
        var screens = new Rect(-2560, -200, 4480, 1440);
        var at = IslandScale.TopLeft(Work, screens, -1280, -200, 728, 572);
        Assert.Equal(-1280, at.X + 364);
        Assert.Equal(-200, at.Y);
    }

    [Theory]
    [InlineData(-10, 0)]       // 在所有螢幕左邊
    [InlineData(1930, 0)]      // 右邊
    [InlineData(500, -5)]      // 上面
    [InlineData(500, 1050)]    // 太靠近底部
    public void 存的位置不在螢幕上就回到預設(double x, double y)
    {
        var at = IslandScale.TopLeft(Work, Screens, x, y, 728, 572);
        Assert.Equal(960, at.X + 364);
        Assert.Equal(0, at.Y);
    }

    [Fact]
    public void 只存了一半的位置當成沒存()
    {
        Assert.Equal(960, IslandScale.TopLeft(Work, Screens, 300, null, 560, 440).X + 280);
        Assert.Equal(960, IslandScale.TopLeft(Work, Screens, null, 30, 560, 440).X + 280);
    }

    [Theory]
    [InlineData(500, 0, 1000, 200, 500)]
    [InlineData(50, 0, 1000, 200, 100)]
    [InlineData(990, 0, 1000, 200, 900)]
    [InlineData(10, 0, 150, 200, 75)]    // 範圍比寬還窄：放正中間
    public void 夾在範圍內(double centre, double left, double right, double width, double expected) =>
        Assert.Equal(expected, IslandScale.Inside(centre, left, right, width));

    [Theory]
    [InlineData(1.0, 408)]
    [InlineData(1.3, 531)]
    public void 換歌小卡片跟著放大後仍在上緣正中間(double scale, double width)
    {
        double window = IslandScale.Window(380 + 14 * 2, scale);
        Assert.Equal(width, window);
        var at = MiniToast.Place(new Rect(0, 0, 1920, 1080), window);
        Assert.InRange(at.X + window / 2, 959.5, 960.5);   // 左邊對齊整數像素，中心最多差半點
        Assert.Equal(0, at.Y);
    }
}
