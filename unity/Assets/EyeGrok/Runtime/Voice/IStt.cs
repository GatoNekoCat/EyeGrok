// Copyright 2026 EyeGrok contributors
// SPDX-License-Identifier: Apache-2.0

using System.Threading;
using System.Threading.Tasks;

namespace EyeGrok.Voice
{
    public interface IStt
    {
        bool Ready { get; }
        Task<string> Transcribe(float[] samples, int rate, int channels, CancellationToken ct);
    }
}
