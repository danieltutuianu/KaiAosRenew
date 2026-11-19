using System;
using System.IO;
using System.Net;

namespace BMW.ISPI.KAI.Decentral.KaiAdminLibData.Encryption
{
    public static class Encryption
    {
        public static string Decrypt(string cipher)
        {
            if (string.IsNullOrEmpty(cipher)) return "";
            if (cipher.StartsWith("ENC:", StringComparison.Ordinal))
                return cipher.Substring(4);
            return cipher;
        }
    }
}

namespace BMW.ISPI.KAI.Decentral.KaiAdminLibData.KaiZentral
{
    public sealed class KaiZentralUrls
    {
        public string WenAccessManagerAddress { get; set; }
        public string RegisterAddress { get; set; }
    }

    public sealed class KaiCentralCredentials
    {
        public string ClientId { get; set; }
        public string ClientSecret { get; set; }
        public string IdentificationHash { get; set; }
    }

    public static class KaiZentralUserRegProc
    {
        public static string WebEamUrlOverride;
        public static string RegisterUrlOverride;
        public static Func<Uri, string, string, WebProxy, KaiCentralCredentials> OnRegister;
        public static int RegisterCallCount;

        public static void Reset()
        {
            WebEamUrlOverride = "";
            RegisterUrlOverride = "https://mock.local/register";
            OnRegister = null;
            RegisterCallCount = 0;
        }

        public static KaiZentralUrls GetKaiZentralURLs(string configPath)
        {
            return new KaiZentralUrls
            {
                WenAccessManagerAddress = WebEamUrlOverride ?? "",
                RegisterAddress = RegisterUrlOverride ?? "https://mock.local/register"
            };
        }

        public static KaiCentralCredentials RegisterWithKaiZentral(
            Uri uri, string email, string password, WebProxy proxy)
        {
            RegisterCallCount++;
            if (OnRegister != null)
                return OnRegister(uri, email, password, proxy);
            return new KaiCentralCredentials
            {
                ClientId = "cid-default",
                ClientSecret = "csec-default",
                IdentificationHash = "hash-default"
            };
        }

        public static void SaveBdrRegistration(string lang, KaiCentralCredentials creds, string configPath)
        {
            Directory.CreateDirectory(configPath);
            var propsPath = Path.Combine(configPath, "KAI_Properties.cfg");
            File.WriteAllLines(propsPath, new[]
            {
                "version=1",
                "lang=" + (lang ?? "en"),
                "ENC:" + (creds != null ? (creds.ClientId ?? "") : ""),
                "ENC:" + (creds != null ? (creds.ClientSecret ?? "") : "")
            });
        }
    }
}

namespace KaiAosRenew
{
    /// <summary>Stub: ClientSettings.Normalize / AppSettings without WPF ThemeManager.</summary>
    internal static class ThemeManager
    {
        public static string NormalizeMode(string mode)
        {
            if (string.IsNullOrWhiteSpace(mode)) return "system";
            mode = mode.Trim().ToLowerInvariant();
            if (mode == "day" || mode == "night" || mode == "system")
                return mode;
            return "system";
        }
    }
}

namespace KaiAosRenew
{
    internal static class BmwHttpLog
    {
        public static void LogNon200(
            string operation, int status, string url,
            string requestDump, string responseBody, bool registerPath = false)
        {
        }

        public static void LogExchange(
            string operation, int status, string url,
            string requestDump, string responseBody, bool registerPath = false)
        {
        }

        public static string FormatRegisterRequest(string url, string email)
        {
            return "POST " + (url ?? "");
        }

        public static string FormatWebEamRequest(string url, string clientId)
        {
            return "POST " + (url ?? "");
        }
    }

    internal static class KaiDiag
    {
        public static WebProxy ResolveProxy(bool useSystemProxy)
        {
            return null;
        }
    }

    internal static class DesktopKeyReaderReg
    {
        public sealed class Paths
        {
            public string DataBin { get; set; }
            public string LogPath { get; set; }
            public string EncPath { get; set; }
        }

        public static Paths ReadAbsolute()
        {
            return new Paths { DataBin = "", LogPath = "", EncPath = "" };
        }
    }

    /// <summary>Minimal stub so KeyReaderLogTailer can compile in logic tests.</summary>
    internal static class UiStrings
    {
        public static string Language = "en";

        public static string T(string key)
        {
            if (string.Equals(key, "KeyReaderLogMissing", StringComparison.OrdinalIgnoreCase))
                return "KeyReader log missing: {0}";
            if (string.Equals(key, "KeyReaderBinUpdated", StringComparison.OrdinalIgnoreCase))
                return "keyreader.bin updated";
            return key ?? "";
        }
    }
}
