using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace ShadowTranslate.Updater
{
    internal class ZEntry
    {
        public string Rel;
        public long Size;
        public uint Crc;
        public DateTime Time;
        public ZipArchiveEntry Entry;
    }

    internal class ZipPack : IDisposable
    {
        public List<ZEntry> Files = new List<ZEntry>();
        public int Protected;
        public string StrippedRoot;
        private ZipArchive _zip;

        private class Cd { public string Name; public uint Crc; public long CSize, USize; }

        public static ZipPack Open(MemoryStream ms)
        {
            var cd = ReadCentralDirectory(ms);
            var p = new ZipPack();
            ms.Position = 0;
            p._zip = new ZipArchive(ms, ZipArchiveMode.Read, true);
            var entries = p._zip.Entries;
            if (entries.Count != cd.Count) throw new InvalidDataException("каталог архива не совпадает с содержимым (" + cd.Count + " / " + entries.Count + ")");

            var raw = new List<ZEntry>(); var rawIsDir = new List<bool>();
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (e.Length != cd[i].USize || e.CompressedLength != cd[i].CSize) throw new InvalidDataException("запись #" + i + " не сопоставлена с каталогом");
                bool isDir = cd[i].Name.EndsWith("/") || cd[i].Name.EndsWith("\\");
                var rel = U.NormalizeRel(cd[i].Name);
                if (rel == null) throw new InvalidDataException("небезопасный путь в архиве: " + cd[i].Name);
                raw.Add(new ZEntry { Rel = rel, Size = e.Length, Crc = cd[i].Crc, Time = e.LastWriteTime.DateTime, Entry = e });
                rawIsDir.Add(isDir);
            }

            string common = null; bool single = raw.Count > 0;
            for (int i = 0; i < raw.Count && single; i++)
            {
                var segs = raw[i].Rel.Split('/');
                if (segs.Length < 2 && !rawIsDir[i]) { single = false; break; }
                if (common == null) common = segs[0]; else if (!string.Equals(common, segs[0], StringComparison.OrdinalIgnoreCase)) single = false;
            }
            if (single && common != null && !IsKnownRoot(common)) p.StrippedRoot = common;

            for (int i = 0; i < raw.Count; i++)
            {
                if (rawIsDir[i]) continue;
                var z = raw[i];
                if (p.StrippedRoot != null) z.Rel = z.Rel.Substring(p.StrippedRoot.Length + 1);
                if (z.Rel.StartsWith("ShadowTranslate/", StringComparison.OrdinalIgnoreCase)) { p.Protected++; continue; }
                p.Files.Add(z);
            }
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in p.Files) if (!seen.Add(f.Rel)) throw new InvalidDataException("дубликат пути в архиве: " + f.Rel);
            return p;
        }

        private static bool IsKnownRoot(string s)
        {
            s = s.ToLowerInvariant();
            return s == "bepinex" || s == "dotnet" || s == "shadowtranslate";
        }

        public void VerifyAll(Action<int> progress)
        {
            long done = 0, all = 0; foreach (var f in Files) all += f.Size;
            var buf = new byte[81920]; int lastPct = -1;
            foreach (var f in Files)
            {
                uint s = Crc32.Init; long n = 0; int r;
                using (var st = f.Entry.Open())
                    while ((r = st.Read(buf, 0, buf.Length)) > 0) { s = Crc32.Update(s, buf, 0, r); n += r; done += r; }
                if (n != f.Size || Crc32.Finish(s) != f.Crc) throw new InvalidDataException("повреждён файл в архиве: " + f.Rel);
                int pct = all > 0 ? (int)(done * 100 / all) : 100;
                if (pct != lastPct) { lastPct = pct; if (progress != null) progress(pct); }
            }
        }

        private static List<Cd> ReadCentralDirectory(MemoryStream ms)
        {
            var b = ms.GetBuffer(); long len = ms.Length;
            long eocd = -1;
            for (long i = len - 22; i >= Math.Max(0, len - 22 - 65535); i--)
                if (b[i] == 0x50 && b[i + 1] == 0x4b && b[i + 2] == 5 && b[i + 3] == 6) { eocd = i; break; }
            if (eocd < 0) throw new InvalidDataException("это не zip-архив (нет записи конца каталога)");
            int total = U16(b, eocd + 10); long cdSize = U32(b, eocd + 12), cdOff = U32(b, eocd + 16);
            if (total == 0xFFFF || cdSize == 0xFFFFFFFFL || cdOff == 0xFFFFFFFFL) throw new NotSupportedException("zip64 не поддерживается");
            if (cdOff + cdSize > len) throw new InvalidDataException("каталог архива выходит за границы файла (архив скачан не полностью?)");
            var list = new List<Cd>(); long p = cdOff;
            for (int i = 0; i < total; i++)
            {
                if (p + 46 > len || U32(b, p) != 0x02014b50) throw new InvalidDataException("повреждён каталог архива");
                uint crc = (uint)U32(b, p + 16); long cs = U32(b, p + 20), us = U32(b, p + 24);
                int nl = U16(b, p + 28), el = U16(b, p + 30), cl = U16(b, p + 32);
                if (cs == 0xFFFFFFFFL || us == 0xFFFFFFFFL) throw new NotSupportedException("zip64 не поддерживается");
                var name = Encoding.UTF8.GetString(b, (int)p + 46, nl);
                list.Add(new Cd { Name = name, Crc = crc, CSize = cs, USize = us });
                p += 46 + nl + el + cl;
            }
            return list;
        }

        private static int U16(byte[] b, long o) { return b[o] | (b[o + 1] << 8); }
        private static long U32(byte[] b, long o) { return (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24)); }

        public void Dispose() { if (_zip != null) _zip.Dispose(); }
    }
}
