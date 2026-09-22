using System.Runtime.InteropServices;

namespace HomeApp.Services;

internal static class AppDataPaths
{
    public static string WorkspaceDirectory
    {
        get
        {
            // .NET redirects LocalApplicationData after package registration. Keep
            // the same user data directory for both development launch modes.
            var localAppData = new Guid("F1B32785-6FBA-4FCF-9D55-7B8E7F157091");
            const uint noPackageRedirection = 0x00010000;
            var result = SHGetKnownFolderPath(ref localAppData, noPackageRedirection, IntPtr.Zero, out var path);
            try
            {
                Marshal.ThrowExceptionForHR(result);
                return Path.Combine(Marshal.PtrToStringUni(path)!, "HomeApp");
            }
            finally { Marshal.FreeCoTaskMem(path); }
        }
    }

    [DllImport("shell32.dll", ExactSpelling = true)]
    private static extern int SHGetKnownFolderPath(ref Guid folder, uint flags, IntPtr token, out IntPtr path);
}
