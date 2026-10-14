using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace ShadowTranslate.Updater
{
    internal class StatusFile
    {
        private readonly string _path;
        private int _seq;
        public string State = "starting";

        public StatusFile(string path) { _path = path; }

        public void Set(string state, string msg, int pct = -1)
        {
            if (_path == null) return;
            State = state;
            _seq++;
            var text = "seq=" + _seq + "\nstate=" + state + "\npct=" + pct + "\nupdater_pid=" + System.Diagnostics.Process.GetCurrentProcess().Id +
                       "\nutc=" + DateTime.UtcNow.ToString("o") + "\nmsg=" + (msg ?? "").Replace('\n', ' ').Replace('\r', ' ') + "\n";
            try
            {
                var tmp = _path + ".tmp";
                File.WriteAllText(tmp, text, new UTF8Encoding(false));
                for (int i = 0; i < 10; i++)
                {
                    try { if (File.Exists(_path)) File.Delete(_path); File.Move(tmp, _path); return; }
                    catch (IOException) { Thread.Sleep(30); }
                    catch (UnauthorizedAccessException) { Thread.Sleep(30); }
                }
            }
            catch { }
        }
    }

    internal class StateStore
    {
        private readonly string _path;
        public string Tag, Sha256, UpdatedUtc, FailUtc;
        public int FailCount;

        public StateStore(string path)
        {
            _path = path;
            try
            {
                if (!File.Exists(path)) return;
                foreach (var line in File.ReadAllLines(path))
                {
                    int i = line.IndexOf('='); if (i <= 0) continue;
                    var k = line.Substring(0, i).Trim(); var v = line.Substring(i + 1).Trim();
                    switch (k)
                    {
                        case "tag": Tag = v; break;
                        case "sha256": Sha256 = v; break;
                        case "updated_utc": UpdatedUtc = v; break;
                        case "fail_utc": FailUtc = v; break;
                        case "fail_count": int.TryParse(v, out FailCount); break;
                    }
                }
            }
            catch (Exception e) { Log.Debug("state: не прочитан: " + e.Message); }
        }

        public void Save()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("tag=" + Tag); sb.AppendLine("sha256=" + Sha256); sb.AppendLine("updated_utc=" + UpdatedUtc);
                sb.AppendLine("fail_count=" + FailCount); sb.AppendLine("fail_utc=" + FailUtc);
                var tmp = _path + ".tmp";
                File.WriteAllText(tmp, sb.ToString());
                if (File.Exists(_path)) File.Delete(_path);
                File.Move(tmp, _path);
            }
            catch (Exception e) { Log.Warn("Не удалось сохранить state.ini: " + e.Message); }
        }

        public void RecordFailure() { FailCount++; FailUtc = DateTime.UtcNow.ToString("o"); Save(); }
        public void ClearFailure() { FailCount = 0; FailUtc = ""; }

        public bool InBackoff(int baseMinutes, out TimeSpan left)
        {
            left = TimeSpan.Zero;
            if (FailCount <= 0 || string.IsNullOrEmpty(FailUtc)) return false;
            DateTime t;
            if (!DateTime.TryParse(FailUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out t)) return false;
            double mins = Math.Min(baseMinutes * Math.Pow(2, Math.Min(FailCount - 1, 10)), 360);
            var until = t.ToUniversalTime().AddMinutes(mins);
            left = until - DateTime.UtcNow;
            return left > TimeSpan.Zero;
        }
    }
}
