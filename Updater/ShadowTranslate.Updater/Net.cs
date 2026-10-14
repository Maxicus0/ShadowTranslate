using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace ShadowTranslate.Updater
{
    internal class ReleaseInfo
    {
        public string Tag, Sha256, Source;
        public long Size;
    }

    internal static class Net
    {
        private const string UA = "ShadowTranslate-Updater/" + U.Version;

        public static void Init()
        {
            try { ServicePointManager.SecurityProtocol = (SecurityProtocolType)(3072 | 12288); }
            catch { try { ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; } catch { } }
            ServicePointManager.Expect100Continue = false;
            ServicePointManager.DefaultConnectionLimit = 8;
        }

        public static HttpWebRequest Req(Cfg c, string url, int timeoutMs, bool redirect, bool gzip)
        {
            var r = (HttpWebRequest)WebRequest.Create(url);
            r.UserAgent = UA; r.Timeout = timeoutMs; r.ReadWriteTimeout = timeoutMs;
            r.AllowAutoRedirect = redirect; r.KeepAlive = false; r.Accept = "*/*";
            if (gzip) r.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            if (!string.IsNullOrEmpty(c.Proxy)) { try { r.Proxy = new WebProxy(c.Proxy); } catch { } }
            return r;
        }

        public static T WithTimeout<T>(Func<T> f, int ms, Action abort)
        {
            T res = default(T); Exception err = null;
            var t = new Thread(delegate () { try { res = f(); } catch (Exception e) { err = e; } });
            t.IsBackground = true; t.Start();
            if (!t.Join(ms)) { try { if (abort != null) abort(); } catch { } throw new TimeoutException("нет ответа за " + ms / 1000 + " с"); }
            if (err != null) throw new Exception(err.Message, err);
            return res;
        }

        public static string GetText(Cfg c, string url, int ms, string accept)
        {
            HttpWebRequest r = Req(c, url, ms, true, true);
            if (accept != null) r.Accept = accept;
            return WithTimeout(delegate ()
            {
                using (var resp = (HttpWebResponse)r.GetResponse())
                using (var s = resp.GetResponseStream())
                using (var sr = new StreamReader(s, Encoding.UTF8))
                    return sr.ReadToEnd();
            }, ms + 2000, r.Abort);
        }

        public static string GetRedirect(Cfg c, string url, int ms)
        {
            HttpWebRequest r = Req(c, url, ms, false, false);
            return WithTimeout(delegate ()
            {
                try
                {
                    using (var resp = (HttpWebResponse)r.GetResponse())
                    {
                        var loc = resp.Headers["Location"];
                        return loc == null ? null : new Uri(resp.ResponseUri, loc).ToString();
                    }
                }
                catch (WebException we)
                {
                    var resp = we.Response as HttpWebResponse;
                    if (resp != null && (int)resp.StatusCode >= 300 && (int)resp.StatusCode < 400)
                    {
                        var loc = resp.Headers["Location"];
                        if (loc != null) return new Uri(resp.ResponseUri, loc).ToString();
                    }
                    throw;
                }
            }, ms + 2000, r.Abort);
        }

        public static string Describe(Exception e)
        {
            while (e.InnerException != null && !(e is WebException)) e = e.InnerException;
            var we = e as WebException;
            if (we != null)
            {
                switch (we.Status)
                {
                    case WebExceptionStatus.NameResolutionFailure: return "не удалось определить адрес сервера (DNS): нет интернета или DNS блокируется";
                    case WebExceptionStatus.ProxyNameResolutionFailure: return "не найден прокси-сервер";
                    case WebExceptionStatus.ConnectFailure: return "не удалось подключиться (адрес/порт недоступен, возможна блокировка)";
                    case WebExceptionStatus.Timeout: return "превышено время ожидания";
                    case WebExceptionStatus.TrustFailure:
                    case WebExceptionStatus.SecureChannelFailure: return "ошибка TLS/сертификата (подмена провайдером или неверные дата/время на ПК)";
                    case WebExceptionStatus.ConnectionClosed: case WebExceptionStatus.ReceiveFailure:
                    case WebExceptionStatus.SendFailure: case WebExceptionStatus.KeepAliveFailure:
                    case WebExceptionStatus.PipelineFailure: return "соединение сброшено (типичный признак DPI/блокировки или нестабильной сети)";
                    case WebExceptionStatus.ProtocolError:
                        var resp = we.Response as HttpWebResponse;
                        if (resp != null)
                        {
                            int code = (int)resp.StatusCode;
                            if (code == 403 || code == 429)
                            {
                                var rem = resp.Headers["X-RateLimit-Remaining"];
                                return rem == "0" ? "превышен лимит запросов GitHub API (HTTP " + code + ")" : "доступ запрещён (HTTP " + code + ")";
                            }
                            if (code == 451) return "ресурс заблокирован (HTTP 451)";
                            if (code == 404) return "не найдено (HTTP 404)";
                            if (code >= 500) return "сервер временно недоступен (HTTP " + code + ")";
                            return "HTTP " + code;
                        }
                        break;
                }
                return we.Status + ": " + we.Message;
            }
            if (e is DownloadLimitExceeded) return e.Message;
            if (e is TimeoutException) return "превышено время ожидания (включая DNS)";
            return e.GetType().Name + ": " + e.Message;
        }
    }

    internal static class Resolver
    {
        private static int PerReq(Stopwatch sw, int totalMs, int min = 2500)
        {
            int left = totalMs - (int)sw.ElapsedMilliseconds;
            return Math.Max(min, left / 2);
        }

        public static ReleaseInfo Latest(Cfg c, int totalMs)
        {
            var sw = Stopwatch.StartNew();
            var errs = new List<string>();
            ReleaseInfo ri = null;

            if (sw.ElapsedMilliseconds < totalMs)
                try
                {
                    var loc = Net.GetRedirect(c, c.WebBase + "/" + c.Repo + "/releases/latest", PerReq(sw, totalMs));
                    var m = loc == null ? null : Regex.Match(loc, "/releases/tag/([^/?#]+)");
                    if (m != null && m.Success) ri = new ReleaseInfo { Tag = Uri.UnescapeDataString(m.Groups[1].Value), Source = "github.com/releases/latest" };
                    else errs.Add("github.com: релизов не найдено");
                }
                catch (Exception e) { errs.Add("github.com: " + Net.Describe(e)); }

            if (ri == null && sw.ElapsedMilliseconds < totalMs)
                try { ri = FromApi(c, PerReq(sw, totalMs)); }
                catch (Exception e) { errs.Add("api.github.com: " + Net.Describe(e)); }

            if (ri == null)
            {
                var urls = new List<string> { c.RawBase + "/" + c.Repo + "/main/update.json" };
                foreach (var b in c.JsdBases) urls.Add(b + "/gh/" + c.Repo + "@main/update.json");
                foreach (var u in urls)
                {
                    if (sw.ElapsedMilliseconds >= totalMs) break;
                    try { ri = FromManifest(c, u, PerReq(sw, totalMs, 2000)); if (ri != null) break; }
                    catch (Exception e) { errs.Add(new Uri(u).Host + ": " + Net.Describe(e)); }
                }
            }

            if (ri == null)
                Log.Warn("Версию релиза узнать не удалось. Причины: " + string.Join("; ", errs.ToArray()));
            else
                Log.Info("Последний релиз: " + ri.Tag + " (источник: " + ri.Source + ")");
            return ri;
        }

        private static ReleaseInfo FromApi(Cfg c, int ms)
        {
            var json = Net.GetText(c, c.ApiBase + "/repos/" + c.Repo + "/releases/latest", ms, "application/vnd.github+json");
            var d = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(json) as Dictionary<string, object>;
            if (d == null || !d.ContainsKey("tag_name")) return null;
            var ri = new ReleaseInfo { Tag = Convert.ToString(d["tag_name"]), Source = "api.github.com" };
            var assets = d.ContainsKey("assets") ? d["assets"] as System.Collections.IEnumerable : null;
            if (assets != null)
                foreach (var a in assets)
                {
                    var ad = a as Dictionary<string, object>;
                    if (ad == null || !string.Equals(Convert.ToString(ad["name"]), c.Asset, StringComparison.OrdinalIgnoreCase)) continue;
                    if (ad.ContainsKey("size")) long.TryParse(Convert.ToString(ad["size"]), out ri.Size);
                    var dg = ad.ContainsKey("digest") ? Convert.ToString(ad["digest"]) : null;
                    if (dg != null && dg.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) ri.Sha256 = dg.Substring(7).ToLowerInvariant();
                }
            return ri;
        }

        private static ReleaseInfo FromManifest(Cfg c, string url, int ms)
        {
            var json = Net.GetText(c, url, ms, "application/json");
            var d = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
            if (d == null || !d.ContainsKey("version")) return null;
            var ri = new ReleaseInfo { Tag = Convert.ToString(d["version"]), Source = new Uri(url).Host + "/update.json" };
            if (d.ContainsKey("sha256")) { var s = Convert.ToString(d["sha256"]).ToLowerInvariant(); if (Regex.IsMatch(s, "^[0-9a-f]{64}$")) ri.Sha256 = s; }
            if (d.ContainsKey("size")) long.TryParse(Convert.ToString(d["size"]), out ri.Size);
            return ri;
        }

        public static void FillHash(Cfg c, ReleaseInfo ri, int totalMs)
        {
            if (ri.Sha256 != null) return;
            var sw = Stopwatch.StartNew();
            var errs = new List<string>();
            try
            {
                var html = Net.GetText(c, c.WebBase + "/" + c.Repo + "/releases/expanded_assets/" + Uri.EscapeDataString(ri.Tag), PerReq(sw, totalMs, 4000), "text/html");
                int i = html.IndexOf("/releases/download/" + ri.Tag + "/" + c.Asset, StringComparison.OrdinalIgnoreCase);
                if (i >= 0)
                {
                    var m = Regex.Match(html.Substring(i, Math.Min(html.Length - i, 4000)), "sha256:([0-9a-fA-F]{64})");
                    if (m.Success) { ri.Sha256 = m.Groups[1].Value.ToLowerInvariant(); Log.Info("SHA-256 релиза получен со страницы релиза GitHub."); return; }
                }
                errs.Add("на странице релиза нет sha256");
            }
            catch (Exception e) { errs.Add("страница релиза: " + Net.Describe(e)); }
            try
            {
                var r = FromApi(c, PerReq(sw, totalMs, 4000));
                if (r != null && r.Tag == ri.Tag && r.Sha256 != null) { ri.Sha256 = r.Sha256; if (ri.Size == 0) ri.Size = r.Size; Log.Info("SHA-256 релиза получен через GitHub API."); return; }
                errs.Add("API: нет digest");
            }
            catch (Exception e) { errs.Add("API: " + Net.Describe(e)); }
            var urls = new List<string> { c.RawBase + "/" + c.Repo + "/main/update.json" };
            foreach (var b in c.JsdBases) urls.Add(b + "/gh/" + c.Repo + "@main/update.json");
            foreach (var u in urls)
                try
                {
                    var r = FromManifest(c, u, PerReq(sw, totalMs, 3000));
                    if (r != null && r.Tag == ri.Tag && r.Sha256 != null) { ri.Sha256 = r.Sha256; if (ri.Size == 0) ri.Size = r.Size; Log.Info("SHA-256 релиза получен из update.json."); return; }
                }
                catch (Exception e) { errs.Add(new Uri(u).Host + ": " + Net.Describe(e)); }
            Log.Warn("SHA-256 релиза получить не удалось (" + string.Join("; ", errs.ToArray()) + "). Зеркала использоваться не будут, только прямая загрузка с GitHub по TLS.");
        }
    }

    internal class DownloadSkipped : Exception { public DownloadSkipped() : base("пропущено пользователем") { } }

    internal class DownloadLimitExceeded : TimeoutException { public DownloadLimitExceeded(int minutes) : base("исчерпан общий лимит времени загрузки (" + minutes + " мин)") { } }

    internal static class Downloader
    {
        public static MemoryStream Download(Cfg c, ReleaseInfo ri, Action<string, int> progress)
        {
            string direct = c.WebBase + "/" + c.Repo + "/releases/download/" + Uri.EscapeDataString(ri.Tag) + "/" + Uri.EscapeDataString(c.Asset);
            var cands = new List<string>();
            cands.Add(direct);
            if (c.UseMirrors && ri.Sha256 != null)
                foreach (var m in c.Mirrors) cands.Add(m.Replace("{url}", direct));
            else if (c.UseMirrors) Log.Info("Зеркала пропущены: без проверенного SHA-256 им доверять нельзя.");

            var sw = Stopwatch.StartNew();
            long limitMs = c.DownloadTimeoutMinutes * 60000L;
            var ms = new MemoryStream();
            var bad = new HashSet<string>();
            long total = ri.Size;

            for (int round = 1; round <= c.Retries; round++)
            {
                foreach (var url in cands.ToArray())
                {
                    if (bad.Contains(url)) continue;
                    if (sw.ElapsedMilliseconds > limitMs) throw new DownloadLimitExceeded(c.DownloadTimeoutMinutes);
                    string host = new Uri(url).Host;
                    try
                    {
                        Log.Info("Загрузка [" + host + "], круг " + round + "/" + c.Retries + (ms.Length > 0 ? ", докачка с " + U.Mb(ms.Length) + " МБ" : "") + "...");
                        Pull(c, url, ms, ref total, ri, sw, limitMs, round == 1, progress, host);
                        if (ms.Length == total || (total <= 0 && ms.Length > 0))
                        {
                            if (ri.Sha256 != null)
                            {
                                var h = U.Sha256(ms.GetBuffer(), (int)ms.Length);
                                if (h != ri.Sha256)
                                {
                                    Log.Warn("SHA-256 не совпал (получено " + h.Substring(0, 12) + "..., ожидалось " + ri.Sha256.Substring(0, 12) + "...). Данные отброшены, источник [" + host + "] исключён.");
                                    ms.SetLength(0); total = ri.Size;
                                    if (url != direct) bad.Add(url);
                                    continue;
                                }
                                Log.Info("SHA-256 совпал.");
                            }
                            return ms;
                        }
                    }
                    catch (DownloadSkipped) { throw; }
                    catch (DownloadLimitExceeded) { throw; }
                    catch (Exception e)
                    {
                        var we = e as WebException;
                        var er = we != null ? we.Response as HttpWebResponse : null;
                        if (er != null && er.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable) { ms.SetLength(0); total = ri.Size; }
                        Log.Warn("[" + host + "] " + Net.Describe(e));
                    }
                }
                if (round < c.Retries)
                {
                    int wait = 2 * round;
                    Log.Info("Пауза " + wait + " с перед следующим кругом...");
                    for (int i = 0; i < wait * 10; i++) { if (ConsoleHost.SkipRequested) throw new DownloadSkipped(); Thread.Sleep(100); }
                }
            }
            throw new IOException("не удалось скачать архив ни из одного источника");
        }

        private static void Pull(Cfg c, string url, MemoryStream ms, ref long total, ReleaseInfo ri, Stopwatch sw, long limitMs, bool firstRound, Action<string, int> progress, string host)
        {
            int connectMs = 15000, stallMs = c.StallSeconds * 1000;
            long pos = ms.Length;
            var req = Net.Req(c, url, connectMs, true, false);
            req.ReadWriteTimeout = stallMs;
            if (pos > 0) req.AddRange(pos);
            var resp = Net.WithTimeout(delegate () { return (HttpWebResponse)req.GetResponse(); }, connectMs + 3000, req.Abort);
            using (resp)
            {
                long newTotal;
                if (resp.StatusCode == HttpStatusCode.PartialContent)
                {
                    var m = Regex.Match(resp.Headers["Content-Range"] ?? "", @"bytes (\d+)-\d+/(\d+)");
                    if (!m.Success || long.Parse(m.Groups[1].Value) != pos) { ms.SetLength(0); throw new IOException("сервер вернул неожиданный диапазон"); }
                    newTotal = long.Parse(m.Groups[2].Value);
                }
                else
                {
                    if (pos > 0) { Log.Info("Сервер не поддерживает докачку, качаю заново."); ms.SetLength(0); pos = 0; }
                    newTotal = resp.ContentLength;
                }
                if (ri.Size > 0 && newTotal > 0 && newTotal != ri.Size) throw new IOException("размер файла (" + newTotal + ") не совпадает с ожидаемым (" + ri.Size + ")");
                if (total > 0 && newTotal > 0 && newTotal != total) { ms.SetLength(0); pos = 0; throw new IOException("размер файла изменился между попытками"); }
                total = newTotal;
                if (total > 0 && ms.Capacity < total) ms.Capacity = (int)Math.Min(int.MaxValue, total + 1024);
                ms.Position = ms.Length;

                using (var s = resp.GetResponseStream())
                {
                    var buf = new byte[65536];
                    var tick = Stopwatch.StartNew(); long lastBytes = ms.Length; var winSw = Stopwatch.StartNew(); long winBytes = ms.Length;
                    long lastLogPct = -1; long startBytes = ms.Length; var speedSw = Stopwatch.StartNew();
                    while (true)
                    {
                        if (ConsoleHost.SkipRequested) throw new DownloadSkipped();
                        if (sw.ElapsedMilliseconds > limitMs) throw new DownloadLimitExceeded(c.DownloadTimeoutMinutes);
                        int n = s.Read(buf, 0, buf.Length);
                        if (n <= 0) break;
                        ms.Write(buf, 0, n);
                        if (firstRound && winSw.ElapsedMilliseconds >= 15000)
                        {
                            if (ms.Length - winBytes < 15L * 30 * 1024 && (total <= 0 || total - ms.Length > 2 * 1024 * 1024))
                                throw new IOException("слишком низкая скорость (<30 КБ/с)");
                            winSw.Restart(); winBytes = ms.Length;
                        }
                        if (tick.ElapsedMilliseconds >= 500)
                        {
                            double speed = (ms.Length - startBytes) / 1048576.0 / Math.Max(0.1, speedSw.Elapsed.TotalSeconds);
                            int pct = total > 0 ? (int)(ms.Length * 100 / total) : -1;
                            string line = "Загрузка: " + U.Mb(ms.Length) + (total > 0 ? " / " + U.Mb(total) : "") + " МБ" + (pct >= 0 ? " (" + pct + "%)" : "") + "  " + speed.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " МБ/с  [" + host + "]   (S - пропустить)";
                            ConsoleHost.Progress(line);
                            progress("Загрузка " + pct + "%", pct);
                            if (pct >= 0 && pct / 10 != lastLogPct) { lastLogPct = pct / 10; Log.Debug("Загрузка " + pct + "% (" + U.Mb(ms.Length) + " МБ)"); }
                            tick.Restart();
                        }
                    }
                }
                ConsoleHost.Progress("Загрузка: " + U.Mb(ms.Length) + " МБ  - завершено");
                ConsoleHost.WriteLine("");
                if (total > 0 && ms.Length < total) throw new IOException("соединение оборвалось на " + U.Mb(ms.Length) + " из " + U.Mb(total) + " МБ");
            }
        }
    }
}
