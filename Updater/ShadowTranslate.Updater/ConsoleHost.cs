using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace ShadowTranslate.Updater
{
    internal static class ConsoleHost
    {
        [DllImport("kernel32.dll")] private static extern bool AllocConsole();
        [DllImport("kernel32.dll")] private static extern bool FreeConsole();
        [DllImport("kernel32.dll")] private static extern IntPtr GetStdHandle(int n);
        [DllImport("kernel32.dll")] private static extern bool SetConsoleOutputCP(uint cp);
        [DllImport("kernel32.dll")] private static extern bool GetConsoleMode(IntPtr h, out uint mode);
        [DllImport("kernel32.dll")] private static extern bool SetConsoleMode(IntPtr h, uint mode);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool SetConsoleTitle(string t);
        [DllImport("kernel32.dll")] private static extern IntPtr GetConsoleWindow();
        [DllImport("user32.dll")] private static extern IntPtr GetSystemMenu(IntPtr hWnd, bool revert);
        [DllImport("user32.dll")] private static extern bool DeleteMenu(IntPtr hMenu, uint pos, uint flags);

        public static readonly bool IsWindows = Environment.OSVersion.Platform == PlatformID.Win32NT;
        private static TextWriter _out;
        private static readonly object L = new object();
        public static volatile bool SkipRequested;
        private static volatile bool _anyKey;
        private static bool _watcher;
        public static bool Visible { get { return _out != null; } }
        private static int _lastProgressLen;

        public static bool Show(string title)
        {
            lock (L)
            {
                if (_out != null) return true;
                try
                {
                    if (!IsWindows) { _out = Console.Out; return true; }
                    FreeConsole();
                    if (!AllocConsole()) return false;
                    SetConsoleOutputCP(65001);
                    SetConsoleTitle(title);
                    var h = GetStdHandle(-11);
                    _out = new StreamWriter(new FileStream(new SafeFileHandle(h, false), FileAccess.Write, 1, false),
                                            new UTF8Encoding(false), 512) { AutoFlush = true };
                    var hin = GetStdHandle(-10); uint m;
                    if (GetConsoleMode(hin, out m)) SetConsoleMode(hin, (m & ~0x40u) | 0x80u);
                    var w = GetConsoleWindow();
                    if (w != IntPtr.Zero) { var menu = GetSystemMenu(w, false); if (menu != IntPtr.Zero) DeleteMenu(menu, 0xF060, 0); }
                    Console.CancelKeyPress += delegate (object s, ConsoleCancelEventArgs e) { e.Cancel = true; };
                    StartKeyWatcher();
                    _watcher = true;
                    return true;
                }
                catch { _out = null; return false; }
            }
        }

        private static void StartKeyWatcher()
        {
            var t = new Thread(delegate ()
            {
                try
                {
                    while (true)
                    {
                        var k = Console.ReadKey(true);
                        _anyKey = true;
                        if (k.Key == ConsoleKey.S) { SkipRequested = true; }
                    }
                }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        public static void WaitAnyKey()
        {
            if (!_watcher)
            {
                try { Console.ReadKey(true); } catch { Thread.Sleep(3000); }
                return;
            }
            _anyKey = false;
            while (!_anyKey) Thread.Sleep(50);
        }

        public static void WriteLine(string s)
        {
            lock (L)
            {
                if (_out == null) return;
                try
                {
                    if (_lastProgressLen > 0) { _out.Write("\r" + new string(' ', _lastProgressLen) + "\r"); _lastProgressLen = 0; }
                    _out.WriteLine(s);
                }
                catch { }
            }
        }

        public static void Progress(string s)
        {
            lock (L)
            {
                if (_out == null) return;
                try
                {
                    var pad = _lastProgressLen > s.Length ? new string(' ', _lastProgressLen - s.Length) : "";
                    _out.Write("\r" + s + pad);
                    _lastProgressLen = s.Length;
                }
                catch { }
            }
        }

        public static void Release()
        {
            lock (L)
            {
                if (_out == null) return;
                try { _out.Flush(); } catch { }
                _out = null;
                if (IsWindows) { try { FreeConsole(); } catch { } }
            }
        }
    }
}
