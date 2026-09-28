using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using FileExplorer.Core;

namespace FileExplorer.Tools.ShellImport
{
    public enum ShellMenuConversionKind
    {
        Direct,
        NeedsReview,
        ShellBridge,
        Unsupported
    }

    public sealed class MenuItemDraft
    {
        public CommandType Command { get; set; } = CommandType.OpenWithApplication;
        public string Name { get; set; }
        public string GroupName { get; set; }
        public string Application { get; set; }
        public ParameterType Parameter { get; set; }
        public string Prefix { get; set; }
        public string Suffix { get; set; }
        public ItemTypeFilter ItemTypeFilter { get; set; }
        public SelectionFilter SelectionFilter { get; set; }
        public string ExtensionFilter { get; set; }
        public MenuContextFilter ContextFilter { get; set; }
        public bool ConfirmBeforeRun { get; set; }
        public string SourceId { get; set; }
        public string SourceSummary { get; set; }
        public bool RequiresEditing { get; set; }
        public string ExpandedCommand { get; set; }
        public string ParsedExecutable { get; set; }
        public string ParsedArguments { get; set; }
        public string PlaceholderSummary { get; set; }
        public bool ExecutableExists { get; set; }
        public string ShellHandlerClsid { get; set; }
        public string ShellHandlerName { get; set; }
        public string ShellAssociationHint { get; set; }
        public bool IsShellBridge { get; set; }

        public MenuItemDraft CreateDuplicate()
        {
            return new MenuItemDraft
            {
                Command = Command,
                Name = Name,
                GroupName = GroupName,
                Application = Application,
                Parameter = Parameter,
                Prefix = Prefix,
                Suffix = Suffix,
                ItemTypeFilter = ItemTypeFilter,
                SelectionFilter = SelectionFilter,
                ExtensionFilter = ExtensionFilter,
                ContextFilter = ContextFilter,
                ConfirmBeforeRun = ConfirmBeforeRun,
                RequiresEditing = RequiresEditing,
                ExpandedCommand = ExpandedCommand,
                ParsedExecutable = ParsedExecutable,
                ParsedArguments = ParsedArguments,
                PlaceholderSummary = PlaceholderSummary,
                ExecutableExists = ExecutableExists,
                ShellHandlerClsid = ShellHandlerClsid,
                ShellHandlerName = ShellHandlerName,
                ShellAssociationHint = ShellAssociationHint,
                IsShellBridge = IsShellBridge
            };
        }
    }

    public sealed class ShellMenuConversion
    {
        internal ShellMenuConversion(ShellMenuConversionKind kind, string reason, MenuItemDraft draft)
        {
            Kind = kind;
            Reason = reason;
            Draft = draft;
        }

        public ShellMenuConversionKind Kind { get; }
        public string Reason { get; }
        public MenuItemDraft Draft { get; }
    }

    public sealed class ShellMenuConverter
    {
        public ShellMenuConverter(IExecutableResolver executableResolver = null)
        {
            this.executableResolver = executableResolver ?? new WindowsExecutableResolver();
        }

