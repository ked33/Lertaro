using System.Text.Json;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.CustomCommands.Tests;

// PluginSettingsService.GetSettingFunc is a shared static delegate (the SDK's own seam for the host to
// wire in real settings access) -- safe to set here since it's reset every test, but [DoNotParallelize]
// keeps two tests in this class from racing on it (MSTest parallelizes at the method level by default).
[TestClass]
[DoNotParallelize]
public sealed class CustomCommandsInstantProviderTests
{
    [TestCleanup]
    public void ResetSettingsFunc() => PluginSettingsService.GetSettingFunc = null;

    private static void ConfigureCommands(List<CustomCommandsInstantProvider.CommandItem> commands) =>
        PluginSettingsService.GetSettingFunc = (pluginId, key, defaultValue) =>
            pluginId == "Lertaro.Plugins.CustomCommands" && key == "Commands" ? commands : defaultValue;

    [TestMethod]
    public void GetInstantResults_EmptyQuery_ReturnsNothing() =>
        Assert.IsEmpty(new CustomCommandsInstantProvider().GetInstantResults(""));

    [TestMethod]
    public void GetInstantResults_NoConfiguredCommands_ReturnsNothing() =>
        Assert.IsEmpty(new CustomCommandsInstantProvider().GetInstantResults("build extra args"));

    [TestMethod]
    public void GetInstantResults_MatchingKeyword_SubstitutesAllArgsPlaceholder()
    {
        ConfigureCommands(new() { new() { Enabled = true, Keyword = "build", Path = "msbuild.exe", Parameter = "%s" } });

        // "My App.sln" (with a space) so the substituted value actually needs ArgQuoting's quotes --
        // a space-free value like "MyApp.sln" would come back unquoted (Quote's no-op fast path).
        var result = new CustomCommandsInstantProvider().GetInstantResults("build My App.sln").Single();

        Assert.AreEqual("Execute", result.ActionType);
        Assert.AreEqual("msbuild.exe \"My App.sln\"", result.ActionArgument);
    }

    [TestMethod]
    public void GetInstantResults_DisabledCommand_IsExcluded()
    {
        ConfigureCommands(new() { new() { Enabled = false, Keyword = "build", Path = "x.exe" } });

        Assert.IsEmpty(new CustomCommandsInstantProvider().GetInstantResults("build"));
    }

    [TestMethod]
    public void GetInstantResults_KeywordMatchIsCaseInsensitive()
    {
        ConfigureCommands(new() { new() { Enabled = true, Keyword = "Build", Path = "x.exe" } });

        Assert.HasCount(1, new CustomCommandsInstantProvider().GetInstantResults("build").ToList());
    }

    [TestMethod]
    public void GetInstantResults_PathWithSpace_IsQuotedInSimplePath()
    {
        ConfigureCommands(new() { new() { Enabled = true, Keyword = "run", Path = @"C:\Program Files\tool.exe" } });

        var result = new CustomCommandsInstantProvider().GetInstantResults("run").Single();

        Assert.AreEqual("\"C:\\Program Files\\tool.exe\"", result.ActionArgument);
    }

    [TestMethod]
    public void GetInstantResults_RunAsAdminWithoutWorkingDir_PrefixesRunas()
    {
        ConfigureCommands(new() { new() { Enabled = true, Keyword = "elevate", Path = "tool.exe", RunAsAdmin = true } });

        var result = new CustomCommandsInstantProvider().GetInstantResults("elevate").Single();

        Assert.AreEqual("runas:tool.exe", result.ActionArgument);
    }

    [TestMethod]
    public void GetInstantResults_WithWorkingDir_ProducesCcExecJsonPayload()
    {
        ConfigureCommands(new() { new() { Enabled = true, Keyword = "run", Path = "tool.exe", Parameter = "%s", WorkingDir = @"C:\work" } });

        var result = new CustomCommandsInstantProvider().GetInstantResults("run arg1").Single();

        Assert.StartsWith("cc_exec:", result.ActionArgument);
        using var doc = JsonDocument.Parse(result.ActionArgument["cc_exec:".Length..]);
        Assert.AreEqual("tool.exe", doc.RootElement.GetProperty("Path").GetString());
        Assert.AreEqual(@"C:\work", doc.RootElement.GetProperty("WorkingDir").GetString());
        Assert.AreEqual("arg1", doc.RootElement.GetProperty("Arguments").GetString());
    }

    [TestMethod]
    public void GetInstantResults_RunSilentlyWithoutWorkingDir_AlsoUsesJsonPayload()
    {
        ConfigureCommands(new() { new() { Enabled = true, Keyword = "run", Path = "tool.exe", RunSilently = true } });

        var result = new CustomCommandsInstantProvider().GetInstantResults("run").Single();

        Assert.StartsWith("cc_exec:", result.ActionArgument);
        using var doc = JsonDocument.Parse(result.ActionArgument["cc_exec:".Length..]);
        Assert.IsTrue(doc.RootElement.GetProperty("RunSilently").GetBoolean());
    }

