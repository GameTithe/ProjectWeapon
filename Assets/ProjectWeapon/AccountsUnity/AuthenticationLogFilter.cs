#nullable enable

using System;
using System.Runtime.CompilerServices;
using UnityEngine;

[assembly: InternalsVisibleTo("ProjectWeapon.Accounts.Tests")]

namespace ProjectWeapon.Accounts.Unity
{
    // The 3.9 SDK uses these tags for browser URLs and transport exceptions.
    internal sealed class AuthenticationLogFilter : ILogHandler
    {
        private readonly ILogHandler next;

        internal AuthenticationLogFilter(ILogHandler next) => this.next = next;

        internal static void Install()
        {
            ILogger logger = Debug.unityLogger;
            if (logger.logHandler is not AuthenticationLogFilter)
                logger.logHandler = new AuthenticationLogFilter(logger.logHandler);
        }

        public void LogFormat(LogType logType, UnityEngine.Object? context, string format, params object[] args)
        {
            if (IsAuthTag(format) || (args.Length != 0 && args[0] is string tag && IsAuthTag(tag)))
                return;
            next.LogFormat(logType, context, format, args);
        }

        public void LogException(Exception exception, UnityEngine.Object? context)
        {
            const string sdkNamespace = "Unity.Services.Authentication.";
            if (exception.GetType().FullName?.StartsWith(sdkNamespace, StringComparison.Ordinal) == true ||
                exception.StackTrace?.IndexOf(sdkNamespace, StringComparison.Ordinal) >= 0)
                return;
            next.LogException(exception, context);
        }

        private static bool IsAuthTag(string value)
            => value == "[Authentication]" || value == "[PlayerAccounts]"
                || value.StartsWith("[Authentication]:", StringComparison.Ordinal)
                || value.StartsWith("[PlayerAccounts]:", StringComparison.Ordinal);
    }
}
