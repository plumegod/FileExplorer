using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shell;
using System.Windows.Threading;
using CommandLine;
using DevExpress.Data.Filtering;
using DevExpress.Mvvm;
using DevExpress.Mvvm.POCO;
using DevExpress.Xpf.Core;
using DevExpress.Xpf.Grid;
using FileExplorer.Common;
using FileExplorer.Core;
using FileExplorer.Helpers;
using FileExplorer.Model;
using FileExplorer.Persistence;
using FileExplorer.Properties;
using FileExplorer.View;
using FileExplorer.ViewModel;
using Microsoft.Win32;

namespace FileExplorer
{
    public partial class App : Application
    {
        public static AssemblyName AssemblyName { get; private set; }

        public static Repository Repository { get; private set; }

        public static PackageManager PackageManager { get; private set; }

        public static ExtensionManager ExtensionManager { get; private set; }        

        public static UserControl TaskbarIconContainer { get; private set; }

        public static double Dpi
        {
            get
            {
                if (dpi == 0)
                {
                    using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                    {
                        dpi = g.DpiX / 96;
                    }
                }

                return dpi;
            }
        }
        private static double dpi = 0;

        public App()
        {
            themeSubscription = UiRuntimeBootstrap.Initialize(Settings.Default.ThemeName, themeName =>
            {
                if (Settings.Default.ThemeName != themeName)
                {
                    Settings.Default.ThemeName = themeName;
                    Settings.Default.Save();
                }
            });

            Current.Dispatcher.ShutdownStarted += Dispatcher_ShutdownStarted;
            Current.DispatcherUnhandledException += Current_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            RuntimeHelpers.RunClassConstructor(typeof(TableView).TypeHandle);
            CommandManager.RegisterClassInputBinding(typeof(TreeListView), new InputBinding(ApplicationCommands.NotACommand, new KeyGesture(Key.F5)));
            CommandManager.RegisterClassInputBinding(typeof(TableView), new InputBinding(ApplicationCommands.NotACommand, new KeyGesture(Key.F5)));
            CommandManager.RegisterClassInputBinding(typeof(CardView), new InputBinding(ApplicationCommands.NotACommand, new KeyGesture(Key.F5)));
        }

        public static void UpdateAndRestart()
        {
            PackageManager.RestartRequired = true;
            SaveSessionAndShutdown();
        }

        public static bool BringToFront(Window window)
        {
            if (window.WindowState == WindowState.Minimized)
                SystemCommands.RestoreWindow(window);

            return window.Activate();
        }

        public static void ParseArgumentsAndRun(IEnumerable<string> args, bool firstRun = false)
        {
            Parser.Default.ParseArguments<Options>(args).WithParsed(async (options) =>
            {
                if (options.Shutdown)
                    SaveSessionAndShutdown();
                else if (options.Background)
                    Preload();
                else if (options.Folders.Count() == 0)
                    await RestoreLastSession(firstRun);
                else
                    await CreateNewMultiTabWindow(options.Folders);

            }).WithNotParsed(async (errors) =>
            {
                await CreateFolderTabs();
            });
        }

        public static void CreateNewSingleTabWindow(FileModel fileModel = null)
        {
            Current.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, async () =>
            {
                MainView mainView = new MainView();
                mainView.Show();

                await InitializeMainViewAsync(mainView, mainViewModel =>
                {
                    mainViewModel.CreateNewTab(fileModel);
                    return Task.CompletedTask;
                });
            });
        }

        public static async Task CreateNewMultiTabWindow(IEnumerable<string> folders)
        {
            await CreateFolderTabs(folders, bringToFront: true, forceNewWindow: true);
        }

        private static void Preload()
        {
            ApplicationThemeHelper.Preload(
                PreloadCategories.Core,
                PreloadCategories.Controls,
                PreloadCategories.Docking,
                PreloadCategories.ExpressionEditor,
                PreloadCategories.Grid,
                PreloadCategories.LayoutControl,
                PreloadCategories.Printing,
                PreloadCategories.Ribbon,
                PreloadCategories.PdfViewer,
                PreloadCategories.RichEdit,
                PreloadCategories.Spreadsheet
            );
        }

