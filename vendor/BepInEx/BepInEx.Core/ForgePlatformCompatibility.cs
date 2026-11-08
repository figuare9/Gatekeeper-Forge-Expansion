using BepInEx.Compatibility;
// Forge compatibility adapter for the retired MonoMod PlatformHelper API.
// The Windows/Proton loader retains BepInEx's existing Wine detection.
using System;
using System.Runtime.InteropServices;
namespace BepInEx.Compatibility;
[Flags]
public enum Platform { Unknown=0, Windows=1, Unix=2, Linux=Unix|4, MacOS=Unix|8, Android=Linux|16, iOS=MacOS|32, Bits64=64, ARM=128, Wine=256 }
public static class PlatformHelper
{
    public static T AsDelegate<T>(this IntPtr pointer) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(pointer);
    public static Platform Current { get; set; } =
        (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)?Platform.Windows:
         RuntimeInformation.IsOSPlatform(OSPlatform.OSX)?Platform.MacOS:Platform.Linux) |
        (IntPtr.Size==8?Platform.Bits64:0);
    public static bool Is(Platform value)=>(Current&value)==value;
    public static string LibrarySuffix=>Is(Platform.Windows)?"dll":Is(Platform.MacOS)?"dylib":"so";
}
