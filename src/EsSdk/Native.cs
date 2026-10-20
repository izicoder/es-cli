// P/Invoke surface for the voidtools Everything SDK (Everything64.dll).
// https://www.voidtools.com/support/everything/sdk/
using System.Reflection;
using System.Runtime.InteropServices;

namespace EsSdk;

internal static class Native
{
    internal const string Dll = "Everything64.dll";

    // Everything_GetRequestFlags values.
    internal const uint ReqFileName = 0x1;
    internal const uint ReqPath = 0x2;
    internal const uint ReqFullPath = 0x4;
    internal const uint ReqExtension = 0x8;
    internal const uint ReqSize = 0x10;
    internal const uint ReqDateModified = 0x40;

    static Native() => DllResolver.Register();

    [DllImport(Dll, CharSet = CharSet.Unicode)]
    internal static extern void Everything_SetSearchW([MarshalAs(UnmanagedType.LPWStr)] string search);

    [DllImport(Dll)]
    internal static extern void Everything_SetMatchPath([MarshalAs(UnmanagedType.Bool)] bool enabled);

    [DllImport(Dll)]
    internal static extern void Everything_SetMatchCase([MarshalAs(UnmanagedType.Bool)] bool enabled);

    [DllImport(Dll)]
    internal static extern void Everything_SetMatchWholeWord([MarshalAs(UnmanagedType.Bool)] bool enabled);

    [DllImport(Dll)]
    internal static extern void Everything_SetRegex([MarshalAs(UnmanagedType.Bool)] bool enabled);

    [DllImport(Dll)]
    internal static extern void Everything_SetMax(uint count);

    [DllImport(Dll)]
    internal static extern void Everything_SetOffset(uint offset);

    [DllImport(Dll)]
    internal static extern void Everything_SetSort(uint sort);

    [DllImport(Dll)]
    internal static extern void Everything_SetRequestFlags(uint flags);

    [DllImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool Everything_QueryW([MarshalAs(UnmanagedType.Bool)] bool wait);

    [DllImport(Dll)]
    internal static extern uint Everything_GetLastError();

    [DllImport(Dll)]
    internal static extern uint Everything_GetNumResults();

    [DllImport(Dll)]
    internal static extern uint Everything_GetTotResults();

    [DllImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool Everything_IsFolderResult(uint index);

    [DllImport(Dll, CharSet = CharSet.Unicode)]
    internal static extern uint Everything_GetResultFullPathNameW(uint index, IntPtr buffer, uint sizeInChars);

    [DllImport(Dll, CharSet = CharSet.Unicode)]
    internal static extern IntPtr Everything_GetResultExtensionW(uint index);

    [DllImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool Everything_GetResultSize(uint index, out long size);

    // FILETIME is two DWORDs = one 64-bit value, so `out long` matches the native layout.
    [DllImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool Everything_GetResultDateModified(uint index, out long fileTime);

    [DllImport(Dll)]
    internal static extern void Everything_CleanUp();
}

/// <summary>
/// Locates Everything64.dll instead of relying on it sitting next to the executable.
/// Order: EVERYTHING_SDK_PATH (file or directory), the app directory, then an Everything install.
/// </summary>
internal static class DllResolver
{
    private static int _registered;

    internal static void Register()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 1) return;

        try
        {
            NativeLibrary.SetDllImportResolver(typeof(DllResolver).Assembly, Resolve);
        }
        catch (InvalidOperationException)
        {
            // A resolver is already installed for this assembly; default probing still applies.
        }
    }

    private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!string.Equals(libraryName, Native.Dll, StringComparison.OrdinalIgnoreCase)) return IntPtr.Zero;

        foreach (var candidate in Candidates())
        {
            if (!File.Exists(candidate)) continue;
            if (NativeLibrary.TryLoad(candidate, out var handle)) return handle;
        }

        return IntPtr.Zero;
    }

    private static IEnumerable<string> Candidates()
    {
        var configured = Environment.GetEnvironmentVariable("EVERYTHING_SDK_PATH");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            yield return Directory.Exists(configured)
                ? Path.Combine(configured, Native.Dll)
                : configured;
        }

        yield return Path.Combine(AppContext.BaseDirectory, Native.Dll);

        string?[] installRoots =
        [
            Environment.GetEnvironmentVariable("ProgramW6432"),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        ];

        foreach (var root in installRoots)
        {
            if (string.IsNullOrEmpty(root)) continue;
            yield return Path.Combine(root, "Everything", Native.Dll);
        }
    }
}
