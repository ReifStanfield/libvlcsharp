using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using LibVLCSharp.Helpers;

namespace LibVLCSharp
{
    /// <summary>
    /// The Core class handles libvlc loading intricacies on various platforms as well as
    /// the libvlc/libvlcsharp version match check.
    /// </summary>
    public static partial class Core
    {
        partial struct Native
        {
#if !UWP10_0 && !NETSTANDARD1_1
            [DllImport(Constants.LibraryName, CallingConvention = CallingConvention.Cdecl,
                EntryPoint = "libvlc_get_version")]
            internal static extern IntPtr LibVLCVersion();
#endif
            [DllImport(Constants.Kernel32, SetLastError = true, CharSet = CharSet.Unicode)]
            internal static extern IntPtr LoadLibraryW(string dllToLoad);

            [DllImport(Constants.LibSystem, EntryPoint = "dlopen")]
            internal static extern IntPtr Dlopen(string libraryPath, int mode = 1);

            [DllImport(Constants.LibSystem, EntryPoint = "setenv", CharSet = CharSet.Ansi)]
            internal static extern int Setenv(string name, string value, int overwrite);
        }

#if !UWP10_0 && !NETSTANDARD1_1
        /// <summary>
        /// Checks whether the major version of LibVLC and LibVLCSharp match <para/>
        /// Throws a VLCException if the major versions mismatch
        /// </summary>
        static void EnsureVersionsMatch()
        {
            var libvlcMajorVersion = int.Parse(Native.LibVLCVersion().FromUtf8()?.Split('.').FirstOrDefault() ?? "0");
            var libvlcsharpMajorVersion = Assembly.GetExecutingAssembly().GetName().Version?.Major;
            if (libvlcMajorVersion != libvlcsharpMajorVersion)
                throw new VLCException($"Version mismatch between LibVLC {libvlcMajorVersion} and LibVLCSharp {libvlcsharpMajorVersion}. " +
                    $"They must share the same major version number");
        }

#endif
        static string LibVLCPath(string dir) => Path.Combine(dir, $"{Constants.LibraryName}{LibraryExtension}");
        static string LibVLCCorePath(string dir) => Path.Combine(dir, $"{Constants.CoreLibraryName}{LibraryExtension}");
        static string LibraryExtension => PlatformHelper.IsWindows ? Constants.WindowsLibraryExtension : Constants.MacLibraryExtension;
#if !UNITY
        internal static void Log(string message)
        {
#if !UWP10_0 && !NETSTANDARD1_1
            Trace.WriteLine(message);
#else
            Debug.WriteLine(message);
#endif
        }
#endif

