// Copyright 2026 EyeGrok contributors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor.Android;
using UnityEngine;

namespace EyeGrok.Editor
{
    public sealed class CameraManifestHook : IPostGenerateGradleAndroidProject
    {
        // Unity rewrites the plugin manifest and may put tools:node before android:name.
        static readonly Regex CameraRemove = new Regex(
            "<uses-permission\\b(?=[^>]*android:name\\s*=\\s*\"android\\.permission\\.CAMERA\")(?=[^>]*tools:node\\s*=\\s*\"remove\")[^>]*/>",
            RegexOptions.CultureInvariant);

        public int callbackOrder { get { return 0; } }

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            if (!CameraBuildFlag.Read()) return;
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;
            var manifests = Directory.GetFiles(path, "AndroidManifest.xml", SearchOption.AllDirectories);
            var patched = 0;
            for (var i = 0; i < manifests.Length; i++)
            {
                if (PatchFile(manifests[i])) patched++;
            }
            Debug.Log("CAMERA_MANIFEST=patched n=" + patched);
        }

        static bool PatchFile(string path)
        {
            var text = File.ReadAllText(path);
            if (!CameraRemove.IsMatch(text))
                return false;
            var updated = CameraRemove.Replace(
                text,
                "<uses-permission android:name=\"android.permission.CAMERA\" />");
            if (CameraRemove.IsMatch(updated))
                throw new InvalidOperationException("camera manifest");
            const string feature = "<uses-feature android:name=\"android.hardware.camera\" android:required=\"false\" />";
            if (updated.IndexOf("android.hardware.camera", StringComparison.Ordinal) < 0)
            {
                const string perm = "<uses-permission android:name=\"android.permission.CAMERA\" />";
                var idx = updated.IndexOf(perm, StringComparison.Ordinal);
                if (idx >= 0)
                    updated = updated.Insert(idx + perm.Length, "\n    " + feature);
            }
            if (updated == text) return false;
            File.WriteAllText(path, updated, new UTF8Encoding(false));
            return true;
        }
    }
}
