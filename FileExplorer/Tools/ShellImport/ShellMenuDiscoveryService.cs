using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FileExplorer.Tools.ShellImport
{
    public interface IShellMenuDiscoveryService
    {
        Task<IReadOnlyList<ShellMenuDiscoveryItem>> DiscoverAsync(
            string extension,
            IReadOnlyCollection<string> scopeIds,
            CancellationToken cancellationToken);
    }

    public sealed class ShellMenuDiscoveryItem
    {
        public ShellMenuDiscoveryItem(
            string title,
            string scope,
            string rawCommand,
            ShellMenuConversionKind conversionKind,
            string reason,
            MenuItemDraft draft,
            bool isDiagnostic = false,
            string sourceSummary = null,
            ShellMenuDiagnosticCode? diagnosticCode = null,
            int? scannedScopeCount = null)
        {
            Title = title;
            Scope = scope;
            RawCommand = rawCommand;
            ConversionKind = conversionKind;
            Reason = reason;
            Draft = draft;
            IsDiagnostic = isDiagnostic;
            SourceSummary = sourceSummary;
            DiagnosticCode = diagnosticCode;
            ScannedScopeCount = scannedScopeCount;
        }

        public string Title { get; }
        public string Scope { get; }
        public string RawCommand { get; }
        public ShellMenuConversionKind ConversionKind { get; }
        public string Reason { get; }
        public MenuItemDraft Draft { get; }
        public bool IsDiagnostic { get; }
        public string SourceSummary { get; }
        public ShellMenuDiagnosticCode? DiagnosticCode { get; }
        public int? ScannedScopeCount { get; }
    }

    public sealed class ShellMenuDiscoveryService : IShellMenuDiscoveryService
    {
        public ShellMenuDiscoveryService(
            IShellMenuScopeProvider scopeProvider,
            IShellMenuInventory inventory,
            ShellMenuAnalyzer analyzer,
            ShellMenuConverter converter)
        {
            this.scopeProvider = scopeProvider;
            this.inventory = inventory;
            this.analyzer = analyzer;
            this.converter = converter;
        }

        public Task<IReadOnlyList<ShellMenuDiscoveryItem>> DiscoverAsync(
            string extension,
            CancellationToken cancellationToken)
        {
            return DiscoverAsync(extension, null, cancellationToken);
        }

        public Task<IReadOnlyList<ShellMenuDiscoveryItem>> DiscoverAsync(
            string extension,
            IReadOnlyCollection<string> scopeIds,
            CancellationToken cancellationToken)
        {
            return Task.Run<IReadOnlyList<ShellMenuDiscoveryItem>>(() =>
            {
                IReadOnlyList<ShellMenuDiagnostic> scopeDiagnostics;
                IReadOnlyList<ShellMenuScope> availableScopes;
                bool includeExtension = scopeIds == null
                    || scopeIds.Contains("extension", StringComparer.OrdinalIgnoreCase);
                string requestedExtension = includeExtension ? extension : null;
                if (scopeProvider is IShellMenuScopeProviderWithDiagnostics providerWithDiagnostics)
                {
                    ShellMenuScopeProviderResult scopeResult = providerWithDiagnostics
                        .GetScopesWithDiagnostics(requestedExtension);
                    availableScopes = scopeResult.Scopes;
                    scopeDiagnostics = scopeResult.Diagnostics;
                }
                else
                {
                    availableScopes = scopeProvider.GetScopes(requestedExtension);
                    scopeDiagnostics = Array.Empty<ShellMenuDiagnostic>();
                }
                ShellMenuScope[] selectedScopes = (scopeIds == null
                    ? availableScopes
                    : availableScopes.Where(scope => scopeIds.Contains(scope.Id, StringComparer.OrdinalIgnoreCase)
                        || (scope.Id.StartsWith("extension:", StringComparison.OrdinalIgnoreCase)
                            && scopeIds.Contains("extension", StringComparer.OrdinalIgnoreCase))))
                    .ToArray();
                ShellMenuScanResult scan = inventory.Scan(selectedScopes, cancellationToken);
                var result = new List<ShellMenuDiscoveryItem>();
                foreach (ShellMenuCandidate candidate in scan.Candidates)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    foreach (ShellMenuEntry entry in analyzer.Analyze(candidate))
                    {
                        ShellMenuConversion conversion = converter.Convert(entry);
                        result.Add(new ShellMenuDiscoveryItem(
                            entry.Title,
                            candidate.Scope.Id,
                            entry.RawCommand,
                            conversion.Kind,
                            conversion.Reason ?? entry.Reason,
                            conversion.Draft,
                            false,
                            String.Join("; ", candidate.Sources.OrderBy(source => source.Precedence).Select(source =>
                                source.Hive + "|" + source.View + "|" + source.RegistryPath)),
                            null,
                            selectedScopes.Length));
                    }
                }
                foreach (ShellMenuDiagnostic diagnostic in scan.Diagnostics)
                {
                    result.Add(new ShellMenuDiscoveryItem(
                        "Scan diagnostic",
                        diagnostic.RegistryPath,
                        null,
                        ShellMenuConversionKind.Unsupported,
                        diagnostic.Code + ": " + diagnostic.Message,
                        null,
                        true,
                        diagnostic.RegistryPath,
                        diagnostic.Code,
                        selectedScopes.Length));
                }
                foreach (ShellMenuDiagnostic diagnostic in scopeDiagnostics)
                {
                    result.Add(new ShellMenuDiscoveryItem(
                        "Scan diagnostic",
                        diagnostic.RegistryPath,
                        null,
                        ShellMenuConversionKind.Unsupported,
                        diagnostic.Code + ": " + diagnostic.Message,
                        null,
                        true,
                        diagnostic.RegistryPath,
                        diagnostic.Code,
                        selectedScopes.Length));
                }
                return result;
            }, cancellationToken);
        }

        private readonly IShellMenuScopeProvider scopeProvider;
        private readonly IShellMenuInventory inventory;
        private readonly ShellMenuAnalyzer analyzer;
        private readonly ShellMenuConverter converter;
    }
}
