namespace XUnity.AutoTranslator.Plugin.Core
{
   public class TranslationResult
   {
      internal TranslationResult( string translatedText, string errorMessage )
      {
         TranslatedText = translatedText;
         ErrorMessage = errorMessage;
      }

      public bool Succeeded => ErrorMessage == null;

      public string TranslatedText { get; }

      public string ErrorMessage { get; }
   }
}
