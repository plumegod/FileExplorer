using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DevExpress.Data.Filtering;
using DevExpress.Data.Filtering.Helpers;
using DevExpress.Mvvm;
using FileExplorer.Core;
using FileExplorer.Helpers;
using FileExplorer.Messages;
using FileExplorer.Model;
using FileExplorer.Tools;
using FileExplorer.Tools.ShellInvoke;
using LiteDB;

namespace FileExplorer.Persistence
{
    public class MenuItem : PersistentItem, IDuplicateAwarePersistentItem
    {
        [Required]
        public string Name { get; set; }

        [Required]
        public CommandType Command
        {
            get { return command; }
            set
            {
                CommandType effectiveValue = IsToolPreset ? CommandType.OpenWithApplication : value;
                if (command != effectiveValue)
                {
                    command = effectiveValue;
                    RaisePropertyChanged(nameof(Command));
                }
            }
        }
        private CommandType command;

        [Required]
        public ParameterType Parameter
        {
            get { return parameter; }
            set
            {
                if (parameter != value)
                {
                    parameter = value;
                    RaisePropertyChanged(nameof(Parameter));
                }
            }
        }
        private ParameterType parameter;

        public string Application { get; set; }

        public string ToolPresetId
        {
            get { return toolPresetId; }
            set
            {
                if (toolPresetId == value)
                    return;
                toolPresetId = value;
                if (IsToolPreset)
                    Command = CommandType.OpenWithApplication;
                RaisePropertyChanged(nameof(ToolPresetId));
                RaisePropertyChanged(nameof(IsToolPreset));
                RaisePropertyChanged(nameof(CanDuplicate));
                RaisePropertyChanged(nameof(DuplicateToolTip));
            }
        }
        private string toolPresetId;

        [BsonIgnore]
        public MenuContextFilter ContextFilter
        {
            get { return contextFilter; }
            set
            {
                MenuContextFilter effective = Enum.IsDefined(typeof(MenuContextFilter), value)
                    ? value
                    : MenuContextFilter.Legacy;
                if (contextFilter == effective)
                    return;
                contextFilter = effective;
                RaisePropertyChanged(nameof(ContextFilter));
            }
        }
        private MenuContextFilter contextFilter = MenuContextFilter.Legacy;

        [BsonField("ContextFilter")]
        public int PersistedContextFilter
        {
            get { return (int)ContextFilter; }
            set { ContextFilter = (MenuContextFilter)value; }
        }

        public string ImportedMenuSourceId { get; set; }

        public string ImportedMenuSourceSummary { get; set; }

        public string ShellHandlerClsid { get; set; }

        public string ShellHandlerName { get; set; }

        public string ShellAssociationHint { get; set; }

        public string GroupName { get; set; }

        public string Expression { get; set; }

        public string Prefix { get; set; }

        public string Suffix { get; set; }

        public string Shortcut
        {
            get { return shortcut; }
            set
            {
                shortcut = value == null ? String.Empty : value;
            }
        }
        private string shortcut;

        public string ExtensionFilter { get; set; }

        public bool ConfirmBeforeRun { get; set; }

        public bool ShowErrors { get; set; }

        public ItemTypeFilter ItemTypeFilter { get; set; }

        public SelectionFilter SelectionFilter { get; set; }

        [BsonIgnore]
        public bool CanDuplicate => String.IsNullOrEmpty(ToolPresetId);

        [BsonIgnore]
        public bool IsToolPreset => !String.IsNullOrEmpty(ToolPresetId);

        [BsonIgnore]
        public string DuplicateToolTip => CanDuplicate ? FileExplorer.Properties.Resources.Copy : FileExplorer.Properties.Resources.ToolPresetCannotDuplicate;

        [BsonIgnore]
        public ImageSource Icon
        {
            get
            {
                switch (Command)
                {
                    case CommandType.Open:
                        return Open.Value;
                    case CommandType.OpenInNewTab:
                        return OpenInNewTab.Value;
                    case CommandType.OpenInNewWindow:
                        return OpenInNewWindow.Value;
                    case CommandType.OpenWithApplication:
                        return IconHelper.GetIcon(Application);
                    case CommandType.InvokeShellHandler:
                        return Open.Value;
                }

                return IconHelper.GetIcon(Application);
            }
        }

