using System;

namespace XUnity.AutoTranslator.Plugin.Core.Endpoints
{
   public interface ITranslationContextBase
   {
      string UntranslatedText { get; }

      string[] UntranslatedTexts { get; }

      UntranslatedTextInfo UntranslatedTextInfo { get; }

      UntranslatedTextInfo[] UntranslatedTextInfos { get; }

      string SourceLanguage { get; }

      string DestinationLanguage { get; }

      void Fail( string reason, Exception exception );

      void Fail( string reason );

      object UserState { get; set; }
   }
}
