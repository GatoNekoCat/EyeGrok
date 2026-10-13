// Copyright 2026 EyeGrok contributors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace EyeGrok.Editor
{
    public static class BuildScript
    {
        public static void BuildAndroid()
        {
            try
            {
                if (Application.unityVersion != "6000.3.26f1")
                {
                    Debug.LogError("unity version " + Application.unityVersion);
                    EditorApplication.Exit(1);
                    return;
                }

                var variant = Arg("-eyegrokVariant");
                var outPath = Arg("-eyegrokOut");
                if (variant != "app" && variant != "probe-xr" && variant != "probe-flat")
                {
                    Debug.LogError("variant");
                    EditorApplication.Exit(1);
                    return;
                }
                if (string.IsNullOrEmpty(outPath))
                {
                    Debug.LogError("out");
                    EditorApplication.Exit(1);
                    return;
                }

                BuildSettingsApplier.Apply();
                BuildSettingsApplier.SetOpenXrForVariant(variant);
                if (SetProbePlugin(variant == "probe-flat" || variant == "probe-xr", true))
                {
                    Debug.Log("PROBE_JAVA=reload");
                    EditorApplication.Exit(2);
                    return;
                }

                var scene = BuildSettingsApplier.SceneForVariant(variant);
                if (string.IsNullOrEmpty(scene) || !File.Exists(scene))
                {
                    Debug.LogError("scene missing");
                    EditorApplication.Exit(1);
                    return;
                }

                ApplySigning();
                EditorUserBuildSettings.buildAppBundle = false;
                EditorUserBuildSettings.development = false;
                EditorUserBuildSettings.connectProfiler = false;
                EditorUserBuildSettings.allowDebugging = false;

                var options = new BuildPlayerOptions
                {
                    scenes = new[] { scene },
                    locationPathName = outPath,
                    target = BuildTarget.Android,
                    targetGroup = BuildTargetGroup.Android,
                    options = BuildOptions.None
                };

                var report = BuildPipeline.BuildPlayer(options);
                RestoreSharedSettings();
                if (report.summary.result != BuildResult.Succeeded)
                {
                    Debug.LogError("BUILD_FAILED " + report.summary.result);
                    EditorApplication.Exit(1);
                    return;
                }

                Debug.Log("BUILD_OK");
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogError("err=" + ex.GetType().Name);
                try { RestoreSharedSettings(); } catch (Exception) { }
                EditorApplication.Exit(1);
            }
        }

        // EyeGrokProbe.java is the headset probe. The app player must not compile it in.
        const string ProbePlugin = "Assets/Plugins/Android/EyeGrokProbe.java";

        static bool SetProbePlugin(bool enabled, bool reimport)
        {
            var importer = AssetImporter.GetAtPath(ProbePlugin) as PluginImporter;
            if (importer == null) return false;
            Debug.Log(enabled ? "PROBE_JAVA=on" : "PROBE_JAVA=off");
            if (importer.GetCompatibleWithPlatform(BuildTarget.Android) == enabled) return false;
            importer.SetCompatibleWithPlatform(BuildTarget.Android, enabled);
            if (!reimport)
            {
                AssetDatabase.WriteImportSettingsIfDirty(ProbePlugin);
                return false;
            }
            importer.SaveAndReimport();
            return true;
        }

        static void RestoreSharedSettings()
        {
            BuildSettingsApplier.SetOpenXrForVariant("app");
            SetProbePlugin(false, false);
            AssetDatabase.SaveAssets();
        }

        static void ApplySigning()
        {
            var keystore = Environment.GetEnvironmentVariable("EYEGROK_KEYSTORE");
            var keystorePass = Environment.GetEnvironmentVariable("EYEGROK_KEYSTORE_PASS");
            var alias = Environment.GetEnvironmentVariable("EYEGROK_KEY_ALIAS");
            var keyPass = Environment.GetEnvironmentVariable("EYEGROK_KEY_PASS");
            var custom = !string.IsNullOrEmpty(keystore)
                && !string.IsNullOrEmpty(keystorePass)
                && !string.IsNullOrEmpty(alias)
                && !string.IsNullOrEmpty(keyPass);

            if (!custom)
            {
                PlayerSettings.Android.useCustomKeystore = false;
                Debug.Log("SIGNING=debug");
                return;
            }

            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keystoreName = keystore;
            PlayerSettings.Android.keystorePass = keystorePass;
            PlayerSettings.Android.keyaliasName = alias;
            PlayerSettings.Android.keyaliasPass = keyPass;
            Debug.Log("SIGNING=custom");
        }

        static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name) return args[i + 1];
            }
            return null;
        }
    }
}
