namespace Lertaro.Core;

public static class RecentFolderPaths
{
    // Lexical only: querying existence or resolving links here can block on a disconnected share.
    public static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            path = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"')).Replace('/', '\\');
            if (!Path.IsPathFullyQualified(path) || path.Contains("::{", StringComparison.Ordinal)
                || path.StartsWith(@"\\?\", StringComparison.Ordinal) || path.StartsWith(@"\\.\", StringComparison.Ordinal)
                || path.IndexOfAny(['*', '?', '\0']) >= 0) return null;
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    public static bool IsExcluded(string normalizedPath, IEnumerable<string> exclusions) => exclusions.Any(root =>
        normalizedPath.Equals(root, StringComparison.OrdinalIgnoreCase));
}