        private static async Task CreateFolderTabs(
            IEnumerable<string> folders = null,
            MainView mainView = null,
            bool bringToFront = true,
            bool forceNewWindow = false,
            bool deferInactiveTabs = false)
        {
            Dispatcher dispatcher = Current.Dispatcher;
            if (!dispatcher.CheckAccess())
            {
                Task redirected = await dispatcher.InvokeAsync(
                    () => CreateFolderTabs(folders, mainView, bringToFront, forceNewWindow, deferInactiveTabs));
                await redirected;
                return;
            }

            if (mainView == null && !forceNewWindow)
                mainView = Current.Windows.OfType<MainView>().FirstOrDefault();
            if (mainView == null)
                mainView = new MainView();
            mainView.Show();

            await InitializeMainViewAsync(mainView, async mainViewModel =>
            {
                if (deferInactiveTabs)
                    await mainViewModel.RestoreFolderTabs(folders);
                else
                    await mainViewModel.CreateFolderTabs(folders);
                if (bringToFront)
                    App.BringToFront(mainView);
            });
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            CriteriaOperator.RegisterCustomFunction(new ToggleCaseFunction());
            CriteriaOperator.RegisterCustomFunction(new TitleCaseFunction());
            CriteriaOperator.RegisterCustomFunction(new SentenceCaseFunction());
            CriteriaOperator.RegisterCustomFunction(new RemoveInvalidFileNameCharactersFunction());
            CriteriaOperator.RegisterCustomFunction(new RegexMatchFunction());
            CriteriaOperator.RegisterCustomFunction(new RegexIsMatchFunction());
            CriteriaOperator.RegisterCustomFunction(new RegexConcatFunction());
            CriteriaOperator.RegisterCustomFunction(new RegexReplaceFunction());
            CriteriaOperator.RegisterCustomFunction(new StringFormatFunction());

            Assembly entryAssembly = Assembly.GetEntryAssembly();
            AssemblyName = entryAssembly.GetName();

            Repository = new Repository("Data.db");
            PackageManager = new PackageManager();
            ExtensionManager = new ExtensionManager("PreviewExtensions");
            TaskbarIconContainer = FindResource("TaskbarIconContainer") as UserControl;
            
            FileSystemWatcherHelper.Start();
            
            InitializeJumpList();
            ParseArgumentsAndRun(e.Args, true);

            base.OnStartup(e);

            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Render, () =>
            {
                ApplicationThemeHelper.PreloadAsync(PreloadCategories.ExpressionEditor);
            });
        }

        internal static async Task<bool> InitializeMainViewAsync(
            MainView mainView,
            Func<MainViewModel, Task> createTabs,
            Task readinessTask = null,
            Func<MainView, bool> isAlive = null)
        {
            await (readinessTask ?? Task.CompletedTask).ConfigureAwait(false);

            Dispatcher dispatcher = mainView.Dispatcher;
            if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
                return false;

            try
            {
                Task<bool> initialization = await dispatcher.InvokeAsync(
                    async () =>
                    {
                        if (!(isAlive ?? IsMainViewAlive)(mainView))
                            return false;

                        MainViewModel mainViewModel = mainView.DataContext as MainViewModel;
                        if (mainViewModel == null)
                        {
                            mainViewModel = ViewModelSource.Create<MainViewModel>();
                            mainView.DataContext = mainViewModel;
                        }

                        await createTabs(mainViewModel);
                        return true;
                    },
                    DispatcherPriority.ContextIdle);
                return await initialization;
            }
            catch (TaskCanceledException)
            {
                return false;
            }
            catch (InvalidOperationException) when (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
            {
                return false;
            }
        }

        private static bool IsMainViewAlive(MainView mainView)
        {
            return mainView.IsLoaded
                && mainView.IsVisible
                && !mainView.Dispatcher.HasShutdownStarted
                && !mainView.Dispatcher.HasShutdownFinished;
        }

        protected override void OnExit(ExitEventArgs e)
        {
            themeSubscription.Dispose();
            Repository?.Shutdown();

            if (PackageManager.UpdateStatus == UpdateStatus.ReadyToInstall)
                PackageManager.LaunchUpdater();

            Journal.Shutdown();
            base.OnExit(e);
        }

        private readonly IDisposable themeSubscription;

        protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
        {
            SaveSessionAndShutdown();
            base.OnSessionEnding(e);
        }

        #region Session

        public static void SaveSessionAndShutdown()
        {
            if (Settings.Default.SaveLastSession)
            {
                List<IDocument> documents = new List<IDocument>();
                StringBuilder openedFolderPaths = new StringBuilder();

                foreach (MainView view in Current.Windows?.OfType<MainView>())
                {
                    if (view.DataContext is MainViewModel mainViewModel && mainViewModel.DocumentManagerService is TabbedWindowDocumentUIService documentManagerService)
                    {
                        foreach (IDocumentGroup documentGroup in documentManagerService.Groups)
                        {
                            foreach (IDocument document in documentGroup.Documents)
                            {
                                if (!documents.Contains(document) && document.Content is BrowserTabViewModel tabViewModel)
                                {
                                    documents.Add(document);

                                    if (FileModel.FolderExists(tabViewModel.SessionPath))
                                        openedFolderPaths.Append(tabViewModel.SessionPath).Append(';');
                                }
                            }
                            openedFolderPaths.Append('|');
                        }
                    }
                }

                Settings.Default.LastSession = openedFolderPaths.ToString();
                Settings.Default.Save();
            }

            Current.Shutdown();
        }

        private static async Task RestoreLastSession(bool firstRun)
        {
            if (Settings.Default.SaveLastSession)
            {
                if (firstRun && !String.IsNullOrWhiteSpace(Settings.Default.LastSession))
                {
                    await RestoreWindowGroupsAsync(
                        Settings.Default.LastSession.Split("|"),
                        groups => CreateFolderTabs(
                            groups.Split(";"),
                            bringToFront: false,
                            forceNewWindow: true,
                            deferInactiveTabs: true));
                }
                else if (!String.IsNullOrWhiteSpace(MainViewModel.LastClosedWindowSession))
                    await CreateFolderTabs(MainViewModel.LastClosedWindowSession.Split(";"), bringToFront: true, forceNewWindow: true, deferInactiveTabs: true);
                else
                    await CreateFolderTabs();
            }
            else
                await CreateFolderTabs();
        }

        internal static async Task RestoreWindowGroupsAsync(
            IEnumerable<string> groups,
            Func<string, Task> restoreGroup)
        {
            Task[] restorations = groups.Select(restoreGroup).ToArray();
            await Task.WhenAll(restorations);
        }

        #endregion

        #region Events

        private void Dispatcher_ShutdownStarted(object sender, EventArgs e)
        {
            RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", true);

            if (Settings.Default.AddToStartup)
                registryKey.SetValue(Utilities.AppName, Utilities.AppPath + " --background");
            else
                registryKey.DeleteValue(Utilities.AppName, false);

            FileSystemWatcherHelper.Stop();
            
            Repository.Database.Dispose();
            Cache.Database.Dispose();
        }

        private void Current_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            e.Handled = true;

            HandleDispatcherException(
                e.Exception,
                exception => Journal.WriteLog(exception),
                exception => Utilities.ShowMessage(exception));
        }

        internal static void HandleDispatcherException(
            Exception exception,
            Action<Exception> logException,
            Action<Exception> showException)
        {
            logException(exception);
            if (Interlocked.Exchange(ref dispatcherErrorDialogOpen, 1) != 0)
                return;

            try
            {
                showException(exception);
            }
            finally
            {
                Volatile.Write(ref dispatcherErrorDialogOpen, 0);
            }
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            try
            {
                string message = String.Format("A fatal error has occurred in the application.\nSorry for the inconvenience.\n\n{0}:\n{1}",
                        e.ExceptionObject.GetType(), e.ExceptionObject.ToString());

                if (e.ExceptionObject is Exception)
                    Journal.WriteLog(e.ExceptionObject as Exception, true);
                else
                    Journal.WriteLog("Fatal Error: " + message, true);

                MessageBox.Show(message, "Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Journal.Shutdown();
                Environment.Exit(-1);
            }
        }

        #endregion

        private static int dispatcherErrorDialogOpen;

        #region JumpList

        public static void AddToJumpList(FileModel fileModel)
        {
            if (fileModel?.IsDirectory == true)
            {
                JumpTask jumpTask = CreateJumpTask(fileModel);
                ApplicationJumpList.JumpItems.Add(jumpTask);
                ApplicationJumpList.Apply();
            }
        }

        public static void RemoveFromJumpList(FileModel fileModel)
        {
            JumpTask jumpTask = ApplicationJumpList.JumpItems.OfType<JumpTask>().FirstOrDefault(x => x.Arguments == fileModel.FullPath);
            if (jumpTask != null)
            {
                ApplicationJumpList.JumpItems.Remove(jumpTask);
                ApplicationJumpList.Apply();
            }
        }

        private void InitializeJumpList()
        {
            foreach (FileModel fileModel in FileModel.QuickAccess.Folders)
            {
                JumpTask jumpTask = CreateJumpTask(fileModel);
                ApplicationJumpList.JumpItems.Add(jumpTask);
            }

            JumpList.SetJumpList(this, ApplicationJumpList);
            ApplicationJumpList.Apply();
        }

        private static JumpTask CreateJumpTask(FileModel fileModel)
        {
            JumpTask jumpTask = new JumpTask();
            jumpTask.ApplicationPath = Assembly.GetExecutingAssembly().Location;
            jumpTask.IconResourcePath = "%WINDIR%\\system32\\imageres.dll";
            jumpTask.Title = fileModel.Name;
            jumpTask.Arguments = fileModel.FullPath;
            jumpTask.Description = fileModel.Description;

            jumpTask.IconResourceIndex = folderIndexes.ContainsKey(fileModel.FullPath) ? folderIndexes[fileModel.FullPath] : 4;

            return jumpTask;
        }

        private static readonly JumpList ApplicationJumpList = new JumpList();

        private static readonly Dictionary<string, int> folderIndexes = new Dictionary<string, int>()
        {
            { FileSystemHelper.UserFolders[0], 105 },
            { FileSystemHelper.UserFolders[1], 107 },
            { FileSystemHelper.UserFolders[2], 175 },
            { FileSystemHelper.UserFolders[3], 103 },
            { FileSystemHelper.UserFolders[4], 108 },
            { FileSystemHelper.UserFolders[5], 178 }
        };

        #endregion
    }
}
