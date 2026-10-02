using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.FolderCascader;

public class FolderCascaderPlugin : IPlugin, IConfigurable
{
    public string Name => TranslationService.Get("FolderCascader_PluginName");
    public string Description => TranslationService.Get("FolderCascader_PluginDesc");



    public class FolderConfigItem
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string SubMenu { get; set; } = string.Empty;
        public string ShortcutKey { get; set; } = string.Empty;
    }

    internal static string NormalizeShortcut(string? value)
    {
        var key = value?.Trim().ToUpperInvariant() ?? "";
        return key.Length == 1 && key[0] is >= 'A' and <= 'Z' ? key : "";
    }

    internal static PluginConfigField CreateShortcutField() => new()
    {
        Key = "ShortcutKey",
        LabelKey = "FolderCascader_Config_ShortcutKey",
        DescriptionKey = "FolderCascader_Config_ShortcutKeyDesc",
        FieldType = ConfigFieldType.Text,
        MaxLength = 1,
        DefaultValue = ""
    };

    internal static List<string> FindShortcutProblems(IEnumerable<FolderConfigItem> folders)
    {
        var entries = folders.Where(f => !string.IsNullOrWhiteSpace(f.Path) && f.Path != "-" && f.Name != "-").ToList();
        var problems = entries.Where(f => !string.IsNullOrWhiteSpace(f.ShortcutKey) && NormalizeShortcut(f.ShortcutKey) == "")
            .Select(f => $"{(string.IsNullOrWhiteSpace(f.Name) ? f.Path : f.Name)} ({f.ShortcutKey})").ToList();
        foreach (var group in entries.Where(f => NormalizeShortcut(f.ShortcutKey) != "").GroupBy(f => (
                     Level: string.Join("/", Navigation.MenuBuilder.SplitSubMenuPath(f.SubMenu)),
                     Key: NormalizeShortcut(f.ShortcutKey))))
        {
            if (group.Count() > 1)
                problems.Add($"({group.Key.Key}): {string.Join(", ", group.Select(f => string.IsNullOrWhiteSpace(f.Name) ? f.Path : f.Name))}");
        }
        return problems;
    }

    internal static void WarnShortcutProblems()
    {
        var folders = PluginSettingsService.GetSetting("Lertaro.Plugins.FolderCascader", "Folders", new List<FolderConfigItem>());
        var problems = FindShortcutProblems(folders ?? []);
        if (problems.Count > 0)
            PluginMessageBoxService.Show(
                string.Format(TranslationService.Get("FolderCascader_Config_ShortcutKeyWarning"), string.Join("\n", problems)),
                TranslationService.Get("FolderCascader_PluginName"), icon: System.Windows.MessageBoxImage.Warning);
    }

    public PluginConfigSchema GetConfigSchema() => new PluginConfigSchema
    {
        OnSave = WarnShortcutProblems,
        Fields = new List<PluginConfigField>
        {
            new PluginConfigField
            {
                Key = "ContentGroup",
                LabelKey = "FolderCascader_Group_Content",
                FieldType = ConfigFieldType.Group,
                SubFields = new List<PluginConfigField>
                {
                    new PluginConfigField
                    {
                        Key = "ShowOpenedFolders",
                        LabelKey = "FolderCascader_Config_ShowOpenedFolders",
                        DescriptionKey = "FolderCascader_Config_ShowOpenedFoldersDesc",
                        FieldType = ConfigFieldType.Boolean,
                        DefaultValue = true
                    },

                    new PluginConfigField
                    {
                        Key = "ShowFavorites",
                        LabelKey = "FolderCascader_Config_ShowFavorites",
                        DescriptionKey = "FolderCascader_Config_ShowFavoritesDesc",
                        FieldType = ConfigFieldType.Boolean,
                        DefaultValue = true
                    },

                    new PluginConfigField
                    {
                        Key = "ShowHistory",
                        LabelKey = "FolderCascader_Config_ShowHistory",
                        DescriptionKey = "FolderCascader_Config_ShowHistoryDesc",
                        FieldType = ConfigFieldType.Boolean,
                        DefaultValue = true
                    },

                    new PluginConfigField
                    {
                        Key = "ShowRecentFolders",
                        LabelKey = "FolderCascader_ShowRecentFolders",
                        DescriptionKey = "FolderCascader_ShowRecentFoldersDesc",
                        FieldType = ConfigFieldType.Boolean,
                        DefaultValue = true
                    },

                    new PluginConfigField
                    {
                        Key = "Folders",
                        LabelKey = "FolderCascader_Config_FoldersLabel",
                        DescriptionKey = "FolderCascader_Config_FoldersDesc",
                        FieldType = ConfigFieldType.Array,
                        DefaultValue = new List<object>
                        {
                            new Dictionary<string, object>
                            {
                                { "Name", "" },
                                { "Path", "shell:::{679f85cb-0220-4080-b29b-5540cc05aab6}" }
                            },
                            new Dictionary<string, object>
                            {
                                { "Name", "" },
                                { "Path", "shell:::{20d04fe0-3aea-1069-a2d8-08002b30309d}" }
                            },
                            new Dictionary<string, object>
                            {
                                { "Name", "" },
                                { "Path", "shell:::{450d8fba-ad25-11d0-98a8-0800361b1103}" }
                            }
                        },
                        SubFields = new List<PluginConfigField>
                        {
                            new PluginConfigField
                            {
                                Key = "Name",
                                LabelKey = "FolderCascader_Config_FolderName",
                                FieldType = ConfigFieldType.Text,
                                DefaultValue = ""
                            },
                            new PluginConfigField
                            {
                                Key = "Path",
                                LabelKey = "FolderCascader_Config_FolderPath",
                                FieldType = ConfigFieldType.FolderPath,
                                DefaultValue = ""
                            },
                            new PluginConfigField
                            {
                                Key = "SubMenu",
                                LabelKey = "FolderCascader_Config_SubMenuLabel",
                                DescriptionKey = "FolderCascader_Config_SubMenuDesc",
                                FieldType = ConfigFieldType.Text,
                                DefaultValue = ""
                            },
                            CreateShortcutField()
                        }
                    }
                }
            }
        }
    };
}
