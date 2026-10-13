# Third-party notices

EyeGrok is Apache-2.0. See LICENSE. The components below are not covered by that license.

## whisper.unity

Package `com.whisper.unity` is pinned at commit `e951e4a4c6e44c781b1d36bb8dc5bf1b7bae9687` (version 1.4.0). The Library cache copy has no LICENSE file. Its `package.json` says `"license": "MIT"` and sets `licensesUrl` to `https://github.com/Macoron/whisper.unity/blob/master/LICENSE.MD`. The text below is `LICENSE.MD` at that pinned commit.

```
MIT License

Copyright (c) 2023 Macoron

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## whisper.cpp and ggml

The README at the same whisper.unity commit says the native libraries are whisper.cpp v1.7.5. Tag v1.7.5 has one LICENSE file for the repository. The `ggml/` directory in that tag has no LICENSE of its own, so ggml is covered by the same file.

```
MIT License

Copyright (c) 2023-2024 The ggml authors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## Whisper tiny.en weights

The app uses `ggml-tiny.en-q5_1.bin`. whisper.cpp v1.7.5 `models/README.md` says the original Whisper PyTorch models provided by OpenAI are converted to custom ggml format. The weights stay under the OpenAI Whisper MIT license. This text is `LICENSE` from openai/whisper tag v20240930. It is not in the Unity package cache.

