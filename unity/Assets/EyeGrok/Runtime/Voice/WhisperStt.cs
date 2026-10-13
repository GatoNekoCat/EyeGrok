// Copyright 2026 EyeGrok contributors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using Whisper;

namespace EyeGrok.Voice
{
    public sealed class WhisperStt : MonoBehaviour, IStt
    {
        const string ModelRel = "Whisper/ggml-tiny.en-q5_1.bin";

        WhisperManager _manager;
        bool _tried;

        public bool Ready => _manager != null && _manager.IsLoaded;

        void Awake()
        {
            _manager = GetComponent<WhisperManager>();
            if (_manager == null) _manager = gameObject.AddComponent<WhisperManager>();
            _manager.language = "en";
            _manager.translateToEnglish = false;
        }

        public async Task LoadOnce()
        {
            if (_tried || _manager == null) return;
            _tried = true;
            if (string.IsNullOrEmpty(_manager.ModelPath))
            {
                _manager.IsModelPathInStreamingAssets = true;
                _manager.ModelPath = ModelRel;
            }
            await _manager.InitModel();
            if (_manager.IsLoaded)
            {
                Debug.Log("model path=streaming");
                return;
            }
            var dir = Path.Combine(Application.persistentDataPath, "Whisper");
            Directory.CreateDirectory(dir);
            var dst = Path.Combine(dir, "ggml-tiny.en-q5_1.bin");
            var src = Path.Combine(Application.streamingAssetsPath, ModelRel);
            using (var req = UnityWebRequest.Get(src))
            {
                req.downloadHandler = new DownloadHandlerFile(dst);
                var op = req.SendWebRequest();
                while (!op.isDone) await Task.Yield();
                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.Log("model copy=fail");
                    return;
                }
            }
            _manager.IsModelPathInStreamingAssets = false;
            _manager.ModelPath = dst;
            await _manager.InitModel();
            Debug.Log(_manager.IsLoaded ? "model path=persistent" : "model path=fail");
        }

        public async Task<string> Transcribe(float[] samples, int rate, int channels, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (!Ready) return "";
            var result = await _manager.GetTextAsync(samples, rate, channels <= 0 ? 1 : channels);
            if (result == null || result.Result == null) return "";
            return result.Result.Trim();
        }
    }
}
