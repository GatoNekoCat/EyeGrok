// Copyright 2026 EyeGrok contributors
// SPDX-License-Identifier: Apache-2.0

#if EYEGROK_QR
using System;
using System.Collections.Generic;
using UnityEngine;
using ZXing;
using ZXing.Common;

namespace EyeGrok.Pair
{
    public sealed class QrScanner
    {
        public const float TimeoutSeconds = 60f;
        WebCamTexture _texture;
        BarcodeReaderGeneric _reader;
        float _start;
        int _frames;
        bool _running;

        public bool Running { get { return _running; } }
        public string Text { get; private set; }

        public void Start()
        {
            Stop();
            _reader = new BarcodeReaderGeneric
            {
                AutoRotate = true,
                Options = new DecodingOptions
                {
                    TryHarder = true,
                    PossibleFormats = new List<BarcodeFormat> { BarcodeFormat.QR_CODE }
                }
            };
            _texture = new WebCamTexture();
            _texture.Play();
            _start = Time.realtimeSinceStartup;
            _frames = 0;
            _running = true;
            Text = null;
        }

        public void Stop()
        {
            _running = false;
            if (_texture != null)
            {
                if (_texture.isPlaying) _texture.Stop();
                UnityEngine.Object.Destroy(_texture);
                _texture = null;
            }
        }

        public bool Tick()
        {
            if (!_running || _texture == null) return false;
            if (Time.realtimeSinceStartup - _start >= TimeoutSeconds)
            {
                Stop();
                return false;
            }
            if (!_texture.didUpdateThisFrame || _texture.width < 16) return false;
            _frames++;
            if (_frames % 5 != 0) return false;
            var pixels = _texture.GetPixels32();
            var bytes = new byte[pixels.Length * 4];
            for (var i = 0; i < pixels.Length; i++)
            {
                var o = i * 4;
                bytes[o] = pixels[i].r;
                bytes[o + 1] = pixels[i].g;
                bytes[o + 2] = pixels[i].b;
                bytes[o + 3] = pixels[i].a;
            }
            Result result = null;
            try
            {
                result = _reader.Decode(bytes, _texture.width, _texture.height, RGBLuminanceSource.BitmapFormat.RGBA32);
            }
            catch (Exception ex)
            {
                Debug.Log("pair err=" + ex.GetType().Name);
            }
            if (result == null || string.IsNullOrEmpty(result.Text)) return false;
            Text = result.Text;
            Stop();
            return true;
        }
    }
}
#endif
