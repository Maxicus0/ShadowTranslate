using System;
using System.IO;
using System.Text;

namespace ShadowTranslate.Updater
{
    internal static class Log
    {
        private static StreamWriter _file;
        private static readonly object L = new object();
        public static string Path;

        public static void Init(string path)
        {
            Path = path;
            try
            {
                var fi = new FileInfo(path);
                if (fi.Exists && fi.Length > 512 * 1024)
                {
                    var old = path + ".1";
                    try { if (File.Exists(old)) File.Delete(old); File.Move(path, old); } catch { }
                }
                var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                _file = new StreamWriter(fs, new UTF8Encoding(true)) { AutoFlush = true };
            }
            catch { _file = null; }
        }

        public static void Info(string m) { Write("INFO ", m); }
        public static void Warn(string m) { Write("WARN ", m); }
        public static void Error(string m) { Write("ERROR", m); }

        public static void Debug(string m) { Write("DEBUG", m, false); }

        private static void Write(string lvl, string m, bool toConsole = true)
        {
            lock (L)
            {
                var line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " [" + lvl + "] " + m;
                try { if (_file != null) _file.WriteLine(line); } catch { }
                if (toConsole) ConsoleHost.WriteLine(DateTime.Now.ToString("HH:mm:ss") + " " + (lvl.Trim() == "INFO" ? "" : "[" + lvl.Trim() + "] ") + m);
            }
        }
    }
}
