DawnCapture uses the following third-party components, each distributed
under its own license. Every runtime component is MIT-licensed; the build
tooling section at the end covers the Inno Setup license.

— NuGet dependencies

• CommunityToolkit.Mvvm 8.4.0 — MIT — © .NET Foundation and Contributors
• Microsoft.Data.Sqlite 8.0.11 — MIT — © .NET Foundation and Contributors
• Microsoft.Extensions.DependencyInjection 8.0.1 — MIT — © .NET Foundation and Contributors
• Microsoft.Windows.SDK.BuildTools 10.0.28000.2705 — MIT — © Microsoft Corporation
• Microsoft.WindowsAppSDK 2.4.0 — MIT — © Microsoft Corporation
• NAudio 2.2.1 — MIT — © Mark Heath
• System.Drawing.Common 8.0.8 — MIT — © .NET Foundation and Contributors
• Vortice.Direct3D11 3.8.3 — MIT — © Amer Koleci
• Vortice.DXGI 3.8.3 — MIT — © Amer Koleci

— MIT License

The following MIT License applies to each of the components listed above:

MIT License

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

— Build tooling (not a runtime dependency)

The Windows installer (bin\DawnCapture-<version>-x64-setup.exe) is built with
Inno Setup 6 — © 1997-2026 Jordan Russell, © 2000-2026 Martijn Laan.
See https://jrsoftware.org/isinfo.php
Distributed under the Inno Setup License. The license grants permission to
use the software for any purpose, including commercial applications; the
authors separately request that commercial users purchase a license, and
ISCC prints a "Non-commercial use only" notice during compilation.

The Simplified Chinese wizard translation (ChineseSimplified.isl, the Inno Setup
language file that ships with the installer script) is a user-contributed
translation listed on the official Inno Setup translations page — maintainer
Zhenghan Yang (Kira), MIT-licensed project at
https://github.com/kira-96/Inno-Setup-Chinese-Simplified-Translation

— Notes

• NAudio (including WasapiCapture / WasapiLoopbackCapture used for audio capture) is © Mark Heath and contributors. See https://github.com/naudio/NAudio
• Microsoft.Data.Sqlite includes the SQLite library (public domain). See https://www.sqlite.org/copyright.html
• Vortice Direct3D11 / DXGI bindings are © Amer Koleci. See https://github.com/amerkoleci/Vortice.Windows
• System.Drawing.Common on Windows is a thin wrapper over GDI+. See https://github.com/dotnet/winforms

