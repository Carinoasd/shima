// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using Shima.Agents;
using Xunit;

namespace Shima.Tests;

public class CommandsTests
{
    private static Command P(string? line) => Commands.Parse(line);

    [Theory]
    [InlineData("/play", CommandKind.Play)]
    [InlineData("/播放", CommandKind.Play)]
    [InlineData("/pause", CommandKind.Pause)]
    [InlineData("/暫停", CommandKind.Pause)]
    [InlineData("/暂停", CommandKind.Pause)]
    [InlineData("/next", CommandKind.Next)]
    [InlineData("/下一首", CommandKind.Next)]
    [InlineData("/prev", CommandKind.Prev)]
    [InlineData("/previous", CommandKind.Prev)]
    [InlineData("/上一首", CommandKind.Prev)]
    [InlineData("/lyrics", CommandKind.Lyrics)]
    [InlineData("/歌詞", CommandKind.Lyrics)]
    [InlineData("/earlier", CommandKind.Earlier)]
    [InlineData("/提早", CommandKind.Earlier)]
    [InlineData("/later", CommandKind.Later)]
    [InlineData("/延後", CommandKind.Later)]
    [InlineData("/settings", CommandKind.Settings)]
    [InlineData("/設定", CommandKind.Settings)]
    [InlineData("/help", CommandKind.Help)]
    [InlineData("/說明", CommandKind.Help)]
    public void 英文名稱與中文別名都認得(string line, CommandKind kind)
    {
        var c = P(line);
        Assert.Equal(CommandState.Ok, c.State);
        Assert.Equal(kind, c.Spec!.Kind);
    }

    [Theory]
    [InlineData("/NEXT")]
    [InlineData("  /next  ")]
    [InlineData("／next")]
    [InlineData("／ＮＥＸＴ")]
    [InlineData("／下一首")]
    [InlineData("　／下一首　")]
    [InlineData("/ next")]
    public void 大小寫空白與全形都認得(string line) => Assert.Equal(CommandKind.Next, P(line).Spec!.Kind);

