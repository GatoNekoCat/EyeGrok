// Copyright 2026 EyeGrok contributors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EyeGrok.Net;
using EyeGrok.Pair;
using EyeGrok.Ping;
using EyeGrok.Security;
using EyeGrok.Setup;
using UnityEngine;
using UnityEngine.UI;

namespace EyeGrok.UI
{
    public sealed class PairingPanel : MonoBehaviour
    {
        public const string ChecklistShownKey = "eyegrok.checklist.shown";

        public PingView View;
        public GameObject PairRoot;
        public GameObject PadRoot;
        public GameObject UsbRoot;
        public GameObject ChecklistRoot;
        public GameObject ScanButton;
        public Button OpenPairButton;
        public Button OpenChecklistButton;
        public Button RepairButton;
        public Text PadDisplay;
        public Text PadMessage;
        public Text MicLine;
        public Text RelayLine;
        public Text RoutineLine;

        readonly CodePad _pad = new CodePad();
        readonly ChecklistModel _model = new ChecklistModel();
        PairingClient _client;
#if EYEGROK_QR
        QrScanner _scanner;
#endif

        void Awake()
        {
#if !EYEGROK_QR
            if (ScanButton != null) ScanButton.SetActive(false);
#endif
            if (OpenPairButton != null) OpenPairButton.onClick.AddListener(ShowPair);
            if (RepairButton != null) RepairButton.onClick.AddListener(ShowPair);
            if (OpenChecklistButton != null) OpenChecklistButton.onClick.AddListener(ShowChecklist);
            WirePad();
            WirePair();
            WireChecklist();
            HideOverlays();
        }

        void OnDisable()
        {
#if EYEGROK_QR
            if (_scanner != null) _scanner.Stop();
#endif
        }

        void Update()
        {
#if EYEGROK_QR
            if (_scanner == null || !_scanner.Running) return;
            if (_scanner.Tick())
            {
                var text = _scanner.Text;
                _scanner.Stop();
                var _ = FinishQr(text);
            }
#endif
        }

        void WirePair()
        {
            if (PairRoot == null) return;
            var buttons = PairRoot.GetComponentsInChildren<Button>(true);
            for (var i = 0; i < buttons.Length; i++)
            {
                var button = buttons[i];
                var name = button.gameObject.name;
                if (name == "Enter code") button.onClick.AddListener(ShowPad);
                else if (name == "Use USB") button.onClick.AddListener(ShowUsb);
#if EYEGROK_QR
                else if (name == "Scan QR") button.onClick.AddListener(StartScan);
#endif
            }
        }

        void WirePad()
        {
            if (PadRoot == null) return;
            var buttons = PadRoot.GetComponentsInChildren<Button>(true);
            for (var i = 0; i < buttons.Length; i++)
            {
                var button = buttons[i];
                var name = button.gameObject.name;
                if (name.Length == 1 && name[0] >= '0' && name[0] <= '9')
                {
                    var digit = name[0];
                    button.onClick.AddListener(delegate { Push(digit); });
                }
                else if (name == "Backspace") button.onClick.AddListener(Backspace);
                else if (name == "Clear") button.onClick.AddListener(ClearPad);
                else if (name == "OK") button.onClick.AddListener(delegate { var _ = SubmitPad(); });
            }
        }

        void WireChecklist()
        {
            if (ChecklistRoot == null) return;
            var buttons = ChecklistRoot.GetComponentsInChildren<Button>(true);
            for (var i = 0; i < buttons.Length; i++)
            {
                if (buttons[i].gameObject.name == "Test ping")
                    buttons[i].onClick.AddListener(delegate { var _ = RunTestPing(); });
            }
        }

        public void ShowPair()
        {
            HideOverlays();
            if (PairRoot != null) PairRoot.SetActive(true);
        }

        public void ShowPad()
        {
            HideOverlays();
            if (PadRoot != null) PadRoot.SetActive(true);
            _pad.Clear();
            PaintPad("");
        }

        public void ShowUsb()
        {
            HideOverlays();
            if (UsbRoot != null) UsbRoot.SetActive(true);
        }

        public void ShowChecklist()
        {
            HideOverlays();
            if (ChecklistRoot != null) ChecklistRoot.SetActive(true);
            var _ = RefreshChecklist();
        }

        public void HideOverlays()
        {
#if EYEGROK_QR
            if (_scanner != null) _scanner.Stop();
#endif
            if (PairRoot != null) PairRoot.SetActive(false);
            if (PadRoot != null) PadRoot.SetActive(false);
            if (UsbRoot != null) UsbRoot.SetActive(false);
            if (ChecklistRoot != null) ChecklistRoot.SetActive(false);
        }