        static bool _libvlcLoaded;
        internal static bool LibVLCLoaded
        {
#if (DESKTOP && !NETSTANDARD1_1) || WINUI
            get => _libvlcLoaded || LibvlcHandle != IntPtr.Zero;
#else
            get => _libvlcLoaded;
#endif
            set => _libvlcLoaded = value;
        }

#if DESKTOP && !NETSTANDARD1_1 || WINUI
        static List<(string libvlccore, string libvlc)> ComputeLibVLCSearchPaths()
        {
            var paths = new List<(string, string)>();
            string arch;

            if (PlatformHelper.IsMac)
            {
#if !NET45 && !NET40 && !NETSTANDARD1_1
                var macArch = RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                    ? ArchitectureNames.MacOSArm64
                    : ArchitectureNames.MacOS64;
#else
                var macArch = ArchitectureNames.MacOS64;
#endif
                arch = Path.Combine(macArch, Constants.Lib);
            }

#if !NET45 && !NET40 && !NETSTANDARD1_1
            else if (PlatformHelper.IsWindows)
            {
                arch = RuntimeInformation.ProcessArchitecture switch
                {
                    Architecture.X64 => ArchitectureNames.Win64,
                    Architecture.X86 => ArchitectureNames.Win86,
                    Architecture.Arm64 => ArchitectureNames.WinArm64,
                    _ => PlatformHelper.IsX64BitProcess ? ArchitectureNames.Win64 : ArchitectureNames.Win86
                };
            }
#endif

            else
            {
                arch = PlatformHelper.IsX64BitProcess ? ArchitectureNames.Win64 : ArchitectureNames.Win86;
            }

#if NET6_0_OR_GREATER
            var libvlcAssemblyLocation = AppContext.BaseDirectory;
#else
            var libvlcAssemblyLocation = typeof(LibVLC).Assembly.Location;
#if !NET45
            if (string.IsNullOrEmpty(libvlcAssemblyLocation))
            {
                libvlcAssemblyLocation = AppContext.BaseDirectory;
            }
#endif
#endif
            var libvlcDirPath1 = Path.Combine(Path.GetDirectoryName(libvlcAssemblyLocation)!,
                Constants.LibrariesRepositoryFolderName, arch);

            var libvlccorePath1 = LibVLCCorePath(libvlcDirPath1);

            var libvlcPath1 = LibVLCPath(libvlcDirPath1);
            paths.Add((libvlccorePath1, libvlcPath1));

#if NET6_0_OR_GREATER
            var assemblyLocation = AppContext.BaseDirectory;
#else
            var assemblyLocation = Assembly.GetEntryAssembly()?.Location ?? Assembly.GetExecutingAssembly()?.Location;
#endif
            if(!string.IsNullOrEmpty(assemblyLocation))
            { 
                var libvlcDirPath2 = Path.Combine(Path.GetDirectoryName(assemblyLocation)!,
                    Constants.LibrariesRepositoryFolderName, arch);

                var libvlccorePath2 = string.Empty;
                if (PlatformHelper.IsWindows)
                {
                    libvlccorePath2 = LibVLCCorePath(libvlcDirPath2);
                }

                var libvlcPath2 = LibVLCPath(libvlcDirPath2);
                paths.Add((libvlccorePath2, libvlcPath2));
            }
            var libvlcPath3 = LibVLCPath(Path.GetDirectoryName(libvlcAssemblyLocation)!);

            paths.Add((string.Empty, libvlcPath3));

            // Add Win64 folders as fallback for WinArm64 to keep compatibility
            if (arch == ArchitectureNames.WinArm64)
            {
                var fallbackLibvlcDirPath1 = Path.Combine(Path.GetDirectoryName(libvlcAssemblyLocation)!,
                    Constants.LibrariesRepositoryFolderName, ArchitectureNames.Win64);

                var fallbackLibvlccorePath1 = LibVLCCorePath(fallbackLibvlcDirPath1);
                var fallbackLibvlcPath1 = LibVLCPath(fallbackLibvlcDirPath1);
                paths.Add((fallbackLibvlccorePath1, fallbackLibvlcPath1));
            
                if (!string.IsNullOrEmpty(assemblyLocation))
                {
                    var fallbackLibvlcDirPath2 = Path.Combine(Path.GetDirectoryName(assemblyLocation)!,
                        Constants.LibrariesRepositoryFolderName, ArchitectureNames.Win64);

                    var fallbackLibvlccorePath2 = LibVLCCorePath(fallbackLibvlcDirPath2);
                    var fallbackLibvlcPath2 = LibVLCPath(fallbackLibvlcDirPath2);
                    paths.Add((fallbackLibvlccorePath2, fallbackLibvlcPath2));
                }
            }

            // Add osx-x64 folders as fallback for osx-arm64, so that a universal (fat) libvlc
            // shipped under the legacy x64 folder still gets picked up on Apple Silicon
            if (arch == Path.Combine(ArchitectureNames.MacOSArm64, Constants.Lib))
            {
                var macFallbackArch = Path.Combine(ArchitectureNames.MacOS64, Constants.Lib);

                var fallbackMacDirPath1 = Path.Combine(Path.GetDirectoryName(libvlcAssemblyLocation)!,
                    Constants.LibrariesRepositoryFolderName, macFallbackArch);
                paths.Add((LibVLCCorePath(fallbackMacDirPath1), LibVLCPath(fallbackMacDirPath1)));

                if (!string.IsNullOrEmpty(assemblyLocation))
                {
                    var fallbackMacDirPath2 = Path.Combine(Path.GetDirectoryName(assemblyLocation)!,
                        Constants.LibrariesRepositoryFolderName, macFallbackArch);
                    paths.Add((LibVLCCorePath(fallbackMacDirPath2), LibVLCPath(fallbackMacDirPath2)));
                }
            }

            if (PlatformHelper.IsMac)
            {
                var libvlcPath4 = Path.Combine(Path.Combine(Path.GetDirectoryName(libvlcAssemblyLocation)!,
                    Constants.Lib), $"{Constants.LibVLC}{LibraryExtension}");
                var libvlccorePath4 = LibVLCCorePath(Path.Combine(Path.GetDirectoryName(libvlcAssemblyLocation)!, Constants.Lib));
                paths.Add((libvlccorePath4, libvlcPath4));
            }

            return paths;
        }

