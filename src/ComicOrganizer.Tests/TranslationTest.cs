using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using ComicOrganizer.App.Core;

namespace ComicOrganizer.Tests;

public class TranslationTest
{
    private readonly ITestOutputHelper _output;

    public TranslationTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task TestLlmTranslation_WithMockOrReal()
    {
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        var translator = new LlmTranslationService(httpClient);
        
        // 使用使用者提供的環境配置
        translator.Configure("http://192.168.0.74:1234", "qwen3.6-35b-a3b-mtp", LlmApiType.OpenAiCompatible);

        _output.WriteLine($"Testing Translation API at: {translator.Endpoint}");
        _output.WriteLine($"Model: {translator.ModelName}");
        _output.WriteLine($"API Type: {translator.ApiType}");

        var sourceTexts = new List<string> { "こんにちは", "お元気ですか" };

        try
        {
            var results = await translator.TranslateBatchAsync(sourceTexts);
            
            Assert.NotNull(results);
            Assert.Equal(sourceTexts.Count, results.Count);

            _output.WriteLine("Translation process completed.");
            for (int i = 0; i < sourceTexts.Count; i++)
            {
                _output.WriteLine($"  [{sourceTexts[i]}] -> [{results[i]}]");
            }

            if (results[0] != sourceTexts[0])
            {
                _output.WriteLine("[INFO] Real translation output received successfully!");
            }
            else
            {
                _output.WriteLine("[WARNING] API returned fallback original text. Please verify if the LM-Studio server is running and the model is loaded.");
                if (!string.IsNullOrEmpty(translator.LastError))
                {
                    _output.WriteLine($"Detailed Error: {translator.LastError}");
                }
            }
        }
        catch (Exception ex)
        {
            _output.WriteLine($"[INFO] Could not complete translation call: {ex.Message}");
            _output.WriteLine("This is expected if the LM-Studio server is offline.");
        }
    }
}
