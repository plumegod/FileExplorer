using System;
using System.Collections.Generic;
using System.Linq;
using FileExplorer.Core;
using Microsoft.Win32;

namespace FileExplorer.Tools.ShellImport
{
    public enum ShellRegistryHive
    {
        CurrentUser,
        LocalMachine
    }

    public sealed class ShellRegistryValue
    {
        public ShellRegistryValue(object value, RegistryValueKind kind)
        {
            Value = value;
            Kind = kind;
        }

        public object Value { get; }
        public RegistryValueKind Kind { get; }
    }

    public sealed class ShellRegistryKeySnapshot
    {
        public ShellRegistryKeySnapshot(
            IDictionary<string, ShellRegistryValue> values = null,
            IDictionary<string, ShellRegistryKeySnapshot> subKeys = null)
        {
            Values = new Dictionary<string, ShellRegistryValue>(
                values ?? new Dictionary<string, ShellRegistryValue>(),
                StringComparer.OrdinalIgnoreCase);
            SubKeys = new Dictionary<string, ShellRegistryKeySnapshot>(
                subKeys ?? new Dictionary<string, ShellRegistryKeySnapshot>(),
                StringComparer.OrdinalIgnoreCase);
        }

        public IReadOnlyDictionary<string, ShellRegistryValue> Values { get; }
        public IReadOnlyDictionary<string, ShellRegistryKeySnapshot> SubKeys { get; }

        public bool ContentEquals(ShellRegistryKeySnapshot other)
        {
            if (other == null || Values.Count != other.Values.Count || SubKeys.Count != other.SubKeys.Count)
                return false;
            foreach (KeyValuePair<string, ShellRegistryValue> value in Values)
            {
                if (!other.Values.TryGetValue(value.Key, out ShellRegistryValue otherValue)
                    || value.Value.Kind != otherValue.Kind
                    || !Object.Equals(value.Value.Value, otherValue.Value))
                    return false;
            }
            return SubKeys.All(x => other.SubKeys.TryGetValue(x.Key, out ShellRegistryKeySnapshot otherKey)
                && x.Value.ContentEquals(otherKey));
        }
    }

    public interface IShellRegistryReader
    {
        ShellRegistryKeySnapshot ReadAssociation(ShellRegistryHive hive, RegistryView view, string association);
    }

    public interface IShellRegistryReaderWithDiagnostics : IShellRegistryReader
    {
        ShellRegistryReadResult ReadAssociationWithDiagnostics(
            ShellRegistryHive hive,
            RegistryView view,
            string association);
    }

    public sealed class ShellRegistryReadResult
    {
        public ShellRegistryReadResult(
            ShellRegistryKeySnapshot snapshot,
            IReadOnlyList<ShellMenuDiagnostic> diagnostics,
            bool isIncomplete)
        {
            Snapshot = snapshot;
            Diagnostics = diagnostics;
            IsIncomplete = isIncomplete;
        }

        public ShellRegistryKeySnapshot Snapshot { get; }
        public IReadOnlyList<ShellMenuDiagnostic> Diagnostics { get; }
        public bool IsIncomplete { get; }
    }

    public sealed class ShellMenuScope
    {
        private ShellMenuScope(
            string id,
            string association,
            string verbPath,
            ItemTypeFilter itemTypeFilter,
            MenuContextFilter contextFilter,
            string extensionFilter = null,
            string handlerPath = @"shellex\ContextMenuHandlers")
        {
            Id = id;
            Association = association;
            VerbPath = verbPath;
            ItemTypeFilter = itemTypeFilter;
            ContextFilter = contextFilter;
            ExtensionFilter = extensionFilter;
            HandlerPath = handlerPath;
        }

        public string Id { get; }
        public string Association { get; }
        public string VerbPath { get; }
        public ItemTypeFilter ItemTypeFilter { get; }
        public MenuContextFilter ContextFilter { get; }
        public string ExtensionFilter { get; }
        public string HandlerPath { get; }

        public static ShellMenuScope ForFileAssociation(string id, string association, string extensionFilter)
        {
            return new ShellMenuScope(id, association, "shell", ItemTypeFilter.File,
                MenuContextFilter.FileSelection, extensionFilter);
        }

        public static ShellMenuScope File { get; } = new ShellMenuScope("file", "*", "shell", ItemTypeFilter.File, MenuContextFilter.FileSelection);
        public static ShellMenuScope AllFilesystemObjects { get; } = new ShellMenuScope("all-filesystem-objects", "AllFilesystemObjects", "shell", ItemTypeFilter.None, MenuContextFilter.FileSystemSelection);
        public static ShellMenuScope Directory { get; } = new ShellMenuScope("directory", "Directory", "shell", ItemTypeFilter.Folder, MenuContextFilter.DirectorySelection);
        public static ShellMenuScope DirectoryBackground { get; } = new ShellMenuScope(
            "directory-background", "Directory", @"Background\shell", ItemTypeFilter.Folder,
            MenuContextFilter.DirectoryBackground, null, @"Background\shellex\ContextMenuHandlers");
        public static ShellMenuScope Folder { get; } = new ShellMenuScope("folder", "Folder", "shell", ItemTypeFilter.Folder, MenuContextFilter.DirectorySelection);
        public static ShellMenuScope Drive { get; } = new ShellMenuScope("drive", "Drive", "shell", ItemTypeFilter.Folder, MenuContextFilter.DriveSelection);
    }

    public sealed class ShellMenuSource
    {
        public ShellMenuSource(ShellRegistryHive hive, RegistryView view, string registryPath)
            : this(hive, view, SplitAssociation(registryPath), SplitVerbPath(registryPath))
        {
        }

        public ShellMenuSource(
            ShellRegistryHive hive,
            RegistryView view,
            string association,
            string verbPath)
        {
            Hive = hive;
            View = view;
            Association = association ?? String.Empty;
            VerbPath = verbPath ?? String.Empty;
            RegistryPath = String.IsNullOrEmpty(VerbPath)
                ? Association
                : Association + "\\" + VerbPath;
            Precedence = (hive == ShellRegistryHive.CurrentUser ? 0 : 2)
                + (view == RegistryView.Registry64 ? 0 : 1);
            Fingerprint = (Hive + "|" + View + "|" + RegistryPath).ToLowerInvariant();
            LogicalIdentity = (Association + "|" + VerbPath).ToLowerInvariant();
        }

        public ShellRegistryHive Hive { get; }
        public RegistryView View { get; }
        public string Association { get; }
        public string VerbPath { get; }
        public string RegistryPath { get; }
        public int Precedence { get; }
        public string Fingerprint { get; }
        public string LogicalIdentity { get; }

        private static string SplitAssociation(string registryPath)
        {
            int separator = registryPath?.IndexOf('\\') ?? -1;
            return separator < 0 ? registryPath : registryPath.Substring(0, separator);
        }

        private static string SplitVerbPath(string registryPath)
        {
            int separator = registryPath?.IndexOf('\\') ?? -1;
            return separator < 0 ? String.Empty : registryPath.Substring(separator + 1);
        }
    }

    public enum ShellMenuDiagnosticCode
    {
        Shadowed,
        ViewConflict,
        SourceReadFailed
    }

    public sealed class ShellMenuDiagnostic
    {
        public ShellMenuDiagnostic(ShellMenuDiagnosticCode code, string registryPath, string message)
        {
            Code = code;
            RegistryPath = registryPath;
            Message = message;
        }

        public ShellMenuDiagnosticCode Code { get; }
        public string RegistryPath { get; }
        public string Message { get; }
    }
}
