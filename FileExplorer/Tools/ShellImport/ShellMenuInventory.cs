using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Win32;

namespace FileExplorer.Tools.ShellImport
{
    public interface IShellMenuInventory
    {
        ShellMenuScanResult Scan(IEnumerable<ShellMenuScope> scopes, CancellationToken cancellationToken);
    }

    public sealed class ShellMenuInventory : IShellMenuInventory
    {
        public ShellMenuInventory(IShellRegistryReader reader, HkcrMergePolicy mergePolicy)
        {
            this.reader = reader ?? throw new ArgumentNullException(nameof(reader));
            this.mergePolicy = mergePolicy ?? throw new ArgumentNullException(nameof(mergePolicy));
        }

        public ShellMenuScanResult Scan(IEnumerable<ShellMenuScope> scopes, CancellationToken cancellationToken)
        {
            var candidates = new List<ShellMenuCandidate>();
            var diagnostics = new List<ShellMenuDiagnostic>();
            foreach (ShellMenuScope scope in scopes.Distinct())
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (RegistryView view in Views)
                {
                    bool incomplete = false;
                    ShellRegistryKeySnapshot user = Read(scope, ShellRegistryHive.CurrentUser, view, diagnostics, ref incomplete);
                    ShellRegistryKeySnapshot machine = Read(scope, ShellRegistryHive.LocalMachine, view, diagnostics, ref incomplete);
                    var userSource = new ShellMenuSource(ShellRegistryHive.CurrentUser, view, scope.Association);
                    var machineSource = new ShellMenuSource(ShellRegistryHive.LocalMachine, view, scope.Association);
                    HkcrMergeResult merged = mergePolicy.Merge(scope.Association, user, machine, userSource, machineSource);
                    AddCandidates(scope, view, merged.Root, incomplete, candidates);
                    AddAssociationHandlers(scope, view, merged.Root, incomplete, candidates);
                    AddShadowedDiagnostics(scope, merged.Shadowed, diagnostics);
                }
            }

            CollapseEquivalentViews(candidates, diagnostics);
            return new ShellMenuScanResult(candidates, diagnostics);
        }

        private ShellRegistryKeySnapshot Read(
            ShellMenuScope scope,
            ShellRegistryHive hive,
            RegistryView view,
            IList<ShellMenuDiagnostic> diagnostics,
            ref bool incomplete)
        {
            try
            {
                if (reader is IShellRegistryReaderWithDiagnostics readerWithDiagnostics)
                {
                    ShellRegistryReadResult read = readerWithDiagnostics.ReadAssociationWithDiagnostics(
                        hive, view, scope.Association);
                    foreach (ShellMenuDiagnostic diagnostic in read.Diagnostics)
                        diagnostics.Add(diagnostic);
                    incomplete |= read.IsIncomplete;
                    return read.Snapshot;
                }
                return reader.ReadAssociation(hive, view, scope.Association);
            }
            catch (Exception exception)
            {
                incomplete = true;
                diagnostics.Add(new ShellMenuDiagnostic(
                    ShellMenuDiagnosticCode.SourceReadFailed,
                    scope.Association,
                    exception.GetType().Name));
                return null;
            }
        }

        private static void AddCandidates(
            ShellMenuScope scope,
            RegistryView view,
            MergedShellRegistryKey root,
            bool incomplete,
            IList<ShellMenuCandidate> candidates)
        {
            MergedShellRegistryKey shell = Navigate(root, scope.VerbPath);
            if (shell == null)
                return;
            foreach (KeyValuePair<string, MergedShellRegistryKey> verb in shell.SubKeys)
            {
                var sources = verb.Value.Sources.Select(x => new ShellMenuSource(
                    x.Hive, view, scope.Association, scope.VerbPath + "\\" + verb.Key)).ToArray();
                candidates.Add(new ShellMenuCandidate(scope, verb.Key, verb.Value.ToSnapshot(), sources, incomplete, false));
            }
        }

        private static void AddAssociationHandlers(
            ShellMenuScope scope,
            RegistryView view,
            MergedShellRegistryKey root,
            bool incomplete,
            IList<ShellMenuCandidate> candidates)
        {
            MergedShellRegistryKey handlers = Navigate(root, scope.HandlerPath);
            if (handlers == null)
                return;
            foreach (KeyValuePair<string, MergedShellRegistryKey> handler in handlers.SubKeys)
            {
                var sources = handler.Value.Sources.Select(x => new ShellMenuSource(
                    x.Hive, view, scope.Association, scope.HandlerPath + "\\" + handler.Key)).ToArray();
                candidates.Add(new ShellMenuCandidate(scope, handler.Key, handler.Value.ToSnapshot(), sources, incomplete, true));
            }
        }

