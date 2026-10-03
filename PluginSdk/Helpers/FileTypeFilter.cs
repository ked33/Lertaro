using System.IO.Enumeration;

namespace Lertaro.PluginSdk.Helpers;

/// <summary>Expanded custom-filter rules, shared by the plugin and the index before result limits.</summary>
public sealed class FileTypeFilter
{
    private readonly string[] _patterns;
    private readonly bool _files;
    private readonly bool _folders;

    public FileTypeFilter(string rule)
    {
        var patterns = new List<string>();
        foreach (var part in rule.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case ":f": case "folder": case "dir": _folders = true; break;
                case ":-f": case "file": _files = true; break;
                default:
                    patterns.Add(part.Contains('*') || part.Contains('?') ? part : $"*.{part.TrimStart('.')}");
                    break;
            }
        }
        _patterns = patterns.ToArray();
    }

    public bool Matches(string name, bool isDirectory)
    {
        if (isDirectory) return _folders;
        if (_files) return true;
        foreach (var pattern in _patterns)
            if (FileSystemName.MatchesSimpleExpression(pattern, name, ignoreCase: true)) return true;
        return false;
    }
}
