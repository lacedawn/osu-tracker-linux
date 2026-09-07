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
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Console()
                .WriteTo.File(
                    AppPaths.LogPath,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 3)
                .CreateLogger();

            Factory = LoggerFactory.Create(builder =>
            {
                builder.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Debug);
                builder.AddSerilog(Log.Logger, dispose: true);
            });
        }

        public static Microsoft.Extensions.Logging.ILogger<T> For<T>() => Factory.CreateLogger<T>();
    }
}
