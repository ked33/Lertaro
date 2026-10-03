using Lertaro.App.ViewModels.Settings;
using Lertaro.Core;

namespace Lertaro.App.Tests.ViewModels.Settings;

[TestClass]
public sealed class WhitelistSettingsViewModelTests
{
    [TestMethod]
    public void DefaultEditor_IsEmptyAndDoesNotInstallDefaults()
    {
        var settings = new UserSettings();
        var vm = new WhitelistSettingsViewModel(settings);
        Assert.AreEqual(string.Empty, vm.Paths);
        vm.Save();
        Assert.IsEmpty(settings.WhitelistedPaths);
    }

    [TestMethod]
    public void Save_ParsesLinesAndKeepsEnvironmentVariableSpelling()
    {
        var settings = new UserSettings();
        var vm = new WhitelistSettingsViewModel(settings)
        {
            Paths = "  \"%USERPROFILE%\\.claude\"\r\nC:\\work\nc:\\WORK\n\n"
        };
        Assert.IsTrue(vm.IsValid);
        vm.Save();
        CollectionAssert.AreEqual(new[] { @"%USERPROFILE%\.claude", @"C:\work" }, settings.WhitelistedPaths);
        var snapshot = SettingsChangeSnapshot.CaptureExclusions(settings);
        settings.WhitelistedPaths.Clear();
        Assert.HasCount(2, snapshot.Whitelist);
    }

    [TestMethod]
    public void InvalidEdit_IsRejectedWithoutChangingSavedPaths()
    {
        var settings = new UserSettings { WhitelistedPaths = [@"C:\work"] };
        var vm = new WhitelistSettingsViewModel(settings) { Paths = @"relative\path" };
        Assert.IsFalse(vm.IsValid);
        Assert.Throws<ArgumentException>(() => vm.Save());
        CollectionAssert.AreEqual(new[] { @"C:\work" }, settings.WhitelistedPaths);
    }
}
