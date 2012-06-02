﻿///
/// Codewise/FooSync/WPFApp/MainWindow.xaml.cs
/// 
/// by William R. Fraser:
///     http://www.codewise.org/
///     https://github.com/wfraser/FooSync
///     
/// Copyright (c) 2012
/// 

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using Codewise.FooSync;
using Ookii.Dialogs.Wpf;

namespace Codewise.FooSync.WPFApp
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            _foo = new FooSyncEngine();

            InitializeComponent();

            EnableControls(false);
            this.Show();

#if false
            if (System.Diagnostics.Debugger.IsAttached)
            {
                //
                // WRFDEV: for testing purposes
                //

                string error;
                _config = RepositoryConfigLoader.GetRepositoryConfig(@"W:\.FooSync_Repository.xml", out error);
                DirectorySelector.ItemsSource = _config.Directories;
                EnableControls(true);
                DirectorySelector.SelectedIndex = 0;
                InspectButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                return;
            }
#endif

            ShowStartWindow();
        }

        public void ShowStartWindow()
        {
            _start = new StartWindow();
            _start.Left = Math.Max(this.Left + (this.Width / 2) - (_start.Width / 2), 0);
            _start.Top = Math.Max(this.Top + (this.Height / 2) - (_start.Height / 2), 0);
            _start.Topmost = true;
            _start.WindowStyle = System.Windows.WindowStyle.None;
            _start.NewButton.Click += new RoutedEventHandler(NewRepository);
            _start.OpenButton.Click += new RoutedEventHandler(OpenRepository);
            _start.Show();
        }

        private void EnableControls(DependencyObject parent, bool enabled)
        {
            foreach (var obj in LogicalTreeHelper.GetChildren(parent))
            {
                if (obj is Control)
                {
                    if (obj != InspectButton && obj != DoActionsButton)
                    {
                        (obj as Control).IsEnabled = enabled;
                    }
                }
                else if (obj is DependencyObject)
                {
                    EnableControls(obj as DependencyObject, enabled);
                }
            }
        }

        private void EnableControls(bool enabled)
        {
            EnableControls(this as DependencyObject, enabled);
        }

        private void LoadRepositoryConfig(string filename)
        {
            string errStr = string.Empty;
            try
            {
                _config = RepositoryConfigLoader.GetRepositoryConfig(filename, out errStr);
            }
            catch (Exception ex)
            {
                errStr = ex.Message;
            }
            finally
            {
                if (errStr != string.Empty)
                {
                    MessageBox.Show("Loading config failed: " + errStr, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }

            if (_config == null)
            {
                ShowStartWindow();
            }
            else
            {
                RepoNameLabel.DataContext = _config;
                DirectorySelector.DataContext = _config.Directories;
                DirectorySelector.SelectedIndex = 0;

                if (_config.Directories.Count() == 1)
                {
                    DirectorySelector.SelectedIndex = 1;
                    InspectButton.IsEnabled = true;
                }

                EnableControls(true);
            }
        }

        private void OpenRepository(object sender, RoutedEventArgs e)
        {
            string filename = null;
            bool cancelled = false;

            var dlg = new Microsoft.Win32.OpenFileDialog();
            dlg.Filter = "FooSync Repository Config|" + FooSyncEngine.ConfigFileName;
            dlg.FilterIndex = 1;
            dlg.Multiselect = false;

            cancelled = !(dlg.ShowDialog() ?? false);
            filename = dlg.FileName;

            if (cancelled)
            {
                if (_config != null)
                {
                    return;
                }

                ShowStartWindow();
            }
            else
            {
                LoadRepositoryConfig(filename);
            }
        }

        private void NewRepository(object sender, RoutedEventArgs e)
        {
            string repositoryPath = null;

            var win = new CreateRepositoryWindow();
            win.WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner;

            win.RepositoryPath = repositoryPath;

            var result = win.ShowDialog();

            if (result.HasValue && result.Value == true)
            {
                LoadRepositoryConfig(Path.Combine(win.RepositoryPath, FooSyncEngine.ConfigFileName));
            }
            else if (_config == null)
            {
                ShowStartWindow();
            }
        }

        private void ShowAboutWindow(object sender, RoutedEventArgs e)
        {
            var about = new AboutWindow();
            about.ShowActivated = true;
            about.ShowDialog();
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            if (_start != null)
            {
                _start.Close();
            }
        }

        #region Directory inspection methods

        private void Inspect(object sender, RoutedEventArgs e)
        {
            RepositoryDirectory dir = DirectorySelector.SelectedItem as RepositoryDirectory;

            if (dir == null)
                return;

            if (dir.Source == null)
            {
                var result = MessageBox.Show(
                    "There's no source configured for this repository directory that matches your computer.\n\nWould you like to configure one?",
                    "No Valid Source",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Exclamation,
                    MessageBoxResult.No);
                if (result == MessageBoxResult.Yes)
                {
                    MessageBox.Show("[[TODO: Configure Window]]");
                }
                else
                {
                    return;
                }
            }

            InspectButton.IsEnabled = false;
            EnableControls(false);

            NewFiles.DataContext = null;
            (NewFiles.Parent as Expander).IsExpanded = false;
            ChangedFiles.DataContext = null;
            (ChangedFiles.Parent as Expander).IsExpanded = false;
            DeletedFiles.DataContext = null;
            (DeletedFiles.Parent as Expander).IsExpanded = false;

            ProgressDialog dlg = new ProgressDialog();

            dlg.WindowTitle = "Inspecting Directory";
            dlg.Text = "Inspecting Directory...";
            dlg.ShowCancelButton = true;
            dlg.UseCompactPathsForDescription = true;
            dlg.ProgressBarStyle = ProgressBarStyle.MarqueeProgressBar;

            dlg.DoWork += new DoWorkEventHandler(EnumerateFilesWorker);
            dlg.RunWorkerCompleted += new RunWorkerCompletedEventHandler(EnumerateFilesCompletedWorker);
            dlg.Show(dir);
        }

        void EnumerateFilesWorker(object sender, DoWorkEventArgs e)
        {
            var dir = e.Argument as RepositoryDirectory;
            var dlg = sender as ProgressDialog;

            var exceptions = FooSyncEngine.PrepareExceptions(dir.IgnoreRegex, dir.IgnoreGlob);

            DateTime last = DateTime.Now;
            _repo = new FooTree(_foo, Path.Combine(_config.RepositoryPath, dir.Path), exceptions,
            (Progress)delegate(int n, int total, string d)
            {
                if ((DateTime.Now - last).TotalMilliseconds > 100)
                {
                    last = DateTime.Now;
                    dlg.ReportProgress(0, "Enumerating Repository Files...", string.Format("Found {0} files\n{1}", n, d));

                    if (dlg.CancellationPending)
                    {
                        throw new OperationCanceledException();
                    }
                }
            });

            _source = new FooTree(_foo, dir.Source.Path, exceptions,
            (Progress)delegate(int n, int total, string d)
            {
                if ((DateTime.Now - last).TotalMilliseconds > 100)
                {
                    last = DateTime.Now;
                    dlg.ReportProgress(0, "Enumerating Source Files...", string.Format("Found {0} files\n{1}", n, d));

                    if (dlg.CancellationPending)
                    {
                        throw new OperationCanceledException();
                    }
                }
            });

            dlg.ReportProgress(0, "Loading Repository State...", string.Empty);

            try
            {
                _state = new RepositoryState(Path.Combine(_config.RepositoryPath, dir.Path, FooSyncEngine.RepoStateFileName));
            }
            catch (FileNotFoundException)
            {
                _state = new RepositoryState();
                _state.AddSource(_repo, RepositoryState.RepoSourceName);
                _state.AddSource(_source, Environment.MachineName.ToLower());
                _state.Write(Path.Combine(_config.RepositoryPath, dir.Path, FooSyncEngine.RepoStateFileName));
            }

            // save this for the completed worker
            e.Result = dir;
        }

        void EnumerateFilesCompletedWorker(object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Error != null)
            {
                if (!(e.Error is OperationCanceledException))
                {
                    MessageBox.Show(string.Format("An error occured while enumerating files:\n{0}", e.Error.Message), "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }

                InspectButton.IsEnabled = true;
                EnableControls(true);
                return;
            }

            var dir = e.Result as RepositoryDirectory;

            ProgressDialog dlg = new ProgressDialog();

            dlg.WindowTitle = "Inspecting Directory";
            dlg.Text = "Inspecting Directory...";
            dlg.ShowCancelButton = true;
            dlg.UseCompactPathsForDescription = true;
            dlg.ProgressBarStyle = ProgressBarStyle.ProgressBar;

            dlg.DoWork += new DoWorkEventHandler(InspectDirectoryWorker);
            dlg.RunWorkerCompleted += new RunWorkerCompletedEventHandler(InspectDirectoryCompletedWorker);
            dlg.Show(dir);
        }

        private void InspectDirectoryWorker(object sender, DoWorkEventArgs e)
        {
            var dir = e.Argument as RepositoryDirectory;
            var dlg = sender as ProgressDialog;

            DateTime last = DateTime.Now;
            _changeset = _foo.Inspect(_state, _repo, _source,
            (Progress)delegate(int n, int total, string d) 
            {
                if ((DateTime.Now - last).TotalMilliseconds > 100)
                {
                    last = DateTime.Now;
                    var percent = (int)Math.Round((double)n / total * 100);
                    dlg.ReportProgress(percent, "Comparing Files...", string.Format("{0}%:\n{1}", percent, d));

                    if (dlg.CancellationPending)
                    {
                        throw new OperationCanceledException();
                    }
                }
            });

            FooSyncEngine.GetConflicts(_changeset, _state, _repo, _source);
            _changeset.SetDefaultActions();
        }

        private void InspectDirectoryCompletedWorker(object sender, RunWorkerCompletedEventArgs e)
        {
            InspectButton.IsEnabled = true;
            EnableControls(true);

            if (e.Error != null)
            {
                if (!(e.Error is OperationCanceledException))
                {
                    MessageBox.Show(string.Format("An error occurred while comparing files:\n{0}", e.Error.Message), "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                return;
            }

            DoActionsButton.IsEnabled = true;

            if (_changeset.Count() == 0)
            {
                MessageBox.Show("No changes detected. Nothing to do.", "Change set empty", MessageBoxButton.OK, MessageBoxImage.Asterisk);
                return;
            }

            //
            // Partitioning predicates
            //

            Func<FooChangeSetElem, bool> newPred = elem => 
                (!_state.Repository.MTimes.ContainsKey(elem.Filename) && !_repo.Files.ContainsKey(elem.Filename))
                    || (!_state.Source.MTimes.ContainsKey(elem.Filename) && !_source.Files.ContainsKey(elem.Filename));

            Func<FooChangeSetElem, bool> delPred = elem =>
                (_state.Repository.MTimes.ContainsKey(elem.Filename) && !_repo.Files.ContainsKey(elem.Filename))
                    || (_state.Source.MTimes.ContainsKey(elem.Filename) && !_source.Files.ContainsKey(elem.Filename));

            //
            // Partition into 3 classes: new, changed, and deleted.
            //

            NewFiles.DataContext = new BindableChangeSet(_changeset, newPred, _repo, _source);
            if ((NewFiles.DataContext as BindableChangeSet).Count() > 0)
            {
                (NewFiles.Parent as Expander).IsExpanded = true;
            }

            DeletedFiles.DataContext = new BindableChangeSet(_changeset, delPred, _repo, _source);
            if ((DeletedFiles.DataContext as BindableChangeSet).Count() > 0)
            {
                (DeletedFiles.Parent as Expander).IsExpanded = true;
            }

            ChangedFiles.DataContext = new BindableChangeSet(_changeset, elem => !newPred(elem) && !delPred(elem), _repo, _source);
            if ((ChangedFiles.DataContext as BindableChangeSet).Count() > 0)
            {
                (ChangedFiles.Parent as Expander).IsExpanded = true;
            }

            StatsCopyRepo.DataContext = new BindableChangeSet(_changeset, elem => elem.FileOperation == FileOperation.UseRepo, _repo, _source);
            StatsCopySource.DataContext = new BindableChangeSet(_changeset, elem => elem.FileOperation == FileOperation.UseSource, _repo, _source);
            StatsDelRepo.DataContext = new BindableChangeSet(_changeset, elem => elem.FileOperation == FileOperation.DeleteRepo, _repo, _source);
            StatsDelSource.DataContext = new BindableChangeSet(_changeset, elem => elem.FileOperation == FileOperation.DeleteSource, _repo, _source);
            StatsConflict.DataContext = new BindableChangeSet(_changeset, elem => elem.ConflictStatus != ConflictStatus.NoConflict, _repo, _source);
            StatsNoOp.DataContext = new BindableChangeSet(_changeset, elem => elem.FileOperation == FileOperation.NoOp, _repo, _source);
        }

        #endregion

        private void DirectorySelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count == 1)
            {
                if (e.AddedItems[0] is RepositoryDirectory)
                {
                    InspectButton.IsEnabled = true;
                }
                else if (e.AddedItems[0] is ComboBoxItem)
                {
                    if (string.Equals("new", ((ComboBoxItem)e.AddedItems[0]).Tag as string))
                    {
                        InspectButton.IsEnabled = false;
                        MessageBox.Show("[[TODO: Add subdirectory window]]");
                    }
                }
            }
        }

        private void ContextMenu_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            var list = sender as ListView;

            if (list != null && list.ContextMenu != null && list.ContextMenu.Items != null)
            {
                var selectedItems = list.SelectedItems.Cast<BindableChangeSetElem>().ToList();

                bool repoPresent = (selectedItems != null && selectedItems.Count == 1
                            && selectedItems[0].RepositoryDate.HasValue);
                bool sourcePresent = (selectedItems != null && selectedItems.Count == 1
                            && selectedItems[0].SourceDate.HasValue);

                for (int i = 0; i < list.ContextMenu.Items.Count; i++)
                {
                    var item = list.ContextMenu.Items[i] as MenuItem;
                    if (item != null)
                    {
                        //
                        // This is kinda hacky, but x:Name can't be used in a ControlTemplate :/
                        // The alternative is switching on the Header property...
                        //

                        if (i == 0 || i == 3 || i == 6) // Open file [location] / Show properties (Repository)
                        {
                            item.IsEnabled = repoPresent;
                        }
                        else if (i == 1 || i == 4 || i == 7) // Open file [location] / Show properties (Source)
                        {
                            item.IsEnabled = sourcePresent;
                        }
                        else if (i == 9) // Change action to:
                        {
                            repoPresent = selectedItems != null && selectedItems.Count > 0 
                                    && selectedItems.All(elem => elem.RepositoryDate.HasValue);
                            sourcePresent = selectedItems != null && selectedItems.Count > 0
                                    && selectedItems.All(elem => elem.SourceDate.HasValue);

                            for (int j = 0; j < item.Items.Count; j++)
                            {
                                var subItem = item.Items[j] as MenuItem;
                                if (subItem != null)
                                {
                                    if (j == 0) // NoOp
                                    {
                                        subItem.IsEnabled = true;
                                    }
                                    else if (j == 1 || j == 3) // UseRepo || DeleteRepo
                                    {
                                        subItem.IsEnabled = repoPresent;
                                    }
                                    else if (j == 2 || j == 4) // UseSource || DeleteSource
                                    {
                                        subItem.IsEnabled = sourcePresent;
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        private void DoActionsButton_Click(object sender, RoutedEventArgs e)
        {
            var copySrc = new List<string>();
            var copyDst = new List<string>();
            var deletes = new List<string>();

            InspectButton.IsEnabled = false;
            DoActionsButton.IsEnabled = false;
            EnableControls(false);

            foreach (var filename in _changeset)
            {
                var repoFilename = Path.Combine(_repo.Path, filename);
                var sourceFilename = Path.Combine(_source.Path, filename);

                switch (_changeset[filename].FileOperation)
                {
                    case FileOperation.UseRepo:
                        copySrc.Add(repoFilename);
                        copyDst.Add(sourceFilename);
                        break;

                    case FileOperation.UseSource:
                        copySrc.Add(sourceFilename);
                        copyDst.Add(repoFilename);
                        break;

                    case FileOperation.DeleteRepo:
                        deletes.Add(repoFilename);
                        break;

                    case FileOperation.DeleteSource:
                        deletes.Add(sourceFilename);
                        break;

                }
            }

            if (copySrc.Count + deletes.Count == 0)
            {
                MessageBox.Show("No actions to take. Done!", "Nothing to do", MessageBoxButton.OK, MessageBoxImage.Information);
                goto enableControls;
            }

            //
            // Don't bother with the Progress callback for the CopyEngine calls below.
            // We're on Windows (this is WPF, which mono doesn't support), so assume these calls are guaranteed
            // to call into SHFileOperation(), which has its own progress UI.
            //

            bool allCompleted = true;

            if (copySrc.Count > 0)
            {
                allCompleted = CopyEngine.Copy(copySrc, copyDst);

                if (!allCompleted)
                {
                    MessageBox.Show(
                        "Copy operation interrupted. Some files may have been copied, but not all of them.",
                        "File Copy Incomplete",
                        MessageBoxButton.OK, MessageBoxImage.Exclamation);
                }
            }

            if (allCompleted && deletes.Count > 0)
            {
                allCompleted = CopyEngine.Delete(deletes);

                if (!allCompleted)
                {
                    MessageBox.Show(
                        "Delete operation interrupted. Some files may have been deleted as requested, but not all of them.",
                        "File Delete Incomplete",
                        MessageBoxButton.OK, MessageBoxImage.Exclamation);
                }
                else
                {
                    var allEmptiesRemoved = CopyEngine.RemoveEmptyDirectories(deletes);

                    if (!allEmptiesRemoved)
                    {
                        MessageBox.Show(
                            "Some empty directories could not be removed.",
                            "File Delete Incomplete",
                            MessageBoxButton.OK, MessageBoxImage.Exclamation);
                    }
                }
            }

            if (allCompleted)
            {
                FooSyncEngine.UpdateRepoState(_state, _changeset, _repo, _source);
                _state.Write(Path.Combine(_repo.Path, FooSyncEngine.RepoStateFileName));

                NewFiles.DataContext = null;
                (NewFiles.Parent as Expander).IsExpanded = false;
                ChangedFiles.DataContext = null;
                (ChangedFiles.Parent as Expander).IsExpanded = false;
                DeletedFiles.DataContext = null;
                (DeletedFiles.Parent as Expander).IsExpanded = false;

                MessageBox.Show(string.Format("{0} files copied and {1} files deleted successfully.", copySrc.Count, deletes.Count), "All Completed Successfully", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                DoActionsButton.IsEnabled = true;
            }

enableControls:
            InspectButton.IsEnabled = true;
            EnableControls(true);
        }

        private void StatsConflict_SourceUpdated(object sender, DataTransferEventArgs e)
        {
            var tb = sender as TextBlock;
            if (tb != null)
            {
                if (tb.DataContext != null && tb.DataContext is BindableChangeSet && ((BindableChangeSet)tb.DataContext).Count > 0)
                {
                    tb.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Crimson);
                    tb.FontWeight = System.Windows.FontWeights.Bold;
                }
                else
                {
                    tb.Foreground = System.Windows.SystemColors.WindowTextBrush;
                    tb.FontWeight = System.Windows.FontWeights.Normal;
                }
            }
        }

        private void OpenExplorerAt(string filename)
        {
            System.Diagnostics.Process.Start("explorer.exe", "/select," + filename);
        }

        private void OpenWithDefaultApplication(string filename)
        {
            System.Diagnostics.Process.Start(filename);
        }

        private void OpenPropertiesDialog(string filename)
        {
            var info = new NativeMethods.SHELLEXECUTEINFO();

            info.fMask = NativeMethods.SEE_MASK.SEE_MASK_INVOKEIDLIST;
            info.lpVerb = "properties";
            info.lpFile = filename;
            info.nShow = NativeMethods.NShowCommand.SW_SHOW;
            info.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(info);

            //
            // Don't bother looking up error codes; API shows its own error UI.
            //
            NativeMethods.ShellExecuteEx(ref info);
        }

        #region Actions

        public static RoutedCommand OpenLocationRepo = new RoutedCommand("OpenLocationRepo", typeof(MainWindow));
        private void OnOpenLocationRepo(object sender, ExecutedRoutedEventArgs e)
        {
            if (e == null)
                return;

            var list = (e.Parameter as ListView);

            if (list != null)
            {
                if (list.SelectedItems.Count == 1)
                {
                    var item = list.SelectedItem as BindableChangeSetElem;
                    OpenExplorerAt(Path.Combine(_repo.Path, item.Filename));
                }
            }
        }

        public static RoutedCommand OpenLocationSource = new RoutedCommand("OpenLocationSource", typeof(MainWindow));
        private void OnOpenLocationSource(object sender, ExecutedRoutedEventArgs e)
        {
            if (e == null)
                return;

            var list = (e.Parameter as ListView);

            if (list != null)
            {
                if (list.SelectedItems.Count == 1)
                {
                    var item = list.SelectedItem as BindableChangeSetElem;
                    OpenExplorerAt(Path.Combine(_source.Path, item.Filename));
                }
            }
        }

        public static RoutedCommand OpenFileRepo = new RoutedCommand("OpenFileRepo", typeof(MainWindow));
        private void OnOpenFileRepo(object sender, ExecutedRoutedEventArgs e)
        {
            if (e == null)
                return;

            var list = (e.Parameter as ListView);

            if (list != null)
            {
                if (list.SelectedItems.Count == 1)
                {
                    var item = list.SelectedItem as BindableChangeSetElem;
                    OpenWithDefaultApplication(Path.Combine(_repo.Path, item.Filename));
                }
            }
        }

        public static RoutedCommand OpenFileSource = new RoutedCommand("OpenFileSource", typeof(MainWindow));
        private void OnOpenFileSource(object sender, ExecutedRoutedEventArgs e)
        {
            if (e == null)
                return;

            var list = (e.Parameter as ListView);

            if (list != null)
            {
                if (list.SelectedItems.Count == 1)
                {
                    var item = list.SelectedItem as BindableChangeSetElem;
                    OpenWithDefaultApplication(Path.Combine(_source.Path, item.Filename));
                }
            }
        }

        public static RoutedCommand ShowPropertiesSource = new RoutedCommand("ShowPropertiesSource", typeof(MainWindow));
        private void OnShowPropertiesSource(object sender, ExecutedRoutedEventArgs e)
        {
            if (e == null)
                return;

            var list = (e.Parameter as ListView);

            if (list != null)
            {
                if (list.SelectedItems.Count == 1)
                {
                    var item = list.SelectedItem as BindableChangeSetElem;
                    OpenPropertiesDialog(Path.Combine(_source.Path, item.Filename));
                }
            }
        }

        public static RoutedCommand ShowPropertiesRepo = new RoutedCommand("ShowPropertiesRepo", typeof(MainWindow));
        private void OnShowPropertiesRepo(object sender, ExecutedRoutedEventArgs e)
        {
            if (e == null)
                return;

            var list = (e.Parameter as ListView);

            if (list != null)
            {
                if (list.SelectedItems.Count == 1)
                {
                    var item = list.SelectedItem as BindableChangeSetElem;
                    OpenPropertiesDialog(Path.Combine(_repo.Path, item.Filename));
                }
            }
        }

        public static RoutedCommand ChangeFileOperation = new RoutedCommand("ChangeFileOperation", typeof(MainWindow));
        private void OnChangeFileOperation(object sender, ExecutedRoutedEventArgs e)
        {
            if (e == null)
                return;

            var args = e.Parameter as List<object>;

            if (args != null && args.Count == 2)
            {
                var list = args[0] as ListView;
                var newOp = (FooSync.FileOperation)Enum.Parse(typeof(FooSync.FileOperation), args[1] as string);

                if (list != null && list.SelectedItems != null)
                {
                    foreach (BindableChangeSetElem item in list.SelectedItems)
                    {
                        _changeset[item.Filename].FileOperation = newOp;
                    }
                }

                _changeset.AdviseChanged();
            }
        }

        #endregion

        private StartWindow _start = null;
        private RepositoryConfig _config = null;
        private FooSyncEngine _foo = null;
        private FooTree _repo = null;
        private FooTree _source = null;
        private RepositoryState _state = null;
        private FooChangeSet _changeset = null;
    }
}
