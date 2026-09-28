using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;

namespace FileExplorer.Tools.ShellImport
{
    public interface IShellCascadeReader
    {
        ShellRegistryKeySnapshot ReadCommandStoreVerb(RegistryView view, string name);
        ShellRegistryKeySnapshot ReadClassesVerb(RegistryView view, string path);
    }

    public enum ShellMenuEntryKind
    {
        StaticVerb,
        DynamicVerb,
        DynamicHandler,
        Unsupported
    }

    public sealed class ShellMenuEntry
    {
        internal ShellMenuEntry(
            ShellMenuCandidate candidate,
            string title,
            ShellMenuEntryKind kind,
            string rawCommand,
            string breadcrumb,
            string reason,
            ShellRegistryKeySnapshot registryKey,
            string sourceIdentity)
        {
            Candidate = candidate;
            Title = title;
            Kind = kind;
            RawCommand = rawCommand;
            Breadcrumb = breadcrumb;
            Reason = reason;
            RegistryKey = registryKey;
            SourceIdentity = sourceIdentity;
        }

        public ShellMenuCandidate Candidate { get; }
        public string Title { get; }
        public ShellMenuEntryKind Kind { get; }
        public string RawCommand { get; }
        public string Breadcrumb { get; }
        public string Reason { get; }
        public ShellRegistryKeySnapshot RegistryKey { get; }
        public string SourceIdentity { get; }
    }

    public sealed class ShellMenuAnalyzer
    {
        public ShellMenuAnalyzer(IShellCascadeReader cascadeReader)
        {
            this.cascadeReader = cascadeReader ?? throw new ArgumentNullException(nameof(cascadeReader));
        }

        public IReadOnlyList<ShellMenuEntry> Analyze(ShellMenuCandidate candidate)
        {
            if (candidate == null)
                throw new ArgumentNullException(nameof(candidate));
            if (candidate.IsAssociationHandler)
            {
                return new[]
                {
                    new ShellMenuEntry(candidate, candidate.VerbName, ShellMenuEntryKind.DynamicHandler,
                        null, null, "dynamic-handler", candidate.RegistryKey, null)
                };
            }

            RegistryView[] views = candidate.Sources.Select(x => x.View).Distinct().ToArray();
            if (views.Length == 0)
                views = new[] { RegistryView.Registry64 };
            IReadOnlyList<ShellMenuEntry>[] results = views.Select(view => AnalyzeNode(
                candidate, candidate.RegistryKey, candidate.VerbName, null, view,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase), 0, null, null)).ToArray();
            if (results.Skip(1).Any(x => !EntriesEqual(results[0], x)))
                candidate.HasViewConflict = true;
            return results[0];
        }

