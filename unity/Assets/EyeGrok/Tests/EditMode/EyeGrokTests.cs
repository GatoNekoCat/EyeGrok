// Copyright 2026 EyeGrok contributors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using EyeGrok.Config;
using EyeGrok.Net;
using EyeGrok.Ping;
using EyeGrok.Security;
using EyeGrok.UI;
using EyeGrok.Voice;
using NUnit.Framework;
using UnityEngine;

namespace EyeGrok.Tests
{
    public sealed class ManualClock : IClock
    {
        public float Now { get; private set; }
        public readonly List<float> Delays = new List<float>();
        public readonly List<Slot> Pending = new List<Slot>();

        public Task Wait(float seconds, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<bool>();
            var slot = new Slot { Seconds = seconds, Tcs = tcs };
            Pending.Add(slot);
            Delays.Add(seconds);
            if (ct.CanBeCanceled)
            {
                ct.Register(delegate
                {
                    Pending.Remove(slot);
                    tcs.TrySetCanceled(ct);
                });
            }
            return tcs.Task;
        }

        public void CompleteNext()
        {
            var slot = Pending[0];
            Pending.RemoveAt(0);
            Now += slot.Seconds;
            slot.Tcs.TrySetResult(true);
        }

        public void Advance(float seconds)
        {
            Now += seconds;
        }

        public sealed class Slot
        {
            public float Seconds;
            public TaskCompletionSource<bool> Tcs;
        }
    }

    public sealed class FakeTransport : IHttpTransport
    {
        public readonly List<HttpRequest> Requests = new List<HttpRequest>();
        readonly Queue<HttpResponse> _responses = new Queue<HttpResponse>();
        public TaskCompletionSource<HttpResponse> Hold;

        public void Add(int status, string body, bool network)
        {
            _responses.Enqueue(new HttpResponse { Status = status, Body = body, NetworkError = network });
        }

        public Task<HttpResponse> Send(HttpRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            if (ct.IsCancellationRequested)
                return Task.FromCanceled<HttpResponse>(ct);
            if (Hold != null)
            {
                if (ct.CanBeCanceled)
                    ct.Register(delegate { Hold.TrySetCanceled(ct); });
                return Hold.Task;
            }
            if (_responses.Count == 0)
                return Task.FromResult(new HttpResponse { Status = 0, Body = "", NetworkError = true });
            return Task.FromResult(_responses.Dequeue());
        }
    }

    public sealed class MemoryStore : ISecretStore
    {
        readonly Dictionary<string, string> _values = new Dictionary<string, string>();
        public bool Put(string name, string value) { _values[name] = value; return true; }
        public bool TryGet(string name, out string value) { return _values.TryGetValue(name, out value); }
        public void DeleteAll() { _values.Clear(); }
    }

    public sealed class FailingStore : ISecretStore
    {
        public bool Throw;
        public bool Put(string name, string value)
        {
            if (Throw) throw new InvalidOperationException(value);
            return false;
        }
        public bool TryGet(string name, out string value) { value = null; return false; }
        public void DeleteAll() { }
    }

    public sealed class FakeMic : IMic
    {
        public float Seconds = 1f;
        public int Starts;
        public void StartRecording() { Starts++; }
        public MicTake StopRecording()
        {
            return new MicTake
            {
                Samples = new[] { 0.1f },
                Rate = 16000,
                Channels = 1,
                Seconds = Seconds
            };
        }
    }

    public sealed class FakeStt : IStt
    {
        public bool Ready { get; set; }
        public string Text = "hello";
        public int Calls;
        public TaskCompletionSource<string> Gate;
        public FakeStt() { Ready = true; }
        public Task<string> Transcribe(float[] samples, int rate, int channels, CancellationToken ct)
        {
            Calls++;
            if (Gate != null) return Gate.Task;
            return Task.FromResult(Text);
        }
    }

