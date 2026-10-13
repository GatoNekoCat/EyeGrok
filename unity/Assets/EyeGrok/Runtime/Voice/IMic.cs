// Copyright 2026 EyeGrok contributors
// SPDX-License-Identifier: Apache-2.0

namespace EyeGrok.Voice
{
    public sealed class MicTake
    {
        public float[] Samples;
        public int Rate;
        public int Channels;
        public float Seconds;
    }

    public interface IMic
    {
        void StartRecording();
        MicTake StopRecording();
    }
}
