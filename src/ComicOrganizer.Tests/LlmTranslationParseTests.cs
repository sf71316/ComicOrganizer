using System.Collections.Generic;
using ComicOrganizer.App.Core;
using Xunit;
using Xunit.Abstractions;

namespace ComicOrganizer.Tests;

// LlmTranslationService 寬鬆容錯解析 ParseLenientArray 回歸測試。
// 來源：實機 gemma-4-e4b-it 對 1.png 吐出的壞 JSON（最後一格以全形 」 當結尾引號），
// 原本整批 fallback 回原文日文；容錯後應救回 14 格正常譯文。
public class LlmTranslationParseTests
{
    private readonly ITestOutputHelper _o;
    public LlmTranslationParseTests(ITestOutputHelper o) { _o = o; }

    [Fact]
    public void Lenient_RecoversTrailingFullwidthQuote()
    {
        // 注意末尾：…趕走！」]  ← 模型把結尾的 " 寫成全形 」
        string bad = "[\"我準備好了行裝\",\"走\",\"A\",\"讓他們和…北方的鄉里女官一人握有銀貨\",\"8\",\"帶到北斗州去\",\"在日出前回程的當值衛士與宦官不同，不涉及此事\",\"關於我們不必擔心，這裡的女官習慣了與宦官的交涉\",\"那不行！\",\"當然啊！\",\"如果除了我之外的人也消失了，剩下的惠香們就會被懷疑！\",\"…明凜…請你開口…\",\"哈噫\",\"那些失禮又暴戾的人，我會把他們趕走！」]";

        var res = LlmTranslationService.ParseLenientArray(bad);

        Assert.NotNull(res);
        Assert.Equal(14, res!.Count);
        Assert.Equal("我準備好了行裝", res[0]);
        Assert.Equal("那不行！", res[8]);
        // 最後一格：去掉誤當引號的全形 」
        Assert.Equal("那些失禮又暴戾的人，我會把他們趕走！", res[13]);
        for (int i = 0; i < res.Count; i++) _o.WriteLine($"[{i}] {res[i]}");
    }

    [Fact]
    public void Lenient_NormalValidJsonStillWorks()
    {
        string ok = "[\"你好\",\"\",\"謝謝\"]";
        var res = LlmTranslationService.ParseLenientArray(ok);
        Assert.NotNull(res);
        Assert.Equal(3, res!.Count);
        Assert.Equal("你好", res[0]);
        Assert.Equal("", res[1]);
        Assert.Equal("謝謝", res[2]);
    }

    [Fact]
    public void Lenient_HandlesMissingFinalBracketEntirely()
    {
        // 連結尾 ] 都被截斷
        string truncated = "[\"第一句\",\"第二句\",\"第三句被截斷";
        var res = LlmTranslationService.ParseLenientArray(truncated);
        Assert.NotNull(res);
        Assert.Equal(3, res!.Count);
        Assert.Equal("第三句被截斷", res[2]);
    }

    [Fact]
    public void Lenient_ReturnsNullWhenNoArray()
    {
        Assert.Null(LlmTranslationService.ParseLenientArray("這不是 JSON"));
        Assert.Null(LlmTranslationService.ParseLenientArray(""));
    }

    // 噪框判斷：避免模型刪格造成譯文整批位移（2.png 的 "Jup" 案例）
    [Theory]
    [InlineData("Jup", true)]    // 純拉丁
    [InlineData("vhah", true)]   // 純拉丁
    [InlineData("8", true)]      // 單字/純數字
    [InlineData("A", true)]      // 單字
    [InlineData("SS6J", true)]   // 純拉丁數字
    [InlineData("  ", true)]     // 空白
    [InlineData("な", true)]     // 單一假名
    [InlineData("十", true)]     // 單一漢字
    [InlineData("ばさっ", false)]            // 含假名的擬聲詞 → 交給 LLM 判斷
    [InlineData("それはだめよ！", false)]     // 正常對白
    [InlineData("明凜 SS6J", false)]          // 含漢字（名字）→ 不濾，送翻
    [InlineData("北斗州まで手引きさせます", false)]
    public void IsLikelyNoise_ClassifiesCorrectly(string text, bool expected)
    {
        Assert.Equal(expected, LlmTranslationService.IsLikelyNoise(text));
    }

    [Theory]
    // 純 host（本地/OpenAI）→ 補 /v1/chat/completions
    [InlineData("http://192.168.0.74:1234", "http://192.168.0.74:1234/v1/chat/completions")]
    [InlineData("https://api.openai.com", "https://api.openai.com/v1/chat/completions")]
    [InlineData("https://api.openai.com/", "https://api.openai.com/v1/chat/completions")]
    // OpenRouter base（/api 非版本段）→ 補 /v1/chat/completions
    [InlineData("https://openrouter.ai/api", "https://openrouter.ai/api/v1/chat/completions")]
    // Gemini（已含 /v1beta/openai 版本段）→ 只補 /chat/completions
    [InlineData("https://generativelanguage.googleapis.com/v1beta/openai", "https://generativelanguage.googleapis.com/v1beta/openai/chat/completions")]
    // 已是完整 URL → 原樣
    [InlineData("https://x.y/v1/chat/completions", "https://x.y/v1/chat/completions")]
    public void BuildOpenAiChatUrl_HandlesProviderConventions(string endpoint, string expected)
    {
        Assert.Equal(expected, LlmTranslationService.BuildOpenAiChatUrl(endpoint));
    }
}
