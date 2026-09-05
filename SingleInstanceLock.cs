using System;
using System.IO;

namespace Circle_Tracker
{
    public static class SingleInstanceLock
    {
        public static string GetLockFilePath()
        {
            return GetLockFilePath(
                Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR"),
                Path.GetTempPath(),
                Environment.UserName);
        }

        public static string GetLockFilePath(string? xdgRuntimeDir, string? tempPath = null, string? userName = null)
        {
            if (!string.IsNullOrEmpty(xdgRuntimeDir))
            {
                return Path.Combine(xdgRuntimeDir, "circle-tracker.lock");
            }

            string temp = tempPath ?? Path.GetTempPath();
            string user = userName ?? Environment.UserName;
            return Path.Combine(temp, $"circle-tracker-{user}.lock");
        }

        public static FileStream? TryAcquire(string lockPath)
        {
            return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }

        public static void Release(FileStream? lockFile, string lockPath)
        {
            if (lockFile != null)
            {
                try
                {
                    lockFile.Dispose();
                }
                catch
                {
                }

                try
                {
                    if (File.Exists(lockPath))
                    {
                        File.Delete(lockPath);
                    }
                }
                catch
                {
                }
            }
        }
    }
}
