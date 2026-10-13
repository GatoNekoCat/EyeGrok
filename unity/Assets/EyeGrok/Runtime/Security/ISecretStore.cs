// Copyright 2026 EyeGrok contributors
// SPDX-License-Identifier: Apache-2.0

namespace EyeGrok.Security
{
    public interface ISecretStore
    {
        bool Put(string name, string value);
        bool TryGet(string name, out string value);
        void DeleteAll();
    }
}
