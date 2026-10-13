// Copyright 2026 EyeGrok contributors
// SPDX-License-Identifier: Apache-2.0

using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace EyeGrok.Editor
{
    public static class ShotCapture
    {
        public static void Capture()
        {
            EditorSceneManager.OpenScene(BuildSettingsApplier.AppScenePath);
            var shots = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".local", "shots"));
            Directory.CreateDirectory(shots);
            var pad = FindNamed("Number Pad");
            var checklist = FindNamed("Checklist");
            var pair = FindNamed("Pair Panel");
            var scan = FindNamed("Scan QR");
            if (pad != null) pad.SetActive(false);
            if (checklist != null) checklist.SetActive(false);
            if (pair != null) pair.SetActive(false);
            if (scan != null) scan.SetActive(false);
            Shoot(Path.Combine(shots, "ping.png"));
            if (pad != null)
            {
                pad.SetActive(true);
                Shoot(Path.Combine(shots, "number-pad.png"));
                pad.SetActive(false);
            }
            if (checklist != null)
            {
                checklist.SetActive(true);
                Shoot(Path.Combine(shots, "checklist.png"));
                checklist.SetActive(false);
            }
            if (pair != null)
            {
                pair.SetActive(true);
                Shoot(Path.Combine(shots, "pairing.png"));
                pair.SetActive(false);
            }
            Debug.Log("SHOTS=ok");
            EditorApplication.Exit(0);
        }

        static GameObject FindNamed(string name)
        {
            GameObject best = null;
            var bestChildren = -1;
            var all = Resources.FindObjectsOfTypeAll<Transform>();
            for (var i = 0; i < all.Length; i++)
            {
                var transform = all[i];
                if (transform == null || transform.name != name) continue;
                if (!transform.gameObject.scene.IsValid()) continue;
                if (transform.childCount <= bestChildren) continue;
                best = transform.gameObject;
                bestChildren = transform.childCount;
            }
            return best;
        }

        static void Shoot(string path)
        {
            var canvas = Object.FindFirstObjectByType<Canvas>();
            var cam = Camera.main;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 1f;
            var rt = new RenderTexture(1920, 1080, 24);
            cam.targetTexture = rt;
            cam.aspect = 1920f / 1080f;
            Canvas.ForceUpdateCanvases();
            cam.Render();
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            cam.targetTexture = null;
            RenderTexture.active = previous;
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(rt);
        }
    }
}
