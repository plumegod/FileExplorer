using System;
using System.Linq;
using FileExplorer.Core;
using FileExplorer.Persistence;

namespace FileExplorer.Tools
{
    public static class ToolPresetMenuFactory
    {
        public static bool AddIfMissing(PersistentCollection<MenuItem> menuItems, ToolPresetDefinition definition, string executablePath, string displayName)
        {
            if (menuItems.Any(x => String.Equals(x.ToolPresetId, definition.Id, StringComparison.Ordinal)))
                return false;

            menuItems.Add(new MenuItem
            {
                Name = displayName,
                GroupName = definition.GroupName,
                ToolPresetId = definition.Id,
                Command = CommandType.OpenWithApplication,
                Application = executablePath,
                Parameter = ParameterType.Path,
                ExtensionFilter = definition.ExtensionFilter,
                ItemTypeFilter = definition.ItemTypeFilter,
                SelectionFilter = definition.SelectionFilter,
                ConfirmBeforeRun = definition.ConfirmBeforeRun,
                ShowErrors = true,
                Prefix = String.Empty,
                Suffix = String.Empty
            });
            return true;
        }
    }
}