```
MIT License

Copyright (c) 2022 OpenAI

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## Unity packages

Unity packages in this project are under the Unity Companion License, or the Unity Package Distribution License where the package file says so. Neither is the Apache-2.0 license of this repository. The full Companion License is the document at the URL named in each file (https://unity3d.com/legal/licenses/unity_companion_license). That full text is not copied into most of these packages. The blocks below are the LICENSE.md files from the package cache, unchanged.

`com.unity.searcher` ships the full Unity Companion Package License v1.0 in its LICENSE.md. The other files name the license and do not paste it.

Built-in modules named `com.unity.modules.*` have no LICENSE.md in the package cache.

### com.unity.burst 1.8.30

```
Burst copyright © 2022 Unity Technologies
Source code of the package is licensed under the Unity Companion License (see https://unity3d.com/legal/licenses/unity_companion_license); otherwise licensed under the Unity Package Distribution License (see https://unity3d.com/legal/licenses/Unity_Package_Distribution_License ).

Unless expressly provided otherwise, the software under this license is made available strictly on an “AS IS” BASIS WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED. Please review the license for details on these and other terms and conditions.
```

### com.unity.collections 2.6.8

```
com.unity.collections copyright © 2024 Unity Technologies

Licensed under the Unity Companion License for Unity-dependent projects (see https://unity3d.com/legal/licenses/unity_companion_license).

Unless expressly provided otherwise, the Software under this license is made available strictly on an “AS IS” BASIS WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED. Please review the license for details on these and other terms and conditions.
```

### com.unity.ext.nunit 2.0.5

```
Custom Nunit copyright © 2019 Unity Technologies

Licensed under the Unity Package Distribution License (see https://unity3d.com/legal/licenses/Unity_Package_Distribution_License ).

Unless expressly provided otherwise, the software under this license is made available strictly on an “AS IS” BASIS WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED. Please review the license for details on these and other terms and conditions.
```

### com.unity.inputsystem 1.20.0

```
com.unity.inputsystem copyright © 2024 Unity Technologies

Licensed under the Unity Companion License for Unity-dependent projects (see [https://unity3d.com/legal/licenses/unity_companion_license](https://unity3d.com/legal/licenses/unity_companion_license)).

Unless expressly provided otherwise, the Software under this license is made available strictly on an “AS IS” BASIS WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED. Please review the license for details on these and other terms and conditions.
```

### com.unity.mathematics 1.3.3

```
com.unity.mathematics copyright © 2023 Unity Technologies ApS

Licensed under the Unity Companion License for Unity-dependent projects (see https://unity3d.com/legal/licenses/unity_companion_license).
Unless expressly provided otherwise, the Software under this license is made available strictly on an “AS IS” BASIS WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED. Please review the license for details on these and other terms and conditions.
```

### com.unity.nuget.mono-cecil 1.11.6

```
com.unity.nuget.mono-cecil copyright © 2020 Unity Technologies ApS

Licensed under the Unity Companion License for Unity-dependent projects--see [Unity Companion License](http://www.unity3d.com/legal/licenses/Unity_Companion_License).

Unless expressly provided otherwise, the Software under this license is made available strictly on an “AS IS” BASIS WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED. Please review the license for details on these and other terms and conditions.
```

### com.unity.render-pipelines.core 17.3.0

```
com.unity.render-pipelines.core copyright © 2020 Unity Technologies ApS

Licensed under the Unity Companion License for Unity-dependent projects--see [Unity Companion License](http://www.unity3d.com/legal/licenses/Unity_Companion_License).

Unless expressly provided otherwise, the Software under this license is made available strictly on an “AS IS” BASIS WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED. Please review the license for details on these and other terms and conditions.
```

### com.unity.render-pipelines.universal 17.3.0

```
com.unity.render-pipelines.universal copyright © 2020 Unity Technologies ApS

Licensed under the Unity Companion License for Unity-dependent projects--see [Unity Companion License](http://www.unity3d.com/legal/licenses/Unity_Companion_License).

Unless expressly provided otherwise, the Software under this license is made available strictly on an “AS IS” BASIS WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED. Please review the license for details on these and other terms and conditions.
```

### com.unity.render-pipelines.universal-config 17.0.3

```
com.unity.render-pipelines.universal-config copyright © 2020 Unity Technologies ApS

Licensed under the Unity Companion License for Unity-dependent projects--see [Unity Companion License](http://www.unity3d.com/legal/licenses/Unity_Companion_License).

Unless expressly provided otherwise, the Software under this license is made available strictly on an “AS IS” BASIS WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED. Please review the license for details on these and other terms and conditions.
```

### com.unity.searcher 4.9.5

```
**Unity Companion Package License v1.0 ("_License_")**

com.unity.searcher copyright © 2025 Unity Technologies ApS

Unity hereby grants to you a worldwide, non-exclusive, no-charge, and royalty-free copyright license to reproduce, prepare derivative works of, publicly display, publicly perform, sublicense, and distribute the software that is made available with this License ("**_Software_**"), subject to the following terms and conditions:

1. *Unity Companion Use Only*. Exercise of the license granted herein is limited to exercise for the creation, use, and/or distribution of applications, software, or other content pursuant to a valid Unity development engine software license ("**_Engine License_**"). That means while use of the Software is not limited to use in the software licensed under the Engine License, the Software may not be used for any purpose other than the creation, use, and/or distribution of Engine License-dependent applications, software, or other content. No other exercise of the license granted herein is permitted.

1. *No Modification of Engine License*. Neither this License nor any exercise of the license granted herein modifies the Engine License in any way.

1. *Ownership & Grant Back to You*. 

    3.1. You own your content. In this License, "derivative works" means derivatives of the Software itself--works derived only from the Software by you under this License (for example, modifying the code of the Software itself to improve its efficacy); “derivative works” of the Software do not include, for example, games, apps, or content that you create using the Software. You keep all right, title, and interest to your own content.

    3.2. Unity owns its content. While you keep all right, title, and interest to your own content per the above, as between Unity and you, Unity will own all right, title, and interest to all intellectual property rights (including patent, trademark, and copyright) in the Software and derivative works of the Software, and you hereby assign and agree to assign all such rights in those derivative works to Unity. 

    3.3. You have a license to those derivative works. Subject to this License, Unity grants to you the same worldwide, non-exclusive, no-charge, and royalty-free copyright license to derivative works of the Software you create as is granted to you for the Software under this License.

1. *Trademarks*. You are not granted any right or license under this License to use any trademarks, service marks, trade names, products names, or branding of Unity or its affiliates ("**_Trademarks_**"). Descriptive uses of Trademarks are permitted; see, for example, Unity’s Branding Usage Guidelines at [https://unity3d.com/public-relations/brand](https://unity3d.com/public-relations/brand).

1. *Notices & Third-Party Rights*. This License, including the copyright notice above, must be provided in all substantial portions of the Software and derivative works thereof (or, if that is impracticable, in any other location where such notices are customarily placed). Further, if the Software is accompanied by a Unity "third-party notices" or similar file, you acknowledge and agree that software identified in that file is governed by those separate license terms.

1. *DISCLAIMER, LIMITATION OF LIABILITY*. THE SOFTWARE AND ANY DERIVATIVE WORKS THEREOF IS PROVIDED ON AN "AS IS" BASIS, AND IS PROVIDED WITHOUT WARRANTY OF ANY KIND, WHETHER EXPRESS OR IMPLIED, INCLUDING ANY WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE, AND/OR NONINFRINGEMENT. IN NO EVENT SHALL ANY COPYRIGHT HOLDER OR AUTHOR BE LIABLE FOR ANY CLAIM, DAMAGES (WHETHER DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL, INCLUDING PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES, LOSS OF USE, DATA, OR PROFITS, AND BUSINESS INTERRUPTION), OR OTHER LIABILITY WHATSOEVER, WHETHER IN AN ACTION OF CONTRACT, TORT, OR OTHERWISE, ARISING FROM OR OUT OF, OR IN CONNECTION WITH, THE SOFTWARE OR ANY DERIVATIVE WORKS THEREOF OR THE USE OF OR OTHER DEALINGS IN SAME, EVEN WHERE ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

1. *USE IS ACCEPTANCE and License Versions*. Your receipt and use of the Software constitutes your acceptance of this License and its terms and conditions. Software released by Unity under this License may be modified or updated and the License with it; upon any such modification or update, you will comply with the terms of the updated License for any use of any of the Software under the updated License. 

1. *Use in Compliance with Law and Termination*. Your exercise of the license granted herein will at all times be in compliance with applicable law and will not infringe any proprietary rights (including intellectual property rights); this License will terminate immediately on any breach by you of this License.

1. *Severability*. If any provision of this License is held to be unenforceable or invalid, that provision will be enforced to the maximum extent possible and the other provisions will remain in full force and effect.

1. *Governing Law and Venue*. This License is governed by and construed in accordance with the laws of Denmark, except for its conflict of laws rules; the United Nations Convention on Contracts for the International Sale of Goods will not apply. If you reside (or your principal place of business is) within the United States, you and Unity agree to submit to the personal and exclusive jurisdiction of and venue in the state and federal courts located in San Francisco County, California concerning any dispute arising out of this License ("**_Dispute_**"). If you reside (or your principal place of business is) outside the United States, you and Unity agree to submit to the personal and exclusive jurisdiction of and venue in the courts located in Copenhagen, Denmark concerning any Dispute.
```

### com.unity.shadergraph 17.3.0

```
com.unity.shadergraph copyright © 2020 Unity Technologies ApS

Licensed under the Unity Companion License for Unity-dependent projects--see [Unity Companion License](http://www.unity3d.com/legal/licenses/Unity_Companion_License).

Unless expressly provided otherwise, the Software under this license is made available strictly on an “AS IS” BASIS WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED. Please review the license for details on these and other terms and conditions.
```

### com.unity.test-framework 1.6.0

```
Test Framework copyright © 2024 Unity Technologies ApS

Licensed under the Unity Companion License for Unity-dependent projects--see [Unity Companion License](http://www.unity3d.com/legal/licenses/Unity_Companion_License). 

Unless expressly provided otherwise, the Software under this license is made available strictly on an “AS IS” BASIS WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED. Please review the license for details on these and other terms and conditions.
```

### com.unity.test-framework.performance 3.5.0

```
com.unity.test-framework.performance copyright © 2020 Unity Technologies ApS

Licensed under the Unity Companion License for Unity-dependent projects--see [Unity Companion License](http://www.unity3d.com/legal/licenses/Unity_Companion_License).

Unless expressly provided otherwise, the Software under this license is made available strictly on an “AS IS” BASIS WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED. Please review the license for details on these and other terms and conditions.
```

### com.unity.ugui 2.0.0

```
uGUI Copyright © 2015-2020 Unity Technologies ApS ("**_Unity_**")

Licensed under the Unity Companion License for Unity-dependent projects (see https://unity3d.com/legal/licenses/unity_companion_license).

_Unless expressly provided otherwise, the Software under this license is made available strictly on an “AS IS” BASIS WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED. Please review the license for details on these and other terms and conditions.
```

EyeGrok draws text with `UnityEngine.UI.Text` and `Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")`. TMP Essential Resources are not imported. Liberation Sans and EmojiOne are not in this repository. The uGUI package still contains TextMesh Pro code. This project does not add a TMP component or a TMP asset.

### com.unity.xr.core-utils 2.6.0

```
XR Core Utilities copyright © 2021 Unity Technologies

Licensed under the Unity Companion License for Unity-dependent projects--see [Unity Companion License](http://www.unity3d.com/legal/licenses/Unity_Companion_License).

Unless expressly provided otherwise, the Software under this license is made available strictly on an “AS IS” BASIS WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED. Please review the license for details on these and other terms and conditions.
```

### com.unity.xr.interaction.toolkit 3.3.2

```
XR Interaction Toolkit copyright © 2026 Unity Technologies

Licensed under the Unity Companion License for Unity-dependent projects (see [Unity Companion License](http://www.unity3d.com/legal/licenses/Unity_Companion_License)).

Unless expressly provided otherwise, the Software under this license is made available strictly on an “AS IS” BASIS WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED. Please review the license for details on these and other terms and conditions.
```

### com.unity.xr.legacyinputhelpers 2.1.13

```
com.unity.xr.legacyinputhelpers copyright © 2025 Unity Technologies

Licensed under the Unity Companion License for Unity-dependent projects--see [Unity Companion License](http://www.unity3d.com/legal/licenses/Unity_Companion_License).

Unless expressly provided otherwise, the Software under this license is made available strictly on an “AS IS” BASIS WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED. Please review the license for details on these and other terms and conditions.
```

### com.unity.xr.management 4.6.1

```
com.unity.xr.management copyright © 2025 Unity Technologies ApS

Licensed under the Unity Companion License for Unity-dependent projects--see [Unity Companion License](http://www.unity3d.com/legal/licenses/Unity_Companion_License).

Unless expressly provided otherwise, the Software under this license is made available strictly on an “AS IS” BASIS WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED. Please review the license for details on these and other terms and conditions.
```

### com.unity.xr.openxr 1.16.1

```
com.unity.xr.openxr copyright © 2025 Unity Technologies ApS

Source code of the package is licensed under the Unity Companion License (see https://unity3d.com/legal/licenses/unity_companion_license); otherwise licensed under the Unity Package Distribution License (see https://unity3d.com/legal/licenses/Unity_Package_Distribution_License ).

Unless expressly provided otherwise, the software under this license is made available strictly on an “AS IS” BASIS WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED. Please review the license for details on these and other terms and conditions.
```

## Project Nayuki QR Code generator

`installer/lib/QrEncoder.ps1` is a Windows PowerShell 5.1 port of Project Nayuki's QR Code generator. Byte mode, error correction M, versions 1 through 20. The port is MIT, and it also carries the EyeGrok Apache-2.0 SPDX header. No QR binary is vendored.

```
Copyright (c) Project Nayuki

Permission is hereby granted, free of charge, to any person obtaining a copy of
this software and associated documentation files (the "Software"), to deal in
the Software without restriction, including without limitation the rights to
use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of
the Software, and to permit persons to whom the Software is furnished to do so,
subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

The Software is provided "as is", without warranty of any kind, express or
implied, including but not limited to the warranties of merchantability,
fitness for a particular purpose and noninfringement. In no event shall the
authors or copyright holders be liable for any claim, damages or other
liability, whether in an action of contract, tort or otherwise, arising from,
out of or in connection with the Software or the use or other dealings in the
Software.
```

## ZXing.Net 0.16.11

Not vendored. `tools/fetch-zxing.ps1` downloads the NuGet package and extracts `lib/netstandard2.0/zxing.dll` into a gitignored plugin folder. The package license expression is Apache-2.0. EyeGrok's `LICENSE` is the same license. The build fails if the pinned SHA-256 does not match.

## Android SDK Platform-Tools

Not redistributed. EyeGrok does not ship `adb`. The Windows installer downloads platform-tools only after Accept, checks the zip SHA-1 pinned in `installer/lib/Core.psm1`, and requires `adb.exe` to have a valid Authenticode signature from Google. The terms page shown before download is the platform-tools release page, also pinned in that file. The SHA-1 comment cites Google's `repository2-3.xml` entry for platform-tools 37.0.1 on Windows.
