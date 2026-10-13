// Copyright 2026 EyeGrok contributors
// SPDX-License-Identifier: Apache-2.0

using System.IO;

namespace EyeGrok.Pair
{
    public static class PendingOrigin
    {
        public const string FileName = "eyegrok-pair-origin.txt";
        public const int MaxBytes = 2048;

        public static string Take(string directory)
        {
            if (string.IsNullOrEmpty(directory)) return null;
            var path = Path.Combine(directory, FileName);
            if (!File.Exists(path)) return null;
            string line = null;
            try
            {
                var info = new FileInfo(path);
                if (info.Length > MaxBytes) return null;
                var text = File.ReadAllText(path).Replace("\r", "");
                if (text.EndsWith("\n")) text = text.Substring(0, text.Length - 1);
                if (text.Length == 0 || text.IndexOf('\n') >= 0) return null;
                line = text;
            }
            catch (System.Exception)
            {
                line = null;
            }
            finally
            {
                try
                {
                    if (File.Exists(path)) File.Delete(path);
                }
                catch (System.Exception)
                {
                }
            }
            return line;
        }
    }
}
