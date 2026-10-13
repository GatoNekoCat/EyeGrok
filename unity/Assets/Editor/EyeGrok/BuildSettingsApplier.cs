// Copyright 2026 EyeGrok contributors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.Management;

namespace EyeGrok.Editor
{
    public static class BuildSettingsApplier
    {
        public const string AppScenePath = "Assets/Scenes/SampleScene.unity";
        public const string ProbeXrScenePath = "Assets/EyeGrok/Probes/P0c_XR.unity";
        public const string ProbeFlatScenePath = "Assets/EyeGrok/Probes/P0d_Flat.unity";
        public const string PipelinePath = "Assets/Settings/EyeGrok_URP.asset";
        public const string RendererPath = "Assets/Settings/EyeGrok_URP_Renderer.asset";

        // OpenXR package loader. Meta and Oculus feature groups stay off; this only toggles the loader.
        const string OpenXrLoaderType = "UnityEngine.XR.OpenXR.OpenXRLoader";

        // Highest SDK platform this editor's Android module will build against is API 36.
        // A platforms/android-37.0 directory is present, but it has no package.xml, so the
        // SDK check still asks to install platforms;android-37.0. That install needs elevation.
        const AndroidSdkVersions TargetSdk = AndroidSdkVersions.AndroidApiLevel36;

        public static void Apply()
        {
            if (Application.unityVersion != "6000.3.26f1")
            {
                throw new InvalidOperationException("unity version");
            }

            PlayerSettings.companyName = "EyeGrok contributors";
            PlayerSettings.productName = "EyeGrok";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "io.github.eyegrok.ping");
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.Android.bundleVersionCode = 1;

            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            // NET_Standard and the deprecated NET_Standard_2_0 share value 6.
            // The editor XML documents NET_Standard as the .NET Standard 2.1 profile.
            PlayerSettings.SetApiCompatibilityLevel(NamedBuildTarget.Android, ApiCompatibilityLevel.NET_Standard);
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel30;
            PlayerSettings.Android.targetSdkVersion = TargetSdk;
            PlayerSettings.Android.forceInternetPermission = true;
            PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.Activity;
            PlayerSettings.colorSpace = ColorSpace.Linear;

            PlayerSettings.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
            PlayerSettings.SetStackTraceLogType(LogType.Warning, StackTraceLogType.None);

            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[]
            {
                GraphicsDeviceType.Vulkan,
                GraphicsDeviceType.OpenGLES3
            });

            SetInputHandler();
            var pipeline = EnsureUrp();
            GraphicsSettings.defaultRenderPipeline = pipeline;
            var currentQuality = QualitySettings.GetQualityLevel();
            var qualityNames = QualitySettings.names;
            for (var i = 0; i < qualityNames.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = pipeline;
            }
            QualitySettings.SetQualityLevel(currentQuality, false);

            EnsureAppScene();
            EnsureAndroidOpenXr(false);
            EnsureOpenXrPackageSettingsLoaded();
            OpenXrFeatures.ApplyAndroid();

