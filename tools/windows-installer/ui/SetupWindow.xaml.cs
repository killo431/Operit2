using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using WinForms = System.Windows.Forms;

namespace Operit.Installer
{
    public partial class SetupWindow : Window
    {
        private readonly ManualResetEvent start;
        private readonly bool installed;
        private readonly SetupDiagnostics diagnostics;
        private bool running;
        private bool finished;
        private bool succeeded;
        private bool finalizing;
        public bool CancelRequested { get; private set; }
        public string InstallFolder { get; private set; }
        public SetupOperation Operation { get; private set; }
        public CleanupPlan DataCleanupPlan { get; private set; }
        public bool DeleteData { get; private set; }

        /// <summary>Creates the custom install, maintenance, or uninstall screen.</summary>
        public SetupWindow(string version, string folder, SetupOperation operation, bool installed, ManualResetEvent start,
            SetupDiagnostics diagnostics)
        {
            InitializeComponent();
            this.start = start;
            this.installed = installed;
            this.diagnostics = diagnostics;
            InstallFolder = folder;
            LocationText.Text = folder;
            VersionText.Text = version;
            Operation = operation;
            MsiLogButton.IsEnabled = !string.IsNullOrWhiteSpace(diagnostics.MsiLogPath);
            if (operation == SetupOperation.Update)
            {
                Heading.Text = "Update Operit2";
                Description.Text = "An older version is installed. Update to use the new version.";
                MainButton.Content = "Update app";
                SecondaryButton.Content = "Not now";
                LocationText.IsEnabled = false;
                ChangeLocationButton.Visibility = Visibility.Collapsed;
                InstallHint.Text = "The update keeps the current install location.\nYour chats, settings, plugins, and workspaces are preserved.";
            }
            else if (operation == SetupOperation.Repair || operation == SetupOperation.Remove)
            {
                bool removing = operation == SetupOperation.Remove;
                Heading.Text = removing ? "Uninstall Operit2" : "Manage Operit2";
                Description.Text = removing ? "Remove the app and keep your chats and personal settings."
                    : installed ? "Repair the app files, or remove the app from this PC." : "The same version is installed; you can repair the app files.";
                MainButton.Content = removing ? "Uninstall" : "Repair app";
                SecondaryButton.Content = removing ? "Cancel" : installed ? "Uninstall app" : "Not now";
                LocationText.IsEnabled = false;
                ChangeLocationButton.Visibility = Visibility.Collapsed;
                InstallHint.Text = removing
                    ? "Data is kept by default. Selecting this deletes chats, settings, plugins, workspaces, and keys,\nand affects other copies on this PC that share the data."
                    : installed ? "Repair restores the files the app needs.\nUninstall keeps your chats and personal settings."
                    : "Repair restores the files the app needs.\nYour chats and personal settings are preserved.";
                DeleteUserData.Visibility = removing ? Visibility.Visible : Visibility.Collapsed;
                CleanupDetailsButton.Visibility = removing ? Visibility.Visible : Visibility.Collapsed;
            }
            InstallHint.Text += "\nWhen it starts, Operit2 at this location is closed automatically.";
        }

