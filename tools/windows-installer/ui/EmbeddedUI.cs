using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Web.Script.Serialization;
using Microsoft.Win32;
using WixToolset.Dtf.WindowsInstaller;

namespace Operit.Installer
{
    public sealed class EmbeddedUI : IEmbeddedUI
    {
        private readonly ManualResetEvent start = new ManualResetEvent(false);
        private readonly ManualResetEvent exit = new ManualResetEvent(false);
        private Thread thread;
        private SetupWindow window;
        private Exception startupError;
        private bool committed;
        private bool finalizing;
        private SetupDiagnostics diagnostics;
        private long total;
        private long completed;
        private int step;
        private bool actionData;
        private bool backwards;
        private int phase;
        private double displayedProgress;

        /// <summary>Runs the custom WPF interface for full UI and leaves unattended MSI modes unattended.</summary>
        public bool Initialize(Session session, string resourcePath, ref InstallUIOptions internalUILevel)
        {
            if (((int)internalUILevel & 7) != (int)InstallUIOptions.Full) return false;
            string logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Operit", "InstallerLogs");
            try
            {
                diagnostics = new SetupDiagnostics(logDirectory, session["OPERIT_RELEASE_VERSION"],
                    session["OriginalDatabase"], session["MsiLogFileLocation"]);
                return InitializeInteractive(session, ref internalUILevel);
            }
            catch (InstallCanceledException) { throw; }
            catch (Exception error)
            {
                string details;
                if (diagnostics != null)
                {
                    diagnostics.AddFailure(0, error.ToString());
                    details = error.Message + "\r\n\r\nDiagnostic log: " + diagnostics.LogPath
                        + "\r\nPlease provide this error and the log to the developer.";
                }
                else details = "Could not create the setup diagnostic log; setup has not started.\r\nLog folder: " + logDirectory
                    + "\r\nOriginal error: " + error.Message + "\r\nCheck write permissions and free disk space for that folder.\r\n"
                    + "You can also run the following command in a command prompt to record the exact Windows Installer error:\r\n"
                    + "msiexec.exe /i \"" + session["OriginalDatabase"] + "\" /L*V \"%USERPROFILE%\\Desktop\\Operit2-install.log\"";
                MessageBox.Show("The setup interface could not start.\r\n\r\n" + details
                    + "\r\n\r\nPress Ctrl+C in this dialog to copy the full text.", "Operit2 setup failed", MessageBoxButton.OK, MessageBoxImage.Error);
                throw new InstallerException("Operit2 setup initialization failed: " + error.Message, error);
            }
        }

        /// <summary>Initializes the interactive session after diagnostic logging has been established.</summary>
        private bool InitializeInteractive(Session session, ref InstallUIOptions internalUILevel)
        {
            string folder = ResolveInstallationFolder(session["INSTALLFOLDER"]);
            string version = session["OPERIT_RELEASE_VERSION"];
            bool installed = session.EvaluateCondition("Installed");
            bool removing = string.Equals(session["REMOVE"], "ALL", StringComparison.OrdinalIgnoreCase);
            var operation = SetupOperationSelection.Resolve(removing,
                Version.Parse(session["ProductVersion"]),
                ProductInstallation.GetRelatedProducts(session["UpgradeCode"])
                    .Where(product => product.IsInstalled).Select(product => product.ProductVersion));
            thread = new Thread(() => RunWindow(version, folder, operation, installed));
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            if (WaitHandle.WaitAny(new WaitHandle[] { start, exit }) == 1)
            {
                thread.Join();
                start.Dispose();
                exit.Dispose();
                if (startupError != null) throw new InstallerException("The Operit2 setup interface could not start.", startupError);
                throw new InstallCanceledException();
            }
            session["INSTALLFOLDER"] = window.InstallFolder;
            diagnostics.Write("Selection", "Operation: " + window.Operation + "; install folder: " + window.InstallFolder);
            if (installed && window.Operation == SetupOperation.Repair)
            {
                session["REINSTALL"] = "ALL";
                session["REINSTALLMODE"] = "amus";
            }
            if (window.Operation == SetupOperation.Remove) session["REMOVE"] = "ALL";
            session["OPERIT_DELETE_USER_DATA"] = window.DeleteData ? "1" : "";
            session["OPERIT_CLEANUP_INTERACTIVE"] = window.DeleteData ? "1" : "";
            if (window.DeleteData) session["OPERIT_CLEANUP_PLAN"] = new JavaScriptSerializer().Serialize(window.DataCleanupPlan);
            internalUILevel = InstallUIOptions.Silent;
            return true;
        }

        /// <summary>Resolves an explicit destination, an existing installation, or the new-install default.</summary>
        private static string ResolveInstallationFolder(string requestedFolder)
        {
            if (!string.IsNullOrEmpty(requestedFolder)) return requestedFolder;
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Operit\Operit2\Installer"))
            {
                if (key == null)
                    return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Operit2");
                string installedFolder = key.GetValue("InstallLocation") as string;
                if (string.IsNullOrEmpty(installedFolder))
                    throw new InstallerException("Existing Operit2 installation information is incomplete: the install folder is missing.");
                return installedFolder;
            }
        }

