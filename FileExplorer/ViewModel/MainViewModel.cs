using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using DevExpress.Mvvm;
using DevExpress.Mvvm.POCO;
using FileExplorer.Core;
using FileExplorer.Messages;
using FileExplorer.Model;
using FileExplorer.Properties;
using FileExplorer.Tools;

namespace FileExplorer.ViewModel
{
    public class MainViewModel
    {
        public static string LastClosedWindowSession { get; set; }

        public virtual IActiveWindowService ActiveWindowService { get { return null; } }

        public virtual IDocumentManagerService DocumentManagerService { get { return null; } }

        public virtual BrowserTabViewModel ActiveTab { get; protected set; }

        public MainViewModel()
        {
            Messenger.Default.Register(this, (CommandMessage message) =>
            {
                if (!ActiveWindowService.IsActive)
                    return;

                if (!String.IsNullOrEmpty(message.ExecutionError))
                {
                    ShowCommandError(message.ExecutionError);
                    return;
                }

                switch (message.MenuItem.Command)
                {
                    case CommandType.Open:
                        foreach (FileModel file in message.Parameters)
                        {
                            if (DocumentManagerService.ActiveDocument?.Content is BrowserTabViewModel viewModel)
                                viewModel.OpenItem(file);
                        }
                        break;

                    case CommandType.OpenInNewTab:
                        foreach (FileModel file in message.Parameters)
                        {
                            if (file.IsDirectory)
                                CreateNewTab(file);
                        }
                        break;

                    case CommandType.OpenInNewWindow:
                        foreach (FileModel file in message.Parameters)
                        {
                            if (file.IsDirectory)
                                App.CreateNewSingleTabWindow(file);
                        }
                        break;

                    case CommandType.OpenWithApplication:
                        ProcessStartInfo processStartInfo = CreateProcessStartInfo(message);
                        bool confirmBeforeRun = message.Invocation?.ConfirmBeforeRun ?? message.MenuItem.ConfirmBeforeRun;
                        OpenWithApplication(processStartInfo, confirmBeforeRun, message.MenuItem.ShowErrors, message.Invocation != null);
                        break;

                    case CommandType.InvokeShellHandler:
                        // Execution is performed in MenuItem.ExecuteCore; only error messages reach here.
                        break;
                }
            });
        }

        public void SetActiveTab(BrowserTabViewModel viewModel)
        {
            ActiveTab = viewModel;
            if (restoreBatchDepth == 0 && viewModel?.IsMaterialized == false)
                MaterializeActiveTab(viewModel);
        }

        private async void MaterializeActiveTab(BrowserTabViewModel viewModel)
        {
            await viewModel.EnsureMaterializedAsync();
        }

        public void CreateNewTab(FileModel selectedFolder = null)
        {
            if (selectedFolder == null)
                selectedFolder = Settings.Default.FirstFolderToOpen == 0
                    ? FileModel.QuickAccess
                    : FileModel.Computer;

            CreateTab(selectedFolder, deferMaterialization: false);
        }

        private BrowserTabViewModel CreateTab(FileModel selectedFolder, bool deferMaterialization)
        {
            EnsureDocumentTracking();

            BrowserTabViewModel viewModel = ViewModelSource.Create<BrowserTabViewModel>();
            viewModel.ParentViewModel = this;
            viewModel.InitializeTab(selectedFolder, deferMaterialization);

            IDocument document = DocumentManagerService.CreateDocument("BrowserTabView", viewModel);
            document.DestroyOnClose = true;
            document.Show();
            return viewModel;
        }

        public async Task CreateFolderTabs(IEnumerable<string> folders = null)
        {
            await CreateFolderTabsCore(folders, deferInactiveTabs: false);
        }

        public async Task RestoreFolderTabs(IEnumerable<string> folders)
        {
            await CreateFolderTabsCore(folders, deferInactiveTabs: true);
        }

        private async Task CreateFolderTabsCore(IEnumerable<string> folders, bool deferInactiveTabs)
        {
            List<string> validFolders = folders?.Where(x => FileModel.FolderExists(x)).ToList();
            if (validFolders?.Count > 0)
            {
                if (deferInactiveTabs)
                    restoreBatchDepth++;

                try
                {
                    foreach (string folder in validFolders)
                    {
                        FileModel selectedFolder = FileModel.Create(folder);
                        if (selectedFolder == null)
                            continue;

                        if (deferInactiveTabs)
                            CreateTab(selectedFolder, deferMaterialization: true);
                        else
                        {
                            await selectedFolder.EnumerateParents();
                            CreateNewTab(selectedFolder);
                        }
                    }
                }
                finally
                {
                    if (deferInactiveTabs)
                        restoreBatchDepth--;
                }

                if (deferInactiveTabs && DocumentManagerService.ActiveDocument?.Content is BrowserTabViewModel activeTab)
                {
                    SetActiveTab(activeTab);
                    await activeTab.EnsureMaterializedAsync();
                }
            }
            else
                CreateNewTab();
        }

