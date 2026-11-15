using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace KaiAosRenew
{
    /// <summary>
    /// Creates/updates a Start Menu .lnk with AppUserModelID so the taskbar
    /// shows the friendly name instead of the .exe file name.
    /// </summary>
    internal static class StartMenuShortcut
    {
        public static void Ensure(string displayName, string aumid)
        {
            if (string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(aumid))
                return;
            try
            {
                var programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
                if (string.IsNullOrEmpty(programs)) return;
                Directory.CreateDirectory(programs);

                // Remove legacy shortcut names so only the friendly one remains.
                TryDelete(Path.Combine(programs, "KaiAosRenew.lnk"));
                TryDelete(Path.Combine(programs, "KaiAosNotifyClient.lnk"));
                TryDelete(Path.Combine(programs, "KAI AOS Renew.lnk"));

                var linkPath = Path.Combine(programs, displayName.Trim() + ".lnk");
                var exe = GetExePath();
                if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) return;

                if (File.Exists(linkPath))
                {
                    try
                    {
                        if (string.Equals(ReadTarget(linkPath), exe, StringComparison.OrdinalIgnoreCase))
                        {
                            WriteAppUserModelId(linkPath, aumid);
                            return;
                        }
                    }
                    catch { }
                }

                CreateShortcut(linkPath, exe, Path.GetDirectoryName(exe), displayName.Trim(), aumid);
            }
            catch { }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch { }
        }

        private static string GetExePath()
        {
            try
            {
                return System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
            }
            catch
            {
                return typeof(StartMenuShortcut).Assembly.Location;
            }
        }

        private static void CreateShortcut(string linkPath, string target, string workDir, string description, string aumid)
        {
            var link = (IShellLinkW)new CShellLink();
            try
            {
                link.SetPath(target);
                if (!string.IsNullOrEmpty(workDir))
                    link.SetWorkingDirectory(workDir);
                link.SetDescription(description ?? "");
                link.SetIconLocation(target, 0);

                var file = (IPersistFile)link;
                file.Save(linkPath, true);
            }
            finally
            {
                Marshal.FinalReleaseComObject(link);
            }
            WriteAppUserModelId(linkPath, aumid);
        }

        private static string ReadTarget(string linkPath)
        {
            var link = (IShellLinkW)new CShellLink();
            try
            {
                ((IPersistFile)link).Load(linkPath, 0);
                var sb = new StringBuilder(260);
                link.GetPath(sb, sb.Capacity, IntPtr.Zero, 0);
                return sb.ToString();
            }
            finally
            {
                Marshal.FinalReleaseComObject(link);
            }
        }

        private static void WriteAppUserModelId(string linkPath, string aumid)
        {
            var link = (IShellLinkW)new CShellLink();
            try
            {
                var file = (IPersistFile)link;
                file.Load(linkPath, 0);
                var propStore = (IPropertyStore)link;
                var pk = new PropertyKey(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);
                var pv = PropVariant.FromString(aumid);
                try
                {
                    propStore.SetValue(ref pk, ref pv);
                    propStore.Commit();
                    file.Save(linkPath, true);
                }
                finally
                {
                    pv.Clear();
                }
            }
            finally
            {
                Marshal.FinalReleaseComObject(link);
            }
        }

        [ComImport]
        [Guid("00021401-0000-0000-C000-000000000046")]
        private class CShellLink { }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct PropertyKey
        {
            public Guid fmtid;
            public uint pid;
            public PropertyKey(Guid fmtid, uint pid)
            {
                this.fmtid = fmtid;
                this.pid = pid;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PropVariant
        {
            public ushort vt;
            public ushort wReserved1;
            public ushort wReserved2;
            public ushort wReserved3;
            public IntPtr pointerValue;

            public static PropVariant FromString(string value)
            {
                return new PropVariant
                {
                    vt = 31, // VT_LPWSTR
                    pointerValue = Marshal.StringToCoTaskMemUni(value)
                };
            }

            public void Clear()
            {
                if (pointerValue != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(pointerValue);
                    pointerValue = IntPtr.Zero;
                }
                vt = 0;
            }
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
        private interface IPropertyStore
        {
            [PreserveSig] int GetCount(out uint cProps);
            [PreserveSig] int GetAt(uint iProp, out PropertyKey pkey);
            [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant pv);
            [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant pv);
            [PreserveSig] int Commit();
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("000214F9-0000-0000-C000-000000000046")]
        private interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, IntPtr pfd, uint fFlags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            void GetHotkey(out short pwHotkey);
            void SetHotkey(short wHotkey);
            void GetShowCmd(out int piShowCmd);
            void SetShowCmd(int iShowCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
            void Resolve(IntPtr hwnd, uint fFlags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }
    }
}
