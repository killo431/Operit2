using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Operit.Installer
{
    public sealed class SetupProblem
    {
        public string Title { get; set; }
        public string Reason { get; set; }
        public string Steps { get; set; }
    }

    public sealed class SetupDiagnostics
    {
        private readonly List<string> failures = new List<string>();
        private readonly List<int> codes = new List<int>();
        private readonly object sync = new object();
        private int firstCode;
        private string firstAction;
        private bool firstFilesInUse;
        public string LogPath { get; }
        public string MsiLogPath { get; }
        public string PackagePath { get; }
        public string Version { get; }
        public string CurrentAction { get; private set; } = "Initializing the setup interface";
        public string LoggingError { get; private set; }
        public bool HasErrors { get { lock (sync) return failures.Count != 0; } }

        /// <summary>Creates a unique diagnostic file outside application and user-data removal directories.</summary>
        public SetupDiagnostics(string directory, string version, string packagePath, string msiLogPath)
        {
            Version = version;
            PackagePath = packagePath;
            MsiLogPath = msiLogPath;
            Directory.CreateDirectory(directory);
            LogPath = Path.Combine(directory, "Operit2-setup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss")
                + "-" + Guid.NewGuid().ToString("N") + ".log");
            File.WriteAllText(LogPath, "Operit2 setup diagnostics\r\nVersion: " + version + "\r\nPackage: " + packagePath
                + "\r\nWindows: " + Environment.OSVersion + "\r\nProcess bitness: " + (Environment.Is64BitProcess ? "64" : "32")
                + "\r\nTEMP: " + Path.GetTempPath() + "\r\nMSI verbose log: " + msiLogPath + "\r\n", new UTF8Encoding(true));
        }

        /// <summary>Records the exact MSI action before its messages arrive.</summary>
        public void StartAction(string action, string description)
        {
            lock (sync)
            {
                CurrentAction = action;
                Write("ActionStart", action + " — " + description);
            }
        }

        /// <summary>Preserves every failure so later transaction errors cannot overwrite the original cause.</summary>
        public void AddFailure(int code, string message, bool filesInUse = false)
        {
            lock (sync)
            {
                if (failures.Count == 0)
                {
                    firstCode = code;
                    firstAction = CurrentAction;
                    firstFilesInUse = filesInUse;
                }
                if (code > 0 && !codes.Contains(code)) codes.Add(code);
                string failure = (code > 0 ? "Error code: " + code + "\r\n" : "")
                    + "Step: " + CurrentAction + "\r\nOriginal error: " + message;
                failures.Add(failure);
                Write("Error", failure);
            }
        }

        /// <summary>Shows only a concrete explanation and the next useful actions on the default error screen.</summary>
        public SetupProblem Problem()
        {
            lock (sync)
            {
                string title = "Setup failed";
                string reason = "Setup could not complete the operation.";
                string steps = "Click \"Copy error details\" and send the information and this log to the developer.";
                if (firstAction == "OperitCloseApplications" || firstFilesInUse)
                {
                    title = "Operit2 is still running";
                    reason = "It could not be closed automatically, so the files in use cannot be replaced.";
                    steps = "1. Right-click the Operit2 icon in the notification area and choose Exit.\n2. If it still will not exit, end Operit2 in Task Manager, then run setup again.";
                }
                else switch (firstCode)
                {
                    case 2502:
                    case 2503:
                        title = "Windows Installer service error";
                        reason = firstCode == 2502 ? "Windows Installer failed during finalization." : "Windows Installer could not run the installation steps.";
                        steps = "1. Restart the computer, then open the installer again.\n2. If it still fails, click \"Copy error details\" and send the information and this log to the developer.";
                        break;
                    case 5: case 1303: case 1310: case 1402: case 1406: case 1925:
                        title = "No permission to write the installation files";
                        reason = "Windows denied access to a folder, file, or registry key required for installation.";
                        steps = "1. Use the default install location for a first install.\n2. If it still fails, give the log to the computer administrator to fix the exact location that was denied.";
                        break;
                    case 112: case 1307:
                        title = "Not enough disk space";
                        reason = "The install folder or the system temporary folder is on a disk without enough free space.";
                        steps = "Free up space on the system drive and the install drive, then install again.";
                        break;
                    case 1308: case 1311: case 1335: case 1619: case 1620:
                        title = "The installer cannot be read";
                        reason = "The installation file may not have downloaded completely, or it was damaged or quarantined.";
                        steps = "Download the MSI again from the official release page, save it to a local folder, and open it.";
                        break;
                    case 1500: case 1618:
                        title = "Another installation is in progress";
                        reason = "Windows is installing other software or updating the system.";
                        steps = "Wait for the other installation and Windows updates to finish, then retry; restart the computer if the message persists.";
                        break;
                    case 1601: case 1719:
                        title = "Windows Installer service unavailable";
                        reason = "The system cannot start or access the Windows Installer service.";
                        steps = "Restart the computer first; if it still fails, ask the administrator to check whether the Windows Installer service is disabled.";
                        break;
                    case 1625:
                        title = "Security policy blocks installation";
                        reason = "This installation was blocked by Windows or by an organizational policy.";
                        steps = "Give the error information and log to the computer administrator.";
                        break;
                    case 1633:
                        title = "The installer does not support this system";
                        reason = "This installer requires 64-bit Windows 10 version 1809 or later.";
                        steps = "Check the Windows version and use the installer that matches the system.";
                        break;
                    case 1612: case 1706:
                        title = "The original installer cannot be found";
                        reason = "Repair, upgrade, or uninstall needs the MSI from the installed version.";
                        steps = "Download the official MSI for the currently installed version and select it when the system asks.";
                        break;
                    case 1638:
                        title = "Another version is already installed";
                        reason = "This PC already has an Operit2 version that conflicts with this installer.";
                        steps = "Check the Operit2 version in Windows Settings > Apps, and download the same or a newer installer.";
                        break;
                }
                if (firstCode > 0) reason += " (error " + firstCode + ")";
                return new SetupProblem { Title = title, Reason = reason, Steps = steps };
            }
        }

        /// <summary>Flushes each diagnostic entry and exposes any write failure to the user.</summary>
        public void Write(string kind, string message)
        {
            lock (sync)
            {
                try
                {
                    File.AppendAllText(LogPath, DateTime.Now.ToString("O") + " [" + kind + "] " + message + "\r\n", Encoding.UTF8);
                }
                catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
                {
                    LoggingError = "Failed to write the diagnostic log: " + LogPath + "\r\n" + error.Message
                        + "\r\nUse \"Copy error details\" to save the current information, and check free disk space and write permissions for that folder.";
                }
            }
        }

        /// <summary>Builds a selectable support report with instructions, original failures, and real log locations.</summary>
        public string Describe()
        {
            lock (sync) return DescribeReport();
        }

        /// <summary>Formats a consistent snapshot while the diagnostic state is locked.</summary>
        private string DescribeReport()
        {
            var text = new StringBuilder("Operit2 setup diagnostics\r\nVersion: " + Version + "\r\nPackage: " + PackagePath + "\r\n\r\n");
            if (HasErrors)
            {
                text.AppendLine("Detected error codes: " + (codes.Count == 0 ? "Windows Installer provided no numeric error code" : string.Join(", ", codes)));
                text.AppendLine("\r\nFirst error\r\n" + failures[0]);
                foreach (var group in codes.GroupBy(Guidance))
                    text.AppendLine("\r\nSuggested action (" + string.Join(" / ", group) + ")\r\n" + group.Key);
                if (codes.Count == 0) text.AppendLine("Check file locks, the install folder, and system permissions using the original error below; if it cannot be resolved, provide this page and the logs.");
                text.AppendLine("\r\nOriginal errors (kept in order of occurrence)\r\n" + string.Join("\r\n\r\n", failures));
            }
            else text.AppendLine("No specific Windows Installer error has been received yet. Current step: " + CurrentAction);
            text.AppendLine("\r\nDiagnostic log: " + LogPath);
            text.AppendLine("MSI verbose log: " + (string.IsNullOrWhiteSpace(MsiLogPath)
                ? "Windows Installer did not provide a log path. Generate a verbose log using the command below." : MsiLogPath));
            if (LoggingError != null) text.AppendLine("\r\n" + LoggingError);
            text.AppendLine("\r\nWhen reporting: click \"Copy error details\" and attach the diagnostic log and the MSI verbose log. You can review usernames and local paths before sending.");
            text.AppendLine("\r\nIf the error occurs before the setup window appears, or you need a fresh full log:\r\n"
                + "1. Press Win+R, type cmd, and press Enter.\r\n2. Enter the command below (replace the path in quotes with the actual downloaded MSI path):\r\n"
                + "msiexec.exe /i \"" + PackagePath + "\" /L*V \"%USERPROFILE%\\Desktop\\Operit2-install.log\"\r\n"
                + "3. Follow the setup UI. When finished, find Operit2-install.log on the desktop; if the desktop is redirected, change the log argument to a full path you can write to.");
            return text.ToString();
        }

        /// <summary>Maps documented MSI codes to explanations without inferring causes from message text.</summary>
        public static string Guidance(int code)
        {
            switch (code)
            {
                case 2502:
                case 2503:
                    return "The Windows Installer transaction state is abnormal. 2502 means InstallFinalize was called without an active install transaction; 2503 means RunScript was called without being marked as installing.\r\n"
                        + "This code alone cannot confirm the root cause; also review the errors that occurred before it and the MSI verbose log.\r\n"
                        + "1. Save this page and the logs, close other installers, restart Windows, and retry with the current signed-in account.\r\n"
                        + "2. Download the MSI to a local folder and open it; use the default install location for a first install, and do not run it from an archive, network share, or a folder without write permission.\r\n"
                        + "3. Press Win+R, type %TEMP%, and try creating and deleting a text file. If access is denied, have the administrator check permissions on that user temporary folder, and check free disk space.\r\n"
                        + "4. Check Windows Security protection history or your organization's security software blocks. Only ask the administrator to inspect the specific folder the log shows as denied (including Windows\\Temp or Windows\\Installer) once the log confirms a permission problem. Do not delete Windows\\Installer, and do not grant Everyone full control.\r\n"
                        + "5. Operit2 installs for the current user only by default. If administrator help is needed, provide the log and let the administrator decide how to proceed, so the app is not installed under a different account.";
                case 5:
                case 1303:
                case 1310:
                case 1402:
                case 1406:
                case 1925:
                    return "A required folder, file, or registry key cannot be accessed.\r\n1. Find the exact denied path or registry key in the original error.\r\n2. Use the default current-user folder for a first install, and confirm that folder is writable.\r\n3. Check security software blocks; on a company computer, ask the administrator to correct permissions based on the log rather than changing permissions for an entire system folder.";
                case 112:
                case 1307:
                    return "Not enough disk space.\r\n1. Check free space on the install folder and the system drive; MSI extraction also uses the system temporary folder.\r\n2. Use Storage cleanup in Windows Settings to free space, then run the installer again.";
                case 1308:
                case 1311:
                case 1335:
                case 1619:
                case 1620:
                    return "The installer cannot be read, is incomplete, or is corrupted.\r\n1. Delete this incomplete download and download it again from the official Operit2 release page.\r\n2. Save it to a local folder and run it there, and check whether security software quarantined the file.\r\n3. If it fails again, provide the installer file name, download source, and log.";
                case 1500:
                case 1618:
                    return "Another Windows installation task is running.\r\n1. Wait for other software installations or Windows updates to finish.\r\n2. Close other setup windows and retry; restart Windows if the message persists. Do not forcefully stop an installer that is updating the system.";
                case 1601:
                case 1719:
                    return "The Windows Installer service cannot be accessed.\r\n1. Restart Windows and retry.\r\n2. Press Win+R, type services.msc, and check whether the Windows Installer service is disabled; ask the administrator to review the service and system policy.";
                case 1602:
                    return "The installation was canceled. Open the MSI again to retry; if you did not cancel it, review the specific error and log above.";
                case 1603:
                    return "Windows Installer reported a failure (summary error). This code alone does not indicate the root cause.\r\n1. First address the other error codes and original errors on this page.\r\n2. Open the MSI verbose log, find Return value 3, and keep the error context preceding it.\r\n3. Provide this page and the log to the developer; there is no need to repeat the same installation.";
                case 1638:
                    return "A conflicting version already exists.\r\nCheck the Operit2 version in Windows Settings > Apps > Installed apps, and download the same or a newer installer; back up and keep user data before removing the existing version.";
                case 1625:
                    return "The installation is blocked by system policy. Contact the computer administrator and provide this error and the log; the administrator needs to review software installation restrictions or organizational security policy.";
                case 1633:
                    return "This installer does not support the current system. Confirm that you are using 64-bit Windows 10 version 1809 or later, and download the installer that matches the system.";
                case 1612:
                case 1706:
                    return "The original installation source required for repair, upgrade, or uninstall is unavailable. Download the official MSI for the currently installed version and specify it when the system asks; if it still fails, provide the log and do not manually delete the Windows Installer cache or product registration.";
                case 1721:
                case 1722:
                case 1723:
                    return "A custom action required by the installation failed. Review the action name in the original error and that action's exception in the log; close all Operit2 windows and background processes and retry, and if it still fails, send the log to the developer.";
                default:
                    return "There is no specific guidance for this error yet. Keep the original error and the failing step below, open the MSI verbose log to review the context, and provide this page and the log to the developer.";
            }
        }
    }
}
