// Copyright 2026 EyeGrok contributors
// SPDX-License-Identifier: Apache-2.0

using System;
using UnityEngine;

namespace EyeGrok.Security
{
    public sealed class RedactingLogHandler : ILogHandler
    {
        readonly ILogHandler _inner;

        public RedactingLogHandler(ILogHandler inner)
        {
            _inner = inner;
        }

        public void LogFormat(LogType logType, UnityEngine.Object context, string format, params object[] args)
        {
            string rendered;
            try
            {
                rendered = args == null || args.Length == 0 ? format : string.Format(format, args);
            }
            catch (FormatException)
            {
                rendered = format;
            }
            _inner.LogFormat(logType, context, "{0}", Redactor.Redact(rendered));
        }

        public void LogException(Exception exception, UnityEngine.Object context)
        {
            if (exception == null)
            {
                _inner.LogException(exception, context);
                return;
            }
            _inner.LogException(new Exception(Redactor.Redact(exception.ToString())), context);
        }
    }

    public static class LogInstall
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            var current = Debug.unityLogger.logHandler;
            if (current is RedactingLogHandler) return;
            Debug.unityLogger.logHandler = new RedactingLogHandler(current);
        }
    }
}
