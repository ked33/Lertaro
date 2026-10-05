namespace Lertaro.Plugins.CustomCommands.Tests;

[TestClass]
public sealed class CommandRunnerTests
{
    private static CustomCommandsInstantProvider.CommandItem MakeCommand(string parameter) =>
        new() { Parameter = parameter };

    [TestMethod]
    public void ResolveParameter_NoPlaceholders_ReturnsParameterUnchanged()
    {
        var result = CommandRunner.ResolveParameter(MakeCommand("--flag"), "ignored");

        Assert.AreEqual("--flag", result);
    }

    [TestMethod]
    public void ResolveParameter_PercentSPositional_SubstitutesNthArgument()
    {
        var result = CommandRunner.ResolveParameter(MakeCommand("%s1"), "hello world");

        Assert.AreEqual("hello", result);
    }

    [TestMethod]
    public void ResolveParameter_BraceStylePositional_SubstitutesNthArgument()
    {
        var result = CommandRunner.ResolveParameter(MakeCommand("{2}"), "a b");

        Assert.AreEqual("b", result);
    }

    [TestMethod]
    public void ResolveParameter_OutOfRangePositional_ResolvesToEmptyNotLiteral()
    {
        var result = CommandRunner.ResolveParameter(MakeCommand("[%s5]"), "a");

        Assert.AreEqual("[]", result);
    }

    [TestMethod]
    public void ResolveParameter_DoubleDigitPositional_MatchesWholeNumberGreedily()
    {
        // %s10 must be read as index 10 (out of range here), not %s1 followed by a literal '0'.
        var result = CommandRunner.ResolveParameter(MakeCommand("[%s10]"), "a");

        Assert.AreEqual("[]", result);
    }

    [TestMethod]
    public void ResolveParameter_PercentSAllArgs_SubstitutesWholeSuffixQuoted()
    {
        var result = CommandRunner.ResolveParameter(MakeCommand("%s"), "hello world");

        Assert.AreEqual("\"hello world\"", result);
    }

    [TestMethod]
    public void ResolveParameter_BraceAllArgs_SubstitutesWholeSuffixQuoted()
    {
        var result = CommandRunner.ResolveParameter(MakeCommand("{}"), "a b");

        Assert.AreEqual("\"a b\"", result);
    }

    [TestMethod]
    public void ResolveParameter_EmptyArgSuffix_AllArgsPlaceholderResolvesToEmpty()
    {
        var result = CommandRunner.ResolveParameter(MakeCommand("%s"), "");

        Assert.AreEqual("", result);
    }

    [TestMethod]
    public void ResolveParameter_QuotedSegmentInArgSuffix_ParsedAsOneArgument()
    {
        var result = CommandRunner.ResolveParameter(MakeCommand("%s1 %s2"), "\"a b\" c");

        Assert.AreEqual("\"a b\" c", result);
    }

    [TestMethod]
    public void ResolveParameter_NullParameter_TreatedAsEmptyTemplate()
    {
        var cmd = new CustomCommandsInstantProvider.CommandItem { Parameter = null! };

        var result = CommandRunner.ResolveParameter(cmd, "anything");

        Assert.AreEqual("", result);
    }

    [TestMethod]
    public void ResolveParameter_CurrentDirectory_IsQuotedOnceAndNeverReparsed()
    {
        var root = Directory.CreateTempSubdirectory("lertaro-args-").FullName;
        try
        {
            var directory = Directory.CreateDirectory(Path.Combine(root, "中文 space %s {1} {} {currentDirectory}")).FullName;
            var command = MakeCommand("{currentDirectory} %s1 {} --last={currentDirectory}");
            var result = CommandRunner.ResolveParameter(command, "literal{currentDirectory}", directory);
            var quoted = "\"" + directory + "\"";

            Assert.AreEqual(quoted + " literal{currentDirectory} literal{currentDirectory} --last=" + quoted, result);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public void ResolveParameter_SubstitutedInput_IsNotExpandedAgain()
    {
        Assert.AreEqual("literal%s{}", CommandRunner.ResolveParameter(MakeCommand("%s1"), "literal%s{}"));
        Assert.AreEqual("literal{}", CommandRunner.ResolveParameter(MakeCommand("%s"), "literal{}"));
    }

    [TestMethod]
    public void ResolveParameter_MissingCurrentDirectory_RefusesContextDependentCommand()
    {
        Assert.ThrowsExactly<DirectoryNotFoundException>(() => CommandRunner.ResolveParameter(MakeCommand("{currentDirectory}"), ""));
        var command = MakeCommand("--flag");
        command.UseCurrentDirectory = true;
        Assert.ThrowsExactly<DirectoryNotFoundException>(() => CommandRunner.ResolveParameter(command, ""));
    }

}
