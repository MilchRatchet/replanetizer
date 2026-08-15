using System.Runtime.InteropServices;

namespace Replanetizer.MemoryHook
{
    internal static class ProcessMemoryFactory
    {
        public static IProcessMemory Create()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
#if _WINDOWS
                return new WindowsProcessMemory();
#else
                return new UnsupportedProcessMemory("Memory hooks are not available in this build.");
#endif
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return new LinuxProcessMemory();
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return new MacOSProcessMemory();
            }

            return new UnsupportedProcessMemory("Memory hooks are not supported on this operating system.");
        }
    }

    internal sealed class UnsupportedProcessMemory : IProcessMemory
    {
        public bool IsAvailable => false;
        public string ErrorMessage { get; }

        public UnsupportedProcessMemory(string errorMessage)
        {
            ErrorMessage = errorMessage;
        }

        public bool Read(long address, byte[] buffer)
        {
            return false;
        }

        public bool Suspend()
        {
            return false;
        }

        public bool Resume()
        {
            return false;
        }

        public void Dispose()
        {
        }
    }
}