        public ShellMenuConversion Convert(ShellMenuEntry entry)
        {
            if (entry == null)
                throw new ArgumentNullException(nameof(entry));
            if (entry.Kind == ShellMenuEntryKind.DynamicHandler)
                return ConvertDynamicHandler(entry);
            if (entry.Kind != ShellMenuEntryKind.StaticVerb)
                return Unsupported(entry.Reason ?? "not-static");
            string forcedReviewReason = entry.Candidate.HasViewConflict
                ? "view-conflict"
                : entry.Candidate.IsSourceIncomplete ? "source-incomplete" : entry.Reason;

            string visibilityReason = GetVisibilityReason(entry.RegistryKey ?? entry.Candidate.RegistryKey);

            if (!TryExpandCommand(entry.RawCommand ?? String.Empty, out string expanded, out string expansionReason))
                return ManualReview(entry, expansionReason, null, entry.RawCommand);
            expanded = expanded.Trim();
            if (!TrySplitExecutable(expanded, out string application, out string arguments, out string splitReason))
                return ManualReview(entry, splitReason, null, entry.RawCommand);

            string extension = Path.GetExtension(application);
            string executionReviewReason = ScriptExtensions.Contains(extension)
                ? "script-host"
                : !String.Equals(extension, ".exe", StringComparison.OrdinalIgnoreCase)
                && !String.Equals(extension, ".com", StringComparison.OrdinalIgnoreCase)
                    ? "unsupported-executable"
                    : DangerousHosts.Contains(Path.GetFileName(application)) ? "dangerous-host" : null;

            string[] tokens;
            try
            {
                tokens = ParseArguments(arguments);
            }
            catch
            {
                return ManualReview(entry, "arguments-unparseable", application, arguments);
            }

            MatchCollection allPlaceholders = PlaceholderToken.Matches(String.Join("\0", tokens));
            if (allPlaceholders.Cast<Match>().Any(x => !SupportedPlaceholder.IsMatch(x.Value)))
                return ManualReview(entry, "unsupported-placeholder", application, arguments);

            var supported = new List<(int TokenIndex, Match Match)>();
            for (int i = 0; i < tokens.Length; i++)
            {
                foreach (Match match in SupportedOccurrence.Matches(tokens[i]))
                    supported.Add((i, match));
            }
            if (supported.Count > 1)
                return ManualReview(entry, "multiple-placeholders", application, arguments);

            string prefix;
            string suffix;
            ParameterType parameter;
            SelectionFilter selection;
            if (supported.Count == 0)
            {
                prefix = WindowsCommandLine.Encode(tokens);
                suffix = String.Empty;
                parameter = ParameterType.Expression;
                selection = SelectionFilter.Single;
            }
            else
            {
                (int tokenIndex, Match match) = supported[0];
                if (!IsPlaceholderCompatible(entry.Candidate.Scope, match.Value))
                    return ManualReview(entry, "scope-placeholder-mismatch", application, arguments);
                string token = tokens[tokenIndex];
                string tokenPrefix = token.Substring(0, match.Index);
                string tokenSuffix = token.Substring(match.Index + match.Length);
                string before = WindowsCommandLine.Encode(tokens.Take(tokenIndex));
                string after = WindowsCommandLine.Encode(tokens.Skip(tokenIndex + 1));
                prefix = (before.Length == 0 ? String.Empty : before + " ") + tokenPrefix;
                suffix = tokenSuffix + (after.Length == 0 ? String.Empty : " " + after);
                if (!RoundTrips(tokens, tokenIndex, match, prefix, suffix, entry.Candidate.Scope))
                    return ManualReview(entry, "round-trip-mismatch", application, arguments);
                parameter = ParameterType.Path;
                selection = match.Value == "%*"
                    && !String.Equals(ReadValue(entry.RegistryKey, "MultiSelectModel"), "Document", StringComparison.OrdinalIgnoreCase)
                    ? SelectionFilter.None
                    : SelectionFilter.Single;
            }

            MenuItemDraft draft = CreateDraft(entry, application, parameter, prefix, suffix, selection);
            PopulateParsingDetails(draft, expanded, application, tokens, supported);
            if (forcedReviewReason != null)
            {
                if (forcedReviewReason == "view-conflict" || forcedReviewReason == "source-incomplete")
                    draft.RequiresEditing = true;
                return NeedsReview(forcedReviewReason, draft);
            }
            if (executionReviewReason != null)
                return NeedsReview(executionReviewReason, draft);
            return visibilityReason == null
                ? new ShellMenuConversion(ShellMenuConversionKind.Direct, null, draft)
                : NeedsReview(visibilityReason, draft);
        }

        private static string GetVisibilityReason(ShellRegistryKeySnapshot key)
        {
            if (HasValue(key, "LegacyDisable") || HasValue(key, "ProgrammaticAccessOnly"))
                return "hidden-by-registration";
            if (HasValue(key, "Extended"))
                return "shift-only-source-would-become-always-visible";
            if (!String.IsNullOrWhiteSpace(ReadValue(key, "AppliesTo")))
                return "conditional-visibility-unresolved";
            if (HasValue(key, "HasLUAShield"))
                return "elevation-sensitive";
            return null;
        }

        private static bool HasValue(ShellRegistryKeySnapshot key, string name)
        {
            return key.Values.ContainsKey(name);
        }

        private static string ReadValue(ShellRegistryKeySnapshot key, string name)
        {
            return key.Values.TryGetValue(name, out ShellRegistryValue value) ? value.Value?.ToString() : null;
        }

