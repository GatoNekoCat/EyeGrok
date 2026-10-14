// Copyright 2026 EyeGrok contributors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using EyeGrok.Security;
using UnityEngine;

namespace EyeGrok.Net
{
    public static class TraceClock
    {
        public static long MonoMs()
        {
            var ticks = Stopwatch.GetTimestamp();
            var freq = Stopwatch.Frequency;
            return ticks / freq * 1000L + ticks % freq * 1000L / freq;
        }

        public static long WallMs()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
    }

    // One ping. The log line and trace.jsonl carry stamps only: no transcript, no reply, no keys.
    public sealed class PingTrace
    {
        public string Tid = "";

        readonly Mark _pttDown = new Mark();
        readonly Mark _pttUp = new Mark();
        readonly Mark _sttStart = new Mark();
        readonly Mark _sttDone = new Mark();
        readonly Mark _postSent = new Mark();
        readonly Mark _postAck = new Mark();
        readonly Mark _replySeen = new Mark();
        readonly Mark _render = new Mark();
        readonly List<Poll> _polls = new List<Poll>();

        bool _flushed;
        bool _postStatusSet;
        int _postStatus;
        bool _sttSet;
        int _sttLen;
        bool _replySet;
        int _replyLen;
        bool _pingRxSet;
        long _pingRx;
        bool _emailSet;
        long _emailSent;
        string _coloPing = "";
        bool _replyRxSet;
        long _replyRx;
        string _coloReply = "";
        bool _servedSet;
        long _served;
        string _coloGet = "";
        bool _rt0;
        long _rtStart;
        bool _rt1;
        long _rtReady;
        bool _rt2;
        long _rtCurl;

        public void MarkPttDown() { _pttDown.Hit(); }
        public void MarkPttUp() { _pttUp.Hit(); }
        public void MarkSttStart() { _sttStart.Hit(); }

        public void MarkSttDone(int rawLen)
        {
            _sttDone.Hit();
            if (rawLen < 0) rawLen = 0;
            _sttLen = rawLen;
            _sttSet = true;
        }

        public void MarkPostSent() { _postSent.Hit(); }
        public void MarkPostAck() { _postAck.Hit(); }

        public void NotePost(int status, string body)
        {
            _postStatus = status;
            _postStatusSet = true;
            if (!_postAck.On) _postAck.Hit();
            if (status != 200 || string.IsNullOrEmpty(body)) return;
            long n;
            int at;
            if (JsonBody.TryReadLong(body, "t_ping_rx", 0, out n, out at))
            {
                _pingRx = n;
                _pingRxSet = true;
            }
            if (JsonBody.TryReadLong(body, "t_email_sent", 0, out n, out at))
            {
                _emailSent = n;
                _emailSet = true;
            }
            string colo;
            if (JsonBody.TryReadToken(body, "colo", 0, out colo)) _coloPing = colo;
        }

        public void MarkPollSend(int index)
        {
            var poll = Slot(index);
            if (poll == null || poll.SendOn) return;
            poll.SendM = TraceClock.MonoMs();
            poll.SendW = TraceClock.WallMs();
            poll.SendOn = true;
        }

        public void MarkPollRecv(int index, int status, string body)
        {
            var poll = Slot(index);
            if (poll == null || poll.RecvOn) return;
            poll.RecvM = TraceClock.MonoMs();
            poll.RecvW = TraceClock.WallMs();
            poll.RecvOn = true;
            poll.Status = status;
            if (string.IsNullOrEmpty(body)) return;
            if (status == 404)
            {
                ReadMiss(poll, body);
                return;
            }
            if (status != 200) return;
            string slice;
            if (!JsonBody.TryObjectAfter(body, "trace", AfterText(body), out slice)) return;
            ReadHit(poll, slice);
            _replySeen.M = poll.RecvM;
            _replySeen.W = poll.RecvW;
            _replySeen.On = true;
        }

        public void NoteReplyLen(int len)
        {
            if (len < 0) len = 0;
            _replyLen = len;
            _replySet = true;
        }

        public void MarkRender() { _render.Hit(); }

        public void SetTid(string nonce)
        {
            if (string.IsNullOrEmpty(nonce) || nonce.Length < 8) return;
            for (var i = 0; i < 8; i++)
            {
                var c = nonce[i];
                var hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f');
                if (!hex) return;
            }
            Tid = nonce.Substring(0, 8);
        }

        public void Flush(string outcome)
        {
            if (_flushed) return;
            _flushed = true;
            try
            {
                var json = Redactor.Redact(Build(CleanOutcome(outcome)));
                UnityEngine.Debug.Log("trace " + json);
                var dir = Application.persistentDataPath;
                if (!string.IsNullOrEmpty(dir))
                    File.AppendAllText(Path.Combine(dir, "trace.jsonl"), json + "\n");
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("trace err=" + ex.GetType().Name);
            }
        }

