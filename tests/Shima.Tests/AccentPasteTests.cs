// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using Xunit;

namespace Shima.Tests;

/// <summary>自訂強調色的輸入框：貼上前後帶空白的色碼也要認得（輸入框不能先把它截短）。</summary>
public class AccentPasteTests
{
    [Theory]
    [InlineData(" #4FD1C5")]
    [InlineData("#4FD1C5 ")]
    [InlineData("  #4fd1c5\t ")]
    public void 前後帶空白的色碼照樣認得(string pasted) => Assert.Equal("#4FD1C5", Accent.Commit(pasted, "#000000"));

    [Fact]
    public void 長度上限7會把前面帶空白的色碼截壞() => Assert.Equal("#000000", Accent.Commit(" #4FD1C5"[..7], "#000000"));
}
