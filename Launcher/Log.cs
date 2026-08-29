using System;
using System.IO;

namespace UltraYeaLauncher
{
    /// <summary>Registro mínimo en launcher.log junto al ejecutable. Nunca lanza excepciones.</summary>
    internal static class Log
    {
        private static readonly string Path = System.IO.Path.Combine(AppContext.BaseDirectory, "launcher.log");
        private static readonly object Gate = new object();

        public static void Write(string message)
        {
            try
            {
                lock (Gate)
                {
                    File.AppendAllText(Path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}");
                }
            }
            catch
            {
                // el registro jamás debe romper el launcher
            }
        }

        public static void Exception(string context, Exception ex)
            => Write($"ERROR [{context}] {ex.GetType().Name}: {ex.Message}");
    }
}