        public void SaveSession()
        {
            if (Settings.Default.SaveLastSession)
            {
                var tabs = DocumentManagerService.Documents.Select(x => x.Content);
                LastClosedWindowSession = tabs.OfType<BrowserTabViewModel>().Select(x => x.SessionPath).Where(x => !String.IsNullOrWhiteSpace(x)).Join(";");
            }
        }

        private void EnsureDocumentTracking()
        {
            IDocumentManagerService service = DocumentManagerService;
            if (ReferenceEquals(trackedDocumentManagerService, service) || service == null)
                return;

            if (trackedDocumentManagerService != null)
                trackedDocumentManagerService.ActiveDocumentChanged -= OnActiveDocumentChanged;
            trackedDocumentManagerService = service;
            trackedDocumentManagerService.ActiveDocumentChanged += OnActiveDocumentChanged;
        }

        private void OnActiveDocumentChanged(object sender, ActiveDocumentChangedEventArgs args)
        {
            SetActiveTab(args.NewDocument?.Content as BrowserTabViewModel);
        }

        private IDocumentManagerService trackedDocumentManagerService;
        private int restoreBatchDepth;

        public static ProcessStartInfo CreateProcessStartInfo(CommandMessage message)
        {
            if (message.Invocation != null)
            {
                return new ProcessStartInfo(message.Invocation.Application, WindowsCommandLine.Encode(message.Invocation.Arguments))
                {
                    WorkingDirectory = message.Invocation.WorkingDirectory
                };
            }

            return new ProcessStartInfo(message.MenuItem.Application, message.Arguments)
            {
                WorkingDirectory = message.Directory
            };
        }

        private void OpenWithApplication(ProcessStartInfo processStartInfo, bool confirmBeforeRun, bool showErrors, bool structuredArguments)
        {
            processStartInfo.RedirectStandardError = true;
            processStartInfo.UseShellExecute = false;            

            if (DocumentManagerService.ActiveDocument?.Content is BrowserTabViewModel viewModel)
            {
                try
                {
                    if (!confirmBeforeRun)
                    {
                        Utilities.StartProcess(processStartInfo, viewModel.DialogService, showErrors);
                        return;
                    }

                    MessageViewModel messageViewModel = ViewModelSource.Create(() => new MessageViewModel());
                    messageViewModel.Title = Properties.Resources.Command;
                    messageViewModel.Icon = IconType.Question;
                    messageViewModel.Content = processStartInfo.Arguments;
                    messageViewModel.ContentReadOnly = false;
                    messageViewModel.Details = $"{Properties.Resources.Application}: {processStartInfo.FileName}{Environment.NewLine}{Properties.Resources.WorkingDirectory}: {processStartInfo.WorkingDirectory}";
                    if (structuredArguments)
                        messageViewModel.Details += Environment.NewLine + Properties.Resources.ToolPresetRawArgumentsWarning;

                    MessageResult result = viewModel.DialogService.ShowDialog(MessageButton.OKCancel, Properties.Resources.RunCommand, "MessageView", messageViewModel);
                    if (result == MessageResult.OK)
                    {
                        processStartInfo.Arguments = messageViewModel.Content;
                        Utilities.StartProcess(processStartInfo, viewModel.DialogService, showErrors);
                    }
                }
                catch (Exception exception)
                {
                    Journal.WriteLog(exception);

                    MessageViewModel messageViewModel = ViewModelSource.Create(() => new MessageViewModel());
                    messageViewModel.Title = Properties.Resources.ErrorDetails;
                    messageViewModel.Icon = IconType.Exclamation;
                    messageViewModel.Content = exception.Message;
                    messageViewModel.Details = exception.StackTrace;

                    viewModel.DialogService.ShowDialog(MessageButton.OK, Properties.Resources.Error, "MessageView", messageViewModel);
                }
            }
        }

        private void ShowCommandError(string errorCode)
        {
            if (DocumentManagerService.ActiveDocument?.Content is BrowserTabViewModel viewModel)
            {
                MessageViewModel messageViewModel = ViewModelSource.Create(() => new MessageViewModel());
                messageViewModel.Title = Properties.Resources.Error;
                messageViewModel.Icon = IconType.Exclamation;
                messageViewModel.Content = ToolPresetErrorText.Get(errorCode);
                viewModel.DialogService.ShowDialog(MessageButton.OK, Properties.Resources.Error, "MessageView", messageViewModel);
            }
        }
    }
}
