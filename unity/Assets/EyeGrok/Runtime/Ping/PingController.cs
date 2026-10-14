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
        bool _checking;
        string _nonce = "";
        PingTrace _trace;
        float _sentAt = -1f;

        public PingController(IMic mic, IStt stt, WebhookClient webhook, RelayClient relay, ISecretStore store, IClock clock)
        {
            _mic = mic;
            _stt = stt;
            _webhook = webhook;
            _relay = relay;
            _store = store;
            _clock = clock;
            State = PingState.Unconfigured;
            Hint = "Not set up — tap Pair";
        }

        public PingState State { get; private set; }
        public string Hint { get; private set; }
        public string Transcript { get; private set; }
        public string Reply { get; private set; }
        public string NoncePrefix { get; private set; }
        public event Action Changed;
        public event Action ReplyArrived;

        public bool Busy
        {
            get
            {
                return _checking
                    || State == PingState.Recording
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
            BeginTrace();
            _trace.MarkPttDown();
            _mic.StartRecording();
            Set(PingState.Recording, "Release to send");
            _run = RecordLoop(_cts.Token);
        }

        public void Release()
        {
            if (State != PingState.Recording) return;
            if (_trace != null) _trace.MarkPttUp();
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
            _checking = true;
            var ct = new CancellationTokenSource();
            _cts = ct;
            try
            {
                _run = CheckOnce(ct.Token);
                await _run;
            }
            finally
            {
                _checking = false;
            }
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

        public void RefreshThinkingHint()
        {
            if (State != PingState.AwaitingReply) return;
            var hint = ThinkingHint();
            if (hint == Hint) return;
            Hint = hint;
            var handler = Changed;
            if (handler != null) handler();
        }

        public void MarkRendered()
        {
            if (_trace != null) _trace.MarkRender();
        }

        public void Forget()
        {
            if (_cts != null) _cts.Cancel();
            if (_slice != null) _slice.Cancel();
            FlushTrace("cancel");
            if (_store != null) _store.DeleteAll();
            Redactor.Clear();
            _nonce = "";
            Transcript = "";
            Reply = "";
            Set(PingState.Unconfigured, "Not set up — tap Pair");
        }

        public async Task SendFixed(string text)
        {
            if (State == PingState.Unconfigured || Busy) return;
            if (string.IsNullOrEmpty(text)) return;
            _cts = new CancellationTokenSource();
            _run = SendTranscript(text, _cts.Token);
            await _run;
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
                    FlushTrace("cancel");
                    Set(PingState.Idle, "Hold to talk");
                    return;
                }
                var take = SafeStop();
                if (_trace != null) _trace.MarkSttStart();
                var seconds = take != null ? take.Seconds : 0f;
                if (seconds < MinRecordSeconds)
                {
                    FlushTrace("error:short");
                    Set(PingState.Idle, "Too short");
                    return;
                }
                Set(PingState.Transcribing, "Transcribing");
                var text = await _stt.Transcribe(take.Samples, take.Rate, take.Channels, ct);
                ct.ThrowIfCancellationRequested();
                var len = text == null ? 0 : text.Length;
                if (_trace != null) _trace.MarkSttDone(len);
                Debug.Log("stt len=" + len);
                text = text == null ? "" : text.Trim();
                if (text.Length == 0 || text.IndexOf("[BLANK_AUDIO]", StringComparison.Ordinal) >= 0)
                {
                    FlushTrace("error:blank");
                    Set(PingState.Error, "Didn't catch that");
                    return;
                }
                await SendTranscript(text, ct);
            }
            catch (OperationCanceledException)
            {
                SafeStop();
                FlushTrace("cancel");
                if (State != PingState.Unconfigured)
                    Set(PingState.Idle, "Hold to talk");
            }
        }

        async Task SendTranscript(string text, CancellationToken ct)
        {
            try
            {
                Transcript = text;
                Set(PingState.Sending, "Sending");
                var nonce = Nonce.New();
                _nonce = nonce;
                NoncePrefix = nonce.Substring(0, 6);
                if (_trace == null) BeginTrace();
                _trace.SetTid(nonce);
                Debug.Log("nonce=" + NoncePrefix);
                var hook = await _webhook.Send(text, nonce, ct);
                if (!hook.Ok)
                {
                    FlushTrace("error:" + (hook.Error ?? "webhook"));
                    Set(PingState.Error, hook.Error);
                    return;
                }
                _sentAt = _clock.Now;
                Set(PingState.AwaitingReply, ThinkingHint());
                var poll = await _relay.Poll(nonce, ct);
                if (poll.Ok)
                {
                    ShowReply(poll.Text);
                    return;
                }
                FlushTrace(PollOutcome(poll.Error));
                Set(PingState.Error, poll.Error);
            }
            catch (OperationCanceledException)
            {
                FlushTrace("cancel");
                throw;
            }
        }

        async Task CheckOnce(CancellationToken ct)
        {
            try
            {
                var poll = await _relay.CheckAgain(_nonce, ct);
                ct.ThrowIfCancellationRequested();
                if (State == PingState.Unconfigured) return;
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
                if (State != PingState.Unconfigured)
                    Set(PingState.Idle, "Hold to talk");
            }
        }

        void ShowReply(string text)
        {
            var len = text == null ? 0 : text.Length;
            Debug.Log("reply len=" + len);
            Reply = ReplyText.Clamp(text);
            if (_trace != null) _trace.NoteReplyLen(len);
            Set(PingState.Done, "Done");
            var arrived = ReplyArrived;
            if (arrived != null) arrived();
            FlushTrace("ok");
        }

        void BeginTrace()
        {
            _trace = new PingTrace();
            if (_webhook != null) _webhook.Trace = _trace;
            if (_relay != null) _relay.Trace = _trace;
        }

        void FlushTrace(string outcome)
        {
            var trace = _trace;
            _trace = null;
            if (_webhook != null) _webhook.Trace = null;
            if (_relay != null) _relay.Trace = null;
            if (trace != null) trace.Flush(outcome);
        }

        string ThinkingHint()
        {
            return "Delivered, thinking " + ThinkingSeconds() + " s";
        }

        int ThinkingSeconds()
        {
            if (_sentAt < 0f) return 0;
            var elapsed = _clock.Now - _sentAt;
            if (elapsed < 0f) return 0;
            if (elapsed > 9999f) return 9999;
            return (int)elapsed;
        }

        static string PollOutcome(string error)
        {
            if (string.IsNullOrEmpty(error)) return "error:relay";
            if (error.IndexOf("120", StringComparison.Ordinal) >= 0) return "timeout";
            return "error:" + error;
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