        /// <summary>Creates the UI on its own STA thread and always releases initialization waiters.</summary>
        private void RunWindow(string version, string folder, SetupOperation operation, bool installed)
        {
            try
            {
                window = new SetupWindow(version, folder, operation, installed, start, diagnostics);
                new Application { ShutdownMode = ShutdownMode.OnMainWindowClose }.Run(window);
            }
            catch (Exception error) { startupError = error; }
            finally { exit.Set(); }
        }

        /// <summary>Processes transaction messages without exposing the legacy Windows Installer dialogs.</summary>
        public MessageResult ProcessMessage(InstallMessage messageType, Record record,
            MessageButtons buttons, MessageIcon icon, MessageDefaultButton defaultButton)
        {
            if (record != null && (messageType == InstallMessage.Info || messageType == InstallMessage.Warning))
                diagnostics.Write(messageType.ToString(), record.ToString());
            if (messageType == InstallMessage.ActionStart && record != null)
            {
                actionData = false;
                string action = record.GetString(1);
                diagnostics.StartAction(action, record.FieldCount >= 2 ? record.GetString(2) : "");
                if (action == "OperitFinishRemoval" || action == "OperitRegisterShellEntries")
                {
                    finalizing = true;
                    window.Dispatcher.Invoke(new Action(window.BeginFinalization));
                }
                if (action == "OperitInstallCompleted") committed = true;
                string label = null;
                if (action == "OperitCloseApplications") label = "Closing running Operit2…";
                if (action == "OperitFinishRemoval") label = "Cleaning up app entries and selected user data…";
                if (action == "InstallFiles") label = "Installing application files…";
                if (action == "CreateShortcuts") label = "Creating shortcuts…";
                if (action == "RemoveFiles") label = "Removing application files…";
                if (action == "RegisterProduct") label = "Finishing installation…";
                UpdateProgress(label);
            }
            if (messageType == InstallMessage.Progress && record != null && record.FieldCount >= 2)
            {
                int type = record.GetInteger(1);
                if (type == 0 && record.FieldCount >= 4)
                {
                    phase++;
                    total = record.GetInteger(2);
                    backwards = record.GetInteger(3) != 0;
                    completed = backwards ? total : 0;
                    actionData = false;
                }
                if (type == 1 && record.FieldCount >= 3)
                {
                    step = record.GetInteger(2);
                    actionData = record.GetInteger(3) != 0;
                }
                if (type == 2) completed += (backwards ? -1L : 1L) * record.GetInteger(2);
                if (type == 3) total += record.GetInteger(2);
                UpdateProgress(null);
            }
            if (messageType == InstallMessage.ActionData && actionData)
            {
                completed += (backwards ? -1L : 1L) * step;
                UpdateProgress(null);
            }
            if (messageType == InstallMessage.Error || messageType == InstallMessage.FatalExit)
            {
                committed = false;
                int code = record != null && record.FieldCount >= 1 ? record.GetInteger(1) : 0;
                diagnostics.AddFailure(code, record == null ? "Windows Installer reported a fatal error but supplied no error record." : record.ToString());
                window.Dispatcher.Invoke(new Action(window.ShowFailure));
            }
            if (messageType == InstallMessage.FilesInUse || messageType == InstallMessage.RMFilesInUse)
            {
                committed = false;
                diagnostics.AddFailure(0, "Operit2 or a related file is in use. Close all Operit2 windows, tray icons, and background processes, then run setup again.\r\n"
                    + (record == null ? "Windows Installer did not provide details about the holding process." : record.ToString()), filesInUse: true);
                window.Dispatcher.Invoke(new Action(window.ShowFailure));
                return MessageResult.Cancel;
            }
            bool canceled = (bool)window.Dispatcher.Invoke(new Func<bool>(() => window.CancelRequested));
            return canceled && !committed && !finalizing ? MessageResult.Cancel : MessageResult.OK;
        }

        /// <summary>Converts MSI planning and execution ticks into a stable visible progress value.</summary>
        private void UpdateProgress(string action)
        {
            double value = total > 0 ? Math.Max(0, Math.Min(1, (double)completed / total)) : 0;
            value = phase <= 1 ? value * 10 : 10 + value * 89;
            displayedProgress = Math.Max(displayedProgress, value);
            window.Dispatcher.Invoke(new Action(() => window.ReportProgress(displayedProgress, action)));
        }

        /// <summary>Displays the final transaction result and waits until the user closes the window.</summary>
        public void Shutdown()
        {
            bool success = committed && !diagnostics.HasErrors;
            diagnostics.Write("Result", success ? "Operation completed" : "Operation did not complete; user requested cancel: " + window.CancelRequested);
            window.Dispatcher.BeginInvoke(new Action(() => window.Complete(success)));
            thread.Join();
            start.Dispose();
            exit.Dispose();
        }
    }
}
