using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.PluginSdk.Tests.Abstractions;

[TestClass]
public sealed class InstantResultProviderDefaultsTests
{
    [TestMethod]
    public void ExistingProvider_DoesNotRunOrClaimKeywordsInInlineWindow()
    {
        var legacy = new LegacyProvider();
        IInstantResultProvider provider = legacy;

        Assert.IsEmpty(provider.GetInlineResults("legacy", @"C:\work"));
        Assert.IsEmpty(provider.GetQueryTriggerKeywords(SearchWindowType.Inline));
        Assert.AreEqual(0, legacy.Calls);
        CollectionAssert.AreEqual(new[] { "legacy" }, provider.GetQueryTriggerKeywords(SearchWindowType.Main).ToArray());
        CollectionAssert.AreEqual(new[] { "legacy" }, provider.GetQueryTriggerKeywords(SearchWindowType.Quick).ToArray());
        Assert.HasCount(1, provider.GetInstantResults("legacy").ToList());
        Assert.AreEqual(1, legacy.Calls);
    }

    private sealed class LegacyProvider : IInstantResultProvider
    {
        public int Calls;
        public IReadOnlyList<string> QueryTriggerKeywords => ["legacy"];

        public IEnumerable<InstantResultItem> GetInstantResults(string query)
        {
            Calls++;
            return [new InstantResultItem { Title = query }];
        }
    }
}
