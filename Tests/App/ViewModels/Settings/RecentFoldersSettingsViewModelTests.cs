using System.IO;
using Lertaro.App.ViewModels.Settings;
using Lertaro.Core;

namespace Lertaro.App.Tests.ViewModels.Settings;

[TestClass]
public sealed class RecentFoldersSettingsViewModelTests
{
    [StaTestMethod]
    public void OptionsAreStagedButDeletionIsImmediateAndDoesNotLoseNewVisits()
    {
        var directory = Path.Combine(Path.GetTempPath(), "RecentFoldersVm_" + Guid.NewGuid().ToString("N"));
        try
        {
            using var store = new RecentFoldersStore(Path.Combine(directory, "history.json"), new(), () => 50);
            store.Record(@"C:\A", 10);
            var settings = new UserSettings();
            var vm = new RecentFoldersSettingsViewModel(settings, store);
            try
            {
                vm.Enabled = false;
                vm.Capacity = 100;
                Assert.IsTrue(settings.RecentFolders.Enabled);
                Assert.AreEqual(200, settings.RecentFolders.Capacity);
                store.Record(@"C:\B", 20);
                Assert.HasCount(2, vm.Items);
                vm.RemoveCommand.Execute(vm.Items.Single(i => i.Path == @"C:\A"));
                Assert.AreEqual(@"C:\B", store.GetSnapshot().Entries.Single().Path);
                vm.Save();
                Assert.IsFalse(settings.RecentFolders.Enabled);
                Assert.AreEqual(100, settings.RecentFolders.Capacity);
                Assert.AreEqual(@"C:\B", store.GetSnapshot().Entries.Single().Path);
            }
            finally { vm.Cleanup(); }
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [StaTestMethod]
    public void InvalidLimitsAndExclusionsCannotBeSavedAndFilterKeepsPathsSearchable()
    {
        var directory = Path.Combine(Path.GetTempPath(), "RecentFoldersVm_" + Guid.NewGuid().ToString("N"));
        try
        {
            using var store = new RecentFoldersStore(Path.Combine(directory, "history.json"), new());
            store.Record(@"C:\Work\Project", 10);
            store.Record(@"C:\Other", 20);
            var settings = new UserSettings();
            var vm = new RecentFoldersSettingsViewModel(settings, store);
            try
            {
                vm.SearchText = "Work";
                Assert.HasCount(1, vm.Items);
                vm.Capacity = 0;
                vm.MenuLimit = 101;
                vm.ExcludedDirectories = "relative";
                Assert.AreNotEqual(string.Empty, vm[nameof(vm.Capacity)]);
                Assert.AreNotEqual(string.Empty, vm[nameof(vm.MenuLimit)]);
                Assert.AreNotEqual(string.Empty, vm[nameof(vm.ExcludedDirectories)]);
                vm.Save();
                Assert.AreEqual(200, settings.RecentFolders.Capacity);
                vm.ClearCommand.Execute(null);
                Assert.HasCount(0, store.GetSnapshot().Entries);
            }
            finally { vm.Cleanup(); }
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
