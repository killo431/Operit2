using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Operit.Installer
{
    public static class InstallerApplications
    {
        /// <summary>Uses Windows Restart Manager to close processes holding this installation's executable before file validation.</summary>
        public static void Close(string installFolder, Action<string> log)
        {
            string executable = Path.Combine(UserDataCleanup.Normalize(installFolder), "operit2.exe");
            if (!File.Exists(executable)) return;
            var key = new StringBuilder(33);
            Check(RmStartSession(out uint session, 0, key));
            try
            {
                Check(RmRegisterResources(session, 1, new[] { executable }, 0, IntPtr.Zero, 0, null));
                log("Closing processes that hold this installation copy: " + executable);
                // RmForceShutdown lets Windows close applications that do not respond to session shutdown.
                Check(RmShutdown(session, 1, IntPtr.Zero));
                log("Windows Restart Manager finished closing the application.");
            }
            finally { RmEndSession(session); }
        }

        /// <summary>Reports system shutdown failures before the installer changes application files.</summary>
        private static void Check(uint result)
        {
            if (result != 0)
                throw new Win32Exception((int)result, "Cannot automatically close the running Operit2 (system error " + result
                    + "). Right-click the Operit2 icon in the notification area, choose Exit, and run setup again. If background processes remain, end Operit2 in Task Manager.");
        }

        /// <summary>Creates an isolated Restart Manager session.</summary>
        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        private static extern uint RmStartSession(out uint session, uint flags, StringBuilder key);

        /// <summary>Registers the exact executable resource rather than matching process names.</summary>
        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        private static extern uint RmRegisterResources(uint session, uint fileCount, string[] files,
            uint applicationCount, IntPtr applications, uint serviceCount, string[] services);

        /// <summary>Requests Windows session shutdown for applications using the registered resource.</summary>
        [DllImport("rstrtmgr.dll")]
        private static extern uint RmShutdown(uint session, uint flags, IntPtr callback);

        /// <summary>Releases the Restart Manager session without restarting applications during installation.</summary>
        [DllImport("rstrtmgr.dll")]
        private static extern uint RmEndSession(uint session);
    }
}
