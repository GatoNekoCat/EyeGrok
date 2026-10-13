// Copyright 2026 EyeGrok contributors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
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
                if (variant != "app" && variant != "probe-xr" && variant != "probe-flat" && variant != "probe-cam")
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
                var camera = variant == "probe-cam" || Arg("-eyegrokQr") == "on";
                CameraBuildFlag.Write(camera);
                var reload = SetQrDefine(camera);
                reload = ApplyPlugin(ProbePlugin, variant == "probe-flat" || variant == "probe-xr") || reload;
                reload = ApplyPlugin(CamProbePlugin, variant == "probe-cam") || reload;
                reload = ApplyPlugin(ZxingPlugin, camera) || reload;
                if (reload)
                {
                    AssetDatabase.SaveAssets();
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
                var dev = Arg("-eyegrokDev") == "on";
                EditorUserBuildSettings.development = dev;
                EditorUserBuildSettings.connectProfiler = false;
                EditorUserBuildSettings.allowDebugging = dev;

                var options = new BuildPlayerOptions
                {
                    scenes = new[] { scene },
                    locationPathName = outPath,
                    target = BuildTarget.Android,
                    targetGroup = BuildTargetGroup.Android,
                    options = dev ? BuildOptions.Development | BuildOptions.AllowDebugging : BuildOptions.None
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

        // Java probes stay out of the app. ZXing is Android-enabled only when the camera flag is on.
        const string ProbePlugin = "Assets/Plugins/Android/EyeGrokProbe.java";
        const string CamProbePlugin = "Assets/Plugins/Android/EyeGrokCamProbe.java";
        const string ZxingPlugin = "Assets/Plugins/ZXing/zxing.dll";

        static bool ApplyPlugin(string path, bool enabled)
        {
            var importer = AssetImporter.GetAtPath(path) as PluginImporter;
            if (importer == null) return false;
            Debug.Log((enabled ? "PLUGIN=on " : "PLUGIN=off ") + Path.GetFileName(path));
            if (importer.GetCompatibleWithPlatform(BuildTarget.Android) == enabled) return false;
            importer.SetCompatibleWithPlatform(BuildTarget.Android, enabled);
            AssetDatabase.WriteImportSettingsIfDirty(path);
            return true;
        }

        static bool SetQrDefine(bool enable)
        {
            PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Android, out var current);
            var list = new List<string>();
            if (current != null)
            {
                for (var i = 0; i < current.Length; i++)
                {
                    if (current[i] == "EYEGROK_QR") continue;
                    if (current[i].Length == 0) continue;
                    list.Add(current[i]);
                }
            }
            var has = current != null && IndexOf(current, "EYEGROK_QR") >= 0;
            if (has == enable) return false;
            if (enable) list.Add("EYEGROK_QR");
            PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.Android, list.ToArray());
            Debug.Log(enable ? "QR_DEFINE=on" : "QR_DEFINE=off");
            return true;
        }

        static int IndexOf(string[] values, string wanted)
        {
            for (var i = 0; i < values.Length; i++)
            {
                if (values[i] == wanted) return i;
            }
            return -1;
        }

        static void RestoreSharedSettings()
        {
            EditorUserBuildSettings.development = false;
            EditorUserBuildSettings.connectProfiler = false;
            EditorUserBuildSettings.allowDebugging = false;
            BuildSettingsApplier.SetOpenXrForVariant("app");
            ApplyPlugin(ProbePlugin, false);
            ApplyPlugin(CamProbePlugin, false);
            ApplyPlugin(ZxingPlugin, false);
            SetQrDefine(false);
            CameraBuildFlag.Write(false);
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

    public static class CameraBuildFlag
    {
        public static bool Include;

        public static void Write(bool on)
        {
            Include = on;
            var dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Library");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "EyeGrokCameraBuild.txt"), on ? "1" : "0");
        }

        public static bool Read()
        {
            if (Include) return true;
            var path = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Library", "EyeGrokCameraBuild.txt");
            if (!File.Exists(path)) return false;
            return File.ReadAllText(path).Trim() == "1";
        }
    }
}
