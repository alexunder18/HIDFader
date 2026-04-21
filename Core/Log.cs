using System;
using System.IO;
using NLog;
using NLog.Config;
using NLog.Targets;

namespace HIDFader.Core
{
    public static class Log
    {
        private static Logger logger;

        public static void Initialize()
        {
            if (logger != null)
                return;

            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var appConfigDir = Path.Combine(appDataPath, "HIDFader");

            if (!Directory.Exists(appConfigDir))
                Directory.CreateDirectory(appConfigDir);

            var logFilePath = Path.Combine(appConfigDir, "HID Fader.log");
            var archivePattern = Path.Combine(appConfigDir, "HID_Fader.{#}.log");
            var config = new LoggingConfiguration();

            var fileTarget = new FileTarget("file")
            {
                FileName = logFilePath,
                Layout = "${longdate} [${level:uppercase=true:padding=-5}] ${logger:shortName=true} - ${message} ${exception:format=tostring}",
                ArchiveFileName = archivePattern,
                ArchiveAboveSize = 5 * 1024 * 1024, // 5 MB per file
                MaxArchiveFiles = 5,
                ArchiveNumbering = ArchiveNumberingMode.Rolling,
                KeepFileOpen = true,
                ConcurrentWrites = false,
                Encoding = System.Text.Encoding.UTF8,
            };

            config.AddTarget(fileTarget);
            config.AddRule(LogLevel.Debug, LogLevel.Fatal, fileTarget);

            LogManager.Configuration = config;
            logger = LogManager.GetLogger("HIDFader");

            logger.Info("=== Logging initialized. Log file: {0} ===", logFilePath);
        }

        public static void Shutdown() => LogManager.Shutdown();
    }
}