    [TestMethod]
    public void GetHighlightMask_QueryKeywordFoundInText_HighlightsThatSpan()
    {
        var mask = new CustomCommandsInstantProvider().GetHighlightMask("Run Build Tool", "build extra");

        Assert.IsNotNull(mask);
        for (var i = 0; i < mask.Length; i++)
            Assert.AreEqual(i is >= 4 and < 9, mask[i], $"index {i}"); // "Build" spans [4,9)
    }

    [TestMethod]
    public void GetHighlightMask_KeywordNotInText_ReturnsNull() =>
        Assert.IsNull(new CustomCommandsInstantProvider().GetHighlightMask("Run Something", "zzz"));

    [TestMethod]
    public void GetHighlightMask_EmptyQuery_ReturnsNull() =>
        Assert.IsNull(new CustomCommandsInstantProvider().GetHighlightMask("text", ""));

    // The host strips only the words a provider publishes, and a user command IS a word the user typed to
    // invoke something -- so an enabled command's keyword has to be in that list, or "build x" is both the
    // command and a fuzzy search for the text "build". A DISABLED command has no feature behind its word,
    // so the word stays searchable as plain text and is deliberately not published.
    [TestMethod]
    public void QueryTriggerKeywords_PublishesEnabledKeywordsTrimmedAndDeduplicated()
    {
        ConfigureCommands(new()
        {
            new() { Enabled = true, Keyword = " build ", Path = "x.exe" },
            new() { Enabled = true, Keyword = "deploy", Path = "y.exe" },
            new() { Enabled = true, Keyword = "BUILD", Path = "z.exe" },
            new() { Enabled = false, Keyword = "off", Path = "w.exe" },
            new() { Enabled = true, Keyword = " 　 ", Path = "v.exe" },
        });

        CollectionAssert.AreEqual(
            new[] { "build", "deploy" },
            new CustomCommandsInstantProvider().QueryTriggerKeywords.ToList());
    }

    // Same separator rule as the host's strip, so a full-width space cannot leave the command firing while
    // the file search still carries "build　x" (or the reverse).
    [TestMethod]
    public void GetInstantResults_FullWidthSeparator_PassesTheRestAsInput()
    {
        ConfigureCommands(new() { new() { Enabled = true, Keyword = "build", Path = "msbuild.exe", Parameter = "%s" } });

        var result = new CustomCommandsInstantProvider().GetInstantResults("build　My App.sln").Single();

        Assert.AreEqual("msbuild.exe \"My App.sln\"", result.ActionArgument);
    }

    // A word that only STARTS a longer first token is not the command: "builder x" is a search, not "build".
    [TestMethod]
    public void GetInstantResults_KeywordOnlyPartOfFirstToken_ReturnsNothing()
    {
        ConfigureCommands(new() { new() { Enabled = true, Keyword = "build", Path = "x.exe" } });

        Assert.IsEmpty(new CustomCommandsInstantProvider().GetInstantResults("builder x"));
    }

    [TestMethod]
    public void InlineResults_CaptureDirectoryAndAdminOptions_WithoutChangingConfiguration()
    {
        var directory = Directory.CreateTempSubdirectory("lertaro-inline-").FullName;
        try
        {
            var normal = new CustomCommandsInstantProvider.CommandItem
            {
                Keyword = "tool", Path = "editor.exe", Parameter = "--new-window {currentDirectory}",
                UseCurrentDirectory = true, WorkingDir = @"C:\fixed", MatchKeywordPrefix = true
            };
            var admin = new CustomCommandsInstantProvider.CommandItem
            {
                Keyword = "toola", Path = "editor.exe", Parameter = "{currentDirectory}",
                UseCurrentDirectory = true, RunAsAdmin = true, MatchKeywordPrefix = true
            };
            ConfigureCommands(new() { normal, admin });
            var provider = new CustomCommandsInstantProvider();
            var results = provider.GetInlineResults("TOOL", directory).ToArray();

            Assert.HasCount(2, results);
            for (var i = 0; i < results.Length; i++)
            {
                Assert.StartsWith("cc_exec:", results[i].ActionArgument);
                using var payload = JsonDocument.Parse(results[i].ActionArgument[8..]);
                Assert.AreEqual(directory, payload.RootElement.GetProperty("WorkingDir").GetString());
                Assert.AreEqual(i == 1, payload.RootElement.GetProperty("RunAsAdmin").GetBoolean());
                Assert.AreEqual((i == 0 ? "--new-window " : "") + ArgQuoting.Quote(directory), payload.RootElement.GetProperty("Arguments").GetString());
            }
            Assert.AreEqual("tool", results[0].TabCompletion);
            Assert.AreEqual("toola", results[1].TabCompletion);
            Assert.AreEqual(@"C:\fixed", normal.WorkingDir);
            Assert.AreEqual("--new-window {currentDirectory}", normal.Parameter);
            Assert.HasCount(1, provider.GetInlineResults("toola", directory).ToList());
            Assert.IsEmpty(provider.GetInstantResults("tool"));
            Assert.IsEmpty(provider.GetInlineResults("tool", Path.Combine(directory, "missing")));
            Assert.IsEmpty(provider.GetInlineResults("tool", ""));
        }
        finally { Directory.Delete(directory); }
    }

