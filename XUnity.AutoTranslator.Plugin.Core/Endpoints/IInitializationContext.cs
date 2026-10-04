using System;

namespace XUnity.AutoTranslator.Plugin.Core.Endpoints
{
   public interface IInitializationContext
   {
      string TranslatorDirectory { get; }

      T GetOrCreateSetting<T>( string section, string key, T defaultValue );

      T GetOrCreateSetting<T>( string section, string key );

      void SetSetting<T>( string section, string key, T value );

      void DisableCertificateChecksFor( params string[] hosts );

      string SourceLanguage { get; }

      string DestinationLanguage { get; }

      void DisableSpamChecks();

      void SetTranslationDelay( float delayInSeconds );
   }
}
