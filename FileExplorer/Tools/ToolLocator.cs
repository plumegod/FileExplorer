using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileExplorer.Persistence;
using Microsoft.Win32;

namespace FileExplorer.Tools
{
    public sealed class ToolLocator : IToolLocator
    {
        public static IReadOnlyList<string> RegistrySearchOrder { get; } = new[] { "HKCU64", "HKCU32", "HKLM64", "HKLM32" };

        public ToolLocator(PersistentCollection<ToolLocationOverride> overrides)
            : this(overrides, null)
        {
        }

        public ToolLocator(PersistentCollection<ToolLocationOverride> overrides, Func<string, IEnumerable<ToolLocationCandidate>> candidateProvider)
        {
            this.overrides = overrides;
            this.candidateProvider = candidateProvider ?? GetSystemCandidates;
        }

        public ToolLocationResult Locate(string executableRole)
        {
            string expectedFileName = ToolPresetCatalog.Default.GetExpectedFileName(executableRole);
            ToolLocationOverride saved = overrides.FirstOrDefault(x => String.Equals(x.ExecutableRole, executableRole, StringComparison.Ordinal));
            if (saved != null)
            {
                return IsValid(saved.ExecutablePath, expectedFileName)
                    ? ToolLocationResult.Found(Path.GetFullPath(saved.ExecutablePath), "override")
                    : ToolLocationResult.Missing(true, saved.ExecutablePath);
            }

            foreach (ToolLocationCandidate candidate in GetCandidates(executableRole))
            {
                if (IsValid(candidate.ExecutablePath, expectedFileName))
                    return ToolLocationResult.Found(Path.GetFullPath(candidate.ExecutablePath), candidate.Source);
            }

            return ToolLocationResult.Missing();
        }

        private IEnumerable<ToolLocationCandidate> GetCandidates(string executableRole)
        {
            ToolLocationCandidate[] direct;
            try
            {
                direct = (candidateProvider(executableRole) ?? Enumerable.Empty<ToolLocationCandidate>()).ToArray();
            }
            catch
            {
                direct = Array.Empty<ToolLocationCandidate>();
            }
            foreach (ToolLocationCandidate candidate in direct.Where(x => x.Stage == ToolLocationStage.Registered))
                yield return candidate;

            string siblingRole = GetSiblingSourceRole(executableRole);
            if (siblingRole != null)
            {
                ToolLocationCandidate[] siblings;
                try
                {
                    siblings = (candidateProvider(siblingRole) ?? Enumerable.Empty<ToolLocationCandidate>()).ToArray();
                }
                catch
                {
                    siblings = Array.Empty<ToolLocationCandidate>();
                }
                string siblingExpected = ToolPresetCatalog.Default.GetExpectedFileName(siblingRole);
                foreach (ToolLocationCandidate candidate in siblings)
                {
                    if (candidate.Stage != ToolLocationStage.Registered || !IsValid(candidate.ExecutablePath, siblingExpected))
                        continue;
                    string derived = DeriveSiblingPath(siblingRole, executableRole, candidate.ExecutablePath);
                    if (!String.IsNullOrWhiteSpace(derived))
                        yield return new ToolLocationCandidate(derived, candidate.Source + ":sibling");
                }
            }

            foreach (ToolLocationCandidate candidate in direct.Where(x => x.Stage == ToolLocationStage.CommonDirectory))
                yield return candidate;
            foreach (ToolLocationCandidate candidate in direct.Where(x => x.Stage == ToolLocationStage.Path))
                yield return candidate;
        }

        private static string GetSiblingSourceRole(string targetRole)
        {
            switch (targetRole)
            {
                case ToolExecutableRole.GitBash: return ToolExecutableRole.GitGui;
                case ToolExecutableRole.GitGui: return ToolExecutableRole.GitBash;
                case ToolExecutableRole.SevenZipFileManager: return ToolExecutableRole.SevenZipGui;
                case ToolExecutableRole.SevenZipGui: return ToolExecutableRole.SevenZipFileManager;
                default: return null;
            }
        }

        private static string DeriveSiblingPath(string sourceRole, string targetRole, string sourcePath)
        {
            string directory = Path.GetDirectoryName(sourcePath);
            if (sourceRole == ToolExecutableRole.GitBash && targetRole == ToolExecutableRole.GitGui)
                return Path.Combine(directory, "cmd", "git-gui.exe");
            if (sourceRole == ToolExecutableRole.GitGui && targetRole == ToolExecutableRole.GitBash)
                return Path.Combine(Directory.GetParent(directory)?.FullName ?? directory, "git-bash.exe");
            if (sourceRole == ToolExecutableRole.SevenZipFileManager && targetRole == ToolExecutableRole.SevenZipGui)
                return Path.Combine(directory, "7zG.exe");
            if (sourceRole == ToolExecutableRole.SevenZipGui && targetRole == ToolExecutableRole.SevenZipFileManager)
                return Path.Combine(directory, "7zFM.exe");
            return null;
        }

