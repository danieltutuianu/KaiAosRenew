using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace KaiAosRenew
{
    /// <summary>
    /// Chromium cookie/password crypto: DPAPI master key + AES-256-GCM (v10/v11).
    /// App-Bound Encryption (v20 / APPB) is not supported.
    /// </summary>
    internal static class ChromiumCrypto
    {
        public enum DecryptStatus
        {
            Ok,
            AppBoundEncrypted,
            Failed
        }

        public static DecryptStatus TryGetMasterKey(string userDataDir, out byte[] masterKey, out string error)
        {
            masterKey = null;
            error = null;
            try
            {
                var localState = Path.Combine(userDataDir, "Local State");
                if (!File.Exists(localState))
                {
                    error = "Local State missing.";
                    return DecryptStatus.Failed;
                }
                var json = File.ReadAllText(localState);
                var m = Regex.Match(json, "\"encrypted_key\"\\s*:\\s*\"([^\"]+)\"");
                if (!m.Success)
                {
                    error = "encrypted_key not found.";
                    return DecryptStatus.Failed;
                }
                var raw = Convert.FromBase64String(m.Groups[1].Value);
                if (raw.Length < 5)
                {
                    error = "encrypted_key too short.";
                    return DecryptStatus.Failed;
                }
                // APPB = App-Bound Encryption (Chrome/Edge v20+)
                if (raw[0] == (byte)'A' && raw[1] == (byte)'P' && raw[2] == (byte)'P' && raw[3] == (byte)'B')
                {
                    error = "App-Bound Encryption (v20).";
                    return DecryptStatus.AppBoundEncrypted;
                }
                // DPAPI prefix
                byte[] protectedKey;
                if (raw[0] == (byte)'D' && raw[1] == (byte)'P' && raw[2] == (byte)'A' && raw[3] == (byte)'P' && raw[4] == (byte)'I')
                {
                    protectedKey = new byte[raw.Length - 5];
                    Buffer.BlockCopy(raw, 5, protectedKey, 0, protectedKey.Length);
                }
                else
                {
                    protectedKey = raw;
                }
                masterKey = ProtectedData.Unprotect(protectedKey, null, DataProtectionScope.CurrentUser);
                return DecryptStatus.Ok;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return DecryptStatus.Failed;
            }
        }

        public static DecryptStatus TryDecryptPassword(byte[] masterKey, byte[] encrypted, out string plain, out string error)
        {
            plain = null;
            error = null;
            if (encrypted == null || encrypted.Length < 3 + 12 + 16)
            {
                error = "Password blob too short.";
                return DecryptStatus.Failed;
            }
            var prefix = Encoding.ASCII.GetString(encrypted, 0, 3);
            if (prefix == "v20")
            {
                error = "App-Bound Encryption (v20).";
                return DecryptStatus.AppBoundEncrypted;
            }
            if (prefix != "v10" && prefix != "v11")
            {
                error = "Unknown password prefix: " + prefix;
                return DecryptStatus.Failed;
            }
            if (masterKey == null || masterKey.Length == 0)
            {
                error = "Master key missing.";
                return DecryptStatus.Failed;
            }

            var nonce = new byte[12];
            Buffer.BlockCopy(encrypted, 3, nonce, 0, 12);
            var cipherAndTagLen = encrypted.Length - 3 - 12;
            var cipherAndTag = new byte[cipherAndTagLen];
            Buffer.BlockCopy(encrypted, 3 + 12, cipherAndTag, 0, cipherAndTagLen);

            try
            {
                var decrypted = AesGcmDecrypt(masterKey, nonce, cipherAndTag);
                plain = Encoding.UTF8.GetString(decrypted);
                return DecryptStatus.Ok;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return DecryptStatus.Failed;
            }
        }

        /// <summary>AES-256-GCM via Windows CNG (tag is last 16 bytes of cipherAndTag).</summary>
        private static byte[] AesGcmDecrypt(byte[] key, byte[] nonce, byte[] cipherAndTag)
        {
            const int tagLen = 16;
            if (cipherAndTag.Length < tagLen)
                throw new CryptographicException("Ciphertext too short for GCM tag.");

            var cipherLen = cipherAndTag.Length - tagLen;
            var cipher = new byte[cipherLen];
            var tag = new byte[tagLen];
            Buffer.BlockCopy(cipherAndTag, 0, cipher, 0, cipherLen);
            Buffer.BlockCopy(cipherAndTag, cipherLen, tag, 0, tagLen);

            IntPtr hAlg = IntPtr.Zero;
            IntPtr hKey = IntPtr.Zero;
            try
            {
                int status = BCryptOpenAlgorithmProvider(out hAlg, "AES", null, 0);
                if (status != 0) throw new CryptographicException("BCryptOpenAlgorithmProvider " + status);

                status = BCryptSetProperty(hAlg, "ChainingMode",
                    Encoding.Unicode.GetBytes("ChainingModeGCM\0"), Encoding.Unicode.GetBytes("ChainingModeGCM\0").Length, 0);
                if (status != 0) throw new CryptographicException("BCryptSetProperty " + status);

                status = BCryptGenerateSymmetricKey(hAlg, out hKey, IntPtr.Zero, 0, key, key.Length, 0);
                if (status != 0) throw new CryptographicException("BCryptGenerateSymmetricKey " + status);

                var authInfo = new BCRYPT_AUTHENTICATED_CIPHER_MODE_INFO();
                authInfo.cbSize = Marshal.SizeOf(typeof(BCRYPT_AUTHENTICATED_CIPHER_MODE_INFO));
                authInfo.dwInfoVersion = 1;
                authInfo.pbNonce = Marshal.AllocHGlobal(nonce.Length);
                Marshal.Copy(nonce, 0, authInfo.pbNonce, nonce.Length);
                authInfo.cbNonce = nonce.Length;
                authInfo.pbTag = Marshal.AllocHGlobal(tag.Length);
                Marshal.Copy(tag, 0, authInfo.pbTag, tag.Length);
                authInfo.cbTag = tag.Length;

                try
                {
                    var plain = new byte[cipherLen];
                    int plainLen = plain.Length;
                    var authInfoPtr = Marshal.AllocHGlobal(Marshal.SizeOf(authInfo));
                    try
                    {
                        Marshal.StructureToPtr(authInfo, authInfoPtr, false);
                        status = BCryptDecrypt(hKey, cipher, cipher.Length, authInfoPtr, null, 0,
                            plain, plain.Length, ref plainLen, 0);
                        if (status != 0)
                            throw new CryptographicException("BCryptDecrypt " + status);
                        if (plainLen != plain.Length)
                        {
                            var trimmed = new byte[plainLen];
                            Buffer.BlockCopy(plain, 0, trimmed, 0, plainLen);
                            return trimmed;
                        }
                        return plain;
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(authInfoPtr);
                    }
                }
                finally
                {
                    if (authInfo.pbNonce != IntPtr.Zero) Marshal.FreeHGlobal(authInfo.pbNonce);
                    if (authInfo.pbTag != IntPtr.Zero) Marshal.FreeHGlobal(authInfo.pbTag);
                }
            }
            finally
            {
                if (hKey != IntPtr.Zero) BCryptDestroyKey(hKey);
                if (hAlg != IntPtr.Zero) BCryptCloseAlgorithmProvider(hAlg, 0);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BCRYPT_AUTHENTICATED_CIPHER_MODE_INFO
        {
            public int cbSize;
            public int dwInfoVersion;
            public IntPtr pbNonce;
            public int cbNonce;
            public IntPtr pbAuthData;
            public int cbAuthData;
            public IntPtr pbTag;
            public int cbTag;
            public IntPtr pbMacContext;
            public int cbMacContext;
            public int cbAAD;
            public long cbData;
            public int dwFlags;
        }

        [DllImport("bcrypt.dll")]
        private static extern int BCryptOpenAlgorithmProvider(out IntPtr phAlgorithm, [MarshalAs(UnmanagedType.LPWStr)] string pszAlgId, [MarshalAs(UnmanagedType.LPWStr)] string pszImplementation, int dwFlags);

        [DllImport("bcrypt.dll")]
        private static extern int BCryptCloseAlgorithmProvider(IntPtr hAlgorithm, int dwFlags);

        [DllImport("bcrypt.dll", CharSet = CharSet.Unicode)]
        private static extern int BCryptSetProperty(IntPtr hObject, [MarshalAs(UnmanagedType.LPWStr)] string pszProperty, byte[] pbInput, int cbInput, int dwFlags);

        [DllImport("bcrypt.dll")]
        private static extern int BCryptGenerateSymmetricKey(IntPtr hAlgorithm, out IntPtr phKey, IntPtr pbKeyObject, int cbKeyObject, byte[] pbSecret, int cbSecret, int dwFlags);

        [DllImport("bcrypt.dll")]
        private static extern int BCryptDestroyKey(IntPtr hKey);

        [DllImport("bcrypt.dll")]
        private static extern int BCryptDecrypt(IntPtr hKey, byte[] pbInput, int cbInput, IntPtr pPaddingInfo, byte[] pbIV, int cbIV, byte[] pbOutput, int cbOutput, ref int pcbResult, int dwFlags);
    }
}
