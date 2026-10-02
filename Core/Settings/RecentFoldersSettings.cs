namespace Lertaro.Core;

public sealed class RecentFoldersSettings
{
    public bool Enabled { get; set; } = true;
    public int Capacity { get; set; } = 200;
    public int MenuLimit { get; set; } = 20;
    public List<string> ExcludedDirectories { get; set; } = new();

    public RecentFoldersSettings CopyValidated() => new()
    {
        Enabled = Enabled,
        Capacity = Math.Clamp(Capacity, 1, 5000),
        MenuLimit = Math.Clamp(MenuLimit, 1, Math.Min(Math.Clamp(Capacity, 1, 5000), 100)),
        ExcludedDirectories = (ExcludedDirectories ?? new()).Select(RecentFolderPaths.Normalize)
            .OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList()
    };
}
