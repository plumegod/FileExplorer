using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;

namespace FileExplorer.Tools.ShellImport
{
    public sealed class WindowsShellRegistryReader : IShellRegistryReaderWithDiagnostics, IShellCascadeReader
    {
        public ShellRegistryKeySnapshot ReadAssociation(ShellRegistryHive hive, RegistryView view, string association)
        {
            return Read(hive, view, @"Software\Classes\" + association, null, null);
        }

        public ShellRegistryReadResult ReadAssociationWithDiagnostics(
            ShellRegistryHive hive,
            RegistryView view,
            string association)
        {
            var diagnostics = new List<ShellMenuDiagnostic>();
            bool incomplete = false;
            string path = @"Software\Classes\" + association;
            ShellRegistryKeySnapshot snapshot;
            try
            {
                snapshot = Read(hive, view, path, diagnostics, value => incomplete = value || incomplete);
            }
            catch (Exception exception)
            {
                incomplete = true;
                diagnostics.Add(CreateDiagnostic(hive, view, path, exception));
                snapshot = null;
            }
            return new ShellRegistryReadResult(snapshot, diagnostics, incomplete);
        }

        public ShellRegistryKeySnapshot ReadCommandStoreVerb(RegistryView view, string name)
        {
            const string root = @"Software\Microsoft\Windows\CurrentVersion\Explorer\CommandStore\shell\";
            return Read(ShellRegistryHive.CurrentUser, view, root + name, null, null)
                ?? Read(ShellRegistryHive.LocalMachine, view, root + name, null, null);
        }

        public ShellRegistryKeySnapshot ReadClassesVerb(RegistryView view, string path)
        {
            string[] segments = path.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
                return null;
            string association = segments[0];
            ShellRegistryKeySnapshot user = ReadAssociation(ShellRegistryHive.CurrentUser, view, association);
            ShellRegistryKeySnapshot machine = ReadAssociation(ShellRegistryHive.LocalMachine, view, association);
            var userSource = new ShellMenuSource(ShellRegistryHive.CurrentUser, view, association);
            var machineSource = new ShellMenuSource(ShellRegistryHive.LocalMachine, view, association);
            MergedShellRegistryKey current = new HkcrMergePolicy()
                .Merge(association, user, machine, userSource, machineSource).Root;
            foreach (string segment in segments.Skip(1))
            {
                if (current == null || !current.SubKeys.TryGetValue(segment, out current))
                    return null;
            }
            return current?.ToSnapshot();
        }

        private static ShellRegistryKeySnapshot Read(
            ShellRegistryHive hive,
            RegistryView view,
            string path,
            ICollection<ShellMenuDiagnostic> diagnostics,
            Action<bool> markIncomplete)
        {
            RegistryHive nativeHive = hive == ShellRegistryHive.CurrentUser
                ? RegistryHive.CurrentUser
                : RegistryHive.LocalMachine;
            using (RegistryKey baseKey = RegistryKey.OpenBaseKey(nativeHive, view))
            using (RegistryKey key = baseKey.OpenSubKey(path, false))
                return ReadKey(key, 0, hive, view, path, diagnostics, markIncomplete);
        }

        private static ShellRegistryKeySnapshot ReadKey(
            RegistryKey key,
            int depth,
            ShellRegistryHive hive,
            RegistryView view,
            string path,
            ICollection<ShellMenuDiagnostic> diagnostics,
            Action<bool> markIncomplete)
        {
            if (key == null)
                return null;
            if (depth > MaxDepth)
                throw new InvalidOperationException("Registry tree exceeds the supported depth.");

            var values = new Dictionary<string, ShellRegistryValue>(StringComparer.OrdinalIgnoreCase);
            string[] valueNames;
            try
            {
                valueNames = key.GetValueNames();
            }
            catch (Exception exception)
            {
                if (diagnostics == null)
                    throw;
                diagnostics.Add(CreateDiagnostic(hive, view, path, exception));
                markIncomplete?.Invoke(true);
                valueNames = Array.Empty<string>();
            }
            foreach (string name in valueNames)
            {
                try
                {
                    object value = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                    values[name] = new ShellRegistryValue(value, key.GetValueKind(name));
                }
                catch (Exception exception)
                {
                    if (diagnostics == null)
                        throw;
                    diagnostics.Add(CreateDiagnostic(hive, view, path + "@" + name, exception));
                    markIncomplete?.Invoke(true);
                }
            }

            var children = new Dictionary<string, ShellRegistryKeySnapshot>(StringComparer.OrdinalIgnoreCase);
            string[] childNames;
            try
            {
                childNames = key.GetSubKeyNames();
            }
            catch (Exception exception)
            {
                if (diagnostics == null)
                    throw;
                diagnostics.Add(CreateDiagnostic(hive, view, path, exception));
                markIncomplete?.Invoke(true);
                childNames = Array.Empty<string>();
            }
            foreach (string name in childNames)
            {
                try
                {
                    using (RegistryKey child = key.OpenSubKey(name, false))
                    {
                        ShellRegistryKeySnapshot snapshot = ReadKey(
                            child, depth + 1, hive, view, path + "\\" + name, diagnostics, markIncomplete);
                        if (snapshot != null)
                            children[name] = snapshot;
                    }
                }
                catch (Exception exception)
                {
                    if (diagnostics == null)
                        throw;
                    diagnostics.Add(CreateDiagnostic(hive, view, path + "\\" + name, exception));
                    markIncomplete?.Invoke(true);
                }
            }
            return new ShellRegistryKeySnapshot(values, children);
        }

        private static ShellMenuDiagnostic CreateDiagnostic(
            ShellRegistryHive hive,
            RegistryView view,
            string path,
            Exception exception)
        {
            return new ShellMenuDiagnostic(
                ShellMenuDiagnosticCode.SourceReadFailed,
                hive + "|" + view + "|" + path,
                exception.GetType().Name);
        }

        private const int MaxDepth = 12;
    }

