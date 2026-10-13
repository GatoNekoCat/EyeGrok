// Copyright 2026 EyeGrok contributors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using UnityEngine;
#if EYEGROK_QR
using ZXing;
#endif
using Debug = UnityEngine.Debug;

namespace EyeGrok.Probes
{
    public sealed class P0CamProbe : MonoBehaviour
    {
        const string Expected = "EYEGROK-P0CAM-TEST";

        [SerializeField] TextAsset _fixture;

        bool _permKnown;
        bool _permGranted;
        int _wctDevices = -1;
        int _wctFrames;
        int _c2Ids = -1;
        bool _c2FrameOk;
        bool _qrMatch;

#if EYEGROK_QR
        WebCamTexture _wct;
#endif

        public void ProbePermission()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            var before = UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera);
            _permKnown = true;
            _permGranted = before;
            Debug.Log("P0CAM perm before=" + before);
            if (before)
            {
                Debug.Log("P0CAM perm after=granted");
                return;
            }
            var callbacks = new UnityEngine.Android.PermissionCallbacks();
            callbacks.PermissionGranted += delegate(string _)
            {
                _permGranted = true;
                Debug.Log("P0CAM perm after=granted");
            };
            callbacks.PermissionDenied += delegate(string _)
            {
                _permGranted = false;
                Debug.Log("P0CAM perm after=denied");
            };
            UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Camera, callbacks);
#else
            _permKnown = true;
            _permGranted = false;
            Debug.Log("P0CAM perm before=False");
            Debug.Log("P0CAM perm after=denied");
#endif
        }

        public async void ProbeWebCam()
        {
#if EYEGROK_QR
            try
            {
                await ProbeWebCamAsync();
            }
            catch (Exception)
            {
                Debug.Log("P0CAM wct open=fail");
            }
#else
            _wctDevices = 0;
            _wctFrames = 0;
            Debug.Log("P0CAM wct devices=0");
            Debug.Log("P0CAM wct open=fail");
            await Task.Yield();
#endif
        }

        public void ProbeCamera2()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var probe = new AndroidJavaClass("io.github.eyegrok.EyeGrokCamProbe"))
                {
                    LogCameraList(probe.CallStatic<string>("describeCameras", activity));
                    LogCameraFrame(probe.CallStatic<string>("openAndFrame", activity));
                }
            }
            catch (Exception ex)
            {
                Debug.Log("P0CAM c2 open=fail err=" + ex.GetType().Name);
            }
#else
            _c2Ids = 0;
            _c2FrameOk = false;
            Debug.Log("P0CAM c2 ids=0");
            Debug.Log("P0CAM c2 open=fail err=InvalidOperationException");
#endif
        }

        public async void ScanQr()
        {
#if EYEGROK_QR
            try
            {
                await ScanQrAsync();
            }
            catch (Exception)
            {
                Debug.Log("P0CAM qr=timeout");
                LogResult();
            }
#else
            Debug.Log("P0CAM qr=timeout");
            LogResult();
            await Task.Yield();
#endif
        }

        public void SelfTest()
        {
#if EYEGROK_QR
            try
            {
                var text = DecodeFixture();
                if (text == Expected) Debug.Log("P0CAM zxing selftest=ok");
                else Debug.Log("P0CAM zxing selftest=fail");
            }
            catch (Exception)
            {
                Debug.Log("P0CAM zxing selftest=fail");
            }
#else
            Debug.Log("P0CAM zxing selftest=fail");
#endif
        }

