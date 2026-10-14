using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace ShadowTranslate.Updater
{
    internal class Opts
    {
        public int Pid; public long PidStart; public string GameDir, GameExe, StatusPath, LocalVersion; public List<string> GameArgs = new List<string>();
        public bool Force;
        public bool Plugin { get { return Pid > 0; } }

        public static Opts Parse(string[] a)
        {
            var o = new Opts();
            for (int i = 0; i < a.Length; i++)
            {
                switch (a[i])
                {
                    case "--pid": o.Pid = int.Parse(a[++i]); break;
                    case "--pid-start": o.PidStart = long.Parse(a[++i]); break;
                    case "--game-dir": o.GameDir = a[++i]; break;
                    case "--game-exe": o.GameExe = a[++i]; break;
                    case "--status": o.StatusPath = a[++i]; break;
                    case "--local-version": o.LocalVersion = a[++i]; break;
                    case "--force": o.Force = true; break;
                    case "--game-args":
                        var s = Encoding.UTF8.GetString(Convert.FromBase64String(a[++i]));
                        if (s.Length > 0) o.GameArgs.AddRange(s.Split(new[] { '\n' }));
                        break;
                }
            }
            return o;
        }
    }

    internal static class Program
    {
        private static void Boot(string msg)
        {
            try
            {
                var f = Path.Combine(Path.GetTempPath(), "ShadowTranslate.Updater.boot.txt");
                if (File.Exists(f) && new FileInfo(f).Length > 64 * 1024) File.Delete(f);
                File.AppendAllText(f, DateTime.Now.ToString("s") + " " + msg + "\r\n");
            }
            catch { }
        }

        [STAThread]
        private static int Main(string[] args)
        {
            Boot("Main start; argc=" + args.Length + "; clr=" + Environment.Version + "; x64os=" + Environment.Is64BitOperatingSystem + "; x64proc=" + Environment.Is64BitProcess);
            AppDomain.CurrentDomain.UnhandledException += delegate (object s, UnhandledExceptionEventArgs e) { Boot("UNHANDLED: " + e.ExceptionObject); };
            try { int rc = MainCore(args); Boot("exit " + rc); return rc; }
            catch (Exception e) { Boot("CRASH in Main: " + e); return 3; }
        }

        private static int MainCore(string[] args)
        {
            Opts o; Exception parseErr = null;
            try { o = Opts.Parse(args); } catch (Exception e) { o = null; parseErr = e; }
            if (o == null)
            {
                try
                {
                    string dir = Path.GetDirectoryName(Path.GetFullPath(Assembly.GetExecutingAssembly().Location));
                    Log.Init(Path.Combine(dir, "updater.log"));
                    Log.Error("Не удалось разобрать аргументы запуска: " + parseErr.GetType().Name + ": " + parseErr.Message +
                              " | argc=" + args.Length + " | argv=[" + string.Join("] [", args) + "]");
                }
                catch { }
                return 2;
            }
            string exeDir = Path.GetDirectoryName(Path.GetFullPath(Assembly.GetExecutingAssembly().Location));
            string root = o.GameDir ?? Path.GetDirectoryName(exeDir);
            string sdir = Path.Combine(root, "ShadowTranslate");
            try { Directory.CreateDirectory(sdir); } catch { }
            Log.Init(Path.Combine(sdir, "updater.log"));
            var st = new StatusFile(o.StatusPath);

            bool owned;
            string key; using (var md5 = MD5.Create()) key = U.Hex(md5.ComputeHash(Encoding.UTF8.GetBytes(root.ToLowerInvariant())));
            Mutex mutex = null;
            try { mutex = new Mutex(true, "Global\\ShadowTranslate.Updater." + key, out owned); }
            catch { owned = true; }
            if (!owned) { Log.Info("Уже запущен другой экземпляр обновлятора."); st.Set("continue", "другой экземпляр обновлятора уже работает"); return 0; }

            try
            {
                var runner = new Runner(o, root, sdir, st);
                return runner.Run();
            }
            catch (Exception e)
            {
                Log.Error("Непредвиденная ошибка: " + e);
                st.Set("continue", "внутренняя ошибка обновлятора");
                return 1;
            }
            finally { try { if (mutex != null) mutex.ReleaseMutex(); } catch { } }
        }
    }

    internal class Runner
    {
        private readonly Opts _o; private readonly string _root, _sdir; private readonly StatusFile _st;
        private Cfg _c; private StateStore _state;

        public Runner(Opts o, string root, string sdir, StatusFile st) { _o = o; _root = root; _sdir = sdir; _st = st; }

        private void ShowConsole() { if (_c.Console != "never") ConsoleHost.Show("ShadowTranslate - обновление"); }

        public int Run()
        {
            _st.Set("starting", "запуск обновлятора");
            _c = Cfg.Load(Path.Combine(_sdir, "updater.ini"));
            Log.Info("=== ShadowTranslate Updater " + U.Version + " | режим: " + (_o.Plugin ? "запуск из игры (pid " + _o.Pid + ")" : "ручной") + " | папка: " + _root);
            Net.Init();
            if (_c.Console == "always" || (!_o.Plugin && _c.Console != "never")) ShowConsole();
            _state = new StateStore(Path.Combine(_sdir, "state.ini"));
            var inst = new Installer(_root, _sdir, _c);

            if (_o.Plugin && inst.HasPendingRollback())
            {
                ShowConsole();
                Log.Warn("Найдено прерванное обновление. Закрываю игру, чтобы вернуть файлы к рабочей версии...");
                _st.Set("restart", "откат прерванного обновления");
                KillGame(); KillStrayProcesses();
            }
            if (inst.RecoverIfNeeded())
            {
                ShowConsole();
                Log.Warn("Предыдущее обновление было прервано, файлы возвращены к прежней версии.");
                if (_o.Plugin) return RestartGame("откат выполнен");
            }
            inst.CleanupLeftovers();

            if (!_c.Enabled && _o.Plugin) return Done("автообновление отключено в updater.ini");
            TimeSpan left;
            if (_o.Plugin && !_o.Force && _state.InBackoff(_c.FailBackoffMinutes, out left))
                return Done("после прошлой неудачи пауза ещё " + (int)Math.Ceiling(left.TotalMinutes) + " мин, обновление пропущено");

            _st.Set("checking", "проверка версии");
            var ri = Resolver.Latest(_c, _c.CheckTimeoutSeconds * 1000);
            if (ri == null) return Done("нет связи с GitHub - игра запускается с текущей версией");

            string local = LocalVersion();
            bool need = _o.Force || NeedUpdate(local, ri.Tag);
            Log.Info("Установлена: " + (local ?? "неизвестно") + ", на GitHub: " + ri.Tag + (need ? " -> нужно обновление" : " -> обновление не требуется"));
            if (!need)
            {
                var lt = U.ParseVer(local); var st = U.ParseVer(_state.Tag);
                if (local != null && (_state.Tag == null || (lt != null && st != null && U.CompareVer(lt, st) > 0))) { _state.Tag = local; _state.Save(); }
                return Done("версия актуальна (" + local + ")");
            }

            ShowConsole();
            ConsoleHost.WriteLine("============================================================");
            Log.Info("Найдена новая версия русификатора: " + ri.Tag + " (у вас " + (local ?? "?") + ")");
            if (_o.Plugin) Log.Info("Игра ожидает завершения загрузки и потом перезапустится сама. Нажмите S, чтобы пропустить обновление.");
            ConsoleHost.WriteLine("============================================================");
            _st.Set("downloading", "получение контрольной суммы");
            Resolver.FillHash(_c, ri, 20000);

            MemoryStream ms;
            try
            {
                ms = Downloader.Download(_c, ri, delegate (string m, int p) { _st.Set("downloading", m, p); });
            }
            catch (DownloadSkipped) { ConsoleHost.SkipRequested = false; return Done("обновление пропущено пользователем"); }
            catch (Exception e) { return Fail("не удалось скачать обновление: " + Net.Describe(e)); }

            ZipPack zip = null;
            try
            {
                _st.Set("verifying", "проверка архива");
                Log.Info("Проверка архива (" + U.Mb(ms.Length) + " МБ) в памяти...");
                zip = ZipPack.Open(ms);
                if (zip.StrippedRoot != null) Log.Info("Корневая папка архива '" + zip.StrippedRoot + "' отброшена.");
                if (zip.Protected > 0) Log.Info("Пропущено записей внутри ShadowTranslate/: " + zip.Protected + " (эта папка принадлежит обновляторю).");
                bool hasReq = false; foreach (var f in zip.Files) if (string.Equals(f.Rel, _c.RequiredEntry, StringComparison.OrdinalIgnoreCase)) hasReq = true;
                if (!hasReq) throw new InvalidDataException("в архиве нет " + _c.RequiredEntry + " - это не тот файл");
                int last = -1;
                zip.VerifyAll(delegate (int p) { if (p / 10 != last) { last = p / 10; Log.Debug("Проверка CRC " + p + "%"); } ConsoleHost.Progress("Проверка архива: " + p + "%"); });
                ConsoleHost.WriteLine("");
                Log.Info("Архив цел: " + zip.Files.Count + " файлов, CRC32 всех файлов совпали.");

                var allRel = new List<string>(); foreach (var f in zip.Files) allRel.Add(f.Rel);
                var plan = inst.BuildPlan(zip);
                Log.Info("План: заменить/добавить " + plan.Write.Count + " (" + U.Mb(plan.WriteBytes) + " МБ), удалить устаревших " + plan.Delete.Count + ", без изменений " + plan.Same + ", сохранено по Preserve " + plan.Preserved + ".");
                if (plan.IsEmpty)
                {
                    _state.Tag = ri.Tag; _state.Sha256 = ri.Sha256; _state.UpdatedUtc = DateTime.UtcNow.ToString("o"); _state.ClearFailure(); _state.Save();
                    try { inst.WriteManifest(allRel); } catch { }
                    return Done("файлы уже соответствуют версии " + ri.Tag + ", перезапуск не нужен");
                }

                inst.CheckDiskSpace(plan.WriteBytes);
                _st.Set("staging", "подготовка файлов", 0);
                Log.Info("Подготовка новых файлов (игра пока не затронута)...");
                inst.Stage(plan, delegate (int p) { ConsoleHost.Progress("Подготовка: " + p + "%"); _st.Set("staging", "подготовка файлов " + p + "%", p); });
                ConsoleHost.WriteLine("");
                zip.Dispose(); zip = null; ms = null;

                return Apply(inst, plan, ri, local, allRel);
            }
            catch (Exception e)
            {
                try { inst.DiscardStage(); } catch { }
                return Fail("ошибка подготовки обновления: " + (e is InvalidDataException || e is IOException ? e.Message : Net.Describe(e)));
            }
            finally { if (zip != null) zip.Dispose(); }
        }

        private int Apply(Installer inst, Plan plan, ReleaseInfo ri, string oldTag, List<string> allRel)
        {
            _st.Set("restart", "закрытие игры для замены файлов");
            Log.Info("Всё готово. Закрываю игру и заменяю файлы...");
            if (_o.Plugin) KillGame();
            KillStrayProcesses();

            int waitSec = _o.Plugin ? 40 : 300;
            if (!WaitUnlocked(inst, plan, waitSec))
            {
                inst.DiscardStage();
                Log.Error("Файлы так и остались заняты - обновление отменено, ничего не изменено.");
                _state.RecordFailure();
                return _o.Plugin ? RestartGame("файлы заняты, обновление отменено") : Done("файлы заняты, обновление отменено");
            }

            try { inst.Commit(plan, ri.Tag); }
            catch (Exception e)
            {
                Log.Error("Замена файлов не удалась, прежняя версия восстановлена: " + e.Message);
                inst.DiscardStage();
                _state.RecordFailure();
                return _o.Plugin ? RestartGame("замена не удалась (откат выполнен)") : Done("замена не удалась, откат выполнен");
            }

            try { inst.WriteManifest(allRel); } catch (Exception e) { Log.Warn("Манифест не записан: " + e.Message); }
            inst.FinishCommit(plan, oldTag);
            _state.Tag = ri.Tag; _state.Sha256 = ri.Sha256; _state.UpdatedUtc = DateTime.UtcNow.ToString("o"); _state.ClearFailure(); _state.Save();
            Log.Info("Обновление до версии " + ri.Tag + " установлено: изменено " + plan.Write.Count + ", удалено " + plan.Delete.Count + ".");
            if (!_o.Plugin) return Done("готово. Запустите игру.");
            return RestartGame("обновление установлено");
        }

        private bool WaitUnlocked(Installer inst, Plan plan, int seconds)
        {
            var sw = Stopwatch.StartNew(); bool warned = false;
            while (true)
            {
                string locked = null;
                var targets = new List<string>();
                foreach (var it in plan.Write) if (it.Replace) targets.Add(it.E.Rel);
                targets.AddRange(plan.Delete);
                foreach (var rel in targets)
                {
                    try { using (new FileStream(U.ToOs(_root, rel), FileMode.Open, FileAccess.Read, FileShare.None)) { } }
                    catch (FileNotFoundException) { }
                    catch (DirectoryNotFoundException) { }
                    catch (IOException) { locked = rel; break; }
                    catch (UnauthorizedAccessException) { locked = rel; break; }
                }
                if (locked == null) return true;
                if (sw.Elapsed.TotalSeconds > seconds) { Log.Error("Файл занят: " + locked); return false; }
                if (!warned) { warned = true; Log.Warn("Файл занят (" + locked + "). " + (_o.Plugin ? "Жду освобождения..." : "Закройте игру - жду освобождения...")); }
                Thread.Sleep(500);
            }
        }

        private void KillGame()
        {
            try
            {
                Process p;
                try { p = Process.GetProcessById(_o.Pid); } catch { return; }
                if (p.HasExited) return;
                if (_o.PidStart > 0 && Math.Abs(p.StartTime.ToUniversalTime().Ticks - _o.PidStart) > TimeSpan.TicksPerSecond * 3)
                { Log.Warn("PID " + _o.Pid + " принадлежит уже другому процессу, не трогаю."); return; }
                Log.Info("Завершаю игру (pid " + _o.Pid + ")...");
                try { p.Kill(); } catch (Exception e) { Log.Warn("Kill: " + e.Message); }
                if (!p.WaitForExit(15000)) Log.Warn("Игра не завершилась за 15 с.");
            }
            catch (Exception e) { Log.Warn("Не удалось завершить игру: " + e.Message); }
        }

        private void KillStrayProcesses()
        {
            if (!_o.Plugin || !ConsoleHost.IsWindows) return;
            try
            {
                int me = Process.GetCurrentProcess().Id;
                string rootPrefix = Path.GetFullPath(_root).TrimEnd('\\') + "\\";
                foreach (var p in Process.GetProcesses())
                {
                    try
                    {
                        if (p.Id == me) continue;
                        var f = p.MainModule.FileName;
                        if (f.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) && !f.StartsWith(Path.Combine(_root, "ShadowTranslate") + "\\", StringComparison.OrdinalIgnoreCase))
                        { Log.Info("Завершаю оставшийся процесс: " + Path.GetFileName(f)); p.Kill(); p.WaitForExit(5000); }
                    }
                    catch { }
                }
            }
            catch { }
        }

        private int RestartGame(string reason)
        {
            Log.Info("Перезапуск игры (" + reason + ")...");
            _st.Set("restart", reason);
            if (_o.Plugin && ConsoleHost.Visible) Thread.Sleep(2500);
            ConsoleHost.Release();
            if (_c.RelaunchGame && _o.GameExe != null && File.Exists(_o.GameExe))
            {
                try
                {
                    var sb = new StringBuilder();
                    foreach (var a in _o.GameArgs) { if (sb.Length > 0) sb.Append(' '); sb.Append(U.Quote(a)); }
                    var psi = new ProcessStartInfo(_o.GameExe, sb.ToString());
                    psi.WorkingDirectory = _root; psi.UseShellExecute = false;
                    psi.EnvironmentVariables["SHADOWTRANSLATE_JUST_UPDATED"] = "1";
                    Process.Start(psi);
                    Log.Info("Игра запущена.");
                }
                catch (Exception e) { Log.Error("Не удалось запустить игру: " + e.Message + ". Запустите её вручную."); }
            }
            else Log.Info("Автозапуск игры отключён/невозможен - запустите игру вручную.");
            _st.Set("continue", "обновлятор завершил работу");
            return 0;
        }

        private int Done(string msg)
        {
            Log.Info(msg);
            if (!_o.Plugin && ConsoleHost.Visible) Pause();
            ConsoleHost.Release();
            _st.Set("continue", msg);
            return 0;
        }

        private int Fail(string msg)
        {
            Log.Error(msg);
            _state.RecordFailure();
            Log.Info("Игра будет запущена с текущей версией. Следующая попытка обновления - позже (или запустите ShadowTranslate.Updater.exe вручную).");
            if (ConsoleHost.Visible) { if (_o.Plugin) Thread.Sleep(6000); else Pause(); }
            ConsoleHost.Release();
            _st.Set("continue", msg);
            return 1;
        }

        private static void Pause()
        {
            ConsoleHost.WriteLine("Нажмите любую клавишу для выхода...");
            ConsoleHost.WaitAnyKey();
        }

        private string LocalVersion()
        {
            string best = null; int[] bv = null;
            foreach (var cand in new[] { _o.LocalVersion, _state.Tag, DllVersion() })
            {
                var v = U.ParseVer(cand);
                if (v != null && (bv == null || U.CompareVer(v, bv) > 0)) { bv = v; best = cand; }
            }
            return best;
        }

        private string DllVersion()
        {
            try
            {
                var f = Path.Combine(_root, "BepInEx", "plugins", "XUnity.AutoTranslator", "XUnity.AutoTranslator.Plugin.Core.dll");
                if (File.Exists(f)) { var v = FileVersionInfo.GetVersionInfo(f).FileVersion; return v; }
            }
            catch { }
            return null;
        }

        private bool NeedUpdate(string local, string remote)
        {
            var r = U.ParseVer(remote); var l = U.ParseVer(local);
            if (r != null && l != null) return U.CompareVer(r, l) > 0;
            if (r == null) return remote != _state.Tag && remote != _o.LocalVersion;
            return true;
        }
    }
}
