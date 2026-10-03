namespace Lertaro.Core.Indexer.NetworkDrive.Walk;

internal sealed record WalkOptions(
    IReadOnlyList<string> ExcludedPaths,
    IReadOnlyList<string> IgnoredPathGlobs,
    IReadOnlyList<string> IgnoredPathRegexes,
    int MaxDepth,
    int WorkerCount,
    bool UseIgnoreFiles,
    IReadOnlyList<string>? WhitelistedPaths = null);

internal readonly record struct NetworkDriveWalkStats(
    int Skipped,
    int Errors,
    int EnumerateErrors,
    int AttributeErrors,
    int ReparseSkipped,
    int SlowDirectories);
