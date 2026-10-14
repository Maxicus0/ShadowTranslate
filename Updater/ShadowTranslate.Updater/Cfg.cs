using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ShadowTranslate.Updater
{
    internal class Cfg
    {
        public bool Enabled = true;
        public string Console = "auto";
        public string Repo = "Maxicus0/ShadowTranslate";
        public string Asset = "ShadowTranslate.zip";
        public int CheckTimeoutSeconds = 12, DownloadTimeoutMinutes = 20, StallSeconds = 20, Retries = 3, FailBackoffMinutes = 30;
        public string Proxy = "";
        public bool UseMirrors = true;
        public bool RelaunchGame = true;
        public string[] Mirrors = { "https://ghfast.top/{url}", "https://gh-proxy.com/{url}", "https://ghproxy.net/{url}" };
        public string[] Preserve = new string[0];
        public string WebBase = "https://github.com", ApiBase = "https://api.github.com", RawBase = "https://raw.githubusercontent.com";
        public string[] JsdBases = { "https://cdn.jsdelivr.net", "https://fastly.jsdelivr.net", "https://gcore.jsdelivr.net" };
        public string RequiredEntry = "BepInEx/plugins/XUnity.AutoTranslator/XUnity.AutoTranslator.Plugin.Core.dll";

        public const string DefaultIni =
@"; ShadowTranslate Updater - настройки автообновления
; Строки, начинающиеся с ; - комментарии. Списки разделяются символом |

[Updater]
; Включить проверку обновлений при запуске игры
Enabled=true

; Консоль обновлятора (отдельная от консоли BepInEx):
;   auto   - показывать только когда реально идёт обновление (по умолчанию)
;   always - показывать всегда
;   never  - никогда (весь процесс всё равно пишется в ShadowTranslate\updater.log)
Console=auto

; Репозиторий и имя файла релиза
Repo=Maxicus0/ShadowTranslate
Asset=ShadowTranslate.zip

; Сколько секунд игра ждёт ответа GitHub о версии. Нет связи - игра просто запускается как есть
CheckTimeoutSeconds=12
; Общий лимит на загрузку архива (минут) и на ""молчание"" соединения (секунд) до смены источника
DownloadTimeoutMinutes=20
StallSeconds=20
; Сколько кругов по всем источникам пытаться скачать
Retries=3

; HTTP-прокси, если нужен (пример: http://127.0.0.1:8080). Пусто = системные настройки прокси
Proxy=

; Зеркала GitHub на случай блокировок. {url} заменяется на прямую ссылку github.com.
; Зеркало используется ТОЛЬКО если известен SHA-256 релиза (получен по защищённому каналу), иначе пропускается.
UseMirrors=true
Mirrors=https://ghfast.top/{url}|https://gh-proxy.com/{url}|https://ghproxy.net/{url}

; Эти файлы НЕ перезаписываются, если уже существуют (шаблоны со *, список через |). Остальное - заменяется.
; По умолчанию пусто: всё из релиза заменяется, в том числе настройки в BepInEx\config.
; Например: Preserve=BepInEx/config/*
Preserve=

; После обновления перезапустить игру автоматически
RelaunchGame=true

; После неудачной попытки обновления следующую не раньше чем через N минут (растёт вдвое, максимум 6 часов)
FailBackoffMinutes=30

[Advanced]
WebBase=https://github.com
ApiBase=https://api.github.com
RawBase=https://raw.githubusercontent.com
";

        public static Cfg Load(string path)
        {
            var c = new Cfg();
            try
            {
                if (!File.Exists(path)) File.WriteAllText(path, DefaultIni, new UTF8Encoding(true));
                foreach (var raw in File.ReadAllLines(path))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line[0] == ';' || line[0] == '#' || line[0] == '[') continue;
                    int i = line.IndexOf('='); if (i <= 0) continue;
                    var k = line.Substring(0, i).Trim().ToLowerInvariant(); var v = line.Substring(i + 1).Trim();
                    switch (k)
                    {
                        case "enabled": c.Enabled = B(v, true); break;
                        case "console": c.Console = v.ToLowerInvariant(); break;
                        case "repo": if (v.Contains("/")) c.Repo = v; break;
                        case "asset": if (v.Length > 0) c.Asset = v; break;
                        case "checktimeoutseconds": c.CheckTimeoutSeconds = I(v, c.CheckTimeoutSeconds, 3, 120); break;
                        case "downloadtimeoutminutes": c.DownloadTimeoutMinutes = I(v, c.DownloadTimeoutMinutes, 1, 240); break;
                        case "stallseconds": c.StallSeconds = I(v, c.StallSeconds, 5, 300); break;
                        case "retries": c.Retries = I(v, c.Retries, 1, 10); break;
                        case "failbackoffminutes": c.FailBackoffMinutes = I(v, c.FailBackoffMinutes, 0, 1440); break;
                        case "proxy": c.Proxy = v; break;
                        case "usemirrors": c.UseMirrors = B(v, true); break;
                        case "relaunchgame": c.RelaunchGame = B(v, true); break;
                        case "mirrors": c.Mirrors = L(v); break;
                        case "preserve": c.Preserve = L(v); break;
                        case "webbase": if (v.Length > 0) c.WebBase = v.TrimEnd('/'); break;
                        case "apibase": if (v.Length > 0) c.ApiBase = v.TrimEnd('/'); break;
                        case "rawbase": if (v.Length > 0) c.RawBase = v.TrimEnd('/'); break;
                    }
                }
            }
            catch (Exception e) { Log.Warn("Не удалось прочитать updater.ini, используются значения по умолчанию: " + e.Message); }
            return c;
        }

        private static bool B(string v, bool d) { bool r; return bool.TryParse(v, out r) ? r : d; }
        private static int I(string v, int d, int min, int max) { int r; if (!int.TryParse(v, out r)) return d; return Math.Max(min, Math.Min(max, r)); }
        private static string[] L(string v)
        {
            var l = new List<string>();
            foreach (var p in v.Split('|')) if (p.Trim().Length > 0) l.Add(p.Trim());
            return l.ToArray();
        }
    }
}