    [Theory]
    [InlineData("/")]
    [InlineData("／")]
    [InlineData("  /  ")]
    public void 只打斜線是說明(string line) => Assert.Equal(CommandKind.Help, P(line).Spec!.Kind);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("下一首")]
    [InlineData("next")]
    [InlineData("現在播的是哪一首？")]
    [InlineData("a /next")]
    [InlineData("\\next")]
    public void 不是斜線開頭就不是指令(string? line) => Assert.Equal(CommandState.NotCommand, P(line).State);

    [Theory]
    [InlineData("/nope", "nope")]
    [InlineData("/pl", "pl")]
    [InlineData("//next", "/next")]
    [InlineData("/usr/bin", "usr/bin")]
    public void 不存在的指令(string line, string name)
    {
        var c = P(line);
        Assert.Equal(CommandState.Unknown, c.State);
        Assert.Equal(name, c.Name);
    }

    [Theory]
    [InlineData("/next now")]
    [InlineData("/play 1")]
    [InlineData("/lyrics on")]
    public void 不吃參數的指令多了字就是參數不對(string line)
    {
        Assert.Equal(CommandState.BadArgs, P(line).State);
        Assert.NotNull(P(line).Spec);
    }

    [Theory]
    [InlineData("/vol 0", 0)]
    [InlineData("/vol 30", 30)]
    [InlineData("/vol 100", 100)]
    [InlineData("/音量 45", 45)]
    [InlineData("/vol 30%", 30)]
    [InlineData("／vol　５０", 50)]
    [InlineData("/vol 007", 7)]
    public void 音量的數字(string line, int level)
    {
        var c = P(line);
        Assert.Equal(CommandState.Ok, c.State);
        Assert.Equal("set", c.Action);
        Assert.Equal(level, c.Number);
    }

    [Theory]
    [InlineData("/vol +", "up")]
    [InlineData("/vol -", "down")]
    [InlineData("/vol ＋", "up")]
    [InlineData("/音量 up", "up")]
    [InlineData("/vol down", "down")]
    public void 音量加減(string line, string action) => Assert.Equal(action, P(line).Action);

    [Theory]
    [InlineData("/vol")]
    [InlineData("/vol 101")]
    [InlineData("/vol -5")]
    [InlineData("/vol 1.5")]
    [InlineData("/vol loud")]
    [InlineData("/vol 99999999999")]
    public void 音量的邊界外是參數不對(string line) => Assert.Equal(CommandState.BadArgs, P(line).State);

    [Theory]
    [InlineData("/sleep 1", 1)]
    [InlineData("/sleep 30", 30)]
    [InlineData("/sleep 600", 600)]
    [InlineData("/睡眠 45", 45)]
    [InlineData("/sleep 30m", 30)]
    [InlineData("/sleep 30 分鐘", 30)]
    public void 睡眠的分鐘(string line, int minutes)
    {
        var c = P(line);
        Assert.Equal("minutes", c.Action);
        Assert.Equal(minutes, c.Number);
    }

    [Theory]
    [InlineData("/sleep track", "track")]
    [InlineData("/sleep 這首播完", "track")]
    [InlineData("/sleep off", "off")]
    [InlineData("/sleep 取消", "off")]
    [InlineData("/sleep OFF", "off")]
    public void 睡眠的其他寫法(string line, string action) => Assert.Equal(action, P(line).Action);

    [Theory]
    [InlineData("/sleep")]
    [InlineData("/sleep 0")]
    [InlineData("/sleep 601")]
    [InlineData("/sleep -5")]
    [InlineData("/sleep soon")]
    public void 睡眠的邊界外是參數不對(string line) => Assert.Equal(CommandState.BadArgs, P(line).State);

    [Fact]
    public void 提醒與待辦的內容原樣保留()
    {
        var remind = P("/remind 每天 8:00 吃藥");
        Assert.Equal(CommandKind.Remind, remind.Spec!.Kind);
        Assert.Equal("每天 8:00 吃藥", remind.Text);
        var todo = P("／待辦　買牛奶！ 和雞蛋");
        Assert.Equal(CommandKind.Todo, todo.Spec!.Kind);
        Assert.Equal("買牛奶！ 和雞蛋", todo.Text);
        Assert.Equal(CommandState.BadArgs, P("/remind").State);
        Assert.Equal(CommandState.BadArgs, P("/todo   ").State);
    }

    [Theory]
    [InlineData("/focus", "start", 0)]
    [InlineData("/focus 50", "start", 50)]
    [InlineData("/focus 180", "start", 180)]
    [InlineData("/專注", "start", 0)]
    [InlineData("/focus stop", "stop", 0)]
    [InlineData("/focus reset", "reset", 0)]
    [InlineData("/專注 重來", "reset", 0)]
    public void 番茄鐘(string line, string action, int minutes)
    {
        var c = P(line);
        Assert.Equal(CommandState.Ok, c.State);
        Assert.Equal(action, c.Action);
        Assert.Equal(minutes, c.Number);
    }

    [Theory]
    [InlineData("/focus 0")]
    [InlineData("/focus 181")]
    [InlineData("/focus later")]
    public void 番茄鐘的邊界外(string line) => Assert.Equal(CommandState.BadArgs, P(line).State);

    [Theory]
    [InlineData("/dnd", "on")]
    [InlineData("/勿擾", "on")]
    [InlineData("/dnd off", "off")]
    [InlineData("/dnd 關", "off")]
    public void 勿擾(string line, string action) => Assert.Equal(action, P(line).Action);

    [Fact]
    public void 勿擾的參數不對() => Assert.Equal(CommandState.BadArgs, P("/dnd maybe").State);

    [Fact]
    public void 名稱與別名不重複()
    {
        var names = Commands.All.SelectMany(s => s.Aliases.Prepend(s.Name)).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
        Assert.All(Commands.All, s => Assert.Equal(s.Name, s.Name.ToLowerInvariant()));
        Assert.All(Commands.All, s => Assert.StartsWith("/" + s.Name, s.Usage));
        Assert.All(Commands.All, s => Assert.NotEmpty(s.Help()));
    }

    [Fact]
    public void 每個指令都解析得到自己()
    {
        foreach (var s in Commands.All)
            Assert.NotEqual(CommandState.Unknown, P("/" + s.Name).State);
    }

    [Fact]
    public void 提示清單只有斜線與名稱時才出現()
    {
        Assert.Empty(Commands.Suggest(null));
        Assert.Empty(Commands.Suggest(""));
        Assert.Empty(Commands.Suggest("next"));
        Assert.Empty(Commands.Suggest("/next now"));
        Assert.Empty(Commands.Suggest("/zzz"));
    }

    [Fact]
    public void 提示清單最多六個且順序照清單()
    {
        var all = Commands.Suggest("/");
        Assert.Equal(Commands.MaxHints, all.Count);
        Assert.Equal(Commands.All.Take(Commands.MaxHints).Select(s => s.Name), all.Select(h => h.Spec.Name));
        Assert.Equal(all.Count, Commands.Suggest("／").Count);
    }

    [Fact]
    public void 提示清單邊打邊篩()
    {
        Assert.Equal(["settings", "sleep"], Commands.Suggest("/s").Select(h => h.Spec.Name).Order());
        Assert.Equal(["sleep"], Commands.Suggest("/sl").Select(h => h.Spec.Name));
        Assert.Equal(["sleep"], Commands.Suggest("／SL").Select(h => h.Spec.Name));
        Assert.Equal(["play"], Commands.Suggest("/pla").Select(h => h.Spec.Name));
        Assert.Equal(["pause", "play", "prev"], Commands.Suggest("/p").Select(h => h.Spec.Name).Order());
    }

    [Fact]
    public void 提示清單用中文打就顯示中文別名()
    {
        var hit = Assert.Single(Commands.Suggest("/下"));
        Assert.Equal(CommandKind.Next, hit.Spec.Kind);
        Assert.Equal("下一首", hit.Shown);
        Assert.Equal("next", Commands.Suggest("/ne").Single().Shown);
    }

    [Fact]
    public void 說明列出每一個指令()
    {
        var plain = Commands.HelpText(false);
        var chinese = Commands.HelpText(true);
        foreach (var s in Commands.All)
        {
            Assert.Contains(s.Usage, plain);
            Assert.Contains(s.Usage, chinese);
        }
        Assert.DoesNotContain("/專注", plain);
        Assert.Contains("/專注", chinese);
        Assert.DoesNotContain("/下一首", chinese);
        Assert.Equal(Commands.All.Count, plain.Split('\n').Length);
    }
}
