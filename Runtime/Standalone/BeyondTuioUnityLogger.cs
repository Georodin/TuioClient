using System;
using Logging = Microsoft.Extensions.Logging;
using UnityDebug = UnityEngine.Debug;

namespace BeyondFutureOne.TuioClient
{
    internal sealed class BeyondTuioUnityLogger : Logging.ILogger
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(Logging.LogLevel logLevel)
        {
            return logLevel != Logging.LogLevel.None;
        }

        public void Log<TState>(Logging.LogLevel logLevel, Logging.EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            var message = formatter != null ? formatter(state, exception) : state?.ToString();

            switch (logLevel)
            {
                case Logging.LogLevel.Trace:
                case Logging.LogLevel.Debug:
                case Logging.LogLevel.Information:
                    break;
                case Logging.LogLevel.Warning:
                case Logging.LogLevel.Critical:
                    UnityDebug.LogWarning(message);
                    break;
                case Logging.LogLevel.Error:
                    if (exception != null)
                    {
                        UnityDebug.LogException(exception);
                    }
                    else
                    {
                        UnityDebug.LogError(message);
                    }
                    break;
            }
        }
    }
}