        [BsonIgnore]
        public ICommand ExecuteCommand
        {
            get
            {
                if (executeCommand == null)
                    executeCommand = new DelegateCommand<MenuInvocationContext>(Execute, CanExecute);

                return executeCommand;
            }
        }
        private ICommand executeCommand;

        [BsonIgnore]
        public PropertyDescriptorCollection Properties
        {
            get
            {
                if (properties == null)
                    properties = TypeDescriptor.GetProperties(typeof(FileModel));

                return properties;
            }
        }
        private PropertyDescriptorCollection properties;

        [BsonIgnore]
        protected static readonly Lazy<ImageSource> Open = new Lazy<ImageSource>(() => new BitmapImage(new Uri("pack://application:,,,/FileExplorer;component/Assets/ICO/Open.ico")));

        [BsonIgnore]
        protected static readonly Lazy<ImageSource> OpenInNewTab = new Lazy<ImageSource>(() => new BitmapImage(new Uri("pack://application:,,,/FileExplorer;component/Assets/ICO/OpenFolder.ico")));

        [BsonIgnore]
        protected static readonly Lazy<ImageSource> OpenInNewWindow = new Lazy<ImageSource>(() => new BitmapImage(new Uri("pack://application:,,,/FileExplorer;component/Assets/ICO/OpenNewWindow.ico")));

        public bool CanExecute(IList<object> items)
        {
            if (items == null || items.Count == 0)
                return false;

            if (items.Count > 1 && SelectionFilter == SelectionFilter.Single)
                return false;

            if (items.Count == 1 && SelectionFilter == SelectionFilter.Multiple)
                return false;

            if (items.OfType<FileModel>().Any(x => x.IsDirectory) && ItemTypeFilter == ItemTypeFilter.File)
                return false;

            if (items.OfType<FileModel>().Any(x => !x.IsDirectory) && ItemTypeFilter == ItemTypeFilter.Folder)
                return false;

            if (items.OfType<FileModel>().Any(x => ExtensionFilter != null && ExtensionFilter.Split('|').Any(y => x.FullName.OrdinalEndsWith(y)) == false))
                return false;

            return true;
        }

        public bool CanExecute(MenuInvocationContext context)
        {
            if (context == null || !MatchesContext(context))
                return false;
            return CanExecute(context.Items);
        }

        public void Execute(IList<object> items)
        {
            List<FileModel> files = items.OfType<FileModel>().ToList();
            string directory = files.FirstOrDefault()?.ParentPath;
            ExecuteCore(items, directory);
        }

        public void Execute(MenuInvocationContext context)
        {
            if (context == null)
                return;
            ExecuteCore(context.Items, context.WorkingDirectory);
        }

        public void PrepareDuplicate()
        {
            ImportedMenuSourceId = null;
            ImportedMenuSourceSummary = null;
        }

