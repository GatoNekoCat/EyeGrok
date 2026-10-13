// Copyright 2026 EyeGrok contributors
// SPDX-License-Identifier: Apache-2.0

using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace EyeGrok.UI
{
    public sealed class HoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public Action Down;
        public Action Up;
        bool _held;

        public void OnPointerDown(PointerEventData eventData)
        {
            if (_held) return;
            _held = true;
            if (Down != null) Down();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            End();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            End();
        }

        void End()
        {
            if (!_held) return;
            _held = false;
            if (Up != null) Up();
        }
    }
}