    [TestMethod]
    public void PrefixMatching_IsOptIn_AndDoesNotMatchPartialWordWithArguments()
    {
        ConfigureCommands(new() { new() { Keyword = "toola", Path = "tool.exe" } });
        var provider = new CustomCommandsInstantProvider();
        Assert.IsEmpty(provider.GetInstantResults("tool"));

        ConfigureCommands(new() { new() { Keyword = "toola", Path = "tool.exe", MatchKeywordPrefix = true } });
        Assert.HasCount(1, provider.GetInstantResults("tool").ToList());
        Assert.IsEmpty(provider.GetInstantResults("tool argument"));
        Assert.IsEmpty(provider.GetInstantResults("toolax"));
        Assert.IsEmpty(provider.GetInstantResults(" "));
    }

    [TestMethod]
    public void TriggerInventory_AndQuickNavigation_ExcludeUnavailableContextCommands()
    {
        ConfigureCommands(new()
        {
            new() { Keyword = "global", Path = "tool.exe", ShowInQuickNav = true },
            new() { Keyword = "local", Path = "tool.exe", UseCurrentDirectory = true, ShowInQuickNav = true },
            new() { Keyword = "parameter", Path = "tool.exe", Parameter = "{currentDirectory}", ShowInQuickNav = true },
            new() { Keyword = "disabled", Path = "tool.exe", Enabled = false, UseCurrentDirectory = true },
        });
        var provider = new CustomCommandsInstantProvider();
        CollectionAssert.AreEqual(new[] { "global" }, provider.GetQueryTriggerKeywords(Lertaro.PluginSdk.Abstractions.SearchWindowType.Main).ToArray());
        CollectionAssert.AreEqual(new[] { "global" }, provider.GetQueryTriggerKeywords(Lertaro.PluginSdk.Abstractions.SearchWindowType.Quick).ToArray());
        CollectionAssert.AreEqual(new[] { "local", "parameter" }, provider.GetQueryTriggerKeywords(Lertaro.PluginSdk.Abstractions.SearchWindowType.Inline).ToArray());
        CollectionAssert.AreEqual(new[] { "global", "local", "parameter" }, provider.QueryTriggerKeywords.ToArray());

        var navigation = new CustomCommandsQuickNavProvider();
        Assert.IsTrue(navigation.CanProvide(null!));
        ConfigureCommands(new()
        {
            new() { Keyword = "local", Path = "tool.exe", UseCurrentDirectory = true, ShowInQuickNav = true },
            new() { Keyword = "parameter", Path = "tool.exe", Parameter = "{currentDirectory}", ShowInQuickNav = true }
        });
        navigation.ClearSession();
        Assert.IsFalse(navigation.CanProvide(null!));
        Assert.IsEmpty(navigation.GetMenuItems(null!, IntPtr.Zero));
    }

    [TestMethod]
    public void InlineResults_PlaceholderAloneRequiresContext_ButKeepsFixedWorkingDirectory()
    {
        var directory = Directory.CreateTempSubdirectory("lertaro-inline-").FullName;
        try
        {
            ConfigureCommands(new() { new() { Keyword = "tool", Path = "editor.exe", Parameter = "{currentDirectory}", WorkingDir = @"C:\fixed" } });
            var provider = new CustomCommandsInstantProvider();
            Assert.IsEmpty(provider.GetInstantResults("tool"));
            using var payload = JsonDocument.Parse(provider.GetInlineResults("tool", directory).Single().ActionArgument[8..]);
            Assert.AreEqual(@"C:\fixed", payload.RootElement.GetProperty("WorkingDir").GetString());
            Assert.AreEqual(ArgQuoting.Quote(directory), payload.RootElement.GetProperty("Arguments").GetString());
        }
        finally { Directory.Delete(directory); }
    }


    [TestMethod]
    public void LegacyConfiguration_NewOptionsRemainOff_AndNoCommandsArePreconfigured()
    {
        var command = JsonSerializer.Deserialize<CustomCommandsInstantProvider.CommandItem>("{\"Keyword\":\"tool\",\"Path\":\"tool.exe\"}")!;
        Assert.IsFalse(command.UseCurrentDirectory);
        Assert.IsFalse(command.MatchKeywordPrefix);
        var commands = new CustomCommandsPlugin().GetConfigSchema().Fields.Single(f => f.Key == "Commands");
        Assert.IsEmpty((List<object>)commands.DefaultValue!);
        Assert.AreEqual(false, commands.SubFields!.Single(f => f.Key == "UseCurrentDirectory").DefaultValue);
        Assert.AreEqual(false, commands.SubFields!.Single(f => f.Key == "MatchKeywordPrefix").DefaultValue);
    }
}
