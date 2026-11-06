using System;
using System.Globalization;
using System.IO;

namespace PairTap
{
    internal static class Diagnostics
    {
        private static readonly object Gate = new object();
        private static readonly string DirectoryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Product.Name, "Logs");
        private static readonly string LogPath = Path.Combine(DirectoryPath, "runtime.log");

        internal static void Write(string message)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(DirectoryPath);
                    var log = new FileInfo(LogPath);
                    if (log.Exists && log.Length >= 512 * 1024)
                    {
                        string previous = Path.Combine(DirectoryPath, "runtime.previous.log");
                        File.Delete(previous);
                        File.Move(LogPath, previous);
                    }
                    File.AppendAllText(LogPath, DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture) +
                        " " + message + Environment.NewLine);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (System.Security.SecurityException) { }
        }

        internal static void Failure(string stage, Exception error)
        {
            Write(stage + " exception=" + error.GetType().Name + " hresult=0x" + error.HResult.ToString("X8"));
        }
    }
}
