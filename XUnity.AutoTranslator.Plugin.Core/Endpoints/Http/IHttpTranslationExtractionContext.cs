using XUnity.AutoTranslator.Plugin.Core.Web;

namespace XUnity.AutoTranslator.Plugin.Core.Endpoints.Http
{
   public interface IHttpTranslationExtractionContext : IHttpResponseInspectionContext
   {
      void Complete( string translatedText );

      void Complete( string[] translatedTexts );
   }
}
