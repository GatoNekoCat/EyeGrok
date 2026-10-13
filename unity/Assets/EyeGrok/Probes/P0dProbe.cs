// Copyright 2026 EyeGrok contributors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using Whisper;
using Debug = UnityEngine.Debug;

namespace EyeGrok.Probes
{
    public sealed class P0dPointer : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        public void OnPointerDown(PointerEventData eventData)
        {
            Debug.Log("P0D pointer down");
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            Debug.Log("P0D pointer up");
        }
    }

    public sealed class P0dProbe : MonoBehaviour
    {
        const string ModelRel = "Whisper/ggml-tiny.en-q5_1.bin";

        WhisperManager _whisper;
        bool _busy;

        void Awake()
        {
            _whisper = GetComponent<WhisperManager>();
            if (_whisper == null) _whisper = gameObject.AddComponent<WhisperManager>();
            _whisper.language = "en";
            _whisper.translateToEnglish = false;
        }

        void Start()
        {
            LogPermission("before");
            RequestMic();
            LogSetupPath();
        }

        public void ProbeServices()
        {
            Debug.Log("P0D services " + AndroidCall("services"));
        }

        public void ProbeKeystore()
        {
            Debug.Log("P0D keystore=" + AndroidCall("keystore"));
        }

        public async void Record3s()
        {
            if (_busy) return;
            _busy = true;
            try
            {
                await RecordAndTranscribe();
            }
            catch (Exception ex)
            {
                Debug.Log("P0D err=" + ex.GetType().Name);
            }
            finally
            {
                _busy = false;
            }
        }

        async Task RecordAndTranscribe()
        {
            LogPermission("before-record");
            var devices = Microphone.devices;
            Debug.Log("P0D devices=" + (devices == null ? 0 : devices.Length));
            int minRate;
            int maxRate;
            Microphone.GetDeviceCaps(null, out minRate, out maxRate);
            Debug.Log("P0D caps min=" + minRate + " max=" + maxRate);
            Debug.Log("P0D requested=16000");

            var clip = Microphone.Start(null, false, 3, 16000);
            if (clip == null)
            {
                Debug.Log("P0D mic=fail");
                return;
            }

            var started = Time.realtimeSinceStartup;
            while (Microphone.GetPosition(null) <= 0 && Time.realtimeSinceStartup - started < 2f)
                await Task.Yield();
            var captureStart = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - captureStart < 3f)
                await Task.Yield();

            var position = Microphone.GetPosition(null);
            var frequency = clip.frequency;
            Debug.Log("P0D actual=" + frequency);
            Microphone.End(null);

            var channels = clip.channels;
            var total = clip.samples * channels;
            var all = new float[total];
            clip.GetData(all, 0);
            var used = position > 0 ? Math.Min(position, clip.samples) * channels : all.Length;
            var samples = new float[used];
            Array.Copy(all, samples, used);

            var loadSw = Stopwatch.StartNew();
            var loaded = await EnsureModel();
            loadSw.Stop();
            Debug.Log("P0D model load ms=" + loadSw.ElapsedMilliseconds + " loaded=" + loaded);
            if (!loaded)
            {
                Debug.Log("P0D transcript empty");
                return;
            }

            var sttSw = Stopwatch.StartNew();
            var result = await _whisper.GetTextAsync(samples, frequency, channels);
            sttSw.Stop();
            Debug.Log("P0D transcribe ms=" + sttSw.ElapsedMilliseconds);
            var text = result == null ? "" : (result.Result ?? "").Trim();
            if (text.Length == 0)
                Debug.Log("P0D transcript empty");
            else
                Debug.Log("P0D transcript " + text);
        }

        async Task<bool> EnsureModel()
        {
            if (_whisper.IsLoaded) return true;
            if (!_whisper.IsModelPathInStreamingAssets && string.IsNullOrEmpty(_whisper.ModelPath))
                return false;

            if (string.IsNullOrEmpty(_whisper.ModelPath))
            {
                _whisper.IsModelPathInStreamingAssets = true;
                _whisper.ModelPath = ModelRel;
            }

            await _whisper.InitModel();
            if (_whisper.IsLoaded)
            {
                Debug.Log("P0D model path=streaming");
                return true;
            }

            var dstDir = Path.Combine(Application.persistentDataPath, "Whisper");
            Directory.CreateDirectory(dstDir);
            var dst = Path.Combine(dstDir, "ggml-tiny.en-q5_1.bin");
            var src = Path.Combine(Application.streamingAssetsPath, ModelRel);
            using (var req = UnityWebRequest.Get(src))
            {
                req.downloadHandler = new DownloadHandlerFile(dst);
                var op = req.SendWebRequest();
                while (!op.isDone) await Task.Yield();
                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.Log("P0D model copy=fail");
                    return false;
                }
            }

            _whisper.IsModelPathInStreamingAssets = false;
            _whisper.ModelPath = dst;
            await _whisper.InitModel();
            if (_whisper.IsLoaded)
            {
                Debug.Log("P0D model path=persistent");
                return true;
            }
            Debug.Log("P0D model path=fail");
            return false;
        }

        static void LogPermission(string when)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            var granted = UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone);
            Debug.Log("P0D permission " + when + "=" + granted);
#else
            Debug.Log("P0D permission " + when + "=editor");
#endif
        }

        static void RequestMic()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            var callbacks = new UnityEngine.Android.PermissionCallbacks();
            callbacks.PermissionGranted += _ => Debug.Log("P0D permission after=granted");
            callbacks.PermissionDenied += _ => Debug.Log("P0D permission after=denied");
            UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone, callbacks);
#endif
        }

        static void LogSetupPath()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var probe = new AndroidJavaClass("io.github.eyegrok.EyeGrokProbe"))
                {
                    var dir = probe.CallStatic<string>("externalDir", activity);
                    var exists = probe.CallStatic<bool>("setupExists", activity);
                    Debug.Log("P0D external=" + dir);
                    Debug.Log("P0D setup-file=" + exists);
                }
            }
            catch (Exception ex)
            {
                Debug.Log("P0D setup-file err=" + ex.GetType().Name);
            }
#else
            Debug.Log("P0D external=editor");
            Debug.Log("P0D setup-file=false");
#endif
        }

        static string AndroidCall(string which)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    if (which == "keystore")
                    {
                        using (var store = new AndroidJavaClass("io.github.eyegrok.EyeGrokKeystore"))
                            return store.CallStatic<string>("probeRoundTrip", activity);
                    }
                    using (var probe = new AndroidJavaClass("io.github.eyegrok.EyeGrokProbe"))
                        return probe.CallStatic<string>("services", activity);
                }
            }
            catch (Exception ex)
            {
                return "fail " + ex.GetType().Name;
            }
#else
            return which == "keystore" ? "fail editor" : "rec=false services=0 engines=0 tts=-1";
#endif
        }
    }
}