        Poll Slot(int index)
        {
            if (index < 1 || index > 125) return null;
            while (_polls.Count < index) _polls.Add(null);
            var at = index - 1;
            if (_polls[at] == null) _polls[at] = new Poll { I = index };
            return _polls[at];
        }

        void ReadMiss(Poll poll, string body)
        {
            long n;
            int at;
            if (JsonBody.TryReadLong(body, "t_served", 0, out n, out at))
            {
                poll.Wt = n;
                poll.HasWt = true;
            }
            string colo;
            if (JsonBody.TryReadToken(body, "colo", 0, out colo)) poll.Colo = colo;
        }

        void ReadHit(Poll poll, string slice)
        {
            long n;
            int at;
            if (JsonBody.TryReadLong(slice, "t_reply_served", 0, out n, out at))
            {
                poll.Wt = n;
                poll.HasWt = true;
                _served = n;
                _servedSet = true;
            }
            if (JsonBody.TryReadLong(slice, "t_reply_rx", 0, out n, out at))
            {
                _replyRx = n;
                _replyRxSet = true;
            }
            string colo;
            if (JsonBody.TryReadToken(slice, "colo_get", 0, out colo))
            {
                poll.Colo = colo;
                _coloGet = colo;
            }
            if (JsonBody.TryReadToken(slice, "colo_reply", 0, out colo)) _coloReply = colo;
            if (JsonBody.TryReadLong(slice, "t_routine_start", 0, out n, out at))
            {
                _rtStart = n;
                _rt0 = true;
            }
            if (JsonBody.TryReadLong(slice, "t_answer_ready", 0, out n, out at))
            {
                _rtReady = n;
                _rt1 = true;
            }
            if (JsonBody.TryReadLong(slice, "t_curl", 0, out n, out at))
            {
                _rtCurl = n;
                _rt2 = true;
            }
        }

        string Build(string outcome)
        {
            var sb = new StringBuilder(256);
            sb.Append("{\"v\":1,\"kind\":\"ping\",\"tid\":\"");
            sb.Append(Tid.Length == 8 ? Tid : "00000000");
            sb.Append("\",\"outcome\":\"").Append(outcome).Append('"');
            var build = SafeToken(Application.version, 24);
            if (build.Length > 0) sb.Append(",\"build\":\"").Append(build).Append('"');
            sb.Append(",\"net\":\"").Append(NetName()).Append("\",\"app\":{");
            var comma = false;
            AppendMark(sb, "t_ptt_down", _pttDown, ref comma);
            AppendMark(sb, "t_ptt_up", _pttUp, ref comma);
            AppendMark(sb, "t_stt_start", _sttStart, ref comma);
            AppendMark(sb, "t_stt_done", _sttDone, ref comma);
            AppendMark(sb, "t_post_sent", _postSent, ref comma);
            AppendMark(sb, "t_post_ack", _postAck, ref comma);
            if (_postStatusSet) AppendRaw(sb, "post_status", _postStatus.ToString(), ref comma);
            var wrotePoll = false;
            for (var i = 0; i < _polls.Count; i++)
            {
                var poll = _polls[i];
                if (poll == null || !poll.SendOn || !poll.RecvOn) continue;
                if (!wrotePoll)
                {
                    if (comma) sb.Append(',');
                    comma = true;
                    sb.Append("\"polls\":[");
                    wrotePoll = true;
                }
                else sb.Append(',');
                sb.Append("{\"i\":").Append(poll.I);
                sb.Append(",\"t_send\":{\"m\":").Append(poll.SendM).Append(",\"w\":").Append(poll.SendW).Append('}');
                sb.Append(",\"t_recv\":{\"m\":").Append(poll.RecvM).Append(",\"w\":").Append(poll.RecvW).Append('}');
                sb.Append(",\"status\":").Append(poll.Status);
                if (poll.HasWt) sb.Append(",\"w_t\":").Append(poll.Wt);
                var colo = SafeToken(poll.Colo, 8);
                if (colo.Length > 0) sb.Append(",\"colo\":\"").Append(colo).Append('"');
                sb.Append('}');
            }
            if (wrotePoll) sb.Append(']');
            AppendMark(sb, "t_reply_seen", _replySeen, ref comma);
            AppendMark(sb, "t_render", _render, ref comma);
            if (_pttDown.On && _pttUp.On) AppendRaw(sb, "rec_ms", (_pttUp.M - _pttDown.M).ToString(), ref comma);
            if (_sttSet) AppendRaw(sb, "stt_len", _sttLen.ToString(), ref comma);
            if (_replySet) AppendRaw(sb, "reply_len", _replyLen.ToString(), ref comma);
            sb.Append('}');
            if (_pingRxSet || _emailSet || _replyRxSet || _servedSet || _coloPing.Length > 0 || _coloReply.Length > 0 || _coloGet.Length > 0)
            {
                sb.Append(",\"worker\":{");
                var w = false;
                if (_pingRxSet) AppendRaw(sb, "t_ping_rx", _pingRx.ToString(), ref w);
                if (_emailSet) AppendRaw(sb, "t_email_sent", _emailSent.ToString(), ref w);
                if (_coloPing.Length > 0) AppendRaw(sb, "colo_ping", "\"" + _coloPing + "\"", ref w);
                if (_replyRxSet) AppendRaw(sb, "t_reply_rx", _replyRx.ToString(), ref w);
                if (_coloReply.Length > 0) AppendRaw(sb, "colo_reply", "\"" + _coloReply + "\"", ref w);
                if (_servedSet) AppendRaw(sb, "t_reply_served", _served.ToString(), ref w);
                if (_coloGet.Length > 0) AppendRaw(sb, "colo_get", "\"" + _coloGet + "\"", ref w);
                sb.Append('}');
            }
            if (_rt0 || _rt1 || _rt2)
            {
                sb.Append(",\"routine\":{");
                var r = false;
                if (_rt0) AppendRaw(sb, "t_routine_start", _rtStart.ToString(), ref r);
                if (_rt1) AppendRaw(sb, "t_answer_ready", _rtReady.ToString(), ref r);
                if (_rt2) AppendRaw(sb, "t_curl", _rtCurl.ToString(), ref r);
                sb.Append('}');
            }
            sb.Append('}');
            return sb.ToString();
        }

