using System;
using System.Collections;
using System.IO;
using XUnity.AutoTranslator.Plugin.Core.Configuration;
using XUnity.AutoTranslator.Plugin.Core.Extensions;
using XUnity.AutoTranslator.Plugin.Core.Web;

namespace XUnity.AutoTranslator.Plugin.Core.Endpoints
{
   public interface ITranslateEndpoint
   {
      string Id { get; }

      string FriendlyName { get; }

      int MaxConcurrency { get; }

      int MaxTranslationsPerRequest { get; }

      void Initialize( IInitializationContext context );

      IEnumerator Translate( ITranslationContext context );
   }
}
