using System.Net;
using System.Text.Json;

namespace Lertaro.Plugins.Translator.Tests;

[TestClass]
public sealed class MicrosoftTranslationFetcherTests
{
    [TestMethod]
    [DataRow("zh-Hans", "zh-CN", true, 2)]
    [DataRow("ZH-HANS", "zh-CN", true, 2)]
    [DataRow("zh-CN", "ja", true, 2)]
    [DataRow("zh-Hans", "zh-CN", false, 1)]
    [DataRow("zh-Hant", "zh-CN", true, 1)]
    [DataRow("en", "zh-CN", true, 1)]
    [DataRow("", "zh-CN", true, 1)]
    [DataRow("zh-Hans", "en", true, 1)]
    [DataRow("zh-Hans", "en-US", true, 1)]
    public async Task TranslateAsync_SwitchesOnlyDetectedSimplifiedChineseWhenEnabled(
        string detectedLanguage, string targetLanguage, bool enabled, int expectedRequests)
    {
        using var handler = new TranslationHandler(detectedLanguage);
        using var client = new HttpClient(handler);

        var result = await MicrosoftTranslationFetcher.TranslateAsync("你好", targetLanguage, enabled, client);

        Assert.HasCount(expectedRequests, handler.Targets);
        Assert.AreEqual(targetLanguage, handler.Targets[0]);
        Assert.IsTrue(handler.Texts.All(text => text == "你好"));
        Assert.AreEqual(expectedRequests == 2 ? "en" : targetLanguage, result.TargetLanguage);
        Assert.AreEqual(expectedRequests == 2 ? "Hello" : "First translation", result.Text);
        Assert.AreEqual(detectedLanguage, result.DetectedLanguage);
    }

    [TestMethod]
    public async Task TranslateAsync_EnglishRequestFails_ReportsFailure()
    {
        using var handler = new TranslationHandler("zh-Hans", failEnglish: true);
        using var client = new HttpClient(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            MicrosoftTranslationFetcher.TranslateAsync("你好", "zh-CN", true, client));

        CollectionAssert.AreEqual(new[] { "zh-CN", "en" }, handler.Targets);
    }

    private sealed class TranslationHandler(string detectedLanguage, bool failEnglish = false) : HttpMessageHandler
    {
        public List<string> Targets { get; } = [];
        public List<string> Texts { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.AreEqual(HttpMethod.Post, request.Method);
            var target = Uri.UnescapeDataString(request.RequestUri!.Query.Split("&to=")[1]);
            Targets.Add(target);
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            Texts.Add(JsonSerializer.Deserialize<string[]>(body)!.Single());
            if (failEnglish && target == "en")
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new[]
                {
                    new
                    {
                        detectedLanguage = new { language = detectedLanguage },
                        translations = new[] { new { text = Targets.Count == 2 ? "Hello" : "First translation", to = target } }
                    }
                }))
            };
        }
    }
}
