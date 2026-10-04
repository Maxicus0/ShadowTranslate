using System.IO;
using System.Text;
using UnityEngine;
using XUnity.AutoTranslator.Plugin.Core.Hooks;
using XUnity.Common.Utilities;
using XUnity.ResourceRedirector;

namespace XUnity.AutoTranslator.Plugin.Core.AssetRedirection
{
   public abstract class TextAssetLoadedHandlerBase : AssetLoadedHandlerBaseV2<TextAsset>
   {
      public TextAssetLoadedHandlerBase()
      {
         HooksSetup.InstallTextAssetHooks();
      }

      public abstract TextAndEncoding TranslateTextAsset( string calculatedModificationPath, TextAsset asset, IAssetOrResourceLoadedContext context );

      protected override sealed bool ReplaceOrUpdateAsset( string calculatedModificationPath, ref TextAsset asset, IAssetOrResourceLoadedContext context )
      {
         var info = TranslateTextAsset( calculatedModificationPath, asset, context );

         if( info != null )
         {
            var ext = asset.GetOrCreateExtensionData<TextAssetExtensionData>();
            ext.Encoding = info.Encoding;
            ext.Text = info.Text;
            ext.Data = info.Bytes;

            return true;
         }

         return false;
      }
   }
}
