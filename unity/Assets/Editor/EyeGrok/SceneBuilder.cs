// Copyright 2026 EyeGrok contributors
// SPDX-License-Identifier: Apache-2.0

using System.IO;
using EyeGrok.Probes;
using EyeGrok.UI;
using EyeGrok.Voice;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.InputSystem.XR;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;
using UnityEngine.XR.Interaction.Toolkit.UI;
using Whisper;

namespace EyeGrok.Editor
{
    public static class SceneBuilder
    {
        public static void CreateProbeScenes()
        {
            try
            {
                BuildSettingsApplier.Apply();
                Directory.CreateDirectory("Assets/EyeGrok/Probes");
                CreateXrProbe();
                CreateFlatProbe();
                AssetDatabase.SaveAssets();
                Debug.Log("PROBE_SCENES=ok");
                EditorApplication.Exit(0);
            }
            catch (System.Exception ex)
            {
                Debug.LogError("err=" + ex.GetType().Name + " " + ex.Message);
                EditorApplication.Exit(1);
            }
        }

        static void CreateXrProbe()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            new GameObject("XR Interaction Manager").AddComponent<XRInteractionManager>();

            var originGo = new GameObject("XR Origin");
            var origin = originGo.AddComponent<XROrigin>();
            var offset = new GameObject("Camera Offset");
            offset.transform.SetParent(originGo.transform, false);
            origin.CameraFloorOffsetObject = offset;
            origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
            origin.CameraYOffset = 0f;

            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(offset.transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.nearClipPlane = 0.05f;
            camGo.AddComponent<AudioListener>();
            BindPose(camGo.AddComponent<TrackedPoseDriver>(), "<XRHMD>/centerEyePosition", "<XRHMD>/centerEyeRotation");
            origin.Camera = cam;

            // Khronos Simple Controller bindings. The Valve Frame profile is documented in OpenXrFeatures.cs.
            CreateHand("Right Controller", offset.transform, "{RightHand}");
            CreateHand("Left Controller", offset.transform, "{LeftHand}");

            var probe = new GameObject("P0c Probe").AddComponent<P0cProbe>();
            CreateWorldButton(cam, probe);

            var events = new GameObject("EventSystem");
            events.AddComponent<EventSystem>();
            events.AddComponent<XRUIInputModule>();

            EditorSceneManager.SaveScene(scene, BuildSettingsApplier.ProbeXrScenePath);
        }

        static void CreateFlatProbe()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.transform.position = new Vector3(0f, 0f, -5f);
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.12f, 0.14f, 0.16f);
            camGo.AddComponent<AudioListener>();

            var probeGo = new GameObject("P0d Probe");
            var probe = probeGo.AddComponent<P0dProbe>();
            var whisper = probeGo.AddComponent<WhisperManager>();
            var serialized = new SerializedObject(whisper);
            serialized.FindProperty("modelPath").stringValue = "Whisper/ggml-tiny.en-q5_1.bin";
            serialized.FindProperty("isModelPathInStreamingAssets").boolValue = true;
            serialized.FindProperty("initOnAwake").boolValue = false;
            serialized.FindProperty("useGpu").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            whisper.language = "en";
            whisper.translateToEnglish = false;

            var canvasGo = new GameObject("Canvas");
            var canvas = canvasGo.AddComponent<Canvas>();
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            TextKit.ConfigureOverlay(canvas, scaler);
            canvasGo.AddComponent<GraphicRaycaster>();

            var panel = new GameObject("Panel");
            panel.transform.SetParent(canvasGo.transform, false);
            var panelRt = panel.AddComponent<RectTransform>();
            panelRt.anchorMin = new Vector2(0.15f, 0.1f);
            panelRt.anchorMax = new Vector2(0.85f, 0.9f);
            panelRt.offsetMin = Vector2.zero;
            panelRt.offsetMax = Vector2.zero;
            var layout = panel.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 24f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;

            var title = CreateUiText(panel.transform, "EyeGrok P0-d", 48);
            var titleLayout = title.gameObject.AddComponent<LayoutElement>();
            titleLayout.preferredHeight = 80f;

            var hold = CreateUiButton(panel.transform, "Hold");
            hold.gameObject.AddComponent<P0dPointer>();
            var record = CreateUiButton(panel.transform, "Record 3 s");
            UnityEventTools.AddPersistentListener(record.onClick, probe.Record3s);
            var services = CreateUiButton(panel.transform, "Probe services");
            UnityEventTools.AddPersistentListener(services.onClick, probe.ProbeServices);
            var keystore = CreateUiButton(panel.transform, "Probe keystore");
            UnityEventTools.AddPersistentListener(keystore.onClick, probe.ProbeKeystore);

            var events = new GameObject("EventSystem");
            events.AddComponent<EventSystem>();
            events.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();

            EditorSceneManager.SaveScene(scene, BuildSettingsApplier.ProbeFlatScenePath);
        }

        static void CreateHand(string name, Transform parent, string hand)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            BindPose(
                go.AddComponent<TrackedPoseDriver>(),
                "<XRController>" + hand + "/devicePosition",
                "<XRController>" + hand + "/deviceRotation");
            var ray = go.AddComponent<XRRayInteractor>();
            ray.selectInput.inputSourceMode = XRInputButtonReader.InputSourceMode.InputAction;
            var performed = new InputAction(name + " Select", InputActionType.Button);
            performed.AddBinding("<XRController>" + hand + "/triggerPressed");
            var value = new InputAction(name + " Trigger", InputActionType.Value, expectedControlType: "Axis");
            value.AddBinding("<XRController>" + hand + "/trigger");
            ray.selectInput.inputActionPerformed = performed;
            ray.selectInput.inputActionValue = value;
            go.AddComponent<XRInteractorLineVisual>();
            EditorUtility.SetDirty(ray);
        }

        static void BindPose(TrackedPoseDriver driver, string position, string rotation)
        {
            var pos = new InputAction("Position", InputActionType.Value, expectedControlType: "Vector3");
            pos.AddBinding(position);
            var rot = new InputAction("Rotation", InputActionType.Value, expectedControlType: "Quaternion");
            rot.AddBinding(rotation);
            driver.positionInput = new InputActionProperty(pos);
            driver.rotationInput = new InputActionProperty(rot);
            driver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
        }

        static void CreateWorldButton(Camera cam, P0cProbe probe)
        {
            var canvasGo = new GameObject("P0c Canvas");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = cam;
            canvasGo.AddComponent<TrackedDeviceGraphicRaycaster>();
            canvasGo.AddComponent<GraphicRaycaster>();
            var rt = canvasGo.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(900f, 500f);
            canvasGo.transform.position = new Vector3(0f, 1.4f, 1.5f);
            // 625 px/m keeps the panel at 1.44 m by 0.80 m and rasterizes one texel per canvas unit.
            TextKit.ConfigureWorldCanvas(canvas, 625f);

            var button = CreateUiButton(canvasGo.transform, "EyeGrok P0-c");
            var buttonRt = button.GetComponent<RectTransform>();
            buttonRt.anchorMin = new Vector2(0.1f, 0.2f);
            buttonRt.anchorMax = new Vector2(0.9f, 0.8f);
            buttonRt.offsetMin = Vector2.zero;
            buttonRt.offsetMax = Vector2.zero;
            UnityEventTools.AddPersistentListener(button.onClick, probe.OnPressed);
        }

        static Button CreateUiButton(Transform parent, string label)
        {
            var go = new GameObject(label);
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.color = new Color(0.9f, 0.9f, 0.92f);
            var button = go.AddComponent<Button>();
            var text = CreateUiText(go.transform, label, 36);
            var textRt = text.rectTransform;
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;
            return button;
        }

        static Text CreateUiText(Transform parent, string value, int size)
        {
            var go = new GameObject(value + " Label");
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<Text>();
            TextKit.Prepare(text, value, size, TextAnchor.MiddleCenter, Color.black, true);
            return text;
        }

        public static void CreatePingScene()
        {
            try
            {
                BuildSettingsApplier.Apply();
                BuildPingScene();
            }
            catch (System.Exception ex)
            {
                Debug.LogError("err=" + ex.GetType().Name + " " + ex.Message);
                EditorApplication.Exit(1);
            }
        }

        static void BuildPingScene()
        {
            Directory.CreateDirectory("Assets/EyeGrok/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.transform.position = new Vector3(0f, 0f, -5f);
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.08f, 0.09f, 0.11f);
            camGo.AddComponent<AudioListener>();

            var root = new GameObject("Ping");
            var whisper = root.AddComponent<WhisperManager>();
            var serialized = new SerializedObject(whisper);
            serialized.FindProperty("modelPath").stringValue = "Whisper/ggml-tiny.en-q5_1.bin";
            serialized.FindProperty("isModelPathInStreamingAssets").boolValue = true;
            serialized.FindProperty("initOnAwake").boolValue = false;
            serialized.FindProperty("useGpu").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            whisper.language = "en";
            whisper.translateToEnglish = false;
            var mic = root.AddComponent<MicRecorder>();
            var stt = root.AddComponent<WhisperStt>();
            var view = root.AddComponent<PingView>();

            var canvasGo = new GameObject("Canvas");
            var canvas = canvasGo.AddComponent<Canvas>();
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            TextKit.ConfigureOverlay(canvas, scaler);
            canvasGo.AddComponent<GraphicRaycaster>();

            view.SetupLine = CreateLabel(canvasGo.transform, "Setup", 32, new Vector2(0.04f, 0.90f), new Vector2(0.96f, 0.98f), Color.white, false);
            view.StatusLine = CreateLabel(canvasGo.transform, "Status", 36, new Vector2(0.04f, 0.82f), new Vector2(0.96f, 0.90f), Color.white, false);
            view.TranscriptLine = CreateLabel(canvasGo.transform, "Transcript", 32, new Vector2(0.04f, 0.68f), new Vector2(0.96f, 0.82f), Color.white, false);
            view.ReplyLine = CreateReply(canvasGo.transform);
            view.CancelButton = CreateLabeledButton(canvasGo.transform, "Cancel", new Vector2(0.04f, 0.36f), new Vector2(0.34f, 0.44f));
            view.CheckButton = CreateLabeledButton(canvasGo.transform, "Check again", new Vector2(0.35f, 0.36f), new Vector2(0.65f, 0.44f));
            view.ForgetButton = CreateHold(canvasGo.transform, "Forget setup", new Vector2(0.66f, 0.36f), new Vector2(0.96f, 0.44f));
            // 32% of the reference height. Pointer exit counts as release.
            view.TalkButton = CreateHold(canvasGo.transform, "Hold to talk", new Vector2(0.04f, 0.02f), new Vector2(0.96f, 0.34f));

            var debug = new GameObject("Debug");
            debug.transform.SetParent(canvasGo.transform, false);
            Stretch(debug.AddComponent<RectTransform>(), new Vector2(0.04f, 0.44f), new Vector2(0.96f, 0.52f));
            debug.AddComponent<Image>().color = new Color(0.15f, 0.16f, 0.18f, 0.95f);
            view.DebugInput = CreateInput(debug.transform);
            view.DebugSend = CreateLabeledButton(debug.transform, "Send", new Vector2(0.78f, 0.1f), new Vector2(0.98f, 0.9f));
            debug.SetActive(false);
            view.DebugPanel = debug;

            view.Mic = mic;
            view.Stt = stt;

            var events = new GameObject("EventSystem");
            events.AddComponent<EventSystem>();
            events.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();

            EditorSceneManager.SaveScene(scene, BuildSettingsApplier.AppScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log("PING_SCENE=ok");
            EditorApplication.Exit(0);
        }

        static Text CreateReply(Transform parent)
        {
            var scrollGo = new GameObject("Reply");
            scrollGo.transform.SetParent(parent, false);
            Stretch(scrollGo.AddComponent<RectTransform>(), new Vector2(0.04f, 0.46f), new Vector2(0.96f, 0.68f));
            var scroll = scrollGo.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            var viewport = new GameObject("Viewport");
            viewport.transform.SetParent(scrollGo.transform, false);
            var viewportRt = viewport.AddComponent<RectTransform>();
            Stretch(viewportRt, Vector2.zero, Vector2.one);
            viewport.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.06f);
            viewport.AddComponent<Mask>().showMaskGraphic = true;

            var content = new GameObject("Content");
            content.transform.SetParent(viewport.transform, false);
            var contentRt = content.AddComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot = new Vector2(0.5f, 1f);
            contentRt.sizeDelta = new Vector2(0f, 200f);
            var fitter = content.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var text = CreateLabel(content.transform, "Reply", 32, Vector2.zero, Vector2.one, Color.white, false);
            text.alignment = TextAnchor.UpperLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            scroll.viewport = viewportRt;
            scroll.content = contentRt;
            return text;
        }

        static Text CreateLabel(Transform parent, string value, int size, Vector2 min, Vector2 max, Color color, bool bestFit)
        {
            var go = new GameObject(string.IsNullOrEmpty(value) ? "Label" : value);
            go.transform.SetParent(parent, false);
            Stretch(go.AddComponent<RectTransform>(), min, max);
            var text = go.AddComponent<Text>();
            TextKit.Prepare(text, value, size, TextAnchor.MiddleCenter, color, bestFit);
            return text;
        }

        static HoldButton CreateHold(Transform parent, string label, Vector2 min, Vector2 max)
        {
            var go = new GameObject(label);
            go.transform.SetParent(parent, false);
            Stretch(go.AddComponent<RectTransform>(), min, max);
            var image = go.AddComponent<Image>();
            image.color = new Color(0.93f, 0.94f, 0.96f);
            image.raycastTarget = true;
            var hold = go.AddComponent<HoldButton>();
            var text = CreateLabel(go.transform, label, 40, Vector2.zero, Vector2.one, Color.black, true);
            text.alignment = TextAnchor.MiddleCenter;
            return hold;
        }

        static Button CreateLabeledButton(Transform parent, string label, Vector2 min, Vector2 max)
        {
            var go = new GameObject(label);
            go.transform.SetParent(parent, false);
            Stretch(go.AddComponent<RectTransform>(), min, max);
            go.AddComponent<Image>().color = new Color(0.75f, 0.78f, 0.82f);
            var button = go.AddComponent<Button>();
            CreateLabel(go.transform, label, 28, Vector2.zero, Vector2.one, Color.black, true);
            return button;
        }

        static InputField CreateInput(Transform parent)
        {
            var go = new GameObject("Debug Input");
            go.transform.SetParent(parent, false);
            Stretch(go.AddComponent<RectTransform>(), new Vector2(0.02f, 0.1f), new Vector2(0.76f, 0.9f));
            var image = go.AddComponent<Image>();
            image.color = Color.white;
            var text = CreateLabel(go.transform, "", 28, new Vector2(0.02f, 0.05f), new Vector2(0.98f, 0.95f), Color.black, false);
            text.alignment = TextAnchor.MiddleLeft;
            text.supportRichText = false;
            var field = go.AddComponent<InputField>();
            field.textComponent = text;
            field.lineType = InputField.LineType.SingleLine;
            return field;
        }

        static void Stretch(RectTransform rt, Vector2 min, Vector2 max)
        {
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
        }
    }
}
