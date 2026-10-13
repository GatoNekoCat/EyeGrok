// Copyright 2026 EyeGrok contributors
// SPDX-License-Identifier: Apache-2.0

using System.IO;
using EyeGrok.Probes;
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
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;
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
            rt.localScale = new Vector3(0.0016f, 0.0016f, 0.0016f);
            canvasGo.transform.position = new Vector3(0f, 1.4f, 1.5f);

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
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = value;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.black;
            text.fontSize = size;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }
    }
}