        /// <summary>Starts an MSI operation or opens the successfully installed application.</summary>
        private void PrimaryAction(object sender, RoutedEventArgs e)
        {
            if (finished)
            {
                if (!succeeded && (!CancelRequested || diagnostics.HasErrors))
                {
                    CopyDetails(sender, e);
                    return;
                }
                if (succeeded && Operation != SetupOperation.Remove)
                    Process.Start(new ProcessStartInfo(Path.Combine(InstallFolder, "operit2.exe")) { UseShellExecute = true });
                Close();
                return;
            }
            try
            {
                if (!Path.IsPathRooted(LocationText.Text))
                    throw new ArgumentException("Enter a full install path, for example D:\\Apps\\Operit2.");
                InstallFolder = Path.GetFullPath(LocationText.Text);
                if (InstallFolder.TrimEnd('\\').Length < 4)
                    throw new ArgumentException("Choose an application folder; do not install directly to a drive root.");
                DeleteData = Operation == SetupOperation.Remove && DeleteUserData.IsChecked == true;
                if (DeleteData)
                {
                    var cleanup = new UserDataCleanup(InstallFolder);
                    DataCleanupPlan = cleanup.CreatePlan();
                    if (MessageBox.Show(this, DataCleanupPlan.Describe(), "Confirm permanent deletion of local user data",
                        MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
                }
            }
            catch (Exception error)
            {
                Description.Text = error.Message;
                return;
            }
            running = true;
            LocationPanel.Visibility = Visibility.Collapsed;
            ProgressPanel.Visibility = Visibility.Visible;
            Heading.Text = Operation == SetupOperation.Remove ? "Uninstalling…"
                : Operation == SetupOperation.Update ? "Updating…"
                : Operation == SetupOperation.Repair ? "Repairing…" : "Installing…";
            Description.Text = "This will take a moment. Keep the setup window open.";
            MainButton.IsEnabled = false;
            MainButton.Content = "Working…";
            SecondaryButton.Content = "Cancel";
            start.Set();
        }

        /// <summary>Selects uninstall from the maintenance screen or closes the wizard.</summary>
        private void SecondaryAction(object sender, RoutedEventArgs e)
        {
            if (!running && !finished && installed && Operation == SetupOperation.Repair)
            {
                Operation = SetupOperation.Remove;
                Heading.Text = "Uninstall Operit2";
                Description.Text = "Remove the app and keep your chats and personal settings.";
                MainButton.Content = "Uninstall";
                SecondaryButton.Content = "Cancel";
                DeleteUserData.Visibility = Visibility.Visible;
                CleanupDetailsButton.Visibility = Visibility.Visible;
                InstallHint.Text = "Data is kept by default. Selecting this deletes chats, settings, plugins, workspaces, and keys,\nand affects other copies on this PC that share the data.\nWhen it starts, Operit2 at this location is closed automatically.";
                return;
            }
            Close();
        }

        /// <summary>Explains the shared data scope when permanent removal is selected.</summary>
        private void CleanupChoiceChanged(object sender, RoutedEventArgs args)
        {
            Description.Text = DeleteUserData.IsChecked == true
                ? "Remove the app and permanently clean up local shared user data." : "Remove the app and keep your chats and personal settings.";
        }

        /// <summary>Displays configured roots without changing or deleting any data.</summary>
        private void ShowCleanupDetails(object sender, RoutedEventArgs args)
        {
            try { MessageBox.Show(this, new UserDataCleanup(InstallFolder).CreatePlan().Describe(), "User data cleanup scope"); }
            catch (Exception error) { MessageBox.Show(this, error.Message, "Cannot determine cleanup scope"); }
        }

        /// <summary>Turns an in-progress close into an MSI cancellation request.</summary>
        private void OnClosing(object sender, CancelEventArgs e)
        {
            if (!running) return;
            e.Cancel = true;
            if (finalizing) return;
            CancelRequested = true;
            ProgressText.Text = "Canceling, please wait…";
            SecondaryButton.IsEnabled = false;
            CloseButton.IsEnabled = false;
        }

        /// <summary>Disables cancellation while completing work after the MSI transaction has committed.</summary>
        public void BeginFinalization()
        {
            finalizing = true;
            SecondaryButton.IsEnabled = false;
            CloseButton.IsEnabled = false;
            SecondaryButton.Content = "Finishing…";
        }

        /// <summary>Opens the system folder picker so the install directory is chosen, not typed.</summary>
        private void EditLocation(object sender, RoutedEventArgs e)
        {
            if (Operation != SetupOperation.Install) return;
            using (var dialog = new WinForms.FolderBrowserDialog
            {
                Description = "Choose the Operit2 install folder",
                SelectedPath = LocationText.Text
            })
            {
                if (dialog.ShowDialog(new WindowOwner(this)) == WinForms.DialogResult.OK)
                    LocationText.Text = dialog.SelectedPath;
            }
        }

        /// <summary>Adapts the WPF window handle so the folder picker stays modal to it.</summary>
        private sealed class WindowOwner : WinForms.IWin32Window
        {
            private readonly IntPtr handle;
            public WindowOwner(Window window) { handle = new WindowInteropHelper(window).Handle; }
            public IntPtr Handle => handle;
        }

        /// <summary>Moves the frameless window using the custom title bar.</summary>
        private void DragTitleBar(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }

        /// <summary>Minimizes the custom window without showing a native title bar.</summary>
        private void Minimize(object sender, RoutedEventArgs e) { WindowState = WindowState.Minimized; }

        /// <summary>Routes the custom close button through cancellation handling.</summary>
        private void RequestClose(object sender, RoutedEventArgs e) { Close(); }

        /// <summary>Updates the progress display with MSI's reported work.</summary>
        public void ReportProgress(double value, string action)
        {
            Progress.Value = Math.Max(0, Math.Min(99, value));
            PercentText.Text = Math.Round(Progress.Value) + "%";
            if (action != null && !CancelRequested) ProgressText.Text = action;
            if (diagnostics.LoggingError != null)
            {
                Description.Text = diagnostics.LoggingError;
                CopyDetailsButton.Visibility = Visibility.Visible;
            }
        }

        /// <summary>Shows errors immediately while leaving Windows Installer time to stop or roll back.</summary>
        public void ShowFailure()
        {
            ExpandDiagnostics();
            Description.Text = "Stopping the install, please wait…";
        }

        /// <summary>Shows a short explanation and keeps raw diagnostics in a collapsed details section.</summary>
        private void ExpandDiagnostics()
        {
            BrandPanel.Visibility = Visibility.Collapsed;
            Grid.SetColumn(ContentPanel, 0);
            Grid.SetColumnSpan(ContentPanel, 2);
            Width = Math.Min(900, SystemParameters.WorkArea.Width - 24);
            Height = Math.Min(TechnicalDetails.IsExpanded ? 800 : 620, SystemParameters.WorkArea.Height - 24);
            Left = SystemParameters.WorkArea.Left + (SystemParameters.WorkArea.Width - Width) / 2;
            Top = SystemParameters.WorkArea.Top + (SystemParameters.WorkArea.Height - Height) / 2;
            LocationPanel.Visibility = Visibility.Collapsed;
            ProgressPanel.Visibility = Visibility.Collapsed;
            ResultText.Visibility = Visibility.Collapsed;
            FailurePanel.Visibility = Visibility.Visible;
            TechnicalDetails.Visibility = Visibility.Visible;
            var problem = diagnostics.Problem();
            Heading.Text = problem.Title;
            ProblemReason.Text = problem.Reason;
            ProblemSteps.Text = problem.Steps;
            ErrorDetailsText.Text = diagnostics.Describe();
            DiagnosticsPanel.Visibility = Visibility.Visible;
            ShowDiagnosticsButton.Visibility = Visibility.Collapsed;
            CopyDetailsButton.Visibility = Visibility.Visible;
        }

        /// <summary>Gives expanded technical details more space without enlarging the default error screen.</summary>
        private void ResizeTechnicalDetails(object sender, RoutedEventArgs e)
        {
            Height = Math.Min(TechnicalDetails.IsExpanded ? 800 : 620, SystemParameters.WorkArea.Height - 24);
            Top = SystemParameters.WorkArea.Top + (SystemParameters.WorkArea.Height - Height) / 2;
        }

        /// <summary>Opens the diagnostic report at any point without changing the MSI operation.</summary>
        private void ShowDiagnostics(object sender, RoutedEventArgs e)
        {
            var report = new Window
            {
                Title = "Operit2 setup log and diagnostics", Owner = this, Width = Math.Min(850, SystemParameters.WorkArea.Width - 40),
                Height = Math.Min(680, SystemParameters.WorkArea.Height - 40), WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new TextBox
                {
                    Text = diagnostics.Describe(), IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Margin = new Thickness(16), Padding = new Thickness(12)
                }
            };
            report.Show();
        }

        /// <summary>Copies the complete diagnostic report and reports clipboard access failures.</summary>
        private void CopyDetails(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(diagnostics.Describe());
                if (finished) MainButton.Content = "Error details copied";
                else CopyDetailsButton.Content = "Error details copied";
            }
            catch (Exception error) { MessageBox.Show(this, "Cannot copy to the clipboard: " + error.Message + "\nSelect the details and press Ctrl+C, or open the diagnostic log.", "Copy failed"); }
        }

        /// <summary>Opens the diagnostic file in the system-associated viewer.</summary>
        private void OpenDiagnosticLog(object sender, RoutedEventArgs e) { OpenDiagnosticPath(diagnostics.LogPath); }

        /// <summary>Opens the exact verbose MSI log supplied by Windows Installer.</summary>
        private void OpenMsiLog(object sender, RoutedEventArgs e) { OpenDiagnosticPath(diagnostics.MsiLogPath); }

        /// <summary>Opens the directory containing this operation's persistent diagnostic log.</summary>
        private void OpenLogFolder(object sender, RoutedEventArgs e) { OpenDiagnosticPath(Path.GetDirectoryName(diagnostics.LogPath)); }

        /// <summary>Reports file-association, missing-file, and permission errors without hiding them.</summary>
        private void OpenDiagnosticPath(string path)
        {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception error) { MessageBox.Show(this, "Cannot open: " + path + "\n" + error.Message + "\nYou can paste this path into File Explorer to view it.", "Cannot open log", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        /// <summary>Shows a success, cancellation, or error result after the transaction ends.</summary>
        public void Complete(bool success)
        {
            running = false;
            finished = true;
            succeeded = success;
            CloseButton.IsEnabled = true;
            ProgressPanel.Visibility = Visibility.Collapsed;
            ResultText.Visibility = Visibility.Visible;
            MainButton.IsEnabled = true;
            SecondaryButton.IsEnabled = true;
            SecondaryButton.Content = "Done";
            if (success)
            {
                Heading.Text = Operation == SetupOperation.Remove ? "Uninstall complete"
                    : Operation == SetupOperation.Update ? "Update complete" : "All set";
                Description.Text = Operation == SetupOperation.Remove ? "See you again soon." : "Your AI workspace is ready.";
                ResultText.Text = Operation == SetupOperation.Remove
                    ? DeleteData ? "Local user data and Operit2 credentials were deleted. Data on other devices and in the cloud was not modified." : "Your chats and personal settings were preserved."
                    : Operation == SetupOperation.Update ? "Operit2 was updated to the new version.\nYour chats and personal settings were preserved."
                    : "Open Operit2 from the desktop or Start menu.\n\nLet the next idea start here.";
                MainButton.Content = Operation == SetupOperation.Remove ? "Done" : "Open Operit2";
            }
            else
            {
                bool canceled = CancelRequested && !diagnostics.HasErrors;
                ExpandDiagnostics();
                if (canceled)
                {
                    Heading.Text = "Operation canceled";
                    FailurePanel.Visibility = Visibility.Collapsed;
                }
                Description.Text = canceled ? "Setup stopped this operation." : "Follow the steps below.";
                Description.Visibility = canceled ? Visibility.Visible : Visibility.Collapsed;
                MainButton.Content = canceled ? "Close" : "Copy error details";
                CopyDetailsButton.Visibility = Visibility.Collapsed;
                SecondaryButton.Content = "Close";
                SecondaryButton.Visibility = canceled ? Visibility.Collapsed : Visibility.Visible;
            }
        }
    }
}
