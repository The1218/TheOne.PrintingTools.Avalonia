# macOS bridge maintenance

`TheOnePrinting.m` is an original AppKit / PDFKit bridge. Consumers use prebuilt runtime assets; they must not compile
this source. The two runtime directories contain the same universal x64 + arm64 library, targeting macOS 14 or later.

From the repository root, maintainers can rebuild with the same Xcode toolchain and SDK:

```sh
DEVELOPER_DIR=/Applications/Xcode.app/Contents/Developer xcrun clang \
  -isysroot /Applications/Xcode.app/Contents/Developer/Platforms/MacOSX.platform/Developer/SDKs/MacOSX.sdk \
  -dynamiclib -fobjc-arc -fvisibility=hidden -arch arm64 -arch x86_64 \
  -mmacosx-version-min=14.0 -framework AppKit -framework PDFKit -framework ApplicationServices \
  -install_name @rpath/libTheOnePrinting.dylib \
  src/TheOne.PrintingTools.Avalonia/native/TheOnePrinting.m \
  -o src/TheOne.PrintingTools.Avalonia/runtimes/osx-arm64/native/libTheOnePrinting.dylib
cp src/TheOne.PrintingTools.Avalonia/runtimes/osx-arm64/native/libTheOnePrinting.dylib \
   src/TheOne.PrintingTools.Avalonia/runtimes/osx-x64/native/libTheOnePrinting.dylib
```

Inspect architectures, dependencies and exported symbols before packing. Re-run the package-consumer smoke after
changing this ABI. Application signing / notarization must include the bundled library according to the application's
distribution requirements. No runtime download or external PrintingTools native binary is used.
