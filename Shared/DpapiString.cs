using System;
using System.Security.Cryptography;
using System.Text;

namespace KaiAosRenew
{
    /// <summary>DPAPI CurrentUser UTF-8 string protect/unprotect (shared Server + Client).</summary>
    public static class DpapiString
    {
        public static string Protect(string plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return "";
            var plain = Encoding.UTF8.GetBytes(plainText);
            try
            {
                var blob = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(blob);
            }
            finally
            {
                Array.Clear(plain, 0, plain.Length);
            }
        }

        public static string Unprotect(string base64)
        {
            if (string.IsNullOrEmpty(base64)) return "";
            try
            {
                var plain = ProtectedData.Unprotect(
                    Convert.FromBase64String(base64), null, DataProtectionScope.CurrentUser);
                try { return Encoding.UTF8.GetString(plain); }
                finally { Array.Clear(plain, 0, plain.Length); }
            }
            catch
            {
                return "";
            }
        }
    }
}
