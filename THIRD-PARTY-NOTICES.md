DawnCapture uses the following third-party components, each distributed
under its own license. Most are MIT-licensed, but not all of them are:
SQLitePCLRaw is Apache-2.0 and the WebView2 bits that arrive with the
Windows App SDK are under a Microsoft BSD-style license. Both are covered
below, as is the Inno Setup license for the build tooling.

The versions named here mirror DawnCapture.csproj, which is the source of
truth; package names follow the NuGet packages actually referenced, which
for the Windows App SDK and NAudio are the split packages rather than the
older single packages. The list covers what is referenced directly and what
ends up redistributed in a published build.

— MIT-licensed dependencies

• CommunityToolkit.Mvvm 8.4.0 — © .NET Foundation and Contributors
• Microsoft.Data.Sqlite 8.0.11 (with Microsoft.Data.Sqlite.Core) — © .NET Foundation and Contributors
• Microsoft.Extensions.DependencyInjection 8.0.1 (with .Abstractions 8.0.2) — © .NET Foundation and Contributors
• Microsoft.Windows.SDK.BuildTools 10.0.28000.2705 — © Microsoft Corporation
• Windows App SDK 2.4 — © Microsoft Corporation. Referenced as the split packages
  Base 2.0.4, Foundation 2.3.9, InteractiveExperiences 2.1.6, WinUI 2.3.6,
  DWrite 2.1.0 and Runtime 2.4.0.
• NAudio.Wasapi 2.2.1 (with NAudio.Core 2.2.1) — © Mark Heath
• System.Drawing.Common 8.0.8 — © .NET Foundation and Contributors
• Vortice.Direct3D11 3.8.3, Vortice.DXGI 3.8.3, Vortice.DirectX 3.8.3 — © Amer Koleci
• Vortice.Mathematics 2.1.0 — © Amer Koleci
• WinRT.Runtime (C#/WinRT) — © Microsoft Corporation
• .NET 10 runtime and class libraries — © .NET Foundation and Contributors. Included
  because the installer and portable builds are published self-contained, so the
  runtime travels with the app instead of being a prerequisite on the machine.

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

— Apache-2.0 dependency

• SQLitePCLRaw 2.1.6 — bundle_e_sqlite3, core, lib.e_sqlite3, provider.e_sqlite3
  — © Eric Sink — Apache License 2.0 — https://www.apache.org/licenses/LICENSE-2.0
  These come in transitively with Microsoft.Data.Sqlite and are what binds the
  SQLite library to .NET. SQLite itself is public domain — see below.

— BSD-3-Clause dependency

• Microsoft.Web.WebView2 1.0.3719.77 — © Microsoft Corporation. It arrives with
  the Windows App SDK and is redistributed here as WebView2Loader.dll and
  Microsoft.Web.WebView2.Core.dll, although the app never hosts web content of
  its own. Its license requires the following to accompany the binary:

Copyright (C) Microsoft Corporation. All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are
met:

   * Redistributions of source code must retain the above copyright
notice, this list of conditions and the following disclaimer.
   * Redistributions in binary form must reproduce the above
copyright notice, this list of conditions and the following disclaimer
in the documentation and/or other materials provided with the
distribution.
   * The name of Microsoft Corporation, or the names of its contributors
may not be used to endorse or promote products derived from this
software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
"AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

— Public domain

• SQLite, the library behind e_sqlite3.dll —
  See https://www.sqlite.org/copyright.html

— Notices carried by the Windows App SDK

The Windows App SDK redistributes components of its own, and its Runtime package
carries a NOTICE.txt (about 330 KB) aggregating their licenses. That file ships
with the NuGet package rather than with the app, so it is found wherever the SDK
is restored — %USERPROFILE%\.nuget\packages\microsoft.windowsappsdk.runtime\
<version>\NOTICE.txt — and the native DLLs in a published build come from there
(Microsoft.ui.xaml.dll, DWriteCore.dll, CoreMessagingXP.dll and the like).

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

