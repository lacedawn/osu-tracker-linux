using Avalonia;
using System;
using System.Diagnostics;
using System.Linq;

namespace Circle_Tracker
{
    class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            if (!EnsureSingleInstance())
            {
                Console.Error.WriteLine("Another instance of circle tracker is already running.");
                return;
            }

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();

        private static bool EnsureSingleInstance()
        {
            Process currentProcess = Process.GetCurrentProcess();
            Process? runningProcess = Process.GetProcesses()
                .FirstOrDefault(p =>
                    p.Id != currentProcess.Id &&
                    p.ProcessName.Equals(currentProcess.ProcessName, StringComparison.Ordinal));
            return runningProcess == null;
        }
    }
}