        void Push(char digit)
        {
            _pad.Push(digit);
            PaintPad("");
        }

        void Backspace()
        {
            _pad.Backspace();
            PaintPad("");
        }

        void ClearPad()
        {
            _pad.Clear();
            PaintPad("");
        }

        void PaintPad(string message)
        {
            if (PadDisplay != null)
            {
                PadDisplay.supportRichText = false;
                PadDisplay.text = _pad.Display;
            }
            if (PadMessage != null)
            {
                PadMessage.supportRichText = false;
                PadMessage.text = message;
            }
        }

        async Task SubmitPad()
        {
            if (!_pad.OkEnabled) return;
            if (!PairCrypto.DammValid(_pad.Digits))
            {
                PaintPad(CodeEntry.DammFail);
                return;
            }
            var relay = ResolveRelay();
            if (string.IsNullOrEmpty(relay))
            {
                PaintPad(CodeEntry.NeedOrigin);
                return;
            }
            var client = Client();
            if (client == null) return;
            PaintPad("Checking code…");
            var code = _pad.Digits;
            var derived = await Task.Run(delegate { return Derive(code); });
            var result = await client.Redeem(relay, derived.Id, derived.Enc, derived.Mac, CancellationToken.None);
            if (result == PairResult.Ok)
            {
                if (View != null) View.MarkConfigured();
                PaintPad("");
                NoteChecklistShown();
                return;
            }
            PaintPad(result.ToString());
        }

        static Derived Derive(string code11)
        {
            var master = PairCrypto.Pbkdf2(code11.Substring(0, 10));
            var idFull = PairCrypto.Hkdf(master, "id", 32);
            var id = new byte[16];
            Buffer.BlockCopy(idFull, 0, id, 0, 16);
            return new Derived
            {
                Id = id,
                Enc = PairCrypto.Hkdf(master, "enc", 32),
                Mac = PairCrypto.Hkdf(master, "mac", 32)
            };
        }

        sealed class Derived
        {
            public byte[] Id;
            public byte[] Enc;
            public byte[] Mac;
        }

#if EYEGROK_QR
        void StartScan()
        {
            if (_scanner == null) _scanner = new QrScanner();
            _scanner.Start();
        }

        async Task FinishQr(string text)
        {
            var client = Client();
            if (client == null) return;
            var relay = ResolveRelay();
            var result = await client.RedeemQr(text, relay, false, CancellationToken.None);
            if (result == PairResult.Ok)
            {
                if (View != null) View.MarkConfigured();
                NoteChecklistShown();
            }
        }
#endif

        async Task RefreshChecklist()
        {
            var devices = Microphone.devices == null ? 0 : Microphone.devices.Length;
            var authorized = true;
#if UNITY_ANDROID && !UNITY_EDITOR
            authorized = UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone);
#endif
            _model.EvaluateMic(authorized, devices);
            string relay = null;
            if (View != null && View.Store != null) View.Store.TryGet("relayUrl", out relay);
            var transport = new UnityHttpTransport();
            await _model.EvaluateRelay(transport, relay, CancellationToken.None);
            PaintChecklist();
        }

        async Task RunTestPing()
        {
            if (View == null) return;
            var state = await View.RunFixed("ping");
            _model.NoteRoutine(state);
            PaintChecklist();
        }

        void PaintChecklist()
        {
            PaintLine(MicLine, "Mic", _model.Mic);
            PaintLine(RelayLine, "Relay", _model.Relay);
            PaintLine(RoutineLine, "Routine", _model.Routine);
        }

        static void PaintLine(Text line, string label, string state)
        {
            if (line == null) return;
            line.supportRichText = false;
            line.text = label + "  " + state;
            if (state == "green") line.color = new Color(0.20f, 0.75f, 0.35f);
            else if (state == "red") line.color = new Color(0.86f, 0.28f, 0.24f);
            else line.color = new Color(0.62f, 0.64f, 0.68f);
        }

        void NoteChecklistShown()
        {
            if (PlayerPrefs.GetInt(ChecklistShownKey, 0) != 0) return;
            PlayerPrefs.SetInt(ChecklistShownKey, 1);
            PlayerPrefs.Save();
            ShowChecklist();
        }

        PairingClient Client()
        {
            if (View == null || View.Store == null) return null;
            if (_client == null)
                _client = new PairingClient(new UnityHttpTransport(), new RealtimeClock(), View.Store);
            return _client;
        }

        string ResolveRelay()
        {
            string relay;
            if (View != null && View.Store != null && View.Store.TryGet("relayUrl", out relay) && !string.IsNullOrEmpty(relay))
                return relay;
            var dir = Path.GetDirectoryName(PingView.SetupPath());
            return PendingOrigin.Take(dir);
        }
    }
}
