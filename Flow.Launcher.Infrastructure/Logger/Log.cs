using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using NLog;
using NLog.Config;
using NLog.Targets;
using Flow.Launcher.Infrastructure.UserSettings;
using JetBrains.Annotations;
using NLog.Fluent;
using NLog.Targets.Wrappers;
using System.Runtime.ExceptionServices;
using System.Text;

namespace Flow.Launcher.Infrastructure.Logger
{
    public static class Log
    {
        public const string DirectoryName = "Logs";

        public static string CurrentLogDirectory { get; }

        static Log()
        {
            CurrentLogDirectory = Path.Combine(DataLocation.DataDirectory(), DirectoryName, Constant.Version);
            if (!Directory.Exists(CurrentLogDirectory))
            {
                Directory.CreateDirectory(CurrentLogDirectory);
            }

            var configuration = new LoggingConfiguration();

            /*
             Log looks like this:
            2021Jun23 - 12:15:39.7283967+07:00 - INFO  - PluginManager - InitializePlugins|"Total init cost for <Program> is <540ms>"
            2021Jun23 - 12:15:39.9217398+07:00 - DEBUG - PluginManager - InitializePlugins|"Init method time cost for <Browser Bookmarks> <724ms>"
            2021Jun23 - 12:15:39.9217398+07:00 - INFO  - PluginManager - InitializePlugins|"Total init cost for <Browser Bookmarks> is <745ms>"
            2021Jun23 - 12:15:40.4979608+07:00 - INFO  - App - OnStartupAsync|"Dependencies Info:
            Python Path: "
            2021Jun23 - 12:15:40.5522216+07:00 - INFO  - App - OnStartupAsync|"End Flow Launcher startup ----------------------------------------------------  "
            2021Jun23 - 12:15:40.5522216+07:00 - INFO  - App - OnStartupAsync|"Startup cost <2287ms>"
            */
            const string layout = 
                @"${date:format=yyyyMMMdd - HH\:mm\:ss.fffffffK} - " +
                @"${pad:padding=-5:inner=${level:uppercase=true}} - ${logger} - ${message}" +
                @"${onexception:inner=${logger}->${newline}" +
                @"${date:format=yyyyMMMdd - HH\:mm\:ss.fffffffK} - " +
                @"${pad:padding=-5:inner=${level:uppercase=true}}${newline}" +
                @"Message:${message}${newline}"+
                @"${exception}${newline}}";
            
            var fileTarget = new FileTarget
            {
                Name = "file",
                FileName = CurrentLogDirectory.Replace(@"\", "/") + "/${shortdate}.txt",
                Layout = layout
            };

            var fileTargetASyncWrapper = new AsyncTargetWrapper(fileTarget)
            {
                Name = "asyncFile"
            };

            var debugTarget = new DebuggerTarget
            {
                Name = "debug",
                Layout = layout,
                OptimizeBufferReuse = false
            };

            configuration.AddTarget(fileTargetASyncWrapper);
            configuration.AddTarget(debugTarget);

#if DEBUG
            var fileRule = new LoggingRule("*", LogLevel.Debug, fileTargetASyncWrapper);
            var debugRule = new LoggingRule("*", LogLevel.Debug, debugTarget);
            configuration.LoggingRules.Add(debugRule);
#else
            var fileRule = new LoggingRule("*", LogLevel.Info, fileTargetASyncWrapper);
#endif
            configuration.LoggingRules.Add(fileRule);

            NLog.Common.InternalLogger.LogToTrace = true;

            LogManager.Configuration = configuration;
        }

        private static void LogFaultyFormat(string message)
        {
            var logger = LogManager.GetLogger("FaultyLogger");
            message = $"Wrong logger message format <{message}>";
            logger.Fatal(message);
        }

        private static bool FormatValid(string message)
        {
            var parts = message.Split('|');
            var valid = parts.Length == 3 && !string.IsNullOrWhiteSpace(parts[1]) &&
                        !string.IsNullOrWhiteSpace(parts[2]);
            return valid;
        }


        public static void Exception(string className, string message, System.Exception exception,
            [CallerMemberName] string methodName = "")
        {
            exception = exception.Demystify();
#if DEBUG
            ExceptionDispatchInfo.Capture(exception).Throw();
#else

            LogInternal(LogLevel.Error, className, message, methodName, exception);
#endif
        }


        public static void Error(string className, string message, [CallerMemberName] string methodName = "")
        {
            LogInternal(LogLevel.Error, className, message, methodName);
        }

        private static void LogInternal(LogLevel level, string className, string message, string methodName = "",
            System.Exception e = null)
        {
            var logger = LogManager.GetLogger(className);

            logger.Log(level, e, "{MethodName:l}|{Message}", methodName, message);
        }

        [Conditional("DEBUG")]
        public static void Debug(string className, string message, [CallerMemberName] string methodName = "")
        {
            LogInternal(LogLevel.Debug, className, message, methodName);
        }

        public static void Info(string className, string message, [CallerMemberName] string methodName = "")
        {
            LogInternal(LogLevel.Info, className, message, methodName);
        }


        public static void Warn(string className, string message, [CallerMemberName] string methodName = "", System.Exception e = null)
        {
            LogInternal(LogLevel.Warn, className, message, methodName, e);
        }
    }
}