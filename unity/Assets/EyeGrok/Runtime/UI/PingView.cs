// Copyright 2026 EyeGrok contributors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EyeGrok.Config;
using EyeGrok.Net;
using EyeGrok.Ping;
using EyeGrok.Security;
using EyeGrok.Voice;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace EyeGrok.UI
{
    public sealed class PingView : MonoBehaviour
    {
        public Text StatusLine;
        public Text TranscriptLine;
        public Text ReplyLine;
        public Text SetupLine;
        public HoldButton TalkButton;
        public HoldButton ForgetButton;
        public Button CancelButton;
        public Button CheckButton;
        public GameObject DebugPanel;
        public InputField DebugInput;
        public Button DebugSend;
        public MicRecorder Mic;
        public WhisperStt Stt;

        public ISecretStore Store { get { return _store; } }

        public void MarkConfigured()
        {
            if (_controller == null) return;
            _controller.NotifyConfigured();
            if (SetupLine != null) SetupLine.text = "Configured";
            Refresh();
        }

        public async Task<PingState> RunFixed(string text)
        {
            await _controller.SendFixed(text);
            return _controller.State;
        }

        PingController _controller;
        ISecretStore _store;
        InputAction _pushToTalk;
        CancellationTokenSource _forget;
        bool _modelLoading;
        AudioSource _cue;
        AudioClip _chime;
        float _nextThink;

        void Awake()
        {
            _store = SecretStores.Create();
            var transport = new UnityHttpTransport();
            var clock = new RealtimeClock();
            _controller = new PingController(
                Mic,
                Stt,
                new WebhookClient(transport, _store),
                new RelayClient(transport, _store, clock),
                _store,
                clock);
            _controller.Changed += Refresh;
            _controller.ReplyArrived += PlayReplyCue;
            PrepareCue();
            if (TalkButton != null)
            {
                TalkButton.Down = OnTalkDown;
                TalkButton.Up = OnTalkUp;
            }
            if (ForgetButton != null)
            {
                ForgetButton.Down = OnForgetDown;
                ForgetButton.Up = OnForgetUp;
            }
            if (CancelButton != null) CancelButton.onClick.AddListener(_controller.Cancel);
            if (CheckButton != null) CheckButton.onClick.AddListener(() => { var _ = _controller.CheckAgain(); });
            if (DebugSend != null) DebugSend.onClick.AddListener(OnDebugSend);
            DisableRichText(StatusLine);
            DisableRichText(TranscriptLine);
            DisableRichText(ReplyLine);
            DisableRichText(SetupLine);
            if (DebugInput != null && DebugInput.textComponent != null)
                DebugInput.textComponent.supportRichText = false;
            ApplyDebugChrome();
            BindPushToTalk();
            LoadStoredSecrets();
            Refresh();
        }

        void Update()
        {
            if (_controller == null || _controller.State != PingState.AwaitingReply) return;
            if (Time.unscaledTime < _nextThink) return;
            _nextThink = Time.unscaledTime + 0.5f;
            _controller.RefreshThinkingHint();
        }

        void Start()
        {
            TryImport();
            var _ = LoadModel();
        }

        void OnApplicationPause(bool paused)
        {
            if (!paused) TryImport();
        }

        void OnDestroy()
        {
            if (_pushToTalk != null)
            {
                _pushToTalk.Disable();
                _pushToTalk.Dispose();
            }
            if (_forget != null) _forget.Cancel();
        }

        async Task LoadModel()
        {
            if (Stt == null) return;
            _modelLoading = true;
            Refresh();
            try
            {
                await Stt.LoadOnce();
            }
            catch (Exception ex)
            {
                Debug.Log("model err=" + ex.GetType().Name);
            }
            _modelLoading = false;
            Refresh();
        }

        void TryImport()
        {
            var outcome = SetupImporter.TryImport(SetupPath(), _store, AllowLoopback());
            if (!outcome.Found) return;
            if (outcome.Ok)
            {
                _controller.NotifyConfigured();
                if (SetupLine != null) SetupLine.text = "Configured";
            }
            else if (SetupLine != null)
            {
                SetupLine.text = outcome.Message;
            }
            Refresh();
        }

        void LoadStoredSecrets()
        {
            string webhookUrl;
            string webhookKey;
            string relayUrl;
            string relayReadKey;
            if (!_store.TryGet("webhookUrl", out webhookUrl)
                || !_store.TryGet("webhookKey", out webhookKey)
                || !_store.TryGet("relayUrl", out relayUrl)
                || !_store.TryGet("relayReadKey", out relayReadKey))
            {
                if (SetupLine != null) SetupLine.text = "Not set up — tap Pair";
                return;
            }
            Redactor.Register(webhookKey);
            Redactor.Register(relayReadKey);
            Redactor.Register(SetupImporter.UrlPath(webhookUrl));
            Redactor.Register(SetupImporter.UrlPath(relayUrl));
            _controller.NotifyConfigured();
            if (SetupLine != null) SetupLine.text = "Configured";
        }

        void OnTalkDown()
        {
            _controller.Press();
        }

        void OnTalkUp()
        {
            _controller.Release();
        }

        void OnForgetDown()
        {
            if (_forget != null) _forget.Cancel();
            _forget = new CancellationTokenSource();
            var _ = ForgetAfter(_forget.Token);
        }

        void OnForgetUp()
        {
            if (_forget != null) _forget.Cancel();
        }

        async Task ForgetAfter(CancellationToken ct)
        {
            try
            {
                await Task.Delay(3000, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            _controller.Forget();
            if (SetupLine != null) SetupLine.text = "Not set up — tap Pair";
            Refresh();
        }

        void OnDebugSend()
        {
            if (!DebugTypingAllowed(InEditor(), Debug.isDebugBuild)) return;
            if (DebugInput == null || !DebugInput.gameObject.activeInHierarchy) return;
            var _ = _controller.SubmitText(DebugInput.text);
        }

        void ApplyDebugChrome()
        {
            var editor = InEditor();
            var show = DebugTypingAllowed(editor, Debug.isDebugBuild);
            if (DebugPanel != null) DebugPanel.SetActive(show);
            ApplyDebugInputObject(DebugInput != null ? DebugInput.gameObject : null, editor, Debug.isDebugBuild);
        }

        static bool InEditor()
        {
#if UNITY_EDITOR
            return true;
#else
            return false;
#endif
        }

        public static bool DebugTypingAllowed(bool editor, bool debugBuild)
        {
            return editor || debugBuild;
        }

        public static void ApplyDebugInputObject(GameObject input, bool editor, bool debugBuild)
        {
            if (input == null) return;
            input.SetActive(DebugTypingAllowed(editor, debugBuild));
        }

        void BindPushToTalk()
        {
            _pushToTalk = new InputAction("PushToTalk", InputActionType.Button);
            _pushToTalk.AddBinding("<Gamepad>/buttonSouth");
            _pushToTalk.AddBinding("<Keyboard>/space");
            _pushToTalk.started += OnPttStarted;
            _pushToTalk.canceled += OnPttCanceled;
            _pushToTalk.Enable();
        }

        void OnPttStarted(InputAction.CallbackContext _)
        {
            _controller.Press();
        }

        void OnPttCanceled(InputAction.CallbackContext _)
        {
            _controller.Release();
        }

        void Refresh()
        {
            var hint = _controller.Hint;
            if (_modelLoading && !_controller.Busy)
                hint = "Loading voice model…";
            if (StatusLine != null)
            {
                StatusLine.supportRichText = false;
                StatusLine.text = _controller.State + " — " + hint;
            }
            if (TranscriptLine != null)
            {
                TranscriptLine.supportRichText = false;
                TranscriptLine.text = TextKit.Ellipsis(_controller.Transcript ?? "", 280);
            }
            if (ReplyLine != null)
                ReplyText.Apply(new UiTextSink(ReplyLine), _controller.Reply);
            if (_controller.State == PingState.Done)
                _controller.MarkRendered();
        }

        void PrepareCue()
        {
            try
            {
                _cue = gameObject.GetComponent<AudioSource>();
                if (_cue == null) _cue = gameObject.AddComponent<AudioSource>();
                _cue.playOnAwake = false;
                _cue.spatialBlend = 0f;
                _chime = BuildChime();
            }
            catch (Exception ex)
            {
                Debug.Log("chime err=" + ex.GetType().Name);
            }
        }

        void PlayReplyCue()
        {
            try
            {
                if (_cue != null && _chime != null) _cue.PlayOneShot(_chime, 0.45f);
            }
            catch (Exception ex)
            {
                Debug.Log("chime err=" + ex.GetType().Name);
            }
            HapticTick();
        }

        static AudioClip BuildChime()
        {
            const int rate = 22050;
            const int n = 3308;
            var data = new float[n];
            for (var i = 0; i < n; i++)
            {
                var t = i / (float)rate;
                var env = Math.Exp(-18.0 * t);
                var sample = Math.Sin(2.0 * Math.PI * 880.0 * t) * 0.65
                    + Math.Sin(2.0 * Math.PI * 1318.5 * t) * 0.35;
                data[i] = (float)(sample * env * 0.4);
            }
            var clip = AudioClip.Create("eyegrok-chime", n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static void HapticTick()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator"))
                {
                    if (vibrator == null) return;
                    using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                    {
                        var sdk = version.GetStatic<int>("SDK_INT");
                        if (sdk >= 26)
                        {
                            using (var effectClass = new AndroidJavaClass("android.os.VibrationEffect"))
                            using (var effect = effectClass.CallStatic<AndroidJavaObject>("createOneShot", 40L, 80))
                                vibrator.Call("vibrate", effect);
                        }
                        else
                        {
                            vibrator.Call("vibrate", 40L);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.Log("haptic err=" + ex.GetType().Name);
            }
#endif
        }

        static void DisableRichText(Text field)
        {
            if (field != null) field.supportRichText = false;
        }

        static bool AllowLoopback()
        {
#if UNITY_EDITOR
            return true;
#else
            return false;
#endif
        }

        public static string SetupPath()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            var dir = AndroidKeystoreStore.ExternalFilesDir();
            if (!string.IsNullOrEmpty(dir))
                return Path.Combine(dir, SetupImporter.FileName);
#endif
            return Path.Combine(Application.persistentDataPath, SetupImporter.FileName);
        }

        sealed class UiTextSink : ITextSink
        {
            readonly Text _field;
            public UiTextSink(Text field) { _field = field; }
            public bool RichText
            {
                get { return _field.supportRichText; }
                set { _field.supportRichText = value; }
            }
            public string Text
            {
                get { return _field.text; }
                set { _field.text = value; }
            }
        }
    }
}