#if EYEGROK_QR
        async Task ProbeWebCamAsync()
        {
            var devices = WebCamTexture.devices;
            _wctDevices = devices == null ? 0 : devices.Length;
            Debug.Log("P0CAM wct devices=" + _wctDevices);
            if (devices != null)
            {
                for (var i = 0; i < devices.Length; i++)
                    Debug.Log("P0CAM wct dev i=" + i + " front=" + devices[i].isFrontFacing);
            }
            if (_wctDevices == 0)
            {
                _wctFrames = 0;
                Debug.Log("P0CAM wct open=fail");
                return;
            }

            StopWebCam();
            _wct = new WebCamTexture(devices[0].name, 640, 480, 15);
            _wct.Play();
            var sw = Stopwatch.StartNew();
            var frames = 0;
            while (sw.ElapsedMilliseconds < 5000 && frames < 8)
            {
                await Task.Yield();
                if (_wct.width > 16 && _wct.didUpdateThisFrame) frames++;
            }
            _wctFrames = frames;
            if (frames <= 0)
            {
                Debug.Log("P0CAM wct frames=0");
                return;
            }
            Debug.Log("P0CAM wct open=ok w=" + _wct.width + " h=" + _wct.height + " frames=" + frames + " ms=" + sw.ElapsedMilliseconds);
        }

        async Task ScanQrAsync()
        {
            if (_wct == null || !_wct.isPlaying)
            {
                var devices = WebCamTexture.devices;
                _wctDevices = devices == null ? 0 : devices.Length;
                if (_wctDevices == 0)
                {
                    Debug.Log("P0CAM qr=timeout");
                    LogResult();
                    return;
                }
                StopWebCam();
                _wct = new WebCamTexture(devices[0].name, 640, 480, 15);
                _wct.Play();
            }

            var sw = Stopwatch.StartNew();
            var seen = 0;
            while (sw.ElapsedMilliseconds < 20000)
            {
                await Task.Yield();
                if (_wct == null || !_wct.didUpdateThisFrame || _wct.width <= 16) continue;
                seen++;
                _wctFrames = seen;
                if (seen % 5 != 0) continue;
                var text = DecodePixels(_wct.GetPixels32(), _wct.width, _wct.height);
                if (text == null) continue;
                _qrMatch = text == Expected;
                Debug.Log("P0CAM qr=ok match=" + _qrMatch + " src=wct");
                LogResult();
                return;
            }
            Debug.Log("P0CAM qr=timeout");
            LogResult();
        }

        string DecodeFixture()
        {
            if (_fixture == null || string.IsNullOrEmpty(_fixture.text)) return null;
            int width;
            int height;
            var bytes = Raster(_fixture.text, 4, 4, out width, out height);
            return DecodeRgba(bytes, width, height);
        }

        static string DecodePixels(Color32[] pixels, int width, int height)
        {
            if (pixels == null || width <= 0 || height <= 0) return null;
            var bytes = new byte[pixels.Length * 4];
            for (var i = 0; i < pixels.Length; i++)
            {
                var o = i * 4;
                bytes[o] = pixels[i].r;
                bytes[o + 1] = pixels[i].g;
                bytes[o + 2] = pixels[i].b;
                bytes[o + 3] = pixels[i].a;
            }
            return DecodeRgba(bytes, width, height);
        }

        static string DecodeRgba(byte[] bytes, int width, int height)
        {
            var reader = new BarcodeReaderGeneric
            {
                AutoRotate = true,
                Options = new ZXing.Common.DecodingOptions
                {
                    TryHarder = true,
                    PossibleFormats = new List<BarcodeFormat> { BarcodeFormat.QR_CODE }
                }
            };
            var result = reader.Decode(bytes, width, height, RGBLuminanceSource.BitmapFormat.RGBA32);
            if (result == null) return null;
            return result.Text;
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

        void StopWebCam()
        {
            if (_wct == null) return;
            if (_wct.isPlaying) _wct.Stop();
            Destroy(_wct);
            _wct = null;
        }

        void OnDestroy()
        {
            StopWebCam();
        }
#endif

        void LogCameraList(string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                _c2Ids = 0;
                Debug.Log("P0CAM c2 ids=0");
                return;
            }
            var lines = raw.Replace("\r", "").Split('\n');
            var count = 0;
            int.TryParse(lines[0], out count);
            _c2Ids = count;
            Debug.Log("P0CAM c2 ids=" + count);
            for (var i = 1; i < lines.Length; i++)
            {
                if (lines[i].Length == 0) continue;
                var parts = lines[i].Split('|');
                if (parts.Length < 3) continue;
                Debug.Log("P0CAM c2 id=" + parts[0] + " facing=" + parts[1] + " hw=" + parts[2]);
            }
        }

        void LogCameraFrame(string raw)
        {
            var parts = string.IsNullOrEmpty(raw) ? new string[0] : raw.Split('|');
            if (parts.Length < 6)
            {
                _c2FrameOk = false;
                Debug.Log("P0CAM c2 open=fail err=FormatException");
                return;
            }
            if (parts[0] != "ok")
            {
                _c2FrameOk = false;
                Debug.Log("P0CAM c2 open=fail err=" + parts[2]);
                return;
            }
            Debug.Log("P0CAM c2 open=ok ms=" + parts[1]);
            if (parts[3] == "ok")
            {
                _c2FrameOk = true;
                Debug.Log("P0CAM c2 frame=ok w=" + parts[4] + " h=" + parts[5]);
                return;
            }
            _c2FrameOk = false;
            Debug.Log("P0CAM c2 frame=fail");
        }

        void LogResult()
        {
            var frame = _wctFrames > 0 || _c2FrameOk;
            if (frame && _qrMatch)
            {
                Debug.Log("P0CAM RESULT=PASS");
                return;
            }
            if (_permKnown && !_permGranted)
            {
                Debug.Log("P0CAM RESULT=FAIL reason=perm");
                return;
            }
            if (_wctDevices == 0 && _c2Ids == 0)
            {
                Debug.Log("P0CAM RESULT=FAIL reason=nodevice");
                return;
            }
            if (!frame)
            {
                Debug.Log("P0CAM RESULT=FAIL reason=noframe");
                return;
            }
            Debug.Log("P0CAM RESULT=FAIL reason=nodecode");
        }
    }
}
