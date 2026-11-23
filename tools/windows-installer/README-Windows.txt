Operit2 - Windows installation troubleshooting, uninstall, and user data

Read this first if installation fails
  The failure page shows what went wrong, the error code, and what to do next.
  The original error, the failing step, and full troubleshooting notes are in
  "Technical details"; expand it when needed.
  Click "Copy error details" to copy the full diagnostics; you can also select
  and copy the detail text directly.
  "View diagnostic log" and "View MSI log" open the files for this operation.
  "Open log folder" locates the diagnostic files, which are kept after the
  installer closes.
  Before and during installation you can also see the paths under
  "Logs and diagnostics".

Closing a running app automatically
  After you start install, update, repair, or uninstall, the installer closes
  Operit2 at this install location, including hidden windows, through Windows
  Restart Manager before checking and replacing files.
  It does not close copies in other folders by process name. Save your work first.
  Unattended installations perform this step too. If the app cannot be closed,
  the operation stops and asks you to exit from the tray or use Task Manager.
  When permanently deleting shared user data, copies of Operit2 in other folders
  must also exit before the safety check passes.

Where the logs are
  Operit2 setup diagnostics: %LOCALAPPDATA%\Operit\InstallerLogs\Operit2-setup-date-time-unique-id.log
  Press Win+R, paste %LOCALAPPDATA%\Operit\InstallerLogs, and press Enter to open it.
  Each operation creates its own file recording the version, package, install
  folder, steps, and original errors.
  Windows Installer verbose log: the MsiLogFileLocation path the system provides;
  the installer shows it directly.
  The MSI enables verbose logging by default; when you pass /L*V explicitly, that file is used.
  The diagnostics folder is separate from user data folders so you can keep using it
  after an uninstall or failure.
  Logs may contain usernames and local paths; review them before sending to the developer.

If the error occurs before the setup window appears, or you need a full log manually
  1. Press Win+R, type cmd, and press Enter.
  2. Enter the command below, replacing the package path with the full path to the
     downloaded MSI and keeping both quotes:
     msiexec.exe /i "D:\Downloads\Operit2.msi" /L*V "%USERPROFILE%\Desktop\Operit2-install.log"
  3. After one installation attempt, find Operit2-install.log on the desktop.
     If the desktop is redirected, change the log path to a full path you can write to.
  4. When reporting, include: the installer file name, the Windows version, the copied
     error details, and both log files.
     Unattended installations have no custom window, so also specify the log path with /L*V.

2502 / 2503: Windows Installer transaction state error
  2502: InstallFinalize was called without an active install transaction.
  2503: RunScript was called without being marked as installing.
  These codes alone do not prove that administrator rights are missing; an earlier
  error may be the real cause.
  1. Save the logs first, close other installations, restart Windows, and retry with
     the current account.
  2. Download the MSI to a local folder; use the default folder for a first install.
  3. Press Win+R -> %TEMP% and try creating and deleting a text file.
     If you cannot write, ask the administrator to check permissions on your temporary
     folder, and check free space on the system drive.
  4. Check Windows Security protection history or your organization's security software
     blocks. When the log clearly shows access denied, ask the administrator to check
     permissions on that path.
     Do not delete Windows\Installer, and do not add Everyone full control to system folders.
  5. Operit2 installs for the current user only by default. If administrator help is
     needed, provide the log first and let the administrator decide, so the app is not
     installed under a different account.

Other common errors
  1603: Summary error. Address the earlier specific error first; search the MSI log for
        Return value 3, review the error context before it, and provide it to the developer.
  1500 / 1618: Wait for other software installations and Windows updates, then retry.
  1307 / not enough disk space: Free space on the install drive and on the drive holding
        the system temporary folder, then retry.
  1303 / 1310 / 1402 / 1406: Check the specific file, folder, or registry permissions in
        the original error.
  1308 / 1311 / 1335 / 1619 / 1620: Download the complete MSI again from the official
        release page and open it locally.
  1601 / 1719: Windows Installer service unavailable; restart, and if it still fails ask
        the administrator to check the service and policy.
  1625: System policy blocks the installation; contact the computer administrator and
        provide the log.
  1633: Confirm the system is 64-bit Windows 10 version 1809 or later.
  File in use: Exit Operit2 windows, tray icons, and background processes, then run the MSI again.

MSI installation
  Uninstall Operit2 in Windows Settings -> Apps -> Installed apps.
  You can also use Operit2 Uninstall in the Start menu, or open the MSI again -> Uninstall.
  Uninstall keeps user data by default. The full uninstall screen lets you select
  "Also permanently delete local user data"; data cleanup runs only after you review the
  actual data folders and confirm.
  Uninstall entry points that show only the basic system UI keep your data; to clean data,
  use the full uninstall screen, or run Operit2DataCleanup.exe in this folder first and
  then uninstall.
  
Portable version
  Exit all Operit2 windows, background processes, and command lines first.
  Run Operit2DataCleanup.exe, review the actual folders, and confirm to permanently clean
  local user data.
  After cleanup you can delete the extracted program folder.
  If you need to delete the program first and clean later, copy Operit2DataCleanup.exe to
  another folder on its own.
  The tool does not depend on Flutter, Rust DLLs, or the MSI, but it needs the Windows
  built-in .NET Framework 4.7.2 or later.

Cleanup scope
  %APPDATA%\Operit2 (including the pinned startup configuration, default data, and workspaces)
  The custom runtimeRoot and workspaceRoot folders recorded in the startup configuration, with all their contents
  %LOCALAPPDATA%\app.operit\Operit2 (managed runtime)
  %LOCALAPPDATA%\Operit2 (crash reports)
  %LOCALAPPDATA%\webview_all_windows\operit2 (browser cache, cookies, and so on)
  App credentials beginning with Operit2: in Windows Credential Manager
  Cleanup covers every local identity; the install version, the portable version, and the
  command line may share this data.
  Export the chats, plugin data, and workspace files you want to keep first.
  Cleanup does not send delete instructions to other devices or the cloud.
  Clear errors are reported when a folder passes through a link, points to a protected
  location, has a broken configuration, or has files in use.
  File deletion cannot be undone; a partial failure says cleanup did not fully complete,
  so follow the message and reopen the tool.

Program entry points
  A normal MSI uninstall removes the desktop, Start menu, and operit2:// protocol entries
  that belong to the current installation copy.
  The standalone cleanup tool only handles data; it does not uninstall the MSI and does not
  remove programs or shortcuts of other copies.
  Deleting the program folder does not automatically delete user data stored in AppData or
  custom locations.

Unattended MSI
  Keep data (default): msiexec /x {ProductCode} /qn
  Explicitly request cleanup: msiexec /x {ProductCode} /qn OPERIT_DELETE_USER_DATA=1
  Run as the current user, close all Operit2 processes, and back up custom folders before running.
  Automatic upgrades, same-version reinstalls, and repairs never clean up user data.