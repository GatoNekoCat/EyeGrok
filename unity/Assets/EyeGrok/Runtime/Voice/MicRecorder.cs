// Copyright 2026 EyeGrok contributors
// SPDX-License-Identifier: Apache-2.0

using System;
using UnityEngine;

namespace EyeGrok.Voice
{
    public sealed class MicRecorder : MonoBehaviour, IMic
    {
        AudioClip _clip;
        float _started;
        bool _recording;

        public void StartRecording()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
                UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone);
#endif
            _clip = Microphone.Start(null, false, 15, 16000);
            _started = Time.realtimeSinceStartup;
            _recording = _clip != null;
        }

        public MicTake StopRecording()
        {
            var seconds = _recording ? Time.realtimeSinceStartup - _started : 0f;
            var rate = _clip != null ? _clip.frequency : 16000;
            var channels = _clip != null && _clip.channels > 0 ? _clip.channels : 1;
            var position = 0;
            if (_recording) position = Microphone.GetPosition(null);
            if (_recording || Microphone.IsRecording(null)) Microphone.End(null);
            _recording = false;
            var samples = new float[0];
            if (_clip != null && position > 0)
            {
                var frames = Math.Min(position, _clip.samples);
                var count = frames * channels;
                var all = new float[_clip.samples * channels];
                _clip.GetData(all, 0);
                samples = new float[count];
                Array.Copy(all, samples, count);
            }
            // Sample rate is passed through. whisper.unity takes the rate.
            // Linear resample to 16 kHz is not applied: P0-d has not shown it is required. UNVERIFIED.
            return new MicTake { Samples = samples, Rate = rate, Channels = channels, Seconds = seconds };
        }
    }
}
