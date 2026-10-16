using System.Reflection;
using System.Runtime.InteropServices;

namespace TheOne.PrintingTools.Avalonia.Platforms;

internal static class NativeLibraries
{
    static NativeLibraries()
    {
        NativeLibrary.SetDllImportResolver(typeof(NativeLibraries).Assembly, Resolve);
    }

    internal static void Initialize()
    {
    }

    private static IntPtr Resolve(string name, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (name == "theone-cups")
        {
            var path = OperatingSystem.IsMacOS() ? "/usr/lib/libcups.2.dylib" : "libcups.so.2";
            if (NativeLibrary.TryLoad(path, assembly, searchPath, out var handle)) return handle;
            throw new PrintException("系统 CUPS 客户端库不可用。请确认操作系统已安装并启用打印服务。");
        }

        if (name == "TheOnePrinting")
        {
            // NuGet resolves RID native assets; project references use the same runtime layout.
            if (NativeLibrary.TryLoad("libTheOnePrinting.dylib", assembly, searchPath, out var handle)) return handle;
            var rid = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "osx-arm64" : "osx-x64";
            var path = Path.Combine(AppContext.BaseDirectory, "runtimes", rid, "native", "libTheOnePrinting.dylib");
            if (NativeLibrary.TryLoad(path, out handle)) return handle;
            throw new PrintException("打印包的 macOS 原生资源缺失，请检查 NuGet 还原与发布产物。");
        }

        return IntPtr.Zero;
    }
}