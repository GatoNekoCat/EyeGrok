// Copyright 2026 EyeGrok contributors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EyeGrok.Config;
using EyeGrok.Net;
using EyeGrok.Pair;
using EyeGrok.Ping;
using EyeGrok.Security;
using EyeGrok.Setup;
using EyeGrok.Voice;
using NUnit.Framework;
using UnityEngine;

namespace EyeGrok.Tests
{
    public sealed class PairingTests
    {
        [Test]
        public void T21_DammValidAndInvalid()
        {
            Assert.AreEqual(4, PairCrypto.DammInterim("572"));
            Assert.IsTrue(PairCrypto.DammValid("5724"));
            Assert.IsFalse(PairCrypto.DammValid("5727"));
            Assert.IsFalse(PairCrypto.DammValid("572"));
            Assert.IsFalse(PairCrypto.DammValid(""));
        }

        [Test]
        public void T22_QrPayloadAcceptAndReject()
        {
            var vectors = LoadVectors();
            var payload = Encoding.UTF8.GetString(Hex(vectors["qr-payload"]));
            PairMaterial ok;
            Assert.IsTrue(PairPayload.TryParse(payload, "https://example.com/relay", false, out ok));
            Assert.IsTrue(PairPayload.TryParse(payload, "https://example.com/relay/", false, out ok));
            Assert.IsTrue(PairPayload.TryParse(payload, "", false, out ok));
            Assert.IsTrue(PairPayload.TryParse(payload, null, false, out ok));
            PairMaterial bad;
            Assert.IsFalse(PairPayload.TryParse(payload.Replace("https://", "http://"), "http://example.com/relay", false, out bad));
            Assert.IsFalse(PairPayload.TryParse("EG1|https://example.com/a|b|c|d", "https://example.com/a", false, out bad));
            var shortId = payload.Replace(PairCrypto.B64Url(Hex(vectors["qr-id"])), "aaaa");
            Assert.IsFalse(PairPayload.TryParse(shortId, "https://example.com/relay", false, out bad));
            Assert.IsFalse(PairPayload.TryParse(payload, "https://example.com/other", false, out bad));
        }

        [Test]
        public void T23_VectorsDecryptByteExactly()
        {
            var vectors = LoadVectors();
            var json = Hex(vectors["json"]);
            var codeId = DeriveCodeId(vectors["code10"]);
            Assert.AreEqual(Hex(vectors["id"]), codeId);
            var plain = PairCrypto.Unprotect(Hex(vectors["iv"]), Hex(vectors["ct"]), Hex(vectors["tag"]), Hex(vectors["enc"]), Hex(vectors["mac"]), codeId);
            Assert.AreEqual(json, plain);
            var qrPlain = PairCrypto.Unprotect(Hex(vectors["iv"]), Hex(vectors["qr-ct"]), Hex(vectors["qr-tag"]), Hex(vectors["qr-enc"]), Hex(vectors["qr-mac"]), Hex(vectors["qr-id"]));
            Assert.AreEqual(json, qrPlain);
        }

        [Test]
        public async Task T24_TamperIsCorruptAndStoresNothing()
        {
            var vectors = LoadVectors();
            var store = new MemoryStore();
            var http = new FakeTransport();
            var ct = Hex(vectors["ct"]);
            ct[0] ^= 0x01;
            http.Add(200, BoxJson(Hex(vectors["iv"]), ct, Hex(vectors["tag"])), false);
            var client = new PairingClient(http, new ManualClock(), store);
            var result = await client.Redeem("https://example.com/relay", DeriveCodeId(vectors["code10"]), Hex(vectors["enc"]), Hex(vectors["mac"]), CancellationToken.None);
            Assert.AreEqual(PairResult.Corrupt, result);
            string value;
            Assert.IsFalse(store.TryGet("webhookKey", out value));

            http.Add(200, BoxJson(Hex(vectors["iv"]), Hex(vectors["ct"]), Flip(Hex(vectors["tag"]))), false);
            result = await client.Redeem("https://example.com/relay", DeriveCodeId(vectors["code10"]), Hex(vectors["enc"]), Hex(vectors["mac"]), CancellationToken.None);
            Assert.AreEqual(PairResult.Corrupt, result);
            Assert.IsFalse(store.TryGet("webhookKey", out value));
        }

        [Test]
        public async Task T25_NotFoundRetriesThenOk()
        {
            var http = new FakeTransport();
            var clock = new ManualClock();
            var store = new MemoryStore();
            for (var i = 0; i < 6; i++) http.Add(404, "{}", false);
            var client = new PairingClient(http, clock, store);
            var task = client.Redeem("https://example.com/relay", new byte[16], new byte[32], new byte[32], CancellationToken.None);
            await Pump(clock, task, 8);
            Assert.IsTrue(task.IsCompleted);
            Assert.AreEqual(PairResult.NotFound, task.Result);
            Assert.AreEqual(6, http.Requests.Count);
            string ignored;
            Assert.IsFalse(store.TryGet("webhookUrl", out ignored));

            var vectors = LoadVectors();
            var http2 = new FakeTransport();
            var clock2 = new ManualClock();
            var store2 = new MemoryStore();
            http2.Add(404, "{}", false);
            http2.Add(200, BoxJson(Hex(vectors["iv"]), Hex(vectors["ct"]), Hex(vectors["tag"])), false);
            var client2 = new PairingClient(http2, clock2, store2);
            var task2 = client2.RedeemCode("https://example.com/relay", vectors["code11"], CancellationToken.None);
            await Pump(clock2, task2, 4);
            Assert.IsTrue(task2.IsCompleted);
            Assert.AreEqual(PairResult.Ok, task2.Result);
            Assert.AreEqual(2, http2.Requests.Count);
            Assert.IsTrue(store2.TryGet("webhookKey", out ignored));
            Assert.AreEqual("0123456789abcdef", ignored);
        }

        [Test]
        public async Task T26_RateLimitAndPairingOffDoNotRetry()
        {
            var http = new FakeTransport();
            http.Add(429, "{}", false);
            http.Add(200, "{}", false);
            var client = new PairingClient(http, new ManualClock(), new MemoryStore());
            var limited = await client.Redeem("https://example.com/relay", new byte[16], new byte[32], new byte[32], CancellationToken.None);
            Assert.AreEqual(PairResult.RateLimited, limited);
            Assert.AreEqual(1, http.Requests.Count);

            var http2 = new FakeTransport();
            http2.Add(503, "{\"error\":\"pairing-off\"}", false);
            http2.Add(200, "{}", false);
            var client2 = new PairingClient(http2, new ManualClock(), new MemoryStore());
            var off = await client2.Redeem("https://example.com/relay", new byte[16], new byte[32], new byte[32], CancellationToken.None);
            Assert.AreEqual(PairResult.PairingOff, off);
            Assert.AreEqual(1, http2.Requests.Count);
        }

        [Test]
        public async Task T27_PairRegistersRedactorAndLogHidesValues()
        {
            var vectors = LoadVectors();
            var http = new FakeTransport();
            http.Add(200, BoxJson(Hex(vectors["iv"]), Hex(vectors["ct"]), Hex(vectors["tag"])), false);
            var store = new MemoryStore();
            var capture = new CaptureLog();
            var previous = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = new RedactingLogHandler(capture);
            Redactor.Clear();
            try
            {
                var client = new PairingClient(http, new ManualClock(), store);
                var result = await client.RedeemCode("https://example.com/relay", vectors["code11"], CancellationToken.None);
                Assert.AreEqual(PairResult.Ok, result);
                Assert.AreEqual("[REDACTED]", Redactor.Redact("0123456789abcdef"));
                Assert.IsFalse(string.IsNullOrEmpty(capture.Last));
                Assert.IsFalse(capture.Last.Contains("0123456789abcdef"));
                Assert.IsFalse(capture.Last.Contains("fedcba9876543210"));
                Assert.IsFalse(capture.Last.Contains("example.com"));
            }
            finally
            {
                Debug.unityLogger.logHandler = previous;
                Redactor.Clear();
            }
        }

        [Test]
        public async Task T28_NumberPadCapsAndDammSkipsNetwork()
        {
            var pad = new CodePad();
            Assert.IsFalse(pad.OkEnabled);
            for (var i = 0; i < 10; i++) pad.Push('1');
            Assert.AreEqual(10, pad.Digits.Length);
            Assert.IsFalse(pad.OkEnabled);
            pad.Push('1');
            Assert.IsTrue(pad.OkEnabled);
            Assert.AreEqual("1111 1111 111", pad.Display);
            pad.Push('2');
            Assert.AreEqual(11, pad.Digits.Length);
            pad.Backspace();
            Assert.AreEqual(10, pad.Digits.Length);
            pad.Clear();
            Assert.AreEqual("", pad.Digits);
            Assert.IsFalse(pad.OkEnabled);

            var check = PairCrypto.DammInterim("1234567890");
            var bad = "1234567890" + ((check + 1) % 10).ToString();
            for (var i = 0; i < bad.Length; i++) pad.Push(bad[i]);
            Assert.IsFalse(PairCrypto.DammValid(pad.Digits));
            var http = new FakeTransport();
            var client = new PairingClient(http, new ManualClock(), new MemoryStore());
            var message = await CodeEntry.Submit(pad, "https://example.com/relay", client, CancellationToken.None);
            Assert.AreEqual(CodeEntry.DammFail, message);
            Assert.AreEqual(0, http.Requests.Count);
            pad.Clear();
            var good = "1234567890" + check.ToString();
            for (var i = 0; i < good.Length; i++) pad.Push(good[i]);
            var missing = await CodeEntry.Submit(pad, "", client, CancellationToken.None);
            Assert.AreEqual(CodeEntry.NeedOrigin, missing);
            Assert.AreEqual(0, http.Requests.Count);
        }

        [Test]
        public async Task T29_ChecklistOnlyOnTrigger()
        {
            var model = new ChecklistModel();
            var http = new FakeTransport();
            var clock = new ManualClock();
            Assert.AreEqual("not tested", model.Routine);
            clock.Advance(600f);
            await Task.Yield();
            Assert.AreEqual(0, http.Requests.Count);

            model.EvaluateMic(true, 1);
            Assert.AreEqual("green", model.Mic);
            model.EvaluateMic(true, 0);
            Assert.AreEqual("red", model.Mic);
            model.EvaluateMic(false, 2);
            Assert.AreEqual("red", model.Mic);

            http.Add(200, "ok", false);
            await model.EvaluateRelay(http, "https://example.com/relay", CancellationToken.None);
            Assert.AreEqual("green", model.Relay);
            http.Add(401, "", false);
            await model.EvaluateRelay(http, "https://example.com/relay", CancellationToken.None);
            Assert.AreEqual("red", model.Relay);
            http.Add(0, "", true);
            await model.EvaluateRelay(http, "https://example.com/relay", CancellationToken.None);
            Assert.AreEqual("red", model.Relay);

            var done = await RunPing(true);
            model.NoteRoutine(done);
            Assert.AreEqual("green", model.Routine);
            var failed = await RunPing(false);
            model.NoteRoutine(failed);
            Assert.AreEqual("red", model.Routine);
        }

        [Test]
        public void T30_PendingOriginReadOnceAndOversizeDeleted()
        {
            var dir = Path.Combine(Path.GetTempPath(), "eyegrok-origin-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var path = Path.Combine(dir, PendingOrigin.FileName);
                File.WriteAllText(path, "https://example.com/relay\n");
                Assert.AreEqual("https://example.com/relay", PendingOrigin.Take(dir));
                Assert.AreEqual("https://example.com/relay", PendingOrigin.Current);
                Assert.IsFalse(File.Exists(path));
                Assert.IsNull(PendingOrigin.Take(dir));
                Assert.AreEqual("https://example.com/relay", PendingOrigin.Current);

                File.WriteAllText(path, new string('a', 2049));
                Assert.IsNull(PendingOrigin.Take(dir));
                Assert.IsFalse(File.Exists(path));

                File.WriteAllText(path, "one\ntwo\n");
                Assert.IsNull(PendingOrigin.Take(dir));
                Assert.IsFalse(File.Exists(path));
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        [Test]
        public void T31_ImporterIgnoresPartFile()
        {
            var dir = Path.Combine(Path.GetTempPath(), "eyegrok-part-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var path = Path.Combine(dir, "eyegrok-setup.json.part");
                File.WriteAllText(path, "{\"webhookUrl\":\"https://example.com/hook\",\"webhookKey\":\"0123456789abcdef\",\"relayUrl\":\"https://example.com/relay\",\"relayReadKey\":\"fedcba9876543210\"}");
                var store = new MemoryStore();
                var outcome = SetupImporter.TryImport(path, store, false);
                Assert.IsFalse(outcome.Found);
                Assert.IsFalse(outcome.Ok);
                Assert.IsFalse(File.Exists(path));
                string value;
                Assert.IsFalse(store.TryGet("webhookKey", out value));
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        static async Task<PingState> RunPing(bool succeed)
        {
            var clock = new ManualClock();
            var http = new FakeTransport();
            if (succeed)
            {
                http.Add(200, "", false);
                http.Add(200, "{\"text\":\"pong\"}", false);
            }
            else
            {
                http.Add(500, "", false);
            }
            var store = new MemoryStore();
            store.Put("webhookUrl", "https://example.invalid/hook");
            store.Put("webhookKey", "0123456789abcdef");
            store.Put("relayUrl", "https://example.invalid/relay");
            store.Put("relayReadKey", "0123456789abcdef");
            var controller = new PingController(new FakeMic(), new FakeStt(), new WebhookClient(http, store), new RelayClient(http, store, clock), store, clock);
            controller.NotifyConfigured();
            var task = controller.SendFixed("ping");
            await Pump(clock, task, 12);
            Assert.IsTrue(task.IsCompleted);
            return controller.State;
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

        static byte[] DeriveCodeId(string code10)
        {
            var full = PairCrypto.Hkdf(PairCrypto.Pbkdf2(code10), "id", 32);
            var id = new byte[16];
            Buffer.BlockCopy(full, 0, id, 0, 16);
            return id;
        }

        static byte[] Flip(byte[] bytes)
        {
            var copy = (byte[])bytes.Clone();
            copy[0] ^= 0x01;
            return copy;
        }

        static string BoxJson(byte[] iv, byte[] ct, byte[] tag)
        {
            return "{\"box\":{\"v\":1,\"iv\":\"" + PairCrypto.B64Url(iv) + "\",\"ct\":\"" + PairCrypto.B64Url(ct) + "\",\"mac\":\"" + PairCrypto.B64Url(tag) + "\"}}";
        }

        static Dictionary<string, string> LoadVectors()
        {
            var path = Path.Combine(Application.dataPath, "EyeGrok", "Tests", "EditMode", "Fixtures", "pair-vectors.txt");
            var map = new Dictionary<string, string>();
            var lines = File.ReadAllLines(path);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var sp = line.IndexOf(' ');
                if (sp <= 0) continue;
                map[line.Substring(0, sp)] = line.Substring(sp + 1);
            }
            return map;
        }

        static byte[] Hex(string spaced)
        {
            var parts = spaced.Split(' ');
            var bytes = new byte[parts.Length];
            for (var i = 0; i < parts.Length; i++) bytes[i] = Convert.ToByte(parts[i], 16);
            return bytes;
        }
    }
}
