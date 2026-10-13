// Copyright 2026 EyeGrok contributors
// SPDX-License-Identifier: Apache-2.0

namespace EyeGrok.UI
{
    public interface ITextSink
    {
        bool RichText { get; set; }
        string Text { get; set; }
    }

    public static class ReplyText
    {
        public const int MaxChars = 4000;

        public static string Clamp(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            if (text.Length <= MaxChars) return text;
            return text.Substring(0, MaxChars);
        }

        public static void Apply(ITextSink sink, string text)
        {
            if (sink == null) return;
            sink.RichText = false;
            sink.Text = Clamp(text);
        }
    }
}