        private IReadOnlyList<ShellMenuEntry> AnalyzeNode(
            ShellMenuCandidate candidate,
            ShellRegistryKeySnapshot node,
            string fallbackTitle,
            string breadcrumb,
            RegistryView view,
            ISet<string> visited,
            int depth,
            string sourceIdentity,
            ShellRegistryKeySnapshot inheritedVisibility)
        {
            node = ApplyInheritedVisibility(node, inheritedVisibility);
            string muiTitle = ReadString(node, "MUIVerb");
            string title = muiTitle ?? ReadString(node, String.Empty) ?? fallbackTitle;
            string titleReason = muiTitle?.StartsWith("@", StringComparison.Ordinal) == true
                ? "indirect-title-unresolved"
                : null;
            if (node.SubKeys.ContainsKey("ddeexec"))
                return Unsupported(candidate, title, breadcrumb, "dde", node, sourceIdentity);
            if (HasDynamicHook(node))
            {
                return new[]
                {
                    new ShellMenuEntry(candidate, title, ShellMenuEntryKind.DynamicVerb,
                        null, breadcrumb, "dynamic-com", node, sourceIdentity)
                };
            }

            string subCommands = ReadString(node, "SubCommands");
            string extendedKey = ReadString(node, "ExtendedSubCommandsKey");
            if (!String.IsNullOrWhiteSpace(subCommands) || !String.IsNullOrWhiteSpace(extendedKey))
            {
                if (depth >= MaxCascadeDepth)
                    return Unsupported(candidate, title, breadcrumb, "cascade-depth", node, sourceIdentity);
                var entries = new List<ShellMenuEntry>();
                string nextBreadcrumb = String.IsNullOrEmpty(breadcrumb) ? title : breadcrumb + " / " + title;
                if (!String.IsNullOrWhiteSpace(subCommands))
                {
                    foreach (string name in subCommands.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(x => x.Trim()).Where(x => x.Length > 0))
                    {
                        string identity = "store:" + name;
                        if (!visited.Add(identity))
                            return Unsupported(candidate, title, breadcrumb, "cascade-cycle", node, sourceIdentity);
                        ShellRegistryKeySnapshot child;
                        try
                        {
                            child = cascadeReader.ReadCommandStoreVerb(view, name);
                        }
                        catch (Exception exception) when (!(exception is OperationCanceledException))
                        {
                            entries.AddRange(Unsupported(candidate, name, nextBreadcrumb, "cascade-read-failed", node,
                                AppendIdentity(sourceIdentity, "command-store:" + name)));
                            visited.Remove(identity);
                            continue;
                        }
                        if (child == null)
                            entries.AddRange(Unsupported(candidate, name, nextBreadcrumb, "cascade-missing", node,
                                AppendIdentity(sourceIdentity, "command-store:" + name)));
                        else
                            entries.AddRange(AnalyzeNode(candidate, child, name, nextBreadcrumb, view, visited, depth + 1,
                                AppendIdentity(sourceIdentity, "command-store:" + name), node));
                        visited.Remove(identity);
                    }
                }
                else
                {
                    string identity = "classes:" + extendedKey;
                    if (!visited.Add(identity))
                        return Unsupported(candidate, title, breadcrumb, "cascade-cycle", node, sourceIdentity);
                    ShellRegistryKeySnapshot child;
                    try
                    {
                        child = cascadeReader.ReadClassesVerb(view, extendedKey);
                    }
                    catch (Exception exception) when (!(exception is OperationCanceledException))
                    {
                        visited.Remove(identity);
                        return Unsupported(candidate, title, breadcrumb, "cascade-read-failed", node,
                            AppendIdentity(sourceIdentity, "classes:" + extendedKey));
                    }
                    if (child == null)
                        entries.AddRange(Unsupported(candidate, title, breadcrumb, "cascade-missing", node,
                            AppendIdentity(sourceIdentity, "classes:" + extendedKey)));
                    else if (child.SubKeys.TryGetValue("shell", out ShellRegistryKeySnapshot shell))
                    {
                        foreach (KeyValuePair<string, ShellRegistryKeySnapshot> verb in shell.SubKeys)
                        {
                            entries.AddRange(AnalyzeNode(candidate, verb.Value, verb.Key, nextBreadcrumb, view,
                                visited, depth + 1, AppendIdentity(sourceIdentity,
                                    "classes:" + extendedKey + @"\shell\" + verb.Key), node));
                        }
                    }
                    else
                        entries.AddRange(AnalyzeNode(candidate, child, title, nextBreadcrumb, view, visited, depth + 1,
                            AppendIdentity(sourceIdentity, "classes:" + extendedKey), node));
                    visited.Remove(identity);
                }
                return entries;
            }

            string command = ReadCommand(node);
            if (String.IsNullOrWhiteSpace(command))
                return Unsupported(candidate, title, breadcrumb, "command-missing", node, sourceIdentity);
            return new[]
            {
                new ShellMenuEntry(candidate, title, ShellMenuEntryKind.StaticVerb,
                    command, breadcrumb, titleReason, node, sourceIdentity)
            };
        }

        private static bool HasDynamicHook(ShellRegistryKeySnapshot node)
        {
            if (node.Values.ContainsKey("ExplorerCommandHandler")
                || node.Values.ContainsKey("DelegateExecute"))
                return true;
            if (node.SubKeys.TryGetValue("command", out ShellRegistryKeySnapshot command)
                && command.Values.ContainsKey("DelegateExecute"))
                return true;
            return node.SubKeys.TryGetValue("DropTarget", out ShellRegistryKeySnapshot dropTarget)
                && dropTarget.Values.ContainsKey("CLSID");
        }

        private static string ReadCommand(ShellRegistryKeySnapshot node)
        {
            return node.SubKeys.TryGetValue("command", out ShellRegistryKeySnapshot command)
                ? ReadString(command, String.Empty)
                : null;
        }

        private static string ReadString(ShellRegistryKeySnapshot node, string name)
        {
            if (node != null && node.Values.TryGetValue(name, out ShellRegistryValue value))
                return value.Value?.ToString();
            return null;
        }

        private static IReadOnlyList<ShellMenuEntry> Unsupported(
            ShellMenuCandidate candidate,
            string title,
            string breadcrumb,
            string reason,
            ShellRegistryKeySnapshot registryKey,
            string sourceIdentity)
        {
            return new[]
            {
                new ShellMenuEntry(candidate, title, ShellMenuEntryKind.Unsupported,
                    null, breadcrumb, reason, registryKey, sourceIdentity)
            };
        }

        private static string AppendIdentity(string current, string next)
        {
            return String.IsNullOrEmpty(current) ? next : current + ">" + next;
        }

        private static bool EntriesEqual(
            IReadOnlyList<ShellMenuEntry> left,
            IReadOnlyList<ShellMenuEntry> right)
        {
            if (left.Count != right.Count)
                return false;
            ShellMenuEntry[] orderedLeft = left.OrderBy(x => x.SourceIdentity ?? x.Title, StringComparer.OrdinalIgnoreCase).ToArray();
            ShellMenuEntry[] orderedRight = right.OrderBy(x => x.SourceIdentity ?? x.Title, StringComparer.OrdinalIgnoreCase).ToArray();
            for (int index = 0; index < orderedLeft.Length; index++)
            {
                ShellMenuEntry a = orderedLeft[index];
                ShellMenuEntry b = orderedRight[index];
                if (a.Kind != b.Kind
                    || !String.Equals(a.Title, b.Title, StringComparison.Ordinal)
                    || !String.Equals(a.RawCommand, b.RawCommand, StringComparison.Ordinal)
                    || !String.Equals(a.Reason, b.Reason, StringComparison.Ordinal)
                    || !String.Equals(a.SourceIdentity, b.SourceIdentity, StringComparison.OrdinalIgnoreCase)
                    || !(a.RegistryKey?.ContentEquals(b.RegistryKey) ?? b.RegistryKey == null))
                    return false;
            }
            return true;
        }

        private static ShellRegistryKeySnapshot ApplyInheritedVisibility(
            ShellRegistryKeySnapshot node,
            ShellRegistryKeySnapshot inherited)
        {
            if (node == null || inherited == null)
                return node;
            var values = node.Values.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
            foreach (string name in VisibilityValueNames)
            {
                if (!values.ContainsKey(name) && inherited.Values.TryGetValue(name, out ShellRegistryValue value))
                    values[name] = value;
            }
            return new ShellRegistryKeySnapshot(
                values,
                node.SubKeys.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase));
        }

        private const int MaxCascadeDepth = 8;
        private static readonly string[] VisibilityValueNames =
        {
            "LegacyDisable", "ProgrammaticAccessOnly", "Extended", "AppliesTo", "HasLUAShield"
        };
        private readonly IShellCascadeReader cascadeReader;
    }
}
