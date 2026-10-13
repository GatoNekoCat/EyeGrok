// Copyright 2026 EyeGrok contributors
// SPDX-License-Identifier: Apache-2.0

using System.Threading;
using System.Threading.Tasks;

namespace EyeGrok.Pair
{
    public sealed class CodePad
    {
        public const int MaxDigits = 11;
        string _digits = "";

        public string Digits { get { return _digits; } }
        public bool OkEnabled { get { return _digits.Length == MaxDigits; } }

        public string Display
        {
            get
            {
                var d = _digits;
                if (d.Length <= 4) return d;
                if (d.Length <= 8) return d.Substring(0, 4) + " " + d.Substring(4);
                return d.Substring(0, 4) + " " + d.Substring(4, 4) + " " + d.Substring(8);
            }
        }

        public void Push(char digit)
        {
            if (digit < '0' || digit > '9') return;
            if (_digits.Length >= MaxDigits) return;
            _digits += digit;
        }

        public void Backspace()
        {
            if (_digits.Length == 0) return;
            _digits = _digits.Substring(0, _digits.Length - 1);
        }

        public void Clear()
        {
            _digits = "";
        }
    }

    public static class CodeEntry
    {
        public const string DammFail = "Check the code";
        public const string NeedOrigin = "Connect USB and run the installer's Code tab first";

        public static async Task<string> Submit(CodePad pad, string relayUrl, PairingClient client, CancellationToken ct)
        {
            if (pad == null || !pad.OkEnabled) return "";
            if (!PairCrypto.DammValid(pad.Digits)) return DammFail;
            if (string.IsNullOrEmpty(relayUrl)) return NeedOrigin;
            var result = await client.RedeemCode(relayUrl, pad.Digits, ct);
            return result.ToString();
        }
    }
}