        private void ExecuteCore(IList<object> items, string directory)
        {
            List<FileModel> files = items.OfType<FileModel>().ToList();

            if (!String.IsNullOrEmpty(ToolPresetId))
            {
                CommandMessage templateMessage = new CommandMessage
                {
                    Directory = directory,
                    Parameters = files,
                    MenuItem = this
                };
                try
                {
                    ToolInvocation invocation = ToolPresetCatalog.Default.BuildInvocation(ToolPresetId, Application, files);
                    templateMessage.Invocation = new ToolInvocation(
                        invocation.Application,
                        invocation.WorkingDirectory,
                        invocation.Arguments,
                        ConfirmBeforeRun);
                }
                catch (ToolPresetException exception)
                {
                    templateMessage.ExecutionError = exception.ErrorCode;
                }
                Messenger.Default.Send(templateMessage);
                return;
            }

            if (Command == CommandType.InvokeShellHandler)
            {
                CommandMessage bridgeMessage = new CommandMessage
                {
                    Directory = directory,
                    Parameters = files,
                    MenuItem = this
                };
                if (!Guid.TryParse(ShellHandlerClsid, out Guid clsid) || clsid == Guid.Empty)
                {
                    bridgeMessage.ExecutionError = "shell-handler-clsid-invalid";
                    Messenger.Default.Send(bridgeMessage);
                    return;
                }

                List<string> paths = files.Select(x => x.FullPath).Where(x => !String.IsNullOrWhiteSpace(x)).ToList();
                if (paths.Count == 0)
                {
                    bridgeMessage.ExecutionError = "shell-handler-paths-empty";
                    Messenger.Default.Send(bridgeMessage);
                    return;
                }

                IntPtr ownerHwnd = IntPtr.Zero;
                try
                {
                    if (System.Windows.Application.Current?.MainWindow != null)
                    {
                        ownerHwnd = new System.Windows.Interop.WindowInteropHelper(
                            System.Windows.Application.Current.MainWindow).Handle;
                    }
                }
                catch
                {
                    ownerHwnd = IntPtr.Zero;
                }

                ShellHandlerInvocationResult result = ShellHandlerInvokerFactory.Default.Invoke(
                    new ShellHandlerInvocationRequest(clsid, paths, directory, ownerHwnd));
                if (result.Status == ShellHandlerInvocationStatus.Failed)
                    bridgeMessage.ExecutionError = result.ErrorCode ?? "shell-handler-failed";
                else if (result.Status == ShellHandlerInvocationStatus.Cancelled)
                    return;

                if (!String.IsNullOrEmpty(bridgeMessage.ExecutionError))
                    Messenger.Default.Send(bridgeMessage);
                return;
            }

            List<string> parameters = new List<string>();

            if (Parameter == ParameterType.Name)
                parameters = files.Select(x => String.Format("\"{0}\"", x.FullName)).ToList();
            else if (Parameter == ParameterType.Path)
                parameters = files.Select(x => String.Format("\"{0}\"", x.FullPath)).ToList();
            else if (!String.IsNullOrEmpty(Expression))
            {
                List<FileModel> expressionResults = new List<FileModel>();
                foreach (FileModel file in files)
                {
                    object result = new ExpressionEvaluator(Properties, CriteriaOperator.Parse(Expression)).Evaluate(file);
                    if (result != null)
                    {
                        parameters.Add(result.ToString());

                        FileModel fileModel = FileModel.Create(result.ToString());
                        if (fileModel != null)
                            expressionResults.Add(fileModel);
                    }
                }
                files = expressionResults;
            }

            CommandMessage message = new CommandMessage
            {
                Arguments = $"{Prefix}{parameters.Join(" ")}{Suffix}",
                Directory = directory,
                Parameters = files,
                MenuItem = this
            };

            Messenger.Default.Send(message);
        }

        private bool MatchesContext(MenuInvocationContext context)
        {
            List<FileModel> files = context.Items.OfType<FileModel>().ToList();
            switch (ContextFilter)
            {
                case MenuContextFilter.Legacy:
                    return true;
                case MenuContextFilter.FileSelection:
                    return context.Source == MenuInvocationSource.Selection
                        && files.Count == context.Items.Count
                        && files.All(x => !x.IsDirectory);
                case MenuContextFilter.DirectorySelection:
                    return context.Source == MenuInvocationSource.Selection
                        && files.Count == context.Items.Count
                        && files.All(x => x.IsDirectory && !x.IsDrive);
                case MenuContextFilter.DirectoryBackground:
                    return context.Source == MenuInvocationSource.Background
                        && files.Count == 1
                        && files[0].IsDirectory;
                case MenuContextFilter.DriveSelection:
                    return context.Source == MenuInvocationSource.Selection
                        && files.Count == context.Items.Count
                        && files.All(x => x.IsDrive);
                case MenuContextFilter.FileSystemSelection:
                    return context.Source == MenuInvocationSource.Selection
                        && files.Count == context.Items.Count
                        && files.All(x => !x.IsDrive && !x.IsRoot);
                default:
                    return true;
            }
        }

        public override string ToString()
        {
            return Name;
        }

        public MenuItem()
        {
            Name = FileExplorer.Properties.Resources.NewMenuItem;
            Shortcut = String.Empty;
            Application = Utilities.AppPath;
        }
    }
}
