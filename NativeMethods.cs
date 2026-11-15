using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
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
        private static extern bool IsWindow(IntPtr hWnd);

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
        /// Activate existing instance (tray/pin second launch). Same strategy as the client:
        /// find HWND, send WM_SHOWME, restore — return true if HWND exists (do not fail on
        /// SendMessageTimeout==0, or Program.Main will kill the tray instance).
        /// </summary>
        public static bool TryActivateOtherInstance()
        {
            try
            {
                var h = FindOtherInstanceHwnd();
                if (h == IntPtr.Zero || !IsWindow(h))
                    return false;

                // Prefer titled main window — reject untitled junk HWNDs (white square).
                var len = GetWindowTextLength(h);
                if (len > 0)
                {
                    var sb = new System.Text.StringBuilder(len + 1);
                    GetWindowText(h, sb, sb.Capacity);
                    if (!IsMainWindowTitle(sb.ToString()))
                        return false;
                }
                else
                {
                    return false;
                }

                IntPtr result;
                SendMessageTimeout(
                    h, Program.WmShowMe, IntPtr.Zero, IntPtr.Zero,
                    SmtoAbortIfHung | SmtoBlock, 1500, out result);

                ShowWindow(h, SwRestore);
                SetForegroundWindow(h);
                return true;
            }
            catch
            {
                return false;
            }
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

            IntPtr found = IntPtr.Zero;
            IntPtr titled = IntPtr.Zero;
            EnumWindows(delegate(IntPtr hwnd, IntPtr lParam)
            {
                uint pid;
                GetWindowThreadProcessId(hwnd, out pid);
                if ((int)pid != targetPid) return true;
                if (!IsWindow(hwnd)) return true;

                var len = GetWindowTextLength(hwnd);
                if (len <= 0) return true;
                var sb = new System.Text.StringBuilder(len + 1);
                GetWindowText(hwnd, sb, sb.Capacity);
                var title = sb.ToString();
                if (IsMainWindowTitle(title))
                {
                    titled = hwnd;
                    return false;
                }
                if (found == IntPtr.Zero)
                    found = hwnd;
                return true;
            }, IntPtr.Zero);

            // Prefer titled window; never return untitled junk for activation.
            if (titled != IntPtr.Zero) return titled;
            return IntPtr.Zero;
        }
    }
}
