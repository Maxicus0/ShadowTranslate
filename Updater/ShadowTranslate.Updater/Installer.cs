using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace ShadowTranslate.Updater
{
    internal class PlanItem { public ZEntry E; public bool Replace; }

    internal class Plan
    {
        public List<PlanItem> Write = new List<PlanItem>();
        public List<string> Delete = new List<string>();
        public int Same, Preserved;
        public long WriteBytes;
        public bool IsEmpty { get { return Write.Count == 0 && Delete.Count == 0; } }
    }

    internal class Installer
    {
        private readonly string _root, _sdir, _stage, _backup, _journal, _manifest;
        private readonly Cfg _c;

        public Installer(string root, string sdir, Cfg c)
        {
            _root = root; _sdir = sdir; _c = c;
            _stage = Path.Combine(sdir, "stage"); _backup = Path.Combine(sdir, "backup");
            _journal = Path.Combine(sdir, "apply.journal"); _manifest = Path.Combine(sdir, "installed.manifest");
        }

        public Plan BuildPlan(ZipPack zip)
        {
            var plan = new Plan();
            var newSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in zip.Files)
            {
                newSet.Add(f.Rel);
                var target = U.ToOs(_root, f.Rel);
                bool exists = File.Exists(target);
                if (exists && IsPreserved(f.Rel)) { plan.Preserved++; continue; }
                if (exists)
                {
                    var crc = Crc32.OfFile(target, f.Size);
                    if (crc.HasValue && crc.Value == f.Crc) { plan.Same++; continue; }
                }
                plan.Write.Add(new PlanItem { E = f, Replace = exists });
                plan.WriteBytes += f.Size;
            }
            foreach (var old in ReadManifest())
            {
                if (newSet.Contains(old) || IsPreserved(old) || old.StartsWith("ShadowTranslate/", StringComparison.OrdinalIgnoreCase)) continue;
                if (File.Exists(U.ToOs(_root, old))) plan.Delete.Add(old);
            }
            return plan;
        }

        private bool IsPreserved(string rel)
        {
            foreach (var p in _c.Preserve) if (U.Glob(p, rel)) return true;
            return false;
        }

        private List<string> ReadManifest()
        {
            var l = new List<string>();
            try
            {
                if (File.Exists(_manifest))
                    foreach (var line in File.ReadAllLines(_manifest))
                    { var r = U.NormalizeRel(line.Trim()); if (r != null) l.Add(r); }
            }
            catch { }
            return l;
        }

        public void WriteManifest(IEnumerable<string> rels)
        {
            var sb = new StringBuilder();
            foreach (var r in rels) sb.AppendLine(r);
            File.WriteAllText(_manifest, sb.ToString(), new UTF8Encoding(false));
        }

        public void CheckDiskSpace(long need)
        {
            try
            {
                var di = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(_root)));
                long free = di.AvailableFreeSpace; long want = need + 150L * 1024 * 1024;
                if (free < want) throw new IOException("недостаточно места на диске: нужно ~" + U.Mb(want) + " МБ, свободно " + U.Mb(free) + " МБ");
            }
            catch (IOException) { throw; }
            catch { }
        }

        public void Stage(Plan plan, Action<int> progress)
        {
            U.DeleteDirQuiet(_stage);
            Directory.CreateDirectory(_stage);
            long done = 0; int last = -1; var buf = new byte[81920];
            foreach (var it in plan.Write)
            {
                var path = U.ToOs(_stage, it.E.Rel);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                uint s = Crc32.Init; long n = 0; int r;
                using (var src = it.E.Entry.Open())
                using (var dst = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    while ((r = src.Read(buf, 0, buf.Length)) > 0) { dst.Write(buf, 0, r); s = Crc32.Update(s, buf, 0, r); n += r; done += r; }
                }
                if (n != it.E.Size || Crc32.Finish(s) != it.E.Crc) throw new IOException("ошибка записи подготовленного файла: " + it.E.Rel);
                try { var t = it.E.Time; if (t.Year > 1980) File.SetLastWriteTime(path, t); } catch { }
                int pct = plan.WriteBytes > 0 ? (int)(done * 100 / plan.WriteBytes) : 100;
                if (pct != last) { last = pct; progress(pct); }
            }
        }

        public void DiscardStage() { U.DeleteDirQuiet(_stage); }

        public void Commit(Plan plan, string tag)
        {
            U.DeleteDirQuiet(_backup);
            Directory.CreateDirectory(_backup);
            using (var j = new FileStream(_journal, FileMode.Create, FileAccess.Write, FileShare.Read))
            using (var jw = new StreamWriter(j, new UTF8Encoding(false)))
            {
                Action<string> J = delegate (string line) { jw.WriteLine(line); jw.Flush(); j.Flush(true); };
                J("BEGIN " + tag);
                try
                {
                    foreach (var rel in plan.Delete)
                    {
                        J("DEL " + rel);
                        MoveOld(rel);
                    }
                    foreach (var it in plan.Write)
                    {
                        var rel = it.E.Rel; var target = U.ToOs(_root, rel);
                        J((it.Replace ? "REP " : "ADD ") + rel);
                        if (it.Replace) MoveOld(rel);
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        var staged = U.ToOs(_stage, rel);
                        U.Retry(delegate { File.Move(staged, target); }, 20, 300);
                    }
                    J("COMMIT");
                }
                catch (Exception)
                {
                    jw.Flush(); jw.Close();
                    Log.Warn("Ошибка при замене файлов - выполняю откат...");
                    RollbackFromJournal();
                    throw;
                }
            }
        }

        private void MoveOld(string rel)
        {
            var target = U.ToOs(_root, rel); var bk = U.ToOs(_backup, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(bk));
            U.Retry(delegate { File.Move(target, bk); }, 60, 500);
        }

        public void FinishCommit(Plan plan, string oldTag)
        {
            foreach (var rel in plan.Delete)
            {
                try
                {
                    var d = Path.GetDirectoryName(U.ToOs(_root, rel));
                    while (d != null && d.Length > _root.Length && Directory.Exists(d) && Directory.GetFileSystemEntries(d).Length == 0)
                    { Directory.Delete(d); d = Path.GetDirectoryName(d); }
                }
                catch { }
            }
            try
            {
                var tr = Path.Combine(_backup, "BepInEx", "Translation");
                if (Directory.Exists(tr))
                {
                    var keepRoot = Path.Combine(_sdir, "backup-translations");
                    var dst = Path.Combine(keepRoot, DateTime.Now.ToString("yyyyMMdd-HHmmss") + "_" + (oldTag ?? "old"));
                    CopyDir(tr, dst);
                    var dirs = Directory.GetDirectories(keepRoot); Array.Sort(dirs);
                    for (int i = 0; i < dirs.Length - 3; i++) U.DeleteDirQuiet(dirs[i]);
                    Log.Info("Старые файлы переводов сохранены в ShadowTranslate\\backup-translations (на случай ваших правок).");
                }
            }
            catch (Exception e) { Log.Debug("backup-translations: " + e.Message); }
            U.DeleteDirQuiet(_backup);
            U.DeleteDirQuiet(_stage);
            try { File.Delete(_journal); } catch { }
        }

        private static void CopyDir(string a, string b)
        {
            Directory.CreateDirectory(b);
            foreach (var f in Directory.GetFiles(a)) File.Copy(f, Path.Combine(b, Path.GetFileName(f)), true);
            foreach (var d in Directory.GetDirectories(a)) CopyDir(d, Path.Combine(b, Path.GetFileName(d)));
        }

        public bool HasPendingRollback()
        {
            if (!File.Exists(_journal)) return false;
            try { foreach (var l in File.ReadAllLines(_journal)) if (l.Trim() == "COMMIT") return false; return true; }
            catch { return false; }
        }

        public bool RecoverIfNeeded()
        {
            if (!File.Exists(_journal)) { U.DeleteDirQuiet(_stage); if (Directory.Exists(_backup)) U.DeleteDirQuiet(_backup); return false; }
            string[] lines; try { lines = File.ReadAllLines(_journal); } catch { return false; }
            bool committed = false; foreach (var l in lines) if (l.Trim() == "COMMIT") committed = true;
            if (committed) { Log.Info("Найдено завершённое обновление без финальной очистки - очищаю."); FinishCommit(new Plan(), null); return false; }
            Log.Warn("Найден журнал незавершённого обновления - откатываю изменения.");
            RollbackFromJournal();
            return true;
        }

        private void RollbackFromJournal()
        {
            string[] lines;
            try { lines = File.ReadAllLines(_journal); } catch { lines = new string[0]; }
            int fail = 0;
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                var line = lines[i]; int sp = line.IndexOf(' ');
                if (sp < 0) continue;
                var op = line.Substring(0, sp); var rel = U.NormalizeRel(line.Substring(sp + 1));
                if (rel == null || (op != "ADD" && op != "REP" && op != "DEL")) continue;
                var target = U.ToOs(_root, rel); var bk = U.ToOs(_backup, rel);
                try
                {
                    if (op == "ADD") { if (File.Exists(target)) U.Retry(delegate { File.Delete(target); }, 20, 300); }
                    else if (File.Exists(bk))
                    {
                        if (File.Exists(target)) U.Retry(delegate { File.Delete(target); }, 20, 300);
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        U.Retry(delegate { File.Move(bk, target); }, 20, 300);
                    }
                }
                catch (Exception e) { fail++; Log.Error("Откат: не удалось восстановить " + rel + ": " + e.Message + " (копия лежит в ShadowTranslate\\backup)"); }
            }
            if (fail == 0)
            {
                U.DeleteDirQuiet(_backup); U.DeleteDirQuiet(_stage);
                try { File.Delete(_journal); } catch { }
                Log.Info("Откат выполнен, прежняя версия восстановлена.");
            }
            else Log.Error("Откат выполнен не полностью (" + fail + " файл.). Журнал и backup сохранены, при следующем запуске попытка повторится.");
        }

        public void CleanupLeftovers()
        {
            try { foreach (var f in Directory.GetFiles(_sdir, "*.old")) File.Delete(f); } catch { }
        }
    }
}
