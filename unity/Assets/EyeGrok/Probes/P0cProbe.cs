// Copyright 2026 EyeGrok contributors
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using UnityEngine;
using UnityEngine.XR.OpenXR;

namespace EyeGrok.Probes
{
    public sealed class P0cProbe : MonoBehaviour
    {
        bool _logged;

        void Start()
        {
            if (_logged) return;
            _logged = true;
            Debug.Log("P0C runtime=" + OpenXRRuntime.name + " ver=" + OpenXRRuntime.version);
            Debug.Log("P0C gfx=" + SystemInfo.graphicsDeviceType);
            var ext = OpenXRRuntime.GetEnabledExtensions();
            var builder = new StringBuilder();
            if (ext != null)
            {
                for (var i = 0; i < ext.Length; i++)
                {
                    if (i > 0) builder.Append(',');
                    builder.Append(ext[i]);
                }
            }
            Debug.Log("P0C ext=" + builder);
        }

        public void OnPressed()
        {
            Debug.Log("P0C_BUTTON_PRESSED");
        }
    }
}