        static void AppendMark(StringBuilder sb, string name, Mark mark, ref bool comma)
        {
            if (!mark.On) return;
            if (comma) sb.Append(',');
            comma = true;
            sb.Append('"').Append(name).Append("\":{\"m\":").Append(mark.M).Append(",\"w\":").Append(mark.W).Append('}');
        }

        static void AppendRaw(StringBuilder sb, string name, string raw, ref bool comma)
        {
            if (comma) sb.Append(',');
            comma = true;
            sb.Append('"').Append(name).Append("\":").Append(raw);
        }

        static int AfterText(string json)
        {
            var key = json.IndexOf("\"text\"", StringComparison.Ordinal);
            if (key < 0) return 0;
            var colon = json.IndexOf(':', key + 6);
            if (colon < 0) return 0;
            var start = json.IndexOf('"', colon + 1);
            if (start < 0) return 0;
            for (var i = start + 1; i < json.Length; i++)
            {
                if (json[i] == '\\')
                {
                    i++;
                    continue;
                }
                if (json[i] == '"') return i + 1;
            }
            return 0;
        }

        static string CleanOutcome(string outcome)
        {
            if (outcome == "ok" || outcome == "timeout" || outcome == "cancel") return outcome;
            if (string.IsNullOrEmpty(outcome) || outcome.Length < 7 || outcome.Substring(0, 6) != "error:") return "error";
            var sb = new StringBuilder("error:");
            for (var i = 6; i < outcome.Length && sb.Length < 48; i++)
            {
                var c = outcome[i];
                var ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == ' ' || c == '_' || c == '-';
                if (ok) sb.Append(c);
            }
            return sb.ToString();
        }

        static string SafeToken(string value, int max)
        {
            if (string.IsNullOrEmpty(value)) return "";
            var sb = new StringBuilder();
            for (var i = 0; i < value.Length && sb.Length < max; i++)
            {
                var c = value[i];
                var ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '.' || c == '_' || c == '-';
                if (ok) sb.Append(c);
            }
            return sb.ToString();
        }

        static string NetName()
        {
            switch (Application.internetReachability)
            {
                case NetworkReachability.ReachableViaCarrierDataNetwork: return "cell";
                case NetworkReachability.ReachableViaLocalAreaNetwork: return "wifi";
                default: return "none";
            }
        }

        sealed class Mark
        {
            public long M;
            public long W;
            public bool On;
            public void Hit()
            {
                if (On) return;
                M = TraceClock.MonoMs();
                W = TraceClock.WallMs();
                On = true;
            }
        }

        sealed class Poll
        {
            public int I;
            public long SendM;
            public long SendW;
            public long RecvM;
            public long RecvW;
            public bool SendOn;
            public bool RecvOn;
            public int Status;
            public long Wt;
            public bool HasWt;
            public string Colo = "";
        }
    }
}
