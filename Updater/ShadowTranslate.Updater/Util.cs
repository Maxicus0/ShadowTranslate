using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace ShadowTranslate.Updater
{
    internal static class U
    {
        public const string Version = "1.0.0";

        public static string Hex(byte[] b)
        {
            var sb = new StringBuilder(b.Length * 2);
            foreach (var x in b) sb.Append(x.ToString("x2"));
            return sb.ToString();
        }

        public static string Sha256(byte[] data, int len)
        {
            using (var h = SHA256.Create()) return Hex(h.ComputeHash(data, 0, len));
        }

        public static string Mb(long bytes)
        {
            return (bytes / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture);
        }

        public static string Quote(string a)
        {
            if (a.Length > 0 && a.IndexOfAny(new[] { ' ', '\t', '"' }) < 0) return a;
            var sb = new StringBuilder("\"");
            int bs = 0;
            foreach (char ch in a)
            {
                if (ch == '\\') { bs++; continue; }
                if (ch == '"') { sb.Append('\\', bs * 2 + 1); sb.Append('"'); }
                else { sb.Append('\\', bs); sb.Append(ch); }
                bs = 0;
            }
            sb.Append('\\', bs * 2);
            sb.Append('"');
            return sb.ToString();
        }

        public static string NormalizeRel(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            s = s.Replace('\\', '/').TrimEnd('/');
            if (s.Length == 0 || s[0] == '/') return null;
            var segs = s.Split('/');
            var bad = Path.GetInvalidFileNameChars();
            foreach (var seg in segs)
            {
                if (seg.Length == 0 || seg == "." || seg == ".." || seg.IndexOfAny(bad) >= 0) return null;
                if (seg.EndsWith(" ") || seg.EndsWith(".")) return null;
            }
            return s;
        }

        public static string ToOs(string root, string rel)
        {
            return Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
        }

        public static bool Glob(string pattern, string rel)
        {
            pattern = pattern.Trim().Replace('\\', '/');
            if (pattern.Length == 0) return false;
            var rx = "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            return System.Text.RegularExpressions.Regex.IsMatch(rel, rx, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        public static int[] ParseVer(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            s = s.Trim();
            if (s.Length > 0 && (s[0] == 'v' || s[0] == 'V')) s = s.Substring(1);
            int cut = s.IndexOfAny(new[] { '-', '+', ' ' });
            if (cut >= 0) s = s.Substring(0, cut);
            var parts = s.Split('.');
            var r = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out r[i])) return null;
            return r;
        }

        public static int CompareVer(int[] a, int[] b)
        {
            int n = Math.Max(a.Length, b.Length);
            for (int i = 0; i < n; i++)
            {
                int x = i < a.Length ? a[i] : 0, y = i < b.Length ? b[i] : 0;
                if (x != y) return x < y ? -1 : 1;
            }
            return 0;
        }

        public static void Retry(Action a, int tries, int delayMs)
        {
            for (int i = 1; ; i++)
            {
                try { a(); return; }
                catch (Exception e)
                {
                    if (!(e is IOException || e is UnauthorizedAccessException) || i >= tries) throw;
                    Thread.Sleep(delayMs);
                }
            }
        }

        public static void DeleteDirQuiet(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
            catch
            {
                try
                {
                    foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                    { try { File.SetAttributes(f, FileAttributes.Normal); File.Delete(f); } catch { } }
                    Directory.Delete(dir, true);
                }
                catch { }
            }
        }
    }

    internal static class Crc32
    {
        private static readonly uint[] T = Build();
        private static uint[] Build()
        {
            var t = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                t[i] = c;
            }
            return t;
        }
        public static uint Update(uint state, byte[] buf, int off, int cnt)
        {
            for (int i = off; i < off + cnt; i++) state = T[(state ^ buf[i]) & 0xFF] ^ (state >> 8);
            return state;
        }
        public const uint Init = 0xFFFFFFFFu;
        public static uint Finish(uint state) { return state ^ 0xFFFFFFFFu; }

        public static long? OfFile(string path, long expectedLen)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    if (fs.Length != expectedLen) return -1;
                    var buf = new byte[81920];
                    uint s = Init; int n;
                    while ((n = fs.Read(buf, 0, buf.Length)) > 0) s = Update(s, buf, 0, n);
                    return Finish(s);
                }
            }
            catch { return null; }
        }
    }
}
