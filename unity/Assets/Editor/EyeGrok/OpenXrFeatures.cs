// Copyright 2026 EyeGrok contributors
// SPDX-License-Identifier: Apache-2.0

using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.Interactions;

namespace EyeGrok.Editor
{
    public static class OpenXrFeatures
    {
        // Frame controller profile, from Valve's OpenXR utilities documentation, which describes
        // the same interaction profile the Frame Unity engine page ships:
        // https://github.com/ValveSoftware/Unity/blob/main/com.valvesoftware.openxr.utils/Documentation~/index.md
        // Feature class: Valve.OpenXR.Utils.SteamFrameControllerProfile
        // Extension: XR_VALVE_frame_controller_interaction
        // Path: /interaction_profiles/valve/frame_controller_valve
        // That feature lives in com.valvesoftware.openxr.utils (BSD-3-Clause), which is outside
        // the V-001 pinned package set. This project enables Khronos Simple Controller only.
        // UNVERIFIED: whether Lepton maps Frame controllers onto that Khronos profile when the
        // Valve feature is absent. Meta and Oculus features and feature groups stay off.
        public static void ApplyAndroid()
        {
            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            if (settings == null) throw new InvalidOperationException("openxr android settings");

            var features = settings.GetFeatures();
            var khr = false;
            for (var i = 0; i < features.Length; i++)
            {
                var feature = features[i];
                if (feature == null) continue;
                var typeName = feature.GetType().FullName ?? "";
                if (IsMetaOrOculus(typeName))
                {
                    feature.enabled = false;
                    continue;
                }
                if (feature is KHRSimpleControllerProfile)
                {
                    feature.enabled = true;
                    khr = true;
                }
            }

            if (!khr) throw new InvalidOperationException("khr simple missing");
            EditorUtility.SetDirty(settings);
            Debug.Log("OPENXR_KHR_SIMPLE=on");
        }

        static bool IsMetaOrOculus(string value)
        {
            return value.IndexOf("Meta", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("Oculus", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
