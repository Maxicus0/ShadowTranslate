using System.Linq;
using XUnity.AutoTranslator.Plugin.Core.Configuration;
using XUnity.Common.Extensions;

namespace XUnity.AutoTranslator.Plugin.Core
{
   public static class AutoTranslatorSettings
   {
      public static string UserAgent => Settings.UserAgent;

      public static string SourceLanguage => Settings.FromLanguage;

      public static string DestinationLanguage => Settings.Language;

      public static bool IsDumpingRedirectedResourcesEnabled => Settings.EnableDumping;

      public static string DefaultRedirectedResourcePath => Settings.RedirectedResourcesPath;
   }
}