        public ToolLocationResult SaveOverride(string executableRole, string executablePath)
        {
            string expectedFileName = ToolPresetCatalog.Default.GetExpectedFileName(executableRole);
            if (!IsValid(executablePath, expectedFileName))
                throw new ToolPresetException(File.Exists(executablePath) ? ToolPresetError.WrongExecutable : ToolPresetError.ExecutableMissing);

            string fullPath = Path.GetFullPath(executablePath);
            Upsert(executableRole, fullPath);
            SaveValidSiblings(executableRole, fullPath);
            return ToolLocationResult.Found(fullPath, "override");
        }

        public void ClearOverride(string executableRole)
        {
            ToolLocationOverride saved = overrides.FirstOrDefault(x => String.Equals(x.ExecutableRole, executableRole, StringComparison.Ordinal));
            if (saved != null)
                overrides.Remove(saved);
        }

        private IEnumerable<ToolLocationCandidate> GetSystemCandidates(string executableRole)
        {
            RoleConfiguration config = RoleConfiguration.For(executableRole);
            foreach (ToolLocationCandidate candidate in GetAppPathCandidates(config))
                yield return candidate;
            foreach (ToolLocationCandidate candidate in GetUninstallCandidates(config))
                yield return candidate;
            foreach (string path in config.CommonPaths)
                yield return new ToolLocationCandidate(Environment.ExpandEnvironmentVariables(path), "common-directory", ToolLocationStage.CommonDirectory);
            foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? String.Empty).Split(Path.PathSeparator))
            {
                if (!String.IsNullOrWhiteSpace(directory))
                    yield return new ToolLocationCandidate(Path.Combine(directory.Trim(), config.ExpectedFileName), "PATH", ToolLocationStage.Path);
            }
        }

        private static IEnumerable<ToolLocationCandidate> GetAppPathCandidates(RoleConfiguration config)
        {
            foreach (RegistryLocation location in RegistryLocations)
            {
                using (RegistryKey root = OpenBaseKey(location))
                using (RegistryKey key = root?.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths\" + config.ExpectedFileName))
                {
                    string value = NormalizeRegisteredPath(key?.GetValue(null)?.ToString());
                    if (!String.IsNullOrWhiteSpace(value))
                        yield return new ToolLocationCandidate(value, "app-paths:" + location.Name);
                }
            }
        }

        private static IEnumerable<ToolLocationCandidate> GetUninstallCandidates(RoleConfiguration config)
        {
            foreach (RegistryLocation location in RegistryLocations)
            {
                using (RegistryKey root = OpenBaseKey(location))
                using (RegistryKey uninstall = root?.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall"))
                {
                    if (uninstall == null)
                        continue;
                    foreach (string subKeyName in uninstall.GetSubKeyNames())
                    {
                        using (RegistryKey product = uninstall.OpenSubKey(subKeyName))
                        {
                            string displayName = product?.GetValue("DisplayName")?.ToString();
                            if (String.IsNullOrWhiteSpace(displayName) || !displayName.StartsWith(config.ProductPrefix, StringComparison.OrdinalIgnoreCase))
                                continue;

                            string installLocation = NormalizeRegisteredPath(product.GetValue("InstallLocation")?.ToString());
                            if (!String.IsNullOrWhiteSpace(installLocation))
                                yield return new ToolLocationCandidate(Path.Combine(installLocation, config.RelativePath), "uninstall:" + location.Name);

                            string displayIcon = NormalizeRegisteredPath(product.GetValue("DisplayIcon")?.ToString());
                            if (!String.IsNullOrWhiteSpace(displayIcon))
                            {
                                string directory = Path.GetDirectoryName(displayIcon);
                                if (!String.IsNullOrWhiteSpace(directory))
                                    yield return new ToolLocationCandidate(Path.Combine(directory, config.IconRelativePath), "display-icon:" + location.Name);
                            }
                        }
                    }
                }
            }
        }

        private void SaveValidSiblings(string executableRole, string executablePath)
        {
            string directory = Path.GetDirectoryName(executablePath);
            if (executableRole == ToolExecutableRole.GitBash)
                TryUpsertSibling(ToolExecutableRole.GitGui, Path.Combine(directory, "cmd", "git-gui.exe"));
            else if (executableRole == ToolExecutableRole.GitGui)
                TryUpsertSibling(ToolExecutableRole.GitBash, Path.Combine(Directory.GetParent(directory)?.FullName ?? directory, "git-bash.exe"));
            else if (executableRole == ToolExecutableRole.SevenZipFileManager)
                TryUpsertSibling(ToolExecutableRole.SevenZipGui, Path.Combine(directory, "7zG.exe"));
            else if (executableRole == ToolExecutableRole.SevenZipGui)
                TryUpsertSibling(ToolExecutableRole.SevenZipFileManager, Path.Combine(directory, "7zFM.exe"));
        }

        private void TryUpsertSibling(string role, string path)
        {
            if (IsValid(path, ToolPresetCatalog.Default.GetExpectedFileName(role)))
                Upsert(role, Path.GetFullPath(path));
        }

        private void Upsert(string role, string path)
        {
            ToolLocationOverride saved = overrides.FirstOrDefault(x => String.Equals(x.ExecutableRole, role, StringComparison.Ordinal));
            if (saved == null)
            {
                overrides.Add(new ToolLocationOverride { ExecutableRole = role, ExecutablePath = path });
            }
            else
            {
                saved.ExecutablePath = path;
                overrides.Update(saved);
            }
        }

        private static bool IsValid(string path, string expectedFileName)
        {
            if (String.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return false;
            return String.Equals(Path.GetFileName(path), expectedFileName, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeRegisteredPath(string value)
        {
            if (String.IsNullOrWhiteSpace(value))
                return null;
            value = Environment.ExpandEnvironmentVariables(value.Trim());
            if (value.StartsWith("\"", StringComparison.Ordinal))
            {
                int closingQuote = value.IndexOf('"', 1);
                if (closingQuote > 1)
                    return value.Substring(1, closingQuote - 1);
            }
            int comma = value.LastIndexOf(',');
            if (comma > 1 && Int32.TryParse(value.Substring(comma + 1), out _))
                value = value.Substring(0, comma);
            return value.Trim('"', ' ');
        }

        private static RegistryKey OpenBaseKey(RegistryLocation location)
        {
            try
            {
                return RegistryKey.OpenBaseKey(location.Hive, location.View);
            }
            catch
            {
                return null;
            }
        }

        private sealed class RoleConfiguration
        {
            private RoleConfiguration(string expectedFileName, string productPrefix, string relativePath, string iconRelativePath, params string[] commonPaths)
            {
                ExpectedFileName = expectedFileName;
                ProductPrefix = productPrefix;
                RelativePath = relativePath;
                IconRelativePath = iconRelativePath;
                CommonPaths = commonPaths;
            }

            public string ExpectedFileName { get; }
            public string ProductPrefix { get; }
            public string RelativePath { get; }
            public string IconRelativePath { get; }
            public IReadOnlyList<string> CommonPaths { get; }

            public static RoleConfiguration For(string role)
            {
                string pf = "%ProgramFiles%";
                string pfx86 = "%ProgramFiles(x86)%";
                switch (role)
                {
                    case ToolExecutableRole.GitBash:
                        return new RoleConfiguration("git-bash.exe", "Git version", "git-bash.exe", "git-bash.exe", pf + @"\Git\git-bash.exe", pfx86 + @"\Git\git-bash.exe");
                    case ToolExecutableRole.GitGui:
                        return new RoleConfiguration("git-gui.exe", "Git version", @"cmd\git-gui.exe", @"cmd\git-gui.exe", pf + @"\Git\cmd\git-gui.exe", pfx86 + @"\Git\cmd\git-gui.exe");
                    case ToolExecutableRole.TortoiseGit:
                        return new RoleConfiguration("TortoiseGitProc.exe", "TortoiseGit ", @"bin\TortoiseGitProc.exe", "TortoiseGitProc.exe", pf + @"\TortoiseGit\bin\TortoiseGitProc.exe", pfx86 + @"\TortoiseGit\bin\TortoiseGitProc.exe");
                    case ToolExecutableRole.TortoiseSvn:
                        return new RoleConfiguration("TortoiseProc.exe", "TortoiseSVN ", @"bin\TortoiseProc.exe", "TortoiseProc.exe", pf + @"\TortoiseSVN\bin\TortoiseProc.exe", pfx86 + @"\TortoiseSVN\bin\TortoiseProc.exe");
                    case ToolExecutableRole.SevenZipFileManager:
                        return new RoleConfiguration("7zFM.exe", "7-Zip ", "7zFM.exe", "7zFM.exe", pf + @"\7-Zip\7zFM.exe", pfx86 + @"\7-Zip\7zFM.exe");
                    case ToolExecutableRole.SevenZipGui:
                        return new RoleConfiguration("7zG.exe", "7-Zip ", "7zG.exe", "7zG.exe", pf + @"\7-Zip\7zG.exe", pfx86 + @"\7-Zip\7zG.exe");
                    default:
                        throw new ToolPresetException(ToolPresetError.UnknownPreset);
                }
            }
        }

        private sealed class RegistryLocation
        {
            public RegistryLocation(RegistryHive hive, RegistryView view, string name)
            {
                Hive = hive;
                View = view;
                Name = name;
            }
            public RegistryHive Hive { get; }
            public RegistryView View { get; }
            public string Name { get; }
        }

        private static readonly RegistryLocation[] RegistryLocations =
        {
            new RegistryLocation(RegistryHive.CurrentUser, RegistryView.Registry64, "HKCU64"),
            new RegistryLocation(RegistryHive.CurrentUser, RegistryView.Registry32, "HKCU32"),
            new RegistryLocation(RegistryHive.LocalMachine, RegistryView.Registry64, "HKLM64"),
            new RegistryLocation(RegistryHive.LocalMachine, RegistryView.Registry32, "HKLM32")
        };

        private readonly PersistentCollection<ToolLocationOverride> overrides;
        private readonly Func<string, IEnumerable<ToolLocationCandidate>> candidateProvider;
    }
}
