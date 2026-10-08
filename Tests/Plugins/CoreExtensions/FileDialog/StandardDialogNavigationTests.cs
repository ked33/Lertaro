using Lertaro.Plugins.CoreExtensions.FileDialog;

namespace Lertaro.Plugins.CoreExtensions.Tests.FileDialog;

[TestClass]
public sealed class StandardDialogNavigationTests
{
    [TestMethod]
    public void BusyControlsAreRetriedAndSuccessWaitsForTheActualDirectory()
    {
        var submissions = 0;
        var reads = 0;
        var succeeded = StandardDialogNavigation.NavigateFolder(@"C:\Target\", () => true,
            () => ++reads >= 6 ? @"C:\Target" : @"C:\Before",
            () => ++submissions >= 3, CancellationToken.None, _ => { });
        Assert.IsTrue(succeeded);
        Assert.AreEqual(3, submissions, "Once submitted, waiting for the folder must not send Enter again.");
        Assert.AreEqual(6, reads);
    }

    [TestMethod]
    public void SuccessfulSubmissionWithoutDirectoryChangeIsNotSuccess()
    {
        var submissions = 0;
        Assert.IsFalse(StandardDialogNavigation.NavigateFolder(@"C:\Target\", () => true,
            () => @"C:\Before", () => { submissions++; return true; }, CancellationToken.None, _ => { }));
        Assert.AreEqual(1, submissions);
    }

    [TestMethod]
    public void AlreadyAtTargetDoesNotTouchControls()
    {
        Assert.IsTrue(StandardDialogNavigation.NavigateFolder(@"C:\Target\", () => true,
            () => @"c:\target", () => throw new AssertFailedException("No input is needed."),
            CancellationToken.None, _ => { }));
    }

    [TestMethod]
    public void LosingForegroundDuringAReadDoesNotSendInput()
    {
        var foreground = true;
        Assert.IsFalse(StandardDialogNavigation.NavigateFolder(@"C:\Target\", () => foreground,
            () => { foreground = false; return @"C:\Before"; },
            () => throw new AssertFailedException("Input would reach a different window."),
            CancellationToken.None, _ => { }));
    }

    [TestMethod]
    public void CancellationAfterSubmissionStopsVerification()
    {
        using var cancellation = new CancellationTokenSource();
        var reads = 0;
        Assert.IsFalse(StandardDialogNavigation.NavigateFolder(@"C:\Target\", () => true,
            () => { reads++; return @"C:\Before"; }, () => true, cancellation.Token,
            _ => cancellation.Cancel()));
        Assert.AreEqual(1, reads);
    }

    [TestMethod]
    [DataRow(@"C:\", @"c:\", true)]
    [DataRow(@"\\server\share\folder\", @"\\server\share\folder", true)]
    [DataRow(@"C:\Target", @"C:\Target2", false)]
    [DataRow(null, @"C:\Target", false)]
    public void PathComparisonHandlesRootsAndUnc(string? left, string right, bool expected)
        => Assert.AreEqual(expected, StandardDialogNavigation.PathsEqual(left, right));

    [TestMethod]
    [DataRow(@"地址: C:\资料", @"C:\资料")]
    [DataRow(@"地址：C:\资料", @"C:\资料")]
    [DataRow(@"Address: \\server\share", @"\\server\share")]
    [DataRow(@"C:\", @"C:\")]
    public void BreadcrumbReaderHandlesLocalizedPrefixes(string text, string expected)
        => Assert.AreEqual(expected, StandardFileDialogAdapter.ParseBreadcrumbPath(text));
}
