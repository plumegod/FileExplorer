using System;
using System.Collections.Generic;
using System.Linq;

namespace FileExplorer.Tools.ShellImport
{
    public sealed class HkcrMergePolicy
    {
        public HkcrMergeResult Merge(
            string association,
            ShellRegistryKeySnapshot user,
            ShellRegistryKeySnapshot machine,
            ShellMenuSource userSource,
            ShellMenuSource machineSource)
        {
            var shadowed = new List<ShellShadowedTree>();
            MergedShellRegistryKey root = MergeNode(
                association, user, machine, userSource, machineSource, shadowed);
            return new HkcrMergeResult(root, shadowed);
        }

        private MergedShellRegistryKey MergeNode(
            string path,
            ShellRegistryKeySnapshot user,
            ShellRegistryKeySnapshot machine,
            ShellMenuSource userSource,
            ShellMenuSource machineSource,
            IList<ShellShadowedTree> shadowed)
        {
            if (user == null)
                return Copy(machine, machineSource);
            if (machine == null)
                return Copy(user, userSource);
            if (!IsSpecial(path))
            {
                shadowed.Add(new ShellShadowedTree(path, machine, machineSource));
                return Copy(user, userSource);
            }

            var children = new Dictionary<string, MergedShellRegistryKey>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in user.SubKeys.Keys.Union(machine.SubKeys.Keys, StringComparer.OrdinalIgnoreCase))
            {
                user.SubKeys.TryGetValue(name, out ShellRegistryKeySnapshot userChild);
                machine.SubKeys.TryGetValue(name, out ShellRegistryKeySnapshot machineChild);
                children[name] = MergeNode(
                    path + "\\" + name, userChild, machineChild, userSource, machineSource, shadowed);
            }

            var values = machine.Values.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, ShellRegistryValue> value in user.Values)
                values[value.Key] = value.Value;
            return new MergedShellRegistryKey(values, children, new[] { userSource, machineSource });
        }

        private static MergedShellRegistryKey Copy(ShellRegistryKeySnapshot snapshot, ShellMenuSource source)
        {
            if (snapshot == null)
                return null;
            return new MergedShellRegistryKey(
                snapshot.Values,
                snapshot.SubKeys.ToDictionary(x => x.Key, x => Copy(x.Value, source), StringComparer.OrdinalIgnoreCase),
                new[] { source });
        }

        private static bool IsSpecial(string path)
        {
            return SpecialPaths.Contains(path);
        }

        private static readonly HashSet<string> SpecialPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "*", @"*\shellex", @"*\shellex\ContextMenuHandlers", @"*\shellex\PropertySheetHandlers",
            "AppID", "CLSID", "Component Categories",
            "Drive", @"Drive\shellex", @"Drive\shellex\ContextMenuHandlers", @"Drive\shellex\PropertySheetHandlers",
            "FileType",
            "Folder", @"Folder\shellex", @"Folder\shellex\ColumnHandler", @"Folder\shellex\ContextMenuHandlers",
            @"Folder\shellex\ExtShellFolderViews", @"Folder\shellex\PropertySheetHandlers",
            @"Installer\Components", @"Installer\Features", @"Installer\Products",
            "Interface", "Mime", @"Mime\Database", @"Mime\Database\Charset", @"Mime\Database\Codepage",
            @"Mime\Database\Content Type", "TypeLib"
        };
    }

    public sealed class HkcrMergeResult
    {
        internal HkcrMergeResult(MergedShellRegistryKey root, IReadOnlyList<ShellShadowedTree> shadowed)
        {
            Root = root;
            Shadowed = shadowed;
        }

        internal MergedShellRegistryKey Root { get; }
        internal IReadOnlyList<ShellShadowedTree> Shadowed { get; }
    }

    internal sealed class MergedShellRegistryKey
    {
        public MergedShellRegistryKey(
            IReadOnlyDictionary<string, ShellRegistryValue> values,
            IReadOnlyDictionary<string, MergedShellRegistryKey> subKeys,
            IEnumerable<ShellMenuSource> sources)
        {
            Values = values.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
            SubKeys = subKeys.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
            Sources = sources.Where(x => x != null).Distinct().ToArray();
        }

        public IReadOnlyDictionary<string, ShellRegistryValue> Values { get; }
        public IReadOnlyDictionary<string, MergedShellRegistryKey> SubKeys { get; }
        public IReadOnlyList<ShellMenuSource> Sources { get; }

        public ShellRegistryKeySnapshot ToSnapshot()
        {
            return new ShellRegistryKeySnapshot(
                Values.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase),
                SubKeys.ToDictionary(x => x.Key, x => x.Value.ToSnapshot(), StringComparer.OrdinalIgnoreCase));
        }
    }

    internal sealed class ShellShadowedTree
    {
        public ShellShadowedTree(string path, ShellRegistryKeySnapshot root, ShellMenuSource source)
        {
            Path = path;
            Root = root;
            Source = source;
        }

        public string Path { get; }
        public ShellRegistryKeySnapshot Root { get; }
        public ShellMenuSource Source { get; }
    }
}
