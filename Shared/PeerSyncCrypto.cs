using System;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace KaiAosRenew
{
    /// <summary>AES-256-CBC + HMAC-SHA256 (encrypt-then-MAC) for peer credential sync.</summary>
    public static class PeerSyncCrypto
    {
        public const string AuthMessage = "kai-aos-peer-v1";
        private const int IvBytes = 16;
        private const int KeyBytes = 32;
        private const int MacBytes = 32;

        public static string ComputeAuthTag(string secret)
        {
            var key = Encoding.UTF8.GetBytes(secret ?? "");
            var data = Encoding.UTF8.GetBytes(AuthMessage);
            using (var hmac = new HMACSHA256(key))
                return Convert.ToBase64String(hmac.ComputeHash(data));
        }

        public static bool FixedTimeEquals(string a, string b)
        {
            if (a == null || b == null) return false;
            var ba = Encoding.UTF8.GetBytes(a);
            var bb = Encoding.UTF8.GetBytes(b);
            if (ba.Length != bb.Length)
            {
                var dummy = 0;
                for (var i = 0; i < ba.Length; i++)
                    dummy |= ba[i] ^ (i < bb.Length ? bb[i] : 0);
                return dummy == int.MinValue;
            }
            var diff = 0;
            for (var i = 0; i < ba.Length; i++)
                diff |= ba[i] ^ bb[i];
            return diff == 0;
        }

        // TODO(security): v1 derives keys with plain SHA-256 of the shared secret (no salt / KDF).
        // A v2 HKDF derivation is intentionally NOT introduced yet: changing it would break compatibility
        // with 1.2.27 peers already in the field (multi-site relay). Migrate once all sites are updated:
        // try v2 then v1 on decrypt, encrypt with v2. Until then use a long random shared secret
        // (the Generate button produces one).
        public static void DeriveKeys(string secret, out byte[] aesKey, out byte[] hmacKey)
        {
            var material = Encoding.UTF8.GetBytes(secret ?? "");
            using (var sha = SHA256.Create())
            {
                aesKey = sha.ComputeHash(Concat(Encoding.UTF8.GetBytes("aes|"), material));
                hmacKey = sha.ComputeHash(Concat(Encoding.UTF8.GetBytes("hmac|"), material));
            }
        }

        public static string EncryptToBase64(string secret, string plaintext)
        {
            if (plaintext == null) plaintext = "";
            byte[] aesKey;
            byte[] hmacKey;
            DeriveKeys(secret, out aesKey, out hmacKey);
            var plain = Encoding.UTF8.GetBytes(plaintext);
            byte[] iv;
            byte[] cipher;
            using (var aes = Aes.Create())
            {
                aes.KeySize = 256;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = aesKey;
                aes.GenerateIV();
                iv = aes.IV;
                using (var enc = aes.CreateEncryptor())
                    cipher = enc.TransformFinalBlock(plain, 0, plain.Length);
            }
            var body = Concat(iv, cipher);
            byte[] mac;
            using (var hmac = new HMACSHA256(hmacKey))
                mac = hmac.ComputeHash(body);
            return Convert.ToBase64String(Concat(body, mac));
        }

        public static bool TryDecryptFromBase64(string secret, string payloadEnc, out string plaintext)
        {
            plaintext = null;
            if (string.IsNullOrWhiteSpace(payloadEnc)) return false;
            byte[] all;
            try { all = Convert.FromBase64String(payloadEnc.Trim()); }
            catch { return false; }
            if (all.Length < IvBytes + 1 + MacBytes) return false;

            byte[] aesKey;
            byte[] hmacKey;
            DeriveKeys(secret, out aesKey, out hmacKey);

            var bodyLen = all.Length - MacBytes;
            var body = new byte[bodyLen];
            Buffer.BlockCopy(all, 0, body, 0, bodyLen);
            var mac = new byte[MacBytes];
            Buffer.BlockCopy(all, bodyLen, mac, 0, MacBytes);

            byte[] expected;
            using (var hmac = new HMACSHA256(hmacKey))
                expected = hmac.ComputeHash(body);
            if (!FixedTimeEqualsBytes(expected, mac))
                return false;

            var iv = new byte[IvBytes];
            Buffer.BlockCopy(body, 0, iv, 0, IvBytes);
            var cipher = new byte[bodyLen - IvBytes];
            Buffer.BlockCopy(body, IvBytes, cipher, 0, cipher.Length);
            try
            {
                using (var aes = Aes.Create())
                {
                    aes.KeySize = 256;
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;
                    aes.Key = aesKey;
                    aes.IV = iv;
                    using (var dec = aes.CreateDecryptor())
                    {
                        var plain = dec.TransformFinalBlock(cipher, 0, cipher.Length);
                        plaintext = Encoding.UTF8.GetString(plain);
                        return true;
                    }
                }
            }
            catch
            {
                return false;
            }
        }

        public static string EncodeCredsPayload(PeerCredsPayload payload)
        {
            if (payload == null) return "{}";
            var o = new JObject();
            o["email"] = payload.Email ?? "";
            o["password"] = payload.Password ?? "";
            o["intervalHours"] = payload.IntervalHours < 1 ? 1 : payload.IntervalHours;
            o["blockAfter"] = payload.BlockAfter < 1 ? 3 : payload.BlockAfter;
            return o.ToString(Formatting.None);
        }

        public static bool TryParseCredsPayload(string json, out PeerCredsPayload payload)
        {
            payload = null;
            if (string.IsNullOrWhiteSpace(json)) return false;
            try
            {
                var o = JObject.Parse(json);
                var p = new PeerCredsPayload();
                p.Email = TokenString(o["email"]);
                p.Password = TokenString(o["password"]);
                var hours = TokenInt(o["intervalHours"], 1);
                if (hours < 1) hours = 1;
                if (hours > 168) hours = 168;
                p.IntervalHours = hours;
                var block = TokenInt(o["blockAfter"], 3);
                if (block < 1) block = 3;
                if (block > 10) block = 10;
                p.BlockAfter = block;
                payload = p;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static string EncryptCreds(string secret, PeerCredsPayload payload)
        {
            return EncryptToBase64(secret, EncodeCredsPayload(payload));
        }

        public static bool TryDecryptCreds(string secret, string payloadEnc, out PeerCredsPayload payload)
        {
            payload = null;
            string plain;
            if (!TryDecryptFromBase64(secret, payloadEnc, out plain)) return false;
            return TryParseCredsPayload(plain, out payload);
        }

        private static string TokenString(JToken t)
        {
            if (t == null || t.Type == JTokenType.Null) return "";
            return t.Type == JTokenType.String ? ((string)t ?? "") : t.ToString();
        }

        private static int TokenInt(JToken t, int fallback)
        {
            if (t == null || t.Type == JTokenType.Null) return fallback;
            if (t.Type == JTokenType.Integer) return (int)t;
            int n;
            if (int.TryParse(t.ToString(), out n)) return n;
            return fallback;
        }

        private static bool FixedTimeEqualsBytes(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            var diff = 0;
            for (var i = 0; i < a.Length; i++)
                diff |= a[i] ^ b[i];
            return diff == 0;
        }

        private static byte[] Concat(byte[] a, byte[] b)
        {
            if (a == null) a = new byte[0];
            if (b == null) b = new byte[0];
            var r = new byte[a.Length + b.Length];
            Buffer.BlockCopy(a, 0, r, 0, a.Length);
            Buffer.BlockCopy(b, 0, r, a.Length, b.Length);
            return r;
        }
    }

    public sealed class PeerCredsPayload
    {
        public string Email { get; set; }
        public string Password { get; set; }
        public int IntervalHours { get; set; }
        public int BlockAfter { get; set; }
    }

    public sealed class PeerCredsEvent
    {
        public long Seq { get; set; }
        public string EventId { get; set; }
        public string OriginServerId { get; set; }
        public string OriginName { get; set; }
        public string CreatedUtc { get; set; }
        public string PayloadEnc { get; set; }
    }
}
