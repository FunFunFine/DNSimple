using System;
using NLog;

namespace DNSimple
{
    public static class LoggerExtensions
    {
        public static T LogDebug<T>(this T item, string message, Logger logger)
        {
            logger.Debug(message);
            return item;
        }

        public static T LogDebug<T>(this T item, Func<T, string> message, Logger logger)
        {
            logger.Debug(message(item));
            return item;
        }
    }
}