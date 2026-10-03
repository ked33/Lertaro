namespace Lertaro.Core;

/// <summary>Directory exceptions, compiled once with the surrounding exclusion rules.</summary>
public sealed class PathWhitelist
{
    public static PathWhitelist Empty { get; } = new(Array.Empty<string>());
    private readonly string[] _roots;
    public bool IsEmpty => _roots.Length == 0;
    internal IReadOnlyList<string> Roots => _roots;

    public PathWhitelist(IEnumerable<string> paths)
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
            if (TryNormalizeDirectory(path, out var normalized))
                roots.Add(normalized);
        // ponytail: O(n²) only while settings are compiled, keeping the per-result list minimal.
        // A prefix tree would only pay off for thousands of manually configured roots.
        var effective = new List<string>();
        foreach (var root in roots.OrderBy(p => p.Length).ThenBy(p => p, StringComparer.OrdinalIgnoreCase))
            if (!effective.Any(parent => IsWithin(root, parent)))
                effective.Add(root);
        _roots = effective.ToArray();
    }

    // ponytail: O(number of configured roots), allocation-free and no filesystem access per result.
    // Small user-maintained lists do not need a trie; consider one only if large lists prove costly.
    public bool Contains(string normalizedPath)
    {
        foreach (var root in _roots)
            if (IsWithin(normalizedPath, root))
                return true;
        return false;
    }

    public bool HasDescendant(string normalizedDirectory)
    {
        foreach (var root in _roots)
            if (IsWithin(root, normalizedDirectory))
                return true;
        return false;
    }

    internal static bool IsWithin(string path, string root)
    {
        var parent = root.AsSpan().TrimEnd(Path.DirectorySeparatorChar);
        return path.AsSpan().StartsWith(parent, StringComparison.OrdinalIgnoreCase)
            && (path.Length == parent.Length || path[parent.Length] == Path.DirectorySeparatorChar);
    }

    // No existence check: an offline share or a folder not created yet is still a valid setting.
    public static bool TryNormalizeDirectory(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
            return false;
        try
        {
            var path = Environment.ExpandEnvironmentVariables(value.Trim().Trim('"'))
                .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            if (!Path.IsPathFullyQualified(path) || path.IndexOfAny(Path.GetInvalidPathChars()) >= 0
                || path.Contains('*') || path.Contains('?') || path.AsSpan(2).Contains(':')
                || path.StartsWith(@"\\.\", StringComparison.Ordinal))
                return false;
            if (path.StartsWith(@"\\", StringComparison.Ordinal)
                && path.Trim('\\').Split('\\', StringSplitOptions.RemoveEmptyEntries).Length < 2)
                return false;
            normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
