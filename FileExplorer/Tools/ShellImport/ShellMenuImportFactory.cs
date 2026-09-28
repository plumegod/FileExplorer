using System;
using System.Linq;
using FileExplorer.Core;
using FileExplorer.Persistence;

namespace FileExplorer.Tools.ShellImport
{
    public static class ShellMenuImportFactory
    {
        public static bool AddIfMissing(PersistentCollection<MenuItem> menuItems, MenuItemDraft draft)
        {
            if (menuItems == null)
                throw new ArgumentNullException(nameof(menuItems));
            if (draft == null)
                throw new ArgumentNullException(nameof(draft));
            if (!String.IsNullOrEmpty(draft.SourceId)
                && menuItems.Any(x => String.Equals(
                    x.ImportedMenuSourceId,
                    draft.SourceId,
                    StringComparison.OrdinalIgnoreCase)))
                return false;

            menuItems.Add(new MenuItem
            {
                Name = draft.Name,
                GroupName = draft.GroupName,
                Command = draft.IsShellBridge ? CommandType.InvokeShellHandler : CommandType.OpenWithApplication,
                Application = draft.IsShellBridge ? null : draft.Application,
                Parameter = draft.Parameter,
                Prefix = draft.Prefix,
                Suffix = draft.Suffix,
                ItemTypeFilter = draft.ItemTypeFilter,
                SelectionFilter = draft.SelectionFilter,
                ExtensionFilter = draft.ExtensionFilter,
                ContextFilter = draft.ContextFilter,
                ConfirmBeforeRun = draft.ConfirmBeforeRun,
                ShowErrors = true,
                ImportedMenuSourceId = draft.SourceId,
                ImportedMenuSourceSummary = draft.SourceSummary,
                ShellHandlerClsid = draft.ShellHandlerClsid,
                ShellHandlerName = draft.ShellHandlerName,
                ShellAssociationHint = draft.ShellAssociationHint,
                ToolPresetId = null
            });
            return true;
        }
    }
}