        private static void AddShadowedDiagnostics(
            ShellMenuScope scope,
            IEnumerable<ShellShadowedTree> shadowed,
            IList<ShellMenuDiagnostic> diagnostics)
        {
            foreach (ShellShadowedTree tree in shadowed)
            {
                string targetPath = scope.Association + "\\" + scope.VerbPath;
                foreach (string verbPath in EnumerateVerbPaths(tree.Path, tree.Root, targetPath))
                {
                    diagnostics.Add(new ShellMenuDiagnostic(
                        ShellMenuDiagnosticCode.Shadowed,
                        verbPath,
                        tree.Source?.Hive.ToString()));
                }
            }
        }

        private static IEnumerable<string> EnumerateVerbPaths(
            string path,
            ShellRegistryKeySnapshot node,
            string targetShellPath)
        {
            if (node == null)
                yield break;
            if (String.Equals(path, targetShellPath, StringComparison.OrdinalIgnoreCase))
            {
                foreach (string verb in node.SubKeys.Keys)
                    yield return path + "\\" + verb;
                yield break;
            }
            foreach (KeyValuePair<string, ShellRegistryKeySnapshot> child in node.SubKeys)
            {
                foreach (string result in EnumerateVerbPaths(path + "\\" + child.Key, child.Value, targetShellPath))
                    yield return result;
            }
        }

        private static MergedShellRegistryKey Navigate(MergedShellRegistryKey root, string relativePath)
        {
            MergedShellRegistryKey current = root;
            foreach (string segment in relativePath.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (current == null || !current.SubKeys.TryGetValue(segment, out current))
                    return null;
            }
            return current;
        }

        private static void CollapseEquivalentViews(
            IList<ShellMenuCandidate> candidates,
            IList<ShellMenuDiagnostic> diagnostics)
        {
            foreach (IGrouping<string, ShellMenuCandidate> group in candidates.GroupBy(
                x => x.Scope.Id + "\0" + x.IsAssociationHandler + "\0" + x.VerbName,
                StringComparer.OrdinalIgnoreCase).ToArray())
            {
                ShellMenuCandidate[] items = group.ToArray();
                if (items.Length < 2)
                    continue;
                ShellMenuCandidate first = items[0];
                if (items.All(x => first.RegistryKey.ContentEquals(x.RegistryKey)))
                {
                    first.AddSources(items.Skip(1).SelectMany(x => x.Sources));
                    first.IsSourceIncomplete = items.Any(x => x.IsSourceIncomplete);
                    foreach (ShellMenuCandidate duplicate in items.Skip(1).ToArray())
                        candidates.Remove(duplicate);
                    continue;
                }

                foreach (ShellMenuCandidate item in items)
                    item.HasViewConflict = true;
                diagnostics.Add(new ShellMenuDiagnostic(
                    ShellMenuDiagnosticCode.ViewConflict,
                    first.Scope.Association + "\\" + first.Scope.VerbPath + "\\" + first.VerbName,
                    "Registry32/Registry64"));
            }
        }

        private readonly IShellRegistryReader reader;
        private readonly HkcrMergePolicy mergePolicy;
        private static readonly RegistryView[] Views = { RegistryView.Registry64, RegistryView.Registry32 };
    }

    public sealed class ShellMenuCandidate
    {
        internal ShellMenuCandidate(
            ShellMenuScope scope,
            string verbName,
            ShellRegistryKeySnapshot registryKey,
            IEnumerable<ShellMenuSource> sources,
            bool sourceIncomplete,
            bool associationHandler)
        {
            Scope = scope;
            VerbName = verbName;
            RegistryKey = registryKey;
            Sources = sources.ToList();
            IsSourceIncomplete = sourceIncomplete;
            IsAssociationHandler = associationHandler;
        }

        public ShellMenuScope Scope { get; }
        public string VerbName { get; }
        public ShellRegistryKeySnapshot RegistryKey { get; }
        public IReadOnlyList<ShellMenuSource> Sources { get; private set; }
        public bool HasViewConflict { get; internal set; }
        public bool IsSourceIncomplete { get; internal set; }
        public bool IsAssociationHandler { get; }

        internal void AddSources(IEnumerable<ShellMenuSource> sources)
        {
            Sources = Sources.Concat(sources).ToArray();
        }
    }

    public sealed class ShellMenuScanResult
    {
        internal ShellMenuScanResult(
            IReadOnlyList<ShellMenuCandidate> candidates,
            IReadOnlyList<ShellMenuDiagnostic> diagnostics)
        {
            Candidates = candidates;
            Diagnostics = diagnostics;
        }

        public IReadOnlyList<ShellMenuCandidate> Candidates { get; }
        public IReadOnlyList<ShellMenuDiagnostic> Diagnostics { get; }
    }
}