    public sealed class CaptureLog : ILogHandler
    {
        public string Last;
        public void LogFormat(LogType logType, UnityEngine.Object context, string format, params object[] args)
        {
            Last = args == null || args.Length == 0 ? format : string.Format(format, args);
        }
        public void LogException(Exception exception, UnityEngine.Object context)
        {
            Last = exception == null ? "" : exception.ToString();
        }
    }

    public sealed class FakeSink : ITextSink
    {
        public bool RichText { get; set; }
        public string Text { get; set; }
    }

    public sealed class EyeGrokTests
    {
        const string FixtureValue = "0123456789abcdef";
        const string NonceHex = "0123456789abcdef0123456789abcdef";

        [Test]
        public void T01_NonceIs32LowerHexAndUnique()
        {
            var seen = new HashSet<string>();
            for (var i = 0; i < 10000; i++)
            {
                var nonce = Nonce.New();
                Assert.IsTrue(Regex.IsMatch(nonce, "^[0-9a-f]{32}$"));
                Assert.IsTrue(seen.Add(nonce));
            }
        }

        [Test]
        public void T02_RedactorMasksSecretBearerAndUrlPath()
        {
            Redactor.Clear();
            Redactor.Register("super-secret-value");
            var masked = Redactor.Redact("super-secret-value Bearer xyz https://example.invalid/hideme");
            Assert.IsFalse(masked.Contains("super-secret-value"));
            Assert.IsFalse(masked.Contains("xyz"));
            Assert.IsFalse(masked.Contains("hideme"));
            Assert.IsTrue(masked.Contains("Bearer [REDACTED]"));
            Assert.IsTrue(masked.Contains("https://example.invalid/[\u2026]"));
            Assert.AreEqual("plain words", Redactor.Redact("plain words"));
            Redactor.Clear();
        }

        [Test]
        public void T03_LogHandlerRedactsSecretAndException()
        {
            Redactor.Clear();
            Redactor.Register("super-secret-value");
            var capture = new CaptureLog();
            var handler = new RedactingLogHandler(capture);
            handler.LogFormat(LogType.Log, null, "saw {0}", "super-secret-value");
            Assert.IsFalse(capture.Last.Contains("super-secret-value"));
            handler.LogException(new InvalidOperationException("super-secret-value"), null);
            Assert.IsFalse(capture.Last.Contains("super-secret-value"));
            Redactor.Clear();
        }

        [Test]
        public void T04_SetupValidation()
        {
            SetupData data;
            string field;
            var clear = "http" + "://example.invalid/a";
            Assert.IsFalse(SetupData.TryParse("{\"webhookUrl\":\"" + clear + "\",\"webhookKey\":\"" + FixtureValue + "\",\"relayUrl\":\"https://example.invalid/b\",\"relayReadKey\":\"" + FixtureValue + "\"}", false, out data, out field));
            Assert.AreEqual("webhookUrl", field);
            Assert.IsFalse(SetupData.TryParse(ValidJson(""), false, out data, out field));
            Assert.AreEqual("webhookKey", field);
            Assert.IsFalse(SetupData.TryParse(ValidJson("short key"), false, out data, out field));
            Assert.AreEqual("webhookKey", field);
            Assert.IsFalse(SetupData.TryParse(ValidJson(new string('k', 513)), false, out data, out field));
            Assert.AreEqual("webhookKey", field);
            Assert.IsFalse(SetupData.TryParse(ValidJson("has a space inside"), false, out data, out field));
            Assert.AreEqual("webhookKey", field);
            Assert.IsFalse(SetupData.TryParse("{\"foo\":1}", false, out data, out field));
            Assert.IsFalse(SetupData.TryParse("not-json", false, out data, out field));
            Assert.IsTrue(SetupData.TryParse(ValidJson(FixtureValue), false, out data, out field));
        }

        [Test]
        public void T05_ImporterDeletesFileOnSuccessAndFailure()
        {
            var dir = Path.Combine(Path.GetTempPath(), "eyegrok-t05-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var okPath = Path.Combine(dir, "ok.json");
                File.WriteAllText(okPath, ValidJson(FixtureValue));
                var store = new MemoryStore();
                var ok = SetupImporter.TryImport(okPath, store, false);
                Assert.IsTrue(ok.Ok);
                Assert.AreEqual("Setup imported", ok.Message);
                Assert.IsFalse(ok.Message.Contains(FixtureValue));
                Assert.IsFalse(File.Exists(okPath));
                string stored;
                Assert.IsTrue(store.TryGet("webhookKey", out stored));
                Assert.AreEqual(FixtureValue, stored);

                var badPath = Path.Combine(dir, "bad.json");
                File.WriteAllText(badPath, "{");
                var bad = SetupImporter.TryImport(badPath, store, false);
                Assert.IsFalse(bad.Ok);
                Assert.IsTrue(bad.Found);
                Assert.IsFalse(File.Exists(badPath));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Test]
        public async Task T06_WebhookBodyStatusAndNoRetry()
        {
            await AssertWebhook(200, true);
            await AssertWebhook(202, false);
            await AssertWebhook(401, false);
            await AssertWebhook(500, false);
        }

        [Test]
        public async Task T07_RelayScheduleDeadlineAndSuccess()
        {
            var clock = new ManualClock();
            var http = new FakeTransport();
            http.Add(404, "{}", false);
            http.Add(404, "{}", false);
            http.Add(200, "{\"text\":\"four\"}", false);
            var relay = new RelayClient(http, Store(), clock);
            var task = relay.Poll(NonceHex, CancellationToken.None);
            await Pump(clock, task, 10);
            Assert.IsTrue(task.Result.Ok);
            Assert.AreEqual("four", task.Result.Text);
            Assert.AreEqual(5f, clock.Delays[0]);
            Assert.AreEqual(4f, clock.Delays[1]);
            Assert.AreEqual(4f, clock.Delays[2]);
            Assert.LessOrEqual(task.Result.Requests, RelayClient.MaxRequests);
            Assert.AreEqual(120f, RelayClient.DeadlineSeconds);

            var clock2 = new ManualClock();
            var http2 = new FakeTransport();
            for (var i = 0; i < 40; i++) http2.Add(404, "{}", false);
            var relay2 = new RelayClient(http2, Store(), clock2);
            var deadline = relay2.Poll(NonceHex, CancellationToken.None);
            await Pump(clock2, deadline, 80);
            Assert.IsFalse(deadline.Result.Ok);
            StringAssert.Contains("120", deadline.Result.Error);
            Assert.LessOrEqual(deadline.Result.Requests, 30);
            Assert.GreaterOrEqual(deadline.Result.Requests, 20);
            Assert.GreaterOrEqual(clock2.Now, 100f);
            Assert.LessOrEqual(clock2.Now, 120f);
        }

        [Test]
        public async Task T08_Relay401AndBackoff()
        {
            var clock = new ManualClock();
            var http = new FakeTransport();
            http.Add(401, "{}", false);
            var task = new RelayClient(http, Store(), clock).Poll(NonceHex, CancellationToken.None);
            await Pump(clock, task, 5);
            Assert.IsFalse(task.Result.Ok);
            Assert.AreEqual("relay 401", task.Result.Error);
            Assert.AreEqual(1, http.Requests.Count);
            Assert.AreEqual(1, clock.Delays.Count);

            var clock2 = new ManualClock();
            var http2 = new FakeTransport();
            for (var i = 0; i < 6; i++) http2.Add(503, "{}", false);
            var task2 = new RelayClient(http2, Store(), clock2).Poll(NonceHex, CancellationToken.None);
            await Pump(clock2, task2, 4);
            Assert.AreEqual(5f, clock2.Delays[0]);
            Assert.AreEqual(8f, clock2.Delays[1]);
            Assert.AreEqual(16f, clock2.Delays[2]);
            Assert.AreEqual(16f, clock2.Delays[3]);
        }

        [Test]
        public async Task T09_HappyPathStates()
        {
            var ctx = NewCtx();
            var seen = new List<PingState>();
            ctx.Controller.Changed += () => seen.Add(ctx.Controller.State);
            ctx.Controller.NotifyConfigured();
            ctx.Controller.Press();
            ctx.Controller.Release();
            await Pump(ctx.Clock, ctx.ControllerRun(), 10);
            Assert.AreEqual(PingState.Done, ctx.Controller.State);
            Assert.Contains(PingState.Recording, seen);
            Assert.Contains(PingState.Transcribing, seen);
            Assert.Contains(PingState.Sending, seen);
            Assert.Contains(PingState.AwaitingReply, seen);
            Assert.Contains(PingState.Done, seen);
            Assert.AreEqual("hello", ctx.Controller.Transcript);
            Assert.AreEqual("four", ctx.Controller.Reply);
        }

        [Test]
        public async Task T10_TooShortEmptyAndCancel()
        {
            var shortCtx = NewCtx();
            shortCtx.Mic.Seconds = 0.2f;
            shortCtx.Controller.NotifyConfigured();
            shortCtx.Controller.Press();
            shortCtx.Controller.Release();
            await Pump(shortCtx.Clock, shortCtx.ControllerRun(), 6);
            Assert.AreEqual(PingState.Idle, shortCtx.Controller.State);
            Assert.AreEqual("Too short", shortCtx.Controller.Hint);
            Assert.AreEqual(0, shortCtx.Http.Requests.Count);

            var empty = NewCtx();
            empty.Stt.Text = "";
            empty.Controller.NotifyConfigured();
            empty.Controller.Press();
            empty.Controller.Release();
            await Pump(empty.Clock, empty.ControllerRun(), 6);
            Assert.AreEqual(PingState.Error, empty.Controller.State);
            Assert.AreEqual("Didn't catch that", empty.Controller.Hint);

            var cancel = NewCtx();
            cancel.Controller.NotifyConfigured();
            cancel.Controller.Press();
            cancel.Controller.Release();
            await Until(cancel, PingState.AwaitingReply);
            var calls = cancel.Http.Requests.Count;
            cancel.Controller.Cancel();
            await Pump(cancel.Clock, cancel.ControllerRun(), 6);
            Assert.AreEqual(PingState.Idle, cancel.Controller.State);
            Assert.AreEqual(calls, cancel.Http.Requests.Count);
        }

        [Test]
        public async Task T11_PressWhileBusyIgnored()
        {
            var ctx = NewCtx();
            ctx.Stt.Gate = new TaskCompletionSource<string>();
            ctx.Controller.NotifyConfigured();
            ctx.Controller.Press();
            ctx.Controller.Release();
            await Until(ctx, PingState.Transcribing);
            ctx.Controller.Press();
            Assert.AreEqual(1, ctx.Stt.Calls);
            Assert.AreEqual(1, ctx.Mic.Starts);
            Assert.AreEqual(PingState.Transcribing, ctx.Controller.State);
            ctx.Stt.Gate.SetResult("hello");
            ctx.Controller.Cancel();
            await Pump(ctx.Clock, ctx.ControllerRun(), 4);
        }

        [Test]
        public async Task T12_IdleStatesOwnNothing()
        {
            await AssertQuiet(await Make(PingState.Unconfigured));
            await AssertQuiet(await Make(PingState.Idle));
            await AssertQuiet(await Make(PingState.Error));
            await AssertQuiet(await Make(PingState.Done));
        }

        [Test]
        public void T13_UnconfiguredThenImport()
        {
            var ctx = NewCtx();
            ctx.Controller.Press();
            Assert.AreEqual(PingState.Unconfigured, ctx.Controller.State);
            Assert.AreEqual(0, ctx.Mic.Starts);
            ctx.Controller.NotifyConfigured();
            Assert.AreEqual(PingState.Idle, ctx.Controller.State);
        }

        [Test]
        public void T14_ReplyRichTextOffAndTruncated()
        {
            var sink = new FakeSink { RichText = true, Text = "x" };
            ReplyText.Apply(sink, new string('a', 4001));
            Assert.IsFalse(sink.RichText);
            Assert.AreEqual(4000, sink.Text.Length);
            ReplyText.Apply(sink, "ok");
            Assert.AreEqual("ok", sink.Text);
            Assert.IsFalse(sink.RichText);
        }

        [Test]
        public async Task T15_CancelDuringTranscribeSendsNothing()
        {
            var ctx = NewCtx();
            ctx.Stt.Gate = new TaskCompletionSource<string>();
            ctx.Controller.NotifyConfigured();
            ctx.Controller.Press();
            ctx.Controller.Release();
            await Until(ctx, PingState.Transcribing);
            ctx.Controller.Cancel();
            ctx.Stt.Gate.TrySetResult("hello");
            await Pump(ctx.Clock, ctx.ControllerRun(), 8);
            Assert.AreEqual(PingState.Idle, ctx.Controller.State);
            Assert.AreEqual(0, ctx.Http.Requests.Count);
        }

        [Test]
        public void T16_SaveFailureIsNotImported()
        {
            Redactor.Clear();
            var dir = Path.Combine(Path.GetTempPath(), "eyegrok-t16-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                AssertSaveFailure(dir, new FailingStore());
                AssertSaveFailure(dir, new FailingStore { Throw = true });
            }
            finally
            {
                Directory.Delete(dir, true);
                Redactor.Clear();
            }
        }

        [Test]
        public async Task T17_CheckAgainBlocksPttAndForgetCancels()
        {
            var ctx = NewCtx();
            ctx.Controller.NotifyConfigured();
            ctx.Controller.Press();
            ctx.Controller.Release();
            await Pump(ctx.Clock, ctx.ControllerRun(), 12);
            Assert.AreEqual(PingState.Done, ctx.Controller.State);
            var calls = ctx.Http.Requests.Count;
            var starts = ctx.Mic.Starts;

            ctx.Http.Hold = new TaskCompletionSource<HttpResponse>();
            var check = ctx.Controller.CheckAgain();
            Assert.IsTrue(ctx.Controller.Busy);
            Assert.AreEqual(calls + 1, ctx.Http.Requests.Count);

            ctx.Controller.Press();
            Assert.AreEqual(starts, ctx.Mic.Starts);
            Assert.AreEqual(PingState.Done, ctx.Controller.State);

            var again = ctx.Controller.CheckAgain();
            Assert.IsTrue(again.IsCompleted);
            Assert.AreEqual(calls + 1, ctx.Http.Requests.Count);

            ctx.Controller.Forget();
            await Pump(ctx.Clock, check, 8);
            Assert.AreEqual(PingState.Unconfigured, ctx.Controller.State);
            Assert.IsFalse(ctx.Controller.Busy);
            Assert.AreEqual(calls + 1, ctx.Http.Requests.Count);
        }

        static void AssertSaveFailure(string dir, ISecretStore store)
        {
            var path = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(path, ValidJson(FixtureValue));
            var outcome = SetupImporter.TryImport(path, store, false);
            Assert.IsTrue(outcome.Found);
            Assert.IsFalse(outcome.Ok);
            Assert.AreEqual("Setup save failed", outcome.Message);
            Assert.IsFalse(outcome.Message.Contains(FixtureValue));
            Assert.IsFalse(outcome.Message.Contains("example.invalid"));
            Assert.IsFalse(File.Exists(path));
            Assert.IsTrue(Redactor.Redact(FixtureValue).Contains(FixtureValue));
        }

        static async Task AssertQuiet(Ctx ctx)
        {
            Assert.IsFalse(ctx.Controller.HasPendingWork);
            Assert.AreEqual(0, ctx.Clock.Pending.Count);
            var calls = ctx.Http.Requests.Count;
            ctx.Clock.Advance(600f);
            await Task.Yield();
            Assert.AreEqual(calls, ctx.Http.Requests.Count);
            Assert.AreEqual(0, ctx.Clock.Pending.Count);
            Assert.IsFalse(ctx.Controller.HasPendingWork);
        }

        static async Task<Ctx> Make(PingState target)
        {
            var ctx = NewCtx();
            if (target == PingState.Unconfigured) return ctx;
            ctx.Controller.NotifyConfigured();
            if (target == PingState.Idle)
            {
                ctx.Mic.Seconds = 0.1f;
                ctx.Controller.Press();
                ctx.Controller.Release();
                await Pump(ctx.Clock, ctx.ControllerRun(), 6);
                Assert.AreEqual(PingState.Idle, ctx.Controller.State);
                return ctx;
            }
            if (target == PingState.Error)
            {
                ctx.Stt.Text = "";
                ctx.Controller.Press();
                ctx.Controller.Release();
                await Pump(ctx.Clock, ctx.ControllerRun(), 6);
                Assert.AreEqual(PingState.Error, ctx.Controller.State);
                return ctx;
            }
            ctx.Controller.Press();
            ctx.Controller.Release();
            await Pump(ctx.Clock, ctx.ControllerRun(), 10);
            Assert.AreEqual(PingState.Done, ctx.Controller.State);
            return ctx;
        }

        static async Task Until(Ctx ctx, PingState state)
        {
            for (var i = 0; i < 40 && ctx.Controller.State != state; i++)
            {
                if (ctx.Clock.Pending.Count > 0) ctx.Clock.CompleteNext();
                await Task.Yield();
                await Task.Yield();
            }
            Assert.AreEqual(state, ctx.Controller.State);
        }

        static async Task Pump(ManualClock clock, Task task, int steps)
        {
            for (var i = 0; i < steps && task != null && !task.IsCompleted; i++)
            {
                if (clock.Pending.Count > 0) clock.CompleteNext();
                await Task.Yield();
                await Task.Yield();
            }
        }

        static async Task AssertWebhook(int status, bool ok)
        {
            var http = new FakeTransport();
            http.Add(status, "", false);
            var result = await new WebhookClient(http, Store()).Send("hello", NonceHex, CancellationToken.None);
            Assert.AreEqual(ok, result.Ok);
            Assert.AreEqual(1, http.Requests.Count);
            Assert.AreEqual("POST", http.Requests[0].Method);
            Assert.AreEqual("{\"text\":\"hello\",\"nonce\":\"" + NonceHex + "\"}", http.Requests[0].Body);
            Assert.AreEqual("Bearer " + FixtureValue, http.Requests[0].Authorization);
            if (!ok) StringAssert.Contains("webhook " + status, result.Error);
        }

        static MemoryStore Store()
        {
            var store = new MemoryStore();
            store.Put("webhookUrl", "https://example.invalid/hook");
            store.Put("webhookKey", FixtureValue);
            store.Put("relayUrl", "https://example.invalid/relay");
            store.Put("relayReadKey", FixtureValue);
            return store;
        }

        static string ValidJson(string key)
        {
            return "{\"webhookUrl\":\"https://example.invalid/hook\",\"webhookKey\":\"" + key + "\",\"relayUrl\":\"https://example.invalid/relay\",\"relayReadKey\":\"" + key + "\"}";
        }

        static Ctx NewCtx()
        {
            var ctx = new Ctx();
            ctx.Clock = new ManualClock();
            ctx.Http = new FakeTransport();
            ctx.Http.Add(200, "", false);
            ctx.Http.Add(200, "{\"text\":\"four\"}", false);
            ctx.Store = Store();
            ctx.Mic = new FakeMic();
            ctx.Stt = new FakeStt();
            ctx.Controller = new PingController(ctx.Mic, ctx.Stt, new WebhookClient(ctx.Http, ctx.Store), new RelayClient(ctx.Http, ctx.Store, ctx.Clock), ctx.Store, ctx.Clock);
            return ctx;
        }

        sealed class Ctx
        {
            public ManualClock Clock;
            public FakeTransport Http;
            public MemoryStore Store;
            public FakeMic Mic;
            public FakeStt Stt;
            public PingController Controller;
            public Task ControllerRun()
            {
                return Controller.CurrentRun;
            }
        }
    }
}