        private bool TrySplitExecutable(
            string command,
            out string application,
            out string arguments,
            out string reason)
        {
            application = null;
            arguments = null;
            reason = "command-empty";
            if (String.IsNullOrWhiteSpace(command))
                return false;

            if (command[0] == '"')
            {
                int end = command.IndexOf('"', 1);
                if (end < 0)
                {
                    reason = "executable-quote-unclosed";
                    return false;
                }
                application = command.Substring(1, end - 1);
                arguments = command.Substring(end + 1).TrimStart();
            }
            else
            {
                int end = command.IndexOfAny(new[] { ' ', '\t' });
                string token = end < 0 ? command : command.Substring(0, end);
                if (!TryResolveExecutable(token, out application))
                {
                    reason = "ambiguous-executable";
                    return false;
                }
                arguments = end < 0 ? String.Empty : command.Substring(end + 1).TrimStart();
            }

            if (!TryResolveExecutable(application, out application))
            {
                reason = "executable-missing";
                return false;
            }
            return true;
        }

        private bool TryResolveExecutable(string value, out string resolved)
        {
            resolved = null;
            string[] candidates = executableResolver.Resolve(value)
                .Where(File.Exists)
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (candidates.Length != 1)
                return false;
            resolved = candidates[0];
            return true;
        }

