using Lertaro.App.Services.ShellMenu.QuickNav;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.App.Tests.Services.ShellMenu.QuickNav;

[TestClass]
public sealed class QuickNavigationAvailabilityTests
{
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void PreparedStateDoesNotPerformAnotherFilesystemOrShellProbe(bool available)
    {
        var item = new DynamicMenuItem { IsPathAvailable = available };
        Assert.AreEqual(available, QuickNavigationMenuContentExtensions.IsPathAvailable(item,
            @"C:\Prepared", _ => throw new InvalidOperationException("Unexpected synchronous probe")));
    }

    [TestMethod]
    public void ProvidersWithoutPreparedStateStillUseTheExistingValidation()
    {
        var probes = 0;
        var item = new DynamicMenuItem();
        Assert.IsFalse(QuickNavigationMenuContentExtensions.IsPathAvailable(item, "missing", _ => { probes++; return false; }));
        Assert.AreEqual(1, probes);
        Assert.IsFalse(QuickNavigationMenuContentExtensions.IsPathAvailable(new() { IsPathAvailable = true }, null,
            _ => throw new InvalidOperationException("A missing path must not be probed")));
    }
}
