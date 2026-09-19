using System;
using System.Runtime.InteropServices;
using System.Text;

namespace CultistAccessibility.SpeechHost
{
    /// <summary>
    /// P/Invoke declarations for Prism 0.18.2 (prism-windows-x64/include/prism.h).
    /// cdecl everywhere; strings are null-terminated UTF-8 owned by the caller;
    /// C bool is one byte (UnmanagedType.I1).
    /// </summary>
    internal static class PrismNative
    {
        private const string Dll = "prism";

        public const int PRISM_OK = 0;

        public const ulong FEATURE_SUPPORTS_SPEAK = 1UL << 2;
        public const ulong FEATURE_SUPPORTS_BRAILLE = 1UL << 4;
        public const ulong FEATURE_SUPPORTS_OUTPUT = 1UL << 5;
        public const ulong FEATURE_SUPPORTS_STOP = 1UL << 7;

        /// <summary>Mirror of PrismConfig (version 3). Blittable; default packing matches the C layout on x64.</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct PrismConfig
        {
            public byte version;
            public IntPtr registry;
            public IntPtr availability_callback;
            public IntPtr availability_userdata;
            public uint availability_poll_interval_ms;
            public uint availability_debounce_samples;
            public uint availability_backoff_max_ms;
            public byte availability_auto_power_manage;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void AvailabilityCallback(IntPtr userdata, ulong backend, IntPtr name, [MarshalAs(UnmanagedType.I1)] bool available);

        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr LoadLibrary(string path);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern PrismConfig prism_config_init();
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr prism_init(IntPtr cfg);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr prism_init(ref PrismConfig cfg);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void prism_shutdown(IntPtr ctx);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr prism_registry_create_best(IntPtr ctx);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr prism_registry_create(IntPtr ctx, ulong id);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int prism_backend_initialize(IntPtr backend);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern ulong prism_registry_id(IntPtr ctx, byte[] utf8Name);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int prism_registry_priority(IntPtr ctx, ulong id);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr prism_registry_name(IntPtr ctx, ulong id);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void prism_backend_free(IntPtr backend);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr prism_backend_name(IntPtr backend);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern ulong prism_backend_get_features(IntPtr backend);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int prism_backend_speak(IntPtr backend, byte[] utf8, [MarshalAs(UnmanagedType.I1)] bool interrupt);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int prism_backend_output(IntPtr backend, byte[] utf8, [MarshalAs(UnmanagedType.I1)] bool interrupt);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int prism_backend_braille(IntPtr backend, byte[] utf8);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int prism_backend_stop(IntPtr backend);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr prism_error_string(int error);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr prism_version_string();

        public static byte[] Utf8Z(string s)
        {
            var bytes = new byte[Encoding.UTF8.GetByteCount(s) + 1];
            Encoding.UTF8.GetBytes(s, 0, s.Length, bytes, 0);
            return bytes;
        }

        /// <summary>Reads a library-owned UTF-8 string. Never free the pointer.</summary>
        public static string FromUtf8(IntPtr p)
        {
            if (p == IntPtr.Zero) return null;
            int len = 0;
            while (Marshal.ReadByte(p, len) != 0) len++;
            var buf = new byte[len];
            Marshal.Copy(p, buf, 0, len);
            return Encoding.UTF8.GetString(buf);
        }

        public static string ErrorString(int error)
        {
            try { return FromUtf8(prism_error_string(error)) ?? ("error " + error); }
            catch { return "error " + error; }
        }
    }
}