        private static string[] ParseArguments(string arguments)
        {
            if (String.IsNullOrWhiteSpace(arguments))
                return Array.Empty<string>();
            IntPtr argv = CommandLineToArgvW("stub.exe " + arguments, out int count);
            if (argv == IntPtr.Zero)
                throw new InvalidOperationException();
            try
            {
                var result = new string[count - 1];
                for (int i = 1; i < count; i++)
                    result[i - 1] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size));
                return result;
            }
            finally
            {
                LocalFree(argv);
            }
        }

        private static bool RoundTrips(
            string[] original,
            int tokenIndex,
            Match placeholder,
            string prefix,
            string suffix,
            ShellMenuScope scope)
        {
            string sentinel = scope == ShellMenuScope.Drive
                ? @"C:\"
                : @"C:\sentinel path\item";
            string[] expected = original.ToArray();
            expected[tokenIndex] = expected[tokenIndex].Substring(0, placeholder.Index)
                + sentinel
                + expected[tokenIndex].Substring(placeholder.Index + placeholder.Length);
            foreach (string materialized in new[]
            {
                prefix + "\"" + sentinel + "\"" + suffix,
                prefix + sentinel + suffix
            })
            {
                string[] actual;
                try
                {
                    actual = ParseArguments(materialized);
                }
                catch
                {
                    continue;
                }
                if (ArgumentListsEquivalent(expected, actual))
                    return true;
            }
            return false;
        }

        private static bool ArgumentListsEquivalent(string[] expected, string[] actual)
        {
            if (expected == null || actual == null || expected.Length != actual.Length)
                return false;
            for (int i = 0; i < expected.Length; i++)
            {
                if (!String.Equals(NormalizeArg(expected[i]), NormalizeArg(actual[i]), StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            return true;
        }

        private static string NormalizeArg(string value)
        {
            if (String.IsNullOrEmpty(value))
                return String.Empty;
            value = value.Trim();
            if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
                value = value.Substring(1, value.Length - 2);
            return value.Trim();
        }


        private static ShellMenuConversion ConvertDynamicHandler(ShellMenuEntry entry)
        {
            if (!TryReadHandlerClsid(entry.RegistryKey, out Guid clsid, out string rawClsid))
                return Unsupported(entry.Reason == "dynamic-handler" ? "handler-clsid-missing" : (entry.Reason ?? "handler-clsid-missing"));

            ShellMenuSource source = entry.Candidate.Sources
                .OrderBy(x => x.Precedence)
                .ThenBy(x => x.Fingerprint, StringComparer.Ordinal)
                .FirstOrDefault();
            string handlerName = entry.Candidate.VerbName ?? entry.Title;
            string provenanceIdentity = source == null
                ? null
                : source.LogicalIdentity + "|handler=" + handlerName.ToLowerInvariant() + "|clsid=" + clsid.ToString("B").ToLowerInvariant();

            MenuItemDraft draft = new MenuItemDraft
            {
                Command = CommandType.InvokeShellHandler,
                Name = entry.Title,
                GroupName = entry.Breadcrumb,
                Application = null,
                Parameter = ParameterType.Path,
                Prefix = String.Empty,
                Suffix = String.Empty,
                ItemTypeFilter = entry.Candidate.Scope.ItemTypeFilter,
                SelectionFilter = SelectionFilter.None,
                ExtensionFilter = entry.Candidate.Scope.ExtensionFilter,
                ContextFilter = entry.Candidate.Scope.ContextFilter,
                ConfirmBeforeRun = false,
                SourceId = provenanceIdentity,
                SourceSummary = entry.Candidate.Scope.Id + ": " + entry.Title + " [" + clsid.ToString("B") + "]",
                ShellHandlerClsid = clsid.ToString("B"),
                ShellHandlerName = handlerName,
                ShellAssociationHint = entry.Candidate.Scope.Association,
                IsShellBridge = true,
                ExpandedCommand = "shell-handler:" + clsid.ToString("B"),
                ParsedExecutable = rawClsid,
                ParsedArguments = handlerName,
                PlaceholderSummary = "shell-handler-menu",
                ExecutableExists = true
            };
            return new ShellMenuConversion(ShellMenuConversionKind.ShellBridge, "shell-bridge", draft);
        }

        private static bool TryReadHandlerClsid(ShellRegistryKeySnapshot key, out Guid clsid, out string raw)
        {
            clsid = Guid.Empty;
            raw = null;
            if (key == null)
                return false;
            raw = ReadValue(key, String.Empty) ?? ReadValue(key, "CLSID");
            if (String.IsNullOrWhiteSpace(raw))
                return false;
            raw = raw.Trim();
            return Guid.TryParse(raw, out clsid);
        }

        private static ShellMenuConversion NeedsReview(string reason, MenuItemDraft draft = null)
            => new ShellMenuConversion(ShellMenuConversionKind.NeedsReview, reason, draft);

        private static ShellMenuConversion ManualReview(
            ShellMenuEntry entry,
            string reason,
            string application,
            string rawArguments)
        {
            MenuItemDraft draft = CreateDraft(
                entry,
                application ?? String.Empty,
                ParameterType.Expression,
                rawArguments ?? entry.RawCommand ?? String.Empty,
                String.Empty,
                SelectionFilter.Single);
            draft.RequiresEditing = true;
            draft.ExpandedCommand = entry.RawCommand;
            draft.ParsedExecutable = application;
            draft.ParsedArguments = rawArguments;
            draft.PlaceholderSummary = String.Join(", ", PlaceholderToken.Matches(
                rawArguments ?? entry.RawCommand ?? String.Empty).Cast<Match>().Select(x => x.Value));
            draft.ExecutableExists = !String.IsNullOrWhiteSpace(application) && File.Exists(application);
            return NeedsReview(reason, draft);
        }

        private static void PopulateParsingDetails(
            MenuItemDraft draft,
            string expanded,
            string application,
            IReadOnlyList<string> tokens,
            IReadOnlyList<(int TokenIndex, Match Match)> placeholders)
        {
            draft.ExpandedCommand = expanded;
            draft.ParsedExecutable = application;
            draft.ParsedArguments = WindowsCommandLine.Encode(tokens);
            draft.PlaceholderSummary = String.Join(", ", placeholders.Select(x =>
                "token=" + x.TokenIndex + ",span=" + x.Match.Index + ":" + x.Match.Length
                + ",value=" + x.Match.Value));
            draft.ExecutableExists = !String.IsNullOrWhiteSpace(application) && File.Exists(application);
        }

        private static bool IsPlaceholderCompatible(ShellMenuScope scope, string placeholder)
        {
            if (String.Equals(placeholder, "%V", StringComparison.OrdinalIgnoreCase))
                return scope.ContextFilter == MenuContextFilter.DirectorySelection
                    || scope.ContextFilter == MenuContextFilter.DirectoryBackground
                    || scope.ContextFilter == MenuContextFilter.DriveSelection;
            if (String.Equals(placeholder, "%*", StringComparison.Ordinal))
                return scope.ContextFilter != MenuContextFilter.DirectoryBackground;
            return scope.ContextFilter != MenuContextFilter.DirectoryBackground;
        }

        private static bool TryExpandCommand(string raw, out string expanded, out string reason)
        {
            var placeholders = new List<string>();
            string protectedCommand = ProtectedShellPlaceholder.Replace(raw, match =>
            {
                int index = placeholders.Count;
                placeholders.Add(match.Value);
                return "\uE000" + index + "\uE001";
            });
            string environmentExpanded = Environment.ExpandEnvironmentVariables(protectedCommand);
            if (UnresolvedEnvironmentVariable.IsMatch(environmentExpanded))
            {
                expanded = null;
                reason = "environment-unresolved";
                return false;
            }
            if (environmentExpanded.IndexOf('%') >= 0)
            {
                expanded = null;
                reason = "unsupported-placeholder";
                return false;
            }
            expanded = ProtectedPlaceholderSentinel.Replace(environmentExpanded, match =>
            {
                int index = Int32.Parse(match.Groups[1].Value);
                return index >= 0 && index < placeholders.Count ? placeholders[index] : String.Empty;
            });
            reason = null;
            return true;
        }

        private static MenuItemDraft CreateDraft(
            ShellMenuEntry entry,
            string application,
            ParameterType parameter,
            string prefix,
            string suffix,
            SelectionFilter selection)
        {
            ShellMenuSource source = entry.Candidate.Sources
                .OrderBy(x => x.Precedence)
                .ThenBy(x => x.Fingerprint, StringComparer.Ordinal)
                .FirstOrDefault();
            string provenanceIdentity = entry.Candidate.HasViewConflict
                ? String.Join("+", entry.Candidate.Sources
                    .Select(x => x.Fingerprint)
                    .OrderBy(x => x, StringComparer.Ordinal))
                : source == null ? null : source.LogicalIdentity + "|effective";
            return new MenuItemDraft
            {
                Name = entry.Title,
                GroupName = entry.Breadcrumb,
                Application = application,
                Parameter = parameter,
                Prefix = prefix,
                Suffix = suffix,
                ItemTypeFilter = entry.Candidate.Scope.ItemTypeFilter,
                SelectionFilter = selection,
                ExtensionFilter = entry.Candidate.Scope.ExtensionFilter,
                ContextFilter = entry.Candidate.Scope.ContextFilter,
                ConfirmBeforeRun = true,
                SourceId = String.IsNullOrEmpty(provenanceIdentity)
                    ? null
                    : provenanceIdentity
                        + (String.IsNullOrEmpty(entry.SourceIdentity)
                            ? String.Empty
                            : "|leaf=" + entry.SourceIdentity.ToLowerInvariant()),
                SourceSummary = entry.Candidate.Scope.Id + ": " + entry.Title
            };
        }

        private static ShellMenuConversion Unsupported(string reason)
            => new ShellMenuConversion(ShellMenuConversionKind.Unsupported, reason, null);

        [DllImport("shell32.dll", SetLastError = true)]
        private static extern IntPtr CommandLineToArgvW(
            [MarshalAs(UnmanagedType.LPWStr)] string commandLine,
            out int argumentCount);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr memory);

        private static readonly Regex PlaceholderToken = new Regex(
            "%%|%\\*|%[0-9]+|%[A-Za-z]+", RegexOptions.Compiled);
        private static readonly Regex UnresolvedEnvironmentVariable = new Regex(
            "%[A-Za-z_][A-Za-z0-9_]*%", RegexOptions.Compiled);
        private static readonly Regex ProtectedShellPlaceholder = new Regex(
            "%(?:1(?![0-9])|[lLvV](?![A-Za-z])|\\*)", RegexOptions.Compiled);
        private static readonly Regex ProtectedPlaceholderSentinel = new Regex(
            "\uE000([0-9]+)\uE001", RegexOptions.Compiled);
        private static readonly Regex SupportedPlaceholder = new Regex("^%(?:1|[lLvV]|\\*)$", RegexOptions.Compiled);
        private static readonly Regex SupportedOccurrence = new Regex("%(?:1|[lLvV]|\\*)", RegexOptions.Compiled);
        private static readonly HashSet<string> ScriptExtensions = new HashSet<string>(
            new[] { ".bat", ".cmd", ".ps1" }, StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> DangerousHosts = new HashSet<string>(
            new[]
            {
                "cmd.exe", "powershell.exe", "pwsh.exe", "rundll32.exe", "control.exe", "mmc.exe",
                "wscript.exe", "cscript.exe", "mshta.exe", "reg.exe", "regsvr32.exe", "schtasks.exe",
                "sc.exe", "diskpart.exe", "bcdedit.exe"
            },
            StringComparer.OrdinalIgnoreCase);
        private readonly IExecutableResolver executableResolver;
    }
}

