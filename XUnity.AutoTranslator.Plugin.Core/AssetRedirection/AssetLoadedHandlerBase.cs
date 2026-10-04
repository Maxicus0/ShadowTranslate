using System;
using System.IO;
using XUnity.AutoTranslator.Plugin.Core.Configuration;
using XUnity.AutoTranslator.Plugin.Core.Extensions;
using XUnity.Common.Extensions;
using XUnity.Common.Logging;
using XUnity.Common.Utilities;
using XUnity.ResourceRedirector;

namespace XUnity.AutoTranslator.Plugin.Core.AssetRedirection
{
   public abstract class AssetLoadedHandlerBase<TAsset>
      where TAsset : UnityEngine.Object
   {
      public AssetLoadedHandlerBase()
      {
         ResourceRedirection.RegisterAssetLoadedHook( HookBehaviour.OneCallbackPerResourceLoaded, 0, HandleAsset );
         ResourceRedirection.RegisterResourceLoadedHook( HookBehaviour.OneCallbackPerResourceLoaded, 0, HandleResource );
      }

      protected bool CheckDirectory { get; set; }

      [Obsolete( "Use AutoTranslatorSettings.IsDumpingRedirectedResourcesEnabled to obtain whether dumping is enabled instead." )]
      protected bool IsDumpingEnabled => AutoTranslatorSettings.IsDumpingRedirectedResourcesEnabled;

      private void HandleAsset( AssetLoadedContext context ) => Handle( context );

      private void HandleResource( ResourceLoadedContext context ) => Handle( context );

      private void Handle( IAssetOrResourceLoadedContext context )
      {
         if( context.Asset.TryCastTo<TAsset>( out var castedAsset ) && ShouldHandleAsset( castedAsset, context ) )
         {
            var unqiuePath = context.GetUniqueFileSystemAssetPath( castedAsset );
            var modificationFilePath = CalculateModificationFilePath( castedAsset, context );
            if( ( CheckDirectory && Directory.Exists( modificationFilePath ) ) || ( !CheckDirectory && File.Exists( modificationFilePath ) ) )
            {
               try
               {
                  bool handled = ReplaceOrUpdateAsset( modificationFilePath, ref castedAsset, context );
                  if( handled )
                  {
                     if( !Settings.EnableSilentMode )
                        XuaLogger.AutoTranslator.Debug( $"Replaced or updated resource file: '{unqiuePath}'." );
                  }
                  else
                  {
                     if( !Settings.EnableSilentMode )
                        XuaLogger.AutoTranslator.Debug( $"Did not replace or update resource file: '{unqiuePath}'." );
                  }

                  context.Complete(
                     skipRemainingPostfixes: handled );
               }
               catch( Exception e )
               {
                  XuaLogger.AutoTranslator.Error( e, $"An error occurred while replacing or updating resource file: '{unqiuePath}'." );
               }
            }
            else if( AutoTranslatorSettings.IsDumpingRedirectedResourcesEnabled )
            {
               try
               {
                  bool handled = DumpAsset( modificationFilePath, castedAsset, context );
                  if( handled )
                  {
                     if( !Settings.EnableSilentMode )
                        XuaLogger.AutoTranslator.Debug( $"Dumped resource file: '{unqiuePath}'." );
                  }
                  else
                  {
                     if( !Settings.EnableSilentMode )
                        XuaLogger.AutoTranslator.Debug( $"Did not dump resource file: '{unqiuePath}'." );
                  }

                  context.Complete(
                     skipRemainingPostfixes: handled );
               }
               catch( Exception e )
               {
                  XuaLogger.AutoTranslator.Error( e, $"An error occurred while dumping resource file: '{unqiuePath}'." );
               }
            }

            if( !UnityObjectReferenceComparer.Default.Equals( castedAsset, context.Asset ) )
            {
               context.Asset = castedAsset;
            }
         }
      }

      protected abstract bool ReplaceOrUpdateAsset( string calculatedModificationPath, ref TAsset asset, IAssetOrResourceLoadedContext context );

      protected abstract bool DumpAsset( string calculatedModificationPath, TAsset asset, IAssetOrResourceLoadedContext context );

      protected abstract string CalculateModificationFilePath( TAsset asset, IAssetOrResourceLoadedContext context );

      protected abstract bool ShouldHandleAsset( TAsset asset, IAssetOrResourceLoadedContext context );
   }
}
