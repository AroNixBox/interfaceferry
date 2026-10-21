This package contains third-party software components governed by the license(s) indicated below:

---

Component Name: UI Toolkit Connector for Figma design (`com.unity.ui.figma`), version 0.2.0-exp.1, including its Figma plugin

License Type: Unity Companion License

Copyright © 2026 Unity Technologies

> com.unity.ui.figma copyright © 2026 Unity Technologies
>
> Licensed under the Unity Companion License for Unity-dependent projects (see https://unity3d.com/legal/licenses/Unity_Companion_License).
>
> Unless expressly provided otherwise, the software under this license is made available strictly on an "AS IS" BASIS WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED. Please review the license for details on these and other terms and conditions.

Current license text: https://unity.com/legal/licenses/unity-companion-license

InterfaceFerry was written by reading this package to learn how it uses the Unity Pipeline package and how it moves data from Figma to Unity. The following portions of this repository are copied from it or closely adapted from it. They are **not** covered by InterfaceFerry's MIT License; they remain under the Unity Companion License. In the source, each starts and ends with a `Derived from com.unity.ui.figma` comment (JSON allows no comments, so `manifest.json` is listed here only).

| File in this repository | Portion | Origin in `com.unity.ui.figma` 0.2.0-exp.1 |
|---|---|---|
| `FigmaPlugin~/code.js` | `imageKeyFor`, `stageImage` (content-hash image keys, staging de-duplication) | Figma plugin `code.js` |
| `FigmaPlugin~/ui.html` | The stylesheet; the transport helpers `base`, `toBase64`, `exec`, `splitPairing` (`port:token` pairing, `POST /api/exec` with a bearer token) | Figma plugin `ui.html` |
| `FigmaPlugin~/manifest.json` | `networkAccess.allowedDomains` (`http://localhost:7800` – `7849`) | Figma plugin `manifest.json` |
| `Editor/FerryCommands.cs` | `IsPng`; the size bound and base64 validation in `StageImage` | `Editor/Pipeline/FigmaPipelineCommands.cs` |
| `Editor/FerryMenu.cs` | `PipelineDescriptor`; copying `port:token` to the clipboard and clearing it after 60 s (`CopyToken`, `ClearCopiedToken`) | `Editor/Window/FigmaConnectorWindow.cs` |

---

Dependencies resolved by the Unity Package Manager and **not** included in this repository:

- **Unity Pipeline** (`com.unity.pipeline`), copyright © 2026 Unity Technologies, licensed under the Unity Package Distribution License (https://unity3d.com/legal/licenses/Unity_Package_Distribution_License).
- **Newtonsoft Json for Unity** (`com.unity.nuget.newtonsoft-json`), Json.NET copyright © 2007 James Newton-King, licensed under the MIT License.
