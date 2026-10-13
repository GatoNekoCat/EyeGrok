// Copyright 2026 EyeGrok contributors
// SPDX-License-Identifier: Apache-2.0

using UnityEngine;

namespace EyeGrok.UI
{
    public sealed class EnableIfQr : MonoBehaviour
    {
        void Awake()
        {
#if !EYEGROK_QR
            gameObject.SetActive(false);
#endif
        }
    }
}