            AssetDatabase.SaveAssets();
            LogSettings();
        }

        public static void SetOpenXrForVariant(string variant)
        {
            var enable = variant == "probe-xr";
            EnsureAndroidOpenXr(enable);
            AssetDatabase.SaveAssets();
        }

        public static string SceneForVariant(string variant)
        {
            switch (variant)
            {
                case "app":
                    return AppScenePath;
                case "probe-xr":
                    return ProbeXrScenePath;
                case "probe-flat":
                    return ProbeFlatScenePath;
                default:
                    return null;
            }
        }

        static void EnsureAppScene()
        {
            if (File.Exists(AppScenePath)) return;
            Directory.CreateDirectory("Assets/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, AppScenePath);
            AssetDatabase.ImportAsset(AppScenePath);
        }

        static UniversalRenderPipelineAsset EnsureUrp()
        {
            var existing = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (existing != null) return existing;

            Directory.CreateDirectory("Assets/Settings");
            var renderer = CreateRendererAsset();
            var pipeline = UniversalRenderPipelineAsset.Create(renderer);
            AssetDatabase.CreateAsset(pipeline, PipelinePath);
            AssetDatabase.SaveAssets();
            return pipeline;
        }

        static ScriptableRendererData CreateRendererAsset()
        {
            var flags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
            MethodInfo method = null;
            foreach (var candidate in typeof(UniversalRenderPipelineAsset).GetMethods(flags))
            {
                if (candidate.Name != "CreateRendererAsset") continue;
                if (candidate.GetParameters().Length != 4) continue;
                method = candidate;
                break;
            }

            if (method != null)
            {
                var created = method.Invoke(null, new object[]
                {
                    RendererPath,
                    RendererType.UniversalRenderer,
                    false,
                    "Renderer"
                });
                var data = created as ScriptableRendererData;
                if (data != null) return data;
            }

            var fallback = ScriptableObject.CreateInstance<UniversalRendererData>();
            AssetDatabase.CreateAsset(fallback, RendererPath);
            return fallback;
        }

        static void EnsureAndroidOpenXr(bool enable)
        {
            var perBuildTarget = GetOrCreateXrSettings();
            if (perBuildTarget == null)
            {
                throw new InvalidOperationException("xr settings");
            }

            var group = BuildTargetGroup.Android;
            if (!perBuildTarget.HasSettingsForBuildTarget(group))
            {
                perBuildTarget.CreateDefaultSettingsForBuildTarget(group);
            }
            if (!perBuildTarget.HasManagerSettingsForBuildTarget(group))
            {
                perBuildTarget.CreateDefaultManagerSettingsForBuildTarget(group);
            }

            var manager = perBuildTarget.ManagerSettingsForBuildTarget(group);
            if (manager == null)
            {
                throw new InvalidOperationException("xr manager");
            }

            var assigned = HasLoader(manager, OpenXrLoaderType);
            if (enable && !assigned)
            {
                var added = XRPackageMetadataStore.AssignLoader(manager, OpenXrLoaderType, group);
                if (!added) throw new InvalidOperationException("openxr assign");
            }
            else if (!enable && assigned)
            {
                var removed = XRPackageMetadataStore.RemoveLoader(manager, OpenXrLoaderType, group);
                if (!removed) throw new InvalidOperationException("openxr remove");
            }

            Debug.Log(enable ? "OPENXR_ANDROID=on" : "OPENXR_ANDROID=off");
        }

        static bool HasLoader(XRManagerSettings manager, string loaderTypeName)
        {
            if (manager.activeLoaders == null) return false;
            foreach (var loader in manager.activeLoaders)
            {
                if (loader != null && loader.GetType().FullName == loaderTypeName) return true;
            }
            return false;
        }

        // The OpenXR build hook throws if its settings object is first touched during BuildPlayer.
        static void EnsureOpenXrPackageSettingsLoaded()
        {
            Type settingsType = null;
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (var i = 0; i < assemblies.Length; i++)
            {
                settingsType = assemblies[i].GetType("UnityEditor.XR.OpenXR.OpenXRPackageSettings");
                if (settingsType != null) break;
            }
            if (settingsType == null) throw new InvalidOperationException("openxr settings type");

            var method = settingsType.GetMethod(
                "GetOrCreateInstance",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (method == null) throw new InvalidOperationException("openxr settings method");
            if (method.Invoke(null, null) == null) throw new InvalidOperationException("openxr settings");
            Debug.Log("OPENXR_SETTINGS=loaded");
        }

        static XRGeneralSettingsPerBuildTarget GetOrCreateXrSettings()
        {
            var flags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
            MethodInfo method = null;
            foreach (var candidate in typeof(XRGeneralSettingsPerBuildTarget).GetMethods(flags))
            {
                if (candidate.Name != "GetOrCreate") continue;
                if (candidate.GetParameters().Length != 0) continue;
                method = candidate;
                break;
            }
            if (method == null) return null;
            return method.Invoke(null, null) as XRGeneralSettingsPerBuildTarget;
        }

        // 1 = Input System package only. Both (2) is unsupported on Android.
        static void SetInputHandler()
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (assets == null || assets.Length == 0) return;
            var serialized = new SerializedObject(assets[0]);
            var property = serialized.FindProperty("activeInputHandler");
            if (property == null) return;
            property.intValue = 1;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static void LogSettings()
        {
            Debug.Log("SETTINGS company=" + PlayerSettings.companyName);
            Debug.Log("SETTINGS product=" + PlayerSettings.productName);
            Debug.Log("SETTINGS id=" + PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android));
            Debug.Log("SETTINGS version=" + PlayerSettings.bundleVersion + " code=" + PlayerSettings.Android.bundleVersionCode);
            Debug.Log("SETTINGS backend=" + PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android));
            Debug.Log("SETTINGS arch=" + PlayerSettings.Android.targetArchitectures);
            Debug.Log("SETTINGS minSdk=" + PlayerSettings.Android.minSdkVersion);
            Debug.Log("SETTINGS targetSdk=" + PlayerSettings.Android.targetSdkVersion);
            Debug.Log("SETTINGS api=" + PlayerSettings.GetApiCompatibilityLevel(NamedBuildTarget.Android));
            Debug.Log("SETTINGS entry=" + PlayerSettings.Android.applicationEntry);
            Debug.Log("SETTINGS internet=" + PlayerSettings.Android.forceInternetPermission);
            var apis = PlayerSettings.GetGraphicsAPIs(BuildTarget.Android);
            Debug.Log("SETTINGS gfx0=" + (apis.Length > 0 ? apis[0].ToString() : "none"));
            Debug.Log("SETTINGS gfx1=" + (apis.Length > 1 ? apis[1].ToString() : "none"));
            Debug.Log("SETTINGS pipeline=" + (GraphicsSettings.defaultRenderPipeline != null ? GraphicsSettings.defaultRenderPipeline.name : "none"));
        }
    }
}
