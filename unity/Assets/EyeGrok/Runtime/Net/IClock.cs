// Copyright 2026 EyeGrok contributors
// SPDX-License-Identifier: Apache-2.0

using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace EyeGrok.Net
{
    public interface IClock
    {
        float Now { get; }
        Task Wait(float seconds, CancellationToken ct);
    }

    public sealed class RealtimeClock : IClock
    {
        public float Now => Time.realtimeSinceStartup;

        public async Task Wait(float seconds, CancellationToken ct)
        {
            var end = Now + seconds;
            while (Now < end)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Yield();
            }
        }
    }
}
