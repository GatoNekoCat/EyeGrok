// Copyright 2026 EyeGrok contributors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace EyeGrok.Tests
{
    public sealed class QrFixtureTests
    {
        const string Expected = "EYEGROK-P0CAM-TEST";

        [Test]
        public void T20_QrFixtureDecodesWithZXing()
        {
            var path = Path.Combine(Application.dataPath, "EyeGrok", "Tests", "EditMode", "Fixtures", "qr-p0cam.txt");
            Assert.IsTrue(File.Exists(path), "fixture missing");
            var ascii = File.ReadAllText(path);
            int width;
            int height;
            var bytes = Raster(ascii, 4, 4, out width, out height);

            var readerType = FindType("ZXing.BarcodeReaderGeneric");
            Assert.IsNotNull(readerType, "zxing.dll missing. Run tools/fetch-zxing.ps1");
            var formatEnum = FindType("ZXing.BarcodeFormat");
            var bitmapFormat = FindType("ZXing.RGBLuminanceSource+BitmapFormat");
            var optionsType = FindType("ZXing.Common.DecodingOptions");
            Assert.IsNotNull(formatEnum);
            Assert.IsNotNull(bitmapFormat);
            Assert.IsNotNull(optionsType);

            var reader = Activator.CreateInstance(readerType);
            var options = Activator.CreateInstance(optionsType);
            var qr = Enum.Parse(formatEnum, "QR_CODE");
            var listType = typeof(List<>).MakeGenericType(formatEnum);
            var list = Activator.CreateInstance(listType);
            listType.GetMethod("Add").Invoke(list, new[] { qr });
            optionsType.GetProperty("PossibleFormats").SetValue(options, list, null);
            optionsType.GetProperty("TryHarder").SetValue(options, true, null);
            readerType.GetProperty("AutoRotate").SetValue(reader, true, null);
            readerType.GetProperty("Options").SetValue(reader, options, null);

            var rgba = Enum.Parse(bitmapFormat, "RGBA32");
            var decode = readerType.GetMethod("Decode", new[] { typeof(byte[]), typeof(int), typeof(int), bitmapFormat });
            Assert.IsNotNull(decode);
            var result = decode.Invoke(reader, new object[] { bytes, width, height, rgba });
            Assert.IsNotNull(result, "zxing returned no result");
            var text = result.GetType().GetProperty("Text").GetValue(result, null) as string;
            Assert.AreEqual(Expected, text);
        }

        static Type FindType(string name)
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (var i = 0; i < assemblies.Length; i++)
            {
                var found = assemblies[i].GetType(name);
                if (found != null) return found;
            }
            var dll = Path.Combine(Application.dataPath, "Plugins", "ZXing", "zxing.dll");
            if (!File.Exists(dll)) return null;
            var loaded = Assembly.LoadFrom(dll);
            return loaded.GetType(name);
        }

        static byte[] Raster(string ascii, int scale, int quiet, out int width, out int height)
        {
            var lines = ascii.Replace("\r", "").Split('\n');
            var rows = new List<string>();
            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].Length > 0) rows.Add(lines[i]);
            }
            var n = rows.Count;
            var modules = n + quiet * 2;
            width = modules * scale;
            height = width;
            var bytes = new byte[width * height * 4];
            for (var i = 0; i < bytes.Length; i += 4)
            {
                bytes[i] = 255;
                bytes[i + 1] = 255;
                bytes[i + 2] = 255;
                bytes[i + 3] = 255;
            }
            for (var y = 0; y < n; y++)
            {
                var row = rows[y];
                var count = row.Length;
                if (count > n) count = n;
                for (var x = 0; x < count; x++)
                {
                    if (row[x] != '#') continue;
                    for (var dy = 0; dy < scale; dy++)
                    {
                        for (var dx = 0; dx < scale; dx++)
                        {
                            var px = (x + quiet) * scale + dx;
                            var py = (y + quiet) * scale + dy;
                            var o = (py * width + px) * 4;
                            bytes[o] = 0;
                            bytes[o + 1] = 0;
                            bytes[o + 2] = 0;
                            bytes[o + 3] = 255;
                        }
                    }
                }
            }
            return bytes;
        }
    }
}
