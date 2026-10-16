# Third-party notices

PDF import uses the native bblanchon.PDFium.Win32, bblanchon.PDFium.macOS and bblanchon.PDFium.Linux NuGet packages,
version 152.0.7961. Their package metadata declares Apache-2.0. PDFium and its bundled components retain their upstream
licenses; a copy of the PDFium license is included as PDFium-LICENSE.txt in this package and docs/PDFium-LICENSE.txt in
the repository. No PDFium source code is incorporated into this library.

- Binary distribution: https://github.com/bblanchon/pdfium-binaries
- PDFium source and license: https://pdfium.googlesource.com/pdfium/+/refs/heads/main/LICENSE
- Bundled third-party component licenses: https://pdfium.googlesource.com/pdfium/+/refs/heads/main/third_party/
- NuGet package metadata: https://www.nuget.org/packages/bblanchon.PDFium.macOS/152.0.7961

This independent package uses Avalonia 12.1.4 and its official Fluent theme under the MIT license below. PrintingTools
additionally uses Avalonia.Skia / SkiaSharp and platform printing services; their respective upstream licenses continue
to apply.

The editor / printing UI and the package-specific semantic palette were extracted from TheOne.Avalonia. The palette
originated in the TheOne Nova-inspired theme; resource names are now package-specific. No Eremex source code or binaries
are included. TheOne-authored code is covered by the repository LICENSE.

Avalonia source and license: https://github.com/AvaloniaUI/Avalonia/tree/12.1.4

```text
The MIT License (MIT)

Copyright (c) AvaloniaUI OÜ
All Rights Reserved

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