        static void LoadLibVLC(string? libvlcDirectoryPath = null)
        {
            // full path to directory location of libvlc and libvlccore has been provided
            if (!string.IsNullOrEmpty(libvlcDirectoryPath))
            {
                bool loadResult;
                var libvlccorePath = LibVLCCorePath(libvlcDirectoryPath!);
                loadResult = LoadNativeLibrary(libvlccorePath, out LibvlccoreHandle);
                if (!loadResult)
                {
                    Log($"Failed to load required native libraries at {libvlccorePath}");
                    return;
                }

                var libvlcPath = LibVLCPath(libvlcDirectoryPath!);
                loadResult = LoadNativeLibrary(libvlcPath, out LibvlcHandle);
                if (!loadResult)
                    Log($"Failed to load required native libraries at {libvlcPath}");
                else
                    ConfigurePluginPath(libvlcDirectoryPath!);
                return;
            }

            var paths = ComputeLibVLCSearchPaths();

            foreach (var (libvlccore, libvlc) in paths)
            {
                LoadNativeLibrary(libvlccore, out LibvlccoreHandle);
                var loadResult = LoadNativeLibrary(libvlc, out LibvlcHandle);
                if (loadResult)
                {
                    ConfigurePluginPath(Path.GetDirectoryName(libvlc)!);
                    break;
                }
            }

            if (!LibVLCLoaded)
            {
                throw new VLCException("Failed to load required native libraries. There might be several reasons for this:" +
#if UNITY
                    $"{Environment.NewLine}Have you installed the latest LibVLC package from VLC Unity for your target platform?" +
#else
                    $"{Environment.NewLine}Have you installed the latest LibVLC package from nuget for your target platform?" +
#endif
                    $"{Environment.NewLine}Search paths include {string.Join("; ", paths.Select(p => $"{p.libvlc},{p.libvlccore}"))}" + 
                    $"{Environment.NewLine}Are you using an unsupported constructor LibVLC option?");
            }
        }

        /// <summary>
        /// Point libvlc at the plugins shipped next to the dylib that was just loaded.
        /// <para/> The macOS builds have their plugin directory baked in at configure time, so they find
        /// nothing once relocated into an application's output directory. VLC_PLUGIN_PATH overrides it.
        /// <para/> A VLC_PLUGIN_PATH set by the application is left untouched.
        /// </summary>
        /// <param name="libvlcDirectory">The directory the libvlc dylib was loaded from</param>
        static void ConfigurePluginPath(string libvlcDirectory)
        {
            if (!PlatformHelper.IsMac)
                return;

            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Constants.VlcPluginPathEnvVar)))
                return;

            var pluginPath = Path.Combine(libvlcDirectory, Constants.Vlc, Constants.Plugins);
            if (!Directory.Exists(pluginPath))
            {
                Log($"No plugins directory at {pluginPath}, leaving {Constants.VlcPluginPathEnvVar} alone");
                return;
            }

            // .NET keeps its own copy of the environment, so setenv is what libvlc will actually read
            Native.Setenv(Constants.VlcPluginPathEnvVar, pluginPath, 1);
            Environment.SetEnvironmentVariable(Constants.VlcPluginPathEnvVar, pluginPath);
            Log($"Set {Constants.VlcPluginPathEnvVar} to {pluginPath}");
        }
#endif
        internal static void EnsureLoaded()
        {
            if (LibVLCLoaded)
            {
                return;
            }

            Initialize();
        }
        static bool LoadNativeLibrary(string nativeLibraryPath, out IntPtr handle)
        {
            handle = IntPtr.Zero;
            Log($"Loading {nativeLibraryPath}");

#if !NETSTANDARD1_1
            if (!File.Exists(nativeLibraryPath))
            {
                Log($"Cannot find {nativeLibraryPath}");
                return false;
            }
#endif
            if (PlatformHelper.IsMac)
            {
                handle = Native.Dlopen(nativeLibraryPath);
            }
            else
            {
                handle = Native.LoadLibraryW(nativeLibraryPath);
            }

            return handle != IntPtr.Zero;
        }
    }
}
