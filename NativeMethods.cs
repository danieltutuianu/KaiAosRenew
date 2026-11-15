using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;

namespace KaiAosRenew
{
    internal static class NativeMethods
    {
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessageTimeout(
            IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam,
            uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetParent(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int SetCurrentProcessExplicitAppUserModelID(string appID);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool OpenClipboard(IntPtr hWndNewOwner);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool CloseClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool EmptyClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalLock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalUnlock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalFree(IntPtr hMem);

        private const int SwRestore = 9;
        private const uint SmtoAbortIfHung = 0x0002;
        private const uint SmtoBlock = 0x0001;
        private const uint CfUnicodeText = 13;
        private const uint GmemMoveable = 0x0002;

        /// <summary>
        /// Direct Win32 clipboard write (CF_UNICODETEXT) — avoids OLE/COM STA message pumping
        /// and rdpclip.exe / clipboard-viewer stalls that can freeze WPF's Clipboard.SetText/SetDataObject.
        /// </summary>
        public static bool TrySetClipboardText(string text)
        {
            if (text == null) return false;
            var opened = false;
            for (var attempt = 0; attempt < 3; attempt++)
            {
                if (OpenClipboard(IntPtr.Zero))
                {
                    opened = true;
                    break;
                }
                System.Threading.Thread.Sleep(15);
            }
            if (!opened) return false;

            var hGlobal = IntPtr.Zero;
            try
            {
                EmptyClipboard();
                var bytes = (text.Length + 1) * 2;
                hGlobal = GlobalAlloc(GmemMoveable, (UIntPtr)bytes);
                if (hGlobal == IntPtr.Zero) return false;
                var target = GlobalLock(hGlobal);
                if (target == IntPtr.Zero) return false;
                try
                {
                    Marshal.Copy(text.ToCharArray(), 0, target, text.Length);
                    Marshal.WriteInt16(target, text.Length * 2, 0);
                }
                finally
                {
                    GlobalUnlock(hGlobal);
                }
                if (SetClipboardData(CfUnicodeText, hGlobal) != IntPtr.Zero)
                {
                    // System now owns hGlobal; must not free it.
                    hGlobal = IntPtr.Zero;
                    return true;
                }
                return false;
            }
            finally
            {
                if (hGlobal != IntPtr.Zero)
                    GlobalFree(hGlobal);
                CloseClipboard();
            }
        }

        public static void SetAppUserModelId(string appId)
        {
            try
            {
                SetCurrentProcessExplicitAppUserModelID(appId);
            }
            catch { }
        }

        /// <summary>
        /// Re-enable Windows toast/balloon notifications for this user when disabled
        /// (otherwise NotifyIcon.ShowBalloonTip is silently ignored).
        /// </summary>
        public static void EnsureTrayNotificationsEnabled()
        {
            try
            {
                using (var push = Registry.CurrentUser.CreateSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\PushNotifications"))
                {
                    if (push != null)
                        push.SetValue("ToastEnabled", 1, RegistryValueKind.DWord);
                }
                using (var adv = Registry.CurrentUser.CreateSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced"))
                {
                    if (adv != null)
                        adv.SetValue("EnableBalloonTips", 1, RegistryValueKind.DWord);
                }
            }
            catch { }
        }

        /// <summary>
        /// True for main window titles like "KAI - AOS Renew" / "KAI AOS Renew"
        /// (hyphen-safe — IndexOf("KAI AOS") fails on "KAI - AOS").
        /// </summary>
        private static bool IsMainWindowTitle(string title)
        {
            if (string.IsNullOrEmpty(title)) return false;
            if (string.Equals(title, "KAI - AOS Renew", StringComparison.OrdinalIgnoreCase))
                return true;
            var compact = title.Replace(" ", "").Replace("-", "");
            return compact.IndexOf("KAIAOS", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Activate existing instance (tray/pin second launch).
        /// 1) Signal named EventWaitHandle (works even when HWND is hidden / hard to find).
        /// 2) Find HWND and PostMessage WM_SHOWME + ShowWindow.
        /// Returns true if another process exists or HWND/event signaling succeeded.
        /// </summary>
        public static bool TryActivateOtherInstance()
        {
            var signaled = SignalShowWindowEvent();
            var otherAlive = OtherKaiAosRenewProcessExists();

            try
            {
                var h = FindOtherInstanceHwnd();
                if (h != IntPtr.Zero && IsWindow(h))
                {
                    // Async — do not block if the UI thread is busy; ShowFromTray still runs via event.
                    PostMessage(h, Program.WmShowMe, IntPtr.Zero, IntPtr.Zero);
                    ShowWindow(h, SwRestore);
                    SetForegroundWindow(h);
                    return true;
                }
            }
            catch { }

            return signaled || otherAlive;
        }

        public static bool SignalShowWindowEvent()
        {
            try
            {
                using (var ev = EventWaitHandle.OpenExisting(Program.ShowWindowEventName))
                {
                    ev.Set();
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        public static bool OtherKaiAosRenewProcessExists()
        {
            try
            {
                var current = Process.GetCurrentProcess();
                foreach (var p in Process.GetProcessesByName(current.ProcessName))
                {
                    try
                    {
                        if (p.Id != current.Id)
                            return true;
                    }
                    catch { }
                    finally
                    {
                        try { p.Dispose(); }
                        catch { }
                    }
                }
            }
            catch { }
            return false;
        }

        public static void PostShowMessageToOtherInstance()
        {
            TryActivateOtherInstance();
        }

        /// <summary>Kill other KaiAosRenew processes (zombie after bad close).</summary>
        public static void TerminateOtherKaiAosRenewProcesses()
        {
            try
            {
                var current = Process.GetCurrentProcess();
                foreach (var p in Process.GetProcessesByName(current.ProcessName))
                {
                    try
                    {
                        if (p.Id == current.Id) continue;
                        p.Kill();
                        p.WaitForExit(3000);
                    }
                    catch { }
                    finally
                    {
                        try { p.Dispose(); }
                        catch { }
                    }
                }
            }
            catch { }
        }

        private static IntPtr FindOtherInstanceHwnd()
        {
            var h = FindWindow(null, "KAI - AOS Renew");
            if (h != IntPtr.Zero && IsWindow(h)) return h;

            var current = Process.GetCurrentProcess();
            var targetPid = 0;
            foreach (var p in Process.GetProcessesByName(current.ProcessName))
            {
                try
                {
                    if (p.Id == current.Id) continue;
                    targetPid = p.Id;
                    // MainWindowHandle is often Zero while the app is Hide()'d in the tray.
                    if (p.MainWindowHandle != IntPtr.Zero && IsWindow(p.MainWindowHandle))
                        return p.MainWindowHandle;
                    break;
                }
                finally
                {
                    p.Dispose();
                }
            }
            if (targetPid == 0) return IntPtr.Zero;

            IntPtr titled = IntPtr.Zero;
            IntPtr anyTop = IntPtr.Zero;
            EnumWindows(delegate(IntPtr hwnd, IntPtr lParam)
            {
                uint pid;
                GetWindowThreadProcessId(hwnd, out pid);
                if ((int)pid != targetPid) return true;
                if (!IsWindow(hwnd)) return true;
                // Skip child windows; owned top-level (ShowInTaskbar=false) still has GetParent==0.
                if (GetParent(hwnd) != IntPtr.Zero) return true;

                var len = GetWindowTextLength(hwnd);
                if (len > 0)
                {
                    var sb = new System.Text.StringBuilder(len + 1);
                    GetWindowText(hwnd, sb, sb.Capacity);
                    if (IsMainWindowTitle(sb.ToString()))
                    {
                        titled = hwnd;
                        return false;
                    }
                }
                if (anyTop == IntPtr.Zero)
                    anyTop = hwnd;
                return true;
            }, IntPtr.Zero);

            if (titled != IntPtr.Zero) return titled;
            return anyTop;
        }
    }
}
