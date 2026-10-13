// Copyright 2026 EyeGrok contributors
// SPDX-License-Identifier: Apache-2.0

using UnityEngine;
using UnityEngine.UI;

namespace EyeGrok.UI
{
    // Built-in uGUI text helpers. Unity 6 ships the legacy font as LegacyRuntime.ttf.
    public static class TextKit
    {
        public const string BuiltinFontResource = "LegacyRuntime.ttf";

        public static Font BuiltinFont()
        {
            return Resources.GetBuiltinResource<Font>(BuiltinFontResource);
        }

        public static void Prepare(Text text, string value, int size, TextAnchor alignment, Color color, bool bestFit)
        {
            if (text == null) return;
            text.font = BuiltinFont();
            text.text = value ?? "";
            text.fontSize = size;
            text.alignment = alignment;
            text.color = color;
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.alignByGeometry = false;
            text.raycastTarget = false;
            if (bestFit)
            {
                text.resizeTextForBestFit = true;
                text.resizeTextMinSize = Mathf.Max(12, size / 2);
                text.resizeTextMaxSize = size;
            }
            else
            {
                text.resizeTextForBestFit = false;
            }
        }

        public static void ConfigureOverlay(Canvas canvas, CanvasScaler scaler)
        {
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.pixelPerfect = true;
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;
            scaler.referencePixelsPerUnit = 100f;
            scaler.dynamicPixelsPerUnit = 1f;
        }

        // World-space glyphs are rasterized from dynamicPixelsPerUnit.
        // Scale is snapped so one canvas unit is 1/pixelsPerMeter meters.
        public static void ConfigureWorldCanvas(Canvas canvas, float pixelsPerMeter)
        {
            if (pixelsPerMeter < 1f) pixelsPerMeter = 1f;
            var scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler == null) scaler = canvas.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;
            scaler.referencePixelsPerUnit = pixelsPerMeter;
            scaler.dynamicPixelsPerUnit = pixelsPerMeter;
            var unit = 1f / pixelsPerMeter;
            canvas.transform.localScale = new Vector3(unit, unit, unit);
        }

        public static string Ellipsis(string value, int maxChars)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (maxChars < 1) return "";
            if (value.Length <= maxChars) return value;
            if (maxChars <= 3) return new string('.', maxChars);
            return value.Substring(0, maxChars - 3) + "...";
        }

        public static string ClampToWidth(Font font, string value, int fontSize, int maxWidth)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (font == null || fontSize < 1 || maxWidth <= 0) return Ellipsis(value, 32);
            font.RequestCharactersInTexture(value, fontSize, FontStyle.Normal);
            if (Measure(font, value, fontSize) <= maxWidth) return value;
            var lo = 0;
            var hi = value.Length;
            while (lo < hi)
            {
                var mid = (lo + hi + 1) / 2;
                var candidate = value.Substring(0, mid) + "...";
                font.RequestCharactersInTexture(candidate, fontSize, FontStyle.Normal);
                if (Measure(font, candidate, fontSize) <= maxWidth) lo = mid;
                else hi = mid - 1;
            }
            if (lo <= 0) return "...";
            return value.Substring(0, lo) + "...";
        }

        static int Measure(Font font, string value, int fontSize)
        {
            var width = 0;
            for (var i = 0; i < value.Length; i++)
            {
                CharacterInfo info;
                if (font.GetCharacterInfo(value[i], out info, fontSize, FontStyle.Normal))
                    width += info.advance;
            }
            return width;
        }
    }
}
