// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using Shima.Agents;
using Xunit;

namespace Shima.Tests;

/// <summary>
/// 常用網站只放行列出的用法：清單裡的網站如果會替你去抓別的網址、或把你轉到別的網站，資料就不只到那個網站本身，要使用者確認。
/// </summary>
public class OpenAllowListTests
{
    private static bool Trusted(string input)
    {
        var what = Opener.Classify(input);
        Assert.Equal(OpenKind.Url, what.Kind);
        return Opener.Trusted(what.Target);
    }

    [Theory]
    // 打開 YouTube、看影片、搜尋某首歌、頻道
    [InlineData("youtube.com")]
    [InlineData("https://www.youtube.com/")]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ&t=30")]
    [InlineData("https://www.youtube.com/results?search_query=%E6%81%8B%E3%81%AB%E3%81%AA%E3%82%8A%E3%81%9F%E3%81%84AQUARIUM")]
    [InlineData("https://www.youtube.com/results?search_query=Aqours+青空Jumping+Heart")]
    [InlineData("https://www.youtube.com/playlist?list=PL123")]
    [InlineData("https://www.youtube.com/@LoveLive_staff")]
    [InlineData("https://www.youtube.com/@LoveLive_staff/videos")]
    [InlineData("https://music.youtube.com/search?q=Liella")]
    [InlineData("https://youtu.be/abc?t=10")]
    // 搜尋
    [InlineData("https://www.google.com/")]
    [InlineData("https://www.google.com/search?q=%E6%99%B4%E5%A4%A9+%E5%91%A8%E6%9D%B0%E5%80%AB")]
    [InlineData("https://www.google.co.jp/search?q=ラブライブ！サンシャイン!!&hl=ja")]
    [InlineData("https://www.bing.com/search?q=Aqours")]
    [InlineData("https://duckduckgo.com/?q=lovelive+sunshine")]
    [InlineData("https://duckduckgo.com/?q=ラブライブ!サンシャイン!!")]
    [InlineData("https://www.baidu.com/s?wd=%E6%99%B4%E5%A4%A9")]
    [InlineData("https://search.yahoo.co.jp/search?p=μs")]
    // 查維基百科的某個條目
    [InlineData("https://ja.wikipedia.org/wiki/ラブライブ!")]
    [InlineData("https://zh.wikipedia.org/wiki/%E6%99%B4%E5%A4%A9_(%E5%91%A8%E6%9D%B0%E5%80%AB%E6%AD%8C%E6%9B%B2)")]
    [InlineData("https://en.m.wikipedia.org/wiki/Love_Live!")]
    // 開 Google 地圖找某個地方
    [InlineData("https://www.google.com/maps/search/?api=1&query=%E6%B2%BC%E6%B4%A5%E9%A7%85")]
    [InlineData("https://www.google.com/maps/place/Numazu/@35.1,138.86,14z")]
    [InlineData("https://www.google.com/maps?q=Numazu")]
    [InlineData("https://maps.google.com/?q=Numazu")]
    [InlineData("https://www.openstreetmap.org/search?query=Numazu")]
    // 聽歌、動畫
    [InlineData("https://open.spotify.com/intl-ja/track/1?si=abc")]
    [InlineData("https://open.spotify.com/search/Aqours")]
    [InlineData("https://music.apple.com/jp/album/1?i=2")]
    [InlineData("https://www.bilibili.com/video/BV1xx?p=2")]
    [InlineData("https://www.nicovideo.jp/watch/sm9")]
    [InlineData("https://www.pixiv.net/en/artworks/1")]
    [InlineData("https://www.lovelive-anime.jp/hasunosora/")]
    [InlineData("https://x.com/lovelive_staff/status/1")]
    [InlineData("https://github.com/Carinoasd/shima/releases")]
    public void 正常的用法照舊直接開(string url) => Assert.True(Trusted(url));

