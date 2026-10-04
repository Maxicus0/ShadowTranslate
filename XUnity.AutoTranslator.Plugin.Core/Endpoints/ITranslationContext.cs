namespace XUnity.AutoTranslator.Plugin.Core.Endpoints
{
   public interface ITranslationContext : ITranslationContextBase
   {
      void Complete( string translatedText );

      void Complete( string[] translatedTexts );
   }
}
