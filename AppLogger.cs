using Circle_Tracker.Services;
using Microsoft.Extensions.Logging;
using Serilog;
using System;
using System.IO;

namespace Circle_Tracker
{
    internal static class AppLogger
    {
        public static readonly ILoggerFactory Factory;

        static AppLogger()
        {
#if DEBUG
            Serilog.Events.LogEventLevel consoleLevel = Serilog.Events.LogEventLevel.Debug;
#else
            Serilog.Events.LogEventLevel consoleLevel = Serilog.Events.LogEventLevel.Warning;
#endif
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.Console(restrictedToMinimumLevel: consoleLevel)
                .WriteTo.File(
                    AppPaths.LogPath,
                    restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Information,
                    rollingInterval: RollingInterval.Day,
                    fileSizeLimitBytes: 10485760,
                    retainedFileCountLimit: 7)
                .CreateLogger();

            Factory = LoggerFactory.Create(builder =>
            {
                builder.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Information);
                builder.AddSerilog(Log.Logger, dispose: true);
            });
        }

        public static Microsoft.Extensions.Logging.ILogger<T> For<T>() => Factory.CreateLogger<T>();
    }
}
