// Copyright 2026 EyeGrok contributors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Threading;
using System.Threading.Tasks;
using EyeGrok.Net;
using EyeGrok.Security;
using EyeGrok.UI;
using EyeGrok.Voice;
using UnityEngine;

namespace EyeGrok.Ping
{
    public sealed class PingController
    {
        public const float MaxRecordSeconds = 15f;
        public const float MinRecordSeconds = 0.5f;

        readonly IMic _mic;
        readonly IStt _stt;
        readonly WebhookClient _webhook;
        readonly RelayClient _relay;
        readonly ISecretStore _store;
        readonly IClock _clock;

        CancellationTokenSource _cts;
        CancellationTokenSource _slice;
        Task _run;
        bool _releaseRequested;
        string _nonce = "";

        public PingController(IMic mic, IStt stt, WebhookClient webhook, RelayClient relay, ISecretStore store, IClock clock)
        {
            _mic = mic;
            _stt = stt;
            _webhook = webhook;
            _relay = relay;
            _store = store;
            _clock = clock;
            State = PingState.Unconfigured;
            Hint = "Not set up — see docs/SETUP.md";
        }

        public PingState State { get; private set; }
        public string Hint { get; private set; }
        public string Transcript { get; private set; }
        public string Reply { get; private set; }
        public string NoncePrefix { get; private set; }
        public event Action Changed;

        public bool Busy
        {
            get
            {
                return State == PingState.Recording
                    || State == PingState.Transcribing
                    || State == PingState.Sending
                    || State == PingState.AwaitingReply;
            }
        }

        public bool HasPendingWork
        {
            get { return _run != null && !_run.IsCompleted; }
        }

        public Task CurrentRun
        {
            get { return _run ?? Task.CompletedTask; }
        }

        public void NotifyConfigured()
        {
            if (State != PingState.Unconfigured) return;
            Set(PingState.Idle, "Hold to talk");
        }

        public void Press()
        {
            if (State == PingState.Unconfigured) return;
            if (Busy) return;
            if (_stt != null && !_stt.Ready) 
            {
                Set(State, "Loading voice model…");
                return;
            }
            _releaseRequested = false;
            _cts = new CancellationTokenSource();
            _mic.StartRecording();
            Set(PingState.Recording, "Release to send");
            _run = RecordLoop(_cts.Token);
        }

        public void Release()
        {
            if (State != PingState.Recording) return;
            _releaseRequested = true;
            if (_slice != null) _slice.Cancel();
        }

        public void Cancel()
        {
            if (!Busy) return;
            if (_cts != null) _cts.Cancel();
            if (_slice != null) _slice.Cancel();
        }

        public async Task CheckAgain()
        {
            if (Busy || string.IsNullOrEmpty(_nonce)) return;
            if (State != PingState.Error && State != PingState.Done) return;
            var ct = new CancellationTokenSource();
            _cts = ct;
            _run = CheckOnce(ct.Token);
            await _run;
        }

        public async Task SubmitText(string text)
        {
#if !UNITY_EDITOR
            if (!Debug.isDebugBuild) return;
#endif
            if (State == PingState.Unconfigured || Busy) return;
            _cts = new CancellationTokenSource();
            _run = SendTranscript(text, _cts.Token);
            await _run;
        }

        public void Forget()
        {
            if (_cts != null) _cts.Cancel();
            if (_slice != null) _slice.Cancel();
            if (_store != null) _store.DeleteAll();
            Redactor.Clear();
            _nonce = "";
            Transcript = "";
            Reply = "";
            Set(PingState.Unconfigured, "Not set up — see docs/SETUP.md");
        }

        async Task RecordLoop(CancellationToken ct)
        {
            var start = _clock.Now;
            try
            {
                while (!_releaseRequested && !ct.IsCancellationRequested && _clock.Now - start < MaxRecordSeconds)
                {
                    _slice = new CancellationTokenSource();
                    using (var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _slice.Token))
                    {
                        var slice = Math.Min(0.25f, MaxRecordSeconds - (_clock.Now - start));
                        if (slice < 0.01f) break;
                        try
                        {
                            await _clock.Wait(slice, linked.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            if (ct.IsCancellationRequested) throw;
                        }
                    }
                }
                if (ct.IsCancellationRequested)
                {
                    SafeStop();
                    Set(PingState.Idle, "Hold to talk");
                    return;
                }
                var take = SafeStop();
                var seconds = take != null ? take.Seconds : 0f;
                if (seconds < MinRecordSeconds)
                {
                    Set(PingState.Idle, "Too short");
                    return;
                }
                Set(PingState.Transcribing, "Transcribing");
                var text = await _stt.Transcribe(take.Samples, take.Rate, take.Channels, ct);
                var len = text == null ? 0 : text.Length;
                Debug.Log("stt len=" + len);
                text = text == null ? "" : text.Trim();
                if (text.Length == 0 || text.IndexOf("[BLANK_AUDIO]", StringComparison.Ordinal) >= 0)
                {
                    Set(PingState.Error, "Didn't catch that");
                    return;
                }
                await SendTranscript(text, ct);
            }
            catch (OperationCanceledException)
            {
                SafeStop();
                if (State != PingState.Unconfigured)
                    Set(PingState.Idle, "Hold to talk");
            }
        }

        async Task SendTranscript(string text, CancellationToken ct)
        {
            Transcript = text;
            Set(PingState.Sending, "Sending");
            var nonce = Nonce.New();
            _nonce = nonce;
            NoncePrefix = nonce.Substring(0, 6);
            Debug.Log("nonce=" + NoncePrefix);
            var hook = await _webhook.Send(text, nonce, ct);
            if (!hook.Ok)
            {
                Set(PingState.Error, hook.Error);
                return;
            }
            Set(PingState.AwaitingReply, "Waiting for reply");
            var poll = await _relay.Poll(nonce, ct);
            if (poll.Ok)
            {
                ShowReply(poll.Text);
                return;
            }
            Set(PingState.Error, poll.Error);
        }

        async Task CheckOnce(CancellationToken ct)
        {
            try
            {
                var poll = await _relay.CheckAgain(_nonce, ct);
                if (poll.Ok)
                {
                    ShowReply(poll.Text);
                    return;
                }
                if (poll.Error == "relay 401" || poll.Error == "relay 400")
                {
                    Set(PingState.Error, poll.Error);
                    return;
                }
                Set(State, poll.Error ?? "No reply yet");
            }
            catch (OperationCanceledException)
            {
                Set(PingState.Idle, "Hold to talk");
            }
        }

        void ShowReply(string text)
        {
            var len = text == null ? 0 : text.Length;
            Debug.Log("reply len=" + len);
            Reply = ReplyText.Clamp(text);
            Set(PingState.Done, "Done");
        }

        MicTake SafeStop()
        {
            try
            {
                return _mic.StopRecording();
            }
            catch (Exception ex)
            {
                Debug.Log("mic err=" + ex.GetType().Name);
                return new MicTake { Samples = new float[0], Rate = 16000, Channels = 1, Seconds = 0f };
            }
        }

        void Set(PingState state, string hint)
        {
            State = state;
            Hint = hint;
            var handler = Changed;
            if (handler != null) handler();
        }
    }
}
