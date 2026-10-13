// Copyright 2026 EyeGrok contributors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Security.Cryptography;
using System.Text;

namespace EyeGrok.Pair
{
    public static class PairCrypto
    {
        static readonly int[,] Damm = {
            { 0, 3, 1, 7, 5, 9, 8, 6, 4, 2 },
            { 7, 0, 9, 2, 1, 5, 4, 8, 6, 3 },
            { 4, 2, 0, 6, 8, 7, 1, 3, 5, 9 },
            { 1, 7, 5, 0, 9, 8, 3, 4, 2, 6 },
            { 6, 1, 2, 3, 0, 4, 5, 9, 7, 8 },
            { 3, 6, 7, 4, 2, 0, 9, 5, 8, 1 },
            { 5, 8, 6, 9, 7, 2, 0, 1, 3, 4 },
            { 8, 9, 4, 5, 3, 6, 2, 0, 1, 7 },
            { 9, 4, 3, 8, 6, 1, 7, 2, 0, 5 },
            { 2, 5, 8, 1, 4, 3, 6, 7, 9, 0 }
        };

        public static int DammInterim(string digits)
        {
            var interim = 0;
            if (digits == null) return -1;
            for (var i = 0; i < digits.Length; i++)
            {
                var d = digits[i] - '0';
                if (d < 0 || d > 9) return -1;
                interim = Damm[interim, d];
            }
            return interim;
        }

        public static bool DammValid(string code)
        {
            return code != null && code.Length > 0 && DammInterim(code) == 0;
        }

        public static byte[] Hkdf(byte[] ikm, string info, int length)
        {
            var salt = new byte[32];
            byte[] prk;
            using (var extract = new HMACSHA256(salt))
                prk = extract.ComputeHash(ikm);
            var infoBytes = Encoding.UTF8.GetBytes(info ?? "");
            var blocks = (length + 31) / 32;
            var prev = new byte[0];
            var okm = new byte[blocks * 32];
            var offset = 0;
            using (var expand = new HMACSHA256(prk))
            {
                for (var i = 1; i <= blocks; i++)
                {
                    var input = new byte[prev.Length + infoBytes.Length + 1];
                    Buffer.BlockCopy(prev, 0, input, 0, prev.Length);
                    Buffer.BlockCopy(infoBytes, 0, input, prev.Length, infoBytes.Length);
                    input[input.Length - 1] = (byte)i;
                    prev = expand.ComputeHash(input);
                    Buffer.BlockCopy(prev, 0, okm, offset, prev.Length);
                    offset += prev.Length;
                }
            }
            var output = new byte[length];
            Buffer.BlockCopy(okm, 0, output, 0, length);
            return output;
        }

        public static byte[] Pbkdf2(string code10)
        {
            var password = Encoding.UTF8.GetBytes(code10 ?? "");
            var salt = Encoding.UTF8.GetBytes("eyegrok-pair-v1");
            using (var kdf = new Rfc2898DeriveBytes(password, salt, 100000, HashAlgorithmName.SHA256))
                return kdf.GetBytes(32);
        }

        public static byte[] Mac(byte[] macKey, byte[] id, byte[] iv, byte[] cipher)
        {
            var label = Encoding.ASCII.GetBytes("EG1");
            var input = new byte[label.Length + 1 + id.Length + iv.Length + cipher.Length];
            var o = 0;
            Buffer.BlockCopy(label, 0, input, o, label.Length);
            o += label.Length;
            input[o++] = 0;
            Buffer.BlockCopy(id, 0, input, o, id.Length);
            o += id.Length;
            Buffer.BlockCopy(iv, 0, input, o, iv.Length);
            o += iv.Length;
            Buffer.BlockCopy(cipher, 0, input, o, cipher.Length);
            using (var hmac = new HMACSHA256(macKey))
                return hmac.ComputeHash(input);
        }

        public static bool MacEqual(byte[] left, byte[] right)
        {
            if (left == null || right == null) return false;
            var diff = left.Length ^ right.Length;
            var n = left.Length < right.Length ? left.Length : right.Length;
            for (var i = 0; i < n; i++) diff |= left[i] ^ right[i];
            return diff == 0;
        }

        public static byte[] Unprotect(byte[] iv, byte[] cipher, byte[] mac, byte[] encKey, byte[] macKey, byte[] id)
        {
            var expect = Mac(macKey, id, iv, cipher);
            if (!MacEqual(expect, mac)) return null;
            try
            {
                using (var aes = Aes.Create())
                {
                    aes.KeySize = 256;
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;
                    aes.Key = encKey;
                    aes.IV = iv;
                    using (var decryptor = aes.CreateDecryptor())
                        return decryptor.TransformFinalBlock(cipher, 0, cipher.Length);
                }
            }
            catch (CryptographicException)
            {
                return null;
            }
        }

        public static string B64Url(byte[] bytes)
        {
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        public static byte[] FromB64Url(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                var ok = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '_';
                if (!ok) return null;
            }
            var padded = text.Replace('-', '+').Replace('_', '/');
            var mod = padded.Length % 4;
            if (mod == 2) padded += "==";
            else if (mod == 3) padded += "=";
            else if (mod == 1) return null;
            try
            {
                return Convert.FromBase64String(padded);
            }
            catch (FormatException)
            {
                return null;
            }
        }
    }
}
