using System;
using System.IO;
using Microsoft.Win32;

namespace KaiAosRenew
{
    /// <summary>
    /// Read/write Desktop KeyReader Plus path settings from
    /// HKCU\Software\BMW\ISPI\ISVM\DesktopKeyReaderPlus\Settings.
    /// </summary>
    internal static class DesktopKeyReaderReg
    {
        public const string SettingsKey =
            @"Software\BMW\ISPI\ISVM\DesktopKeyReaderPlus\Settings";

        public static string PublicRoot
        {
            get { return Path.Combine(Environment.GetEnvironmentVariable("PUBLIC") ?? @"C:\Users\Public"); }
        }

        public static string AppDataRoot
        {
            get { return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData); }
        }

        public sealed class Paths
        {
            public string DataBin { get; set; }
            public string LogPath { get; set; }
            public string EncPath { get; set; }
            /// <summary>0=Errors … 4=Detailed (KeyReader Plus).</summary>
            public int LogLevel { get; set; }
        }

        public static Paths ReadAbsolute()
        {
            var result = new Paths
            {
                DataBin = Path.Combine(PublicRoot, "keyreader", "keyreader.bin"),
                LogPath = Path.Combine(AppDataRoot, "keyreader", "LOG"),
                EncPath = Path.Combine(AppDataRoot, "keyreader", "ENC", "BMWKeyReader.enc"),
                LogLevel = 4
            };
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(SettingsKey, false))
                {
                    if (key == null) return result;
                    result.DataBin = ResolvePath(
                        Convert.ToString(key.GetValue("Data_Bin")),
                        PublicRoot,
                        result.DataBin);
                    result.LogPath = ResolvePath(
                        Convert.ToString(key.GetValue("LogPath")),
                        AppDataRoot,
                        result.LogPath);
                    result.EncPath = ResolvePath(
                        Convert.ToString(key.GetValue("PathToENCFile")),
                        AppDataRoot,
                        result.EncPath);
                    try
                    {
                        var lv = key.GetValue("LogLevel");
                        if (lv != null)
                        {
                            var n = Convert.ToInt32(lv);
                            if (n < 0) n = 0;
                            if (n > 4) n = 4;
                            result.LogLevel = n;
                        }
                    }
                    catch { }
                }
            }
            catch { }
            return result;
        }

        public static void WriteAbsolute(Paths paths)
        {
            if (paths == null) return;
            using (var key = Registry.CurrentUser.CreateSubKey(SettingsKey))
            {
                if (key == null) return;
                // Keep EnvVar as KeyReader expects.
                if (key.GetValue("EnvVar") == null)
                    key.SetValue("EnvVar", "AppData", RegistryValueKind.String);

                key.SetValue("Data_Bin",
                    ToStored(paths.DataBin, PublicRoot),
                    RegistryValueKind.String);
                key.SetValue("LogPath",
                    ToStored(paths.LogPath, AppDataRoot),
                    RegistryValueKind.String);
                key.SetValue("PathToENCFile",
                    ToStored(paths.EncPath, AppDataRoot),
                    RegistryValueKind.String);
                var level = paths.LogLevel;
                if (level < 0) level = 0;
                if (level > 4) level = 4;
                key.SetValue("LogLevel", level, RegistryValueKind.DWord);
            }
        }

        private static string ResolvePath(string stored, string root, string fallback)
        {
            if (string.IsNullOrWhiteSpace(stored))
                return fallback;
            stored = stored.Trim();
            // Registry stores "\keyreader\…" — Path.IsPathRooted is true for leading '\',
            // but these are still relative to Public/AppData, not drive-root absolute.
            var looksDriveAbsolute = stored.Length >= 2 && stored[1] == ':';
            if (looksDriveAbsolute)
                return stored;
            var rel = stored.TrimStart('\\', '/').Replace('/', '\\');
            try
            {
                return Path.GetFullPath(Path.Combine(root, rel));
            }
            catch
            {
                return fallback;
            }
        }

        private static string ToStored(string absolute, string preferredRoot)
        {
            if (string.IsNullOrWhiteSpace(absolute))
                return absolute ?? "";
            absolute = absolute.Trim();
            try
            {
                var full = Path.GetFullPath(absolute);
                var root = Path.GetFullPath(preferredRoot)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;
                if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    var rel = full.Substring(root.Length);
                    return "\\" + rel.Replace('/', '\\');
                }
            }
            catch { }
            // Outside usual root — store absolute so the value is not lost.
            return absolute;
        }
    }
}
