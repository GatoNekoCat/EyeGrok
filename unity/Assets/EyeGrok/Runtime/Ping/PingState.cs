// Copyright 2026 EyeGrok contributors
// SPDX-License-Identifier: Apache-2.0

namespace EyeGrok.Ping
{
    public enum PingState
    {
        Unconfigured,
        Idle,
        Recording,
        Transcribing,
        Sending,
        AwaitingReply,
        Done,
        Error
    }
}