    public interface IShellMenuScopeProvider
    {
        IReadOnlyList<ShellMenuScope> GetScopes(string extension);
    }

    public interface IShellMenuScopeProviderWithDiagnostics : IShellMenuScopeProvider
    {
        ShellMenuScopeProviderResult GetScopesWithDiagnostics(string extension);
    }

    public sealed class ShellMenuScopeProviderResult
    {
        public ShellMenuScopeProviderResult(
            IReadOnlyList<ShellMenuScope> scopes,
            IReadOnlyList<ShellMenuDiagnostic> diagnostics)
        {
            Scopes = scopes;
            Diagnostics = diagnostics;
        }

        public IReadOnlyList<ShellMenuScope> Scopes { get; }
        public IReadOnlyList<ShellMenuDiagnostic> Diagnostics { get; }
    }

    public sealed class WindowsShellMenuScopeProvider : IShellMenuScopeProviderWithDiagnostics
    {
        public WindowsShellMenuScopeProvider(IShellRegistryReader reader = null)
        {
            this.reader = reader ?? new WindowsShellRegistryReader();
        }

        public IReadOnlyList<ShellMenuScope> GetScopes(string extension)
        {
            return GetScopesWithDiagnostics(extension).Scopes;
        }

        public ShellMenuScopeProviderResult GetScopesWithDiagnostics(string extension)
        {
            var scopes = new List<ShellMenuScope>
            {
                ShellMenuScope.File,
                ShellMenuScope.AllFilesystemObjects,
                ShellMenuScope.Directory,
                ShellMenuScope.DirectoryBackground,
                ShellMenuScope.Folder,
                ShellMenuScope.Drive
            };
            var diagnostics = new List<ShellMenuDiagnostic>();
            string normalized = NormalizeExtension(extension);
            if (normalized == null)
                return new ShellMenuScopeProviderResult(scopes, diagnostics);

            var associations = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { normalized };
            foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                ShellRegistryKeySnapshot user = TryReadAssociation(
                    ShellRegistryHive.CurrentUser, view, normalized, diagnostics);
                ShellRegistryKeySnapshot machine = TryReadAssociation(
                    ShellRegistryHive.LocalMachine, view, normalized, diagnostics);
                var userSource = new ShellMenuSource(ShellRegistryHive.CurrentUser, view, normalized);
                var machineSource = new ShellMenuSource(ShellRegistryHive.LocalMachine, view, normalized);
                ShellRegistryKeySnapshot extensionKey = new HkcrMergePolicy()
                    .Merge(normalized, user, machine, userSource, machineSource).Root?.ToSnapshot();
                string progId = ReadValue(extensionKey, String.Empty);
                if (!String.IsNullOrWhiteSpace(progId))
                    associations.Add(progId);
                string perceived = ReadValue(extensionKey, "PerceivedType");
                if (!String.IsNullOrWhiteSpace(perceived))
                    associations.Add(@"SystemFileAssociations\" + perceived);
            }
            associations.Add(@"SystemFileAssociations\" + normalized);
            foreach (string association in associations)
            {
                scopes.Add(ShellMenuScope.ForFileAssociation(
                    "extension:" + association,
                    association,
                    normalized));
            }
            return new ShellMenuScopeProviderResult(scopes, diagnostics);
        }

        private ShellRegistryKeySnapshot TryReadAssociation(
            ShellRegistryHive hive,
            RegistryView view,
            string association,
            ICollection<ShellMenuDiagnostic> diagnostics)
        {
            try
            {
                if (reader is IShellRegistryReaderWithDiagnostics readerWithDiagnostics)
                {
                    ShellRegistryReadResult read = readerWithDiagnostics.ReadAssociationWithDiagnostics(
                        hive, view, association);
                    foreach (ShellMenuDiagnostic diagnostic in read.Diagnostics)
                        diagnostics.Add(diagnostic);
                    return read.Snapshot;
                }
                return reader.ReadAssociation(hive, view, association);
            }
            catch (Exception exception)
            {
                diagnostics.Add(new ShellMenuDiagnostic(
                    ShellMenuDiagnosticCode.SourceReadFailed,
                    hive + "|" + view + @"|Software\Classes\" + association,
                    exception.GetType().Name));
                return null;
            }
        }

        private static string NormalizeExtension(string extension)
        {
            if (String.IsNullOrWhiteSpace(extension))
                return null;
            string result = extension.Trim();
            if (!result.StartsWith(".", StringComparison.Ordinal))
                result = "." + result;
            return result.IndexOfAny(new[] { '\\', '/', '*', '?' }) >= 0
                ? null
                : result.ToLowerInvariant();
        }

        private static string ReadValue(ShellRegistryKeySnapshot key, string name)
        {
            return key != null && key.Values.TryGetValue(name, out ShellRegistryValue value)
                ? value.Value?.ToString()
                : null;
        }

        private readonly IShellRegistryReader reader;
    }
}