    [Theory]
    // 替你去抓任意網址的服務
    [InlineData("https://translate.google.com/translate?sl=auto&tl=en&u=evil.example/clip-secret")]
    [InlineData("https://translate.google.com/")]
    [InlineData("https://www.google.com/s2/favicons?domain=secret.evil.example")]
    [InlineData("https://images.google.com/searchbyimage?image_url=evil.example/x.png")]
    [InlineData("https://www.google.com/searchbyimage?image_url=https://evil.example/x.png")]
    [InlineData("https://www.google.com/search?tbs=sbi&image_url=evil.example/secret.png")]
    [InlineData("https://www.bing.com/images/search?view=detailv2&iss=sbi&q=imgurl:evil.example/secret.jpg")]
    [InlineData("https://www.google.com/maps?q=evil.example/secret.kml")]
    [InlineData("https://web.archive.org/save/evil.example/secret")]
    // 轉址、開放重新導向
    [InlineData("https://www.google.com/url?q=evil.example/secret")]
    [InlineData("https://www.google.com/url?q=/search")]
    [InlineData("https://www.google.com/amp/s/evil.example/secret")]
    [InlineData("https://www.google.com/%61mp/s/evil.example/secret")]
    [InlineData("https://www.google.com/%2561mp/s/evil")]
    [InlineData("https://www.google.com/search/../url?q=x")]
    [InlineData("https://www.google.com/search/%2e%2e/url?q=x")]
    [InlineData("https://www.google.com/search?q=x&btnI=1&as_sitesearch=evil.example")]
    [InlineData("https://www.youtube.com/redirect?q=evil.example%2Fsecret")]
    [InlineData("https://www.youtube.com/attribution_link?u=/watch%3Fv%3Dx")]
    [InlineData("https://m.youtube.com/redirect?q=x")]
    [InlineData("https://www.facebook.com/l.php?u=evil.example")]
    [InlineData("https://www.bing.com/ck/a?u=a1aHR0cHM6Ly9ldmlsLmV4YW1wbGU")]
    [InlineData("https://www.baidu.com/link?url=abc")]
    [InlineData("https://duckduckgo.com/l/?uddg=evil")]
    [InlineData("https://duckduckgo.com/?q=!g+secret")]
    [InlineData("https://duckduckgo.com/?q=secret+!w")]
    [InlineData("https://duckduckgo.com/?q=%21+secret")]
    [InlineData("https://www.pixiv.net/jump.php?evil")]
    [InlineData("https://community.roonlabs.com/clicks/track?url=x")]
    [InlineData("https://ja.wikipedia.org/wiki/Special:GoToInterwiki/google:secret")]
    [InlineData("https://ja.wikipedia.org/w/index.php?title=Special:Search&search=x")]
    [InlineData("https://news.google.com/rss/articles/CBMiabc")]
    [InlineData("https://x.com/share?text=secret")]
    [InlineData("https://x.com/intent/tweet?text=secret")]
    [InlineData("https://x.com/lovelive_staff?ref=secret")]
    [InlineData("https://github.com/someone/repo/blob/main/secret")]
    [InlineData("https://github.com/someone/repo?secret")]
    [InlineData("https://open.spotify.com/track/1/../../evil")]
    // 不在清單裡的路徑與主機（說不出哪些路徑安全的就整個不放）
    [InlineData("https://www.google.com/s2/favicons?domain=abc")]
    [InlineData("https://www.google.com/imgres?imgurl=x")]
    [InlineData("https://docs.google.com/forms/d/e/1/formResponse?entry.1=secret")]
    [InlineData("https://accounts.google.com/")]
    [InlineData("https://open.spotify.com/../evil")]
    [InlineData("https://developer.apple.com/")]
    [InlineData("https://www.roonlabs.com/go?x")]
    // 任何網站：解開跳脫後夾著主機名稱或網址
    [InlineData("https://www.google.com/search?q=secret.evil.example")]
    [InlineData("https://www.google.com/search?q=secret%252Eevil%252Eexample")]
    [InlineData("https://www.google.com/search?q=secret%E3%80%82evil%E3%80%82example")]   // 「。」也是點
    [InlineData("https://www.google.com/search?q=secret．evil．example")]
    [InlineData("https://www.google.com/search?q=10.0.0.1")]
    [InlineData("https://www.google.com/search?q=%5B2001%3Adb8%3A%3A1%5D")]
    [InlineData("https://www.youtube.com/results?search_query=https%3A%2F%2Fevil")]
    [InlineData("https://www.youtube.com/watch?v=x#https://evil")]
    [InlineData("https://www.google.com/search?q=xn--ggle-55da.xn--p1ai")]
    // 搜尋關鍵字裡剛好有點的也要多按一下（可以接受的代價）
    [InlineData("https://www.google.com/search?q=Mr.Children")]
    [InlineData("https://ja.wikipedia.org/wiki/Node.js")]
    public void 會把資料帶到別處的用法要確認(string url) => Assert.False(Trusted(url));

    [Fact]
    public void 查詢字串太長要確認()
    {
        Assert.True(Trusted("https://www.google.com/search?q=" + new string('a', 150)));
        Assert.False(Trusted("https://www.google.com/search?q=" + new string('a', 220)));
        // 用跳脫拉長的不算：解開之後才算長度；夾在路徑裡的也算
        Assert.False(Trusted("https://www.youtube.com/@" + new string('a', 220)));
        Assert.False(Trusted("https://www.google.com/search?q=" + string.Concat(Enumerable.Repeat("%E5%AF%86", 210))));
    }

    [Theory]
    [InlineData("http://www.youtube.com/watch?v=x")]   // 明文：路上的人就能把它轉到別處
    [InlineData("https://www.google.com/search?q=" + "%25252525252541")]   // 疊太多層跳脫
    public void 其他不正常的也要確認(string url) => Assert.False(Opener.Trusted(url));
}
