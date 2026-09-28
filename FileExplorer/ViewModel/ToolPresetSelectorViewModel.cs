using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using FileExplorer.Persistence;
using FileExplorer.Tools;

namespace FileExplorer.ViewModel
{
    public sealed class ToolPresetSelectorViewModel
    {
        public ToolPresetSelectorViewModel(PersistentCollection<MenuItem> menuItems, IToolLocator locator, Func<string, string> resourceLookup = null)
        {
            this.menuItems = menuItems;
            this.locator = locator;
            this.resourceLookup = resourceLookup ?? (key => Properties.Resources.ResourceManager.GetString(key) ?? key);
            var locations = ToolPresetCatalog.Default.Definitions
                .Select(x => x.ExecutableRole)
                .Distinct(StringComparer.Ordinal)
                .ToDictionary(x => x, x => locator.Locate(x), StringComparer.Ordinal);
            Items = new ObservableCollection<ToolPresetSelectionItem>(
                ToolPresetCatalog.Default.Definitions.Select(x => CreateItem(x, locations[x.ExecutableRole])));
        }

        public ObservableCollection<ToolPresetSelectionItem> Items { get; }

        public int ApplySelected()
        {
            int added = 0;
            foreach (ToolPresetSelectionItem item in Items.Where(x => x.IsSelected && x.CanSelect).ToList())
            {
                if (ToolPresetMenuFactory.AddIfMissing(menuItems, item.Definition, item.ExecutablePath, item.DisplayName))
                    added++;
                item.IsSelected = false;
                item.IsAlreadyAdded = true;
            }
            return added;
        }

        public void SetExecutablePath(ToolPresetSelectionItem item, string executablePath)
        {
            locator.SaveOverride(item.ExecutableRole, executablePath);
            SynchronizeExistingApplications();
            Refresh();
        }

        public void ClearOverride(ToolPresetSelectionItem item)
        {
            locator.ClearOverride(item.ExecutableRole);
            SynchronizeExistingApplications();
            Refresh();
        }

        public void Refresh()
        {
            var locations = Items.Select(x => x.ExecutableRole)
                .Distinct(StringComparer.Ordinal)
                .ToDictionary(x => x, x => locator.Locate(x), StringComparer.Ordinal);
            foreach (ToolPresetSelectionItem item in Items)
            {
                item.UpdateLocation(locations[item.ExecutableRole]);
                item.IsAlreadyAdded = menuItems.Any(x => String.Equals(x.ToolPresetId, item.PresetId, StringComparison.Ordinal));
                if (!item.CanSelect)
                    item.IsSelected = false;
            }
        }

        private ToolPresetSelectionItem CreateItem(ToolPresetDefinition definition, ToolLocationResult location)
        {
            bool exists = menuItems.Any(x => String.Equals(x.ToolPresetId, definition.Id, StringComparison.Ordinal));
            return new ToolPresetSelectionItem(definition, resourceLookup(definition.DisplayNameResourceKey), location, exists, resourceLookup);
        }

        private void SynchronizeExistingApplications()
        {
            foreach (MenuItem menuItem in menuItems.Where(x => !String.IsNullOrEmpty(x.ToolPresetId)).ToList())
            {
                ToolPresetDefinition definition;
                try
                {
                    definition = ToolPresetCatalog.Default.GetDefinition(menuItem.ToolPresetId);
                }
                catch (ToolPresetException)
                {
                    continue;
                }

                ToolLocationResult location = locator.Locate(definition.ExecutableRole);
                if (location.IsFound && !String.Equals(menuItem.Application, location.ExecutablePath, StringComparison.OrdinalIgnoreCase))
                {
                    menuItem.Application = location.ExecutablePath;
                    menuItems.Update(menuItem);
                }
            }
        }

        private readonly PersistentCollection<MenuItem> menuItems;
        private readonly IToolLocator locator;
        private readonly Func<string, string> resourceLookup;
    }

    public sealed class ToolPresetSelectionItem : INotifyPropertyChanged
    {
        internal ToolPresetSelectionItem(ToolPresetDefinition definition, string displayName, ToolLocationResult location, bool isAlreadyAdded, Func<string, string> resourceLookup)
        {
            Definition = definition;
            DisplayName = displayName;
            this.resourceLookup = resourceLookup;
            IsAlreadyAdded = isAlreadyAdded;
            UpdateLocation(location);
        }

        public ToolPresetDefinition Definition { get; }
        public string PresetId => Definition.Id;
        public string GroupName => Definition.GroupName;
        public string DisplayName { get; }
        public string ExecutableRole => Definition.ExecutableRole;
        public string ExpectedFileName => Definition.ExpectedFileName;
        public string CommandSummary => Definition.CommandSummary;

        public string ExecutablePath { get; private set; }
        public string LocationSource { get; private set; }
        public bool IsAvailable { get; private set; }
        public bool HasInvalidOverride { get; private set; }
        public bool CanClearOverride => HasInvalidOverride || String.Equals(LocationSource, "override", StringComparison.Ordinal);
        public string StatusText { get; private set; }

        public bool IsAlreadyAdded
        {
            get => isAlreadyAdded;
            set
            {
                if (isAlreadyAdded == value)
                    return;
                isAlreadyAdded = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(CanSelect));
                UpdateStatusText();
            }
        }

        public bool IsSelected
        {
            get => isSelected;
            set
            {
                if (isSelected == value || (value && !CanSelect))
                    return;
                isSelected = value;
                RaisePropertyChanged();
            }
        }

        public bool CanSelect => IsAvailable && !IsAlreadyAdded;

        internal void UpdateLocation(ToolLocationResult location)
        {
            ExecutablePath = location.ExecutablePath;
            LocationSource = location.Source;
            IsAvailable = location.IsFound;
            HasInvalidOverride = location.HasInvalidOverride;
            RaisePropertyChanged(nameof(ExecutablePath));
            RaisePropertyChanged(nameof(LocationSource));
            RaisePropertyChanged(nameof(IsAvailable));
            RaisePropertyChanged(nameof(HasInvalidOverride));
            RaisePropertyChanged(nameof(CanClearOverride));
            RaisePropertyChanged(nameof(CanSelect));
            UpdateStatusText();
        }

        private void UpdateStatusText()
        {
            string key = IsAlreadyAdded
                ? "ToolPresetStatusAdded"
                : HasInvalidOverride
                    ? "ToolPresetStatusInvalidOverride"
                    : IsAvailable ? "ToolPresetStatusFound" : "ToolPresetStatusMissing";
            StatusText = resourceLookup(key) ?? key;
            RaisePropertyChanged(nameof(StatusText));
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void RaisePropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private bool isAlreadyAdded;
        private bool isSelected;
        private readonly Func<string, string> resourceLookup;
    }
}
