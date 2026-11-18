using System;
using System.Security.Cryptography;
using System.Text;

namespace KaiAosRenew
{
    /// <summary>HMAC-SHA256 challenge-response helpers for LAN notify pairing.</summary>
    internal static class NotifyPairing
    {
        public static string GenerateCode()
        {
            // 8 chars, alphanumeric without ambiguous 0/O/1/I
            const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            var bytes = new byte[8];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(bytes);
            var sb = new StringBuilder(8);
            for (var i = 0; i < 8; i++)
                sb.Append(alphabet[bytes[i] % alphabet.Length]);
            return sb.ToString();
        }

        public static string CreateNonce()
        {
            var bytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(bytes);
            return Convert.ToBase64String(bytes);
        }

        public static string ComputeResponse(string pairingCode, string nonceBase64)
        {
            var key = Encoding.UTF8.GetBytes(pairingCode ?? "");
            var data = Encoding.UTF8.GetBytes(nonceBase64 ?? "");
            using (var hmac = new HMACSHA256(key))
                return Convert.ToBase64String(hmac.ComputeHash(data));
        }

        public static bool VerifyResponse(string pairingCode, string nonceBase64, string responseBase64)
        {
            if (string.IsNullOrEmpty(responseBase64)) return false;
            var expected = ComputeResponse(pairingCode, nonceBase64);
            return FixedTimeEquals(expected, responseBase64.Trim());
        }

        private static bool FixedTimeEquals(string a, string b)
        {
            if (a == null || b == null) return false;
            var ba = Encoding.UTF8.GetBytes(a);
            var bb = Encoding.UTF8.GetBytes(b);
            if (ba.Length != bb.Length)
            {
                // Still walk to avoid leaking length via early return timing as much as possible.
                var dummy = 0;
                for (var i = 0; i < ba.Length; i++)
                    dummy |= ba[i] ^ (i < bb.Length ? bb[i] : 0);
                return dummy == int.MinValue; // always false
            }
            var diff = 0;
            for (var i = 0; i < ba.Length; i++)
                diff |= ba[i] ^ bb[i];
            return diff == 0;
        }
    }
}
