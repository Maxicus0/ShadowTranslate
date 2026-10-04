using System;
using System.Collections;
using XUnity.AutoTranslator.Plugin.Core.Configuration;
using XUnity.AutoTranslator.Plugin.Core.Fonts;
using XUnity.AutoTranslator.Plugin.Core.Utilities;
using XUnity.AutoTranslator.Plugin.Utilities;
using XUnity.Common.Logging;

namespace XUnity.AutoTranslator.Plugin.Core.Hooks
{
   internal static class OverrideFontAtlasResetHook
   {
      private const float ResetIntervalSeconds = 300f;

      private static bool _registered;

      public static void Apply()
      {
         if( _registered )
         {
            return;
         }

         _registered = true;

         try
         {
            TranslationScopeHelper.RegisterSceneLoadCallback( OnSceneLoaded );
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Error( e, "OverrideFontAtlasResetHook: Failed to register scene load callback." );
         }

         try
         {
            CoroutineHelper.Start( PeriodicResetLoop() );
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Error( e, "OverrideFontAtlasResetHook: Failed to start periodic reset loop." );
         }
      }

      private static IEnumerator PeriodicResetLoop()
      {
         while( true )
         {
            var wait = CoroutineHelper.CreateWaitForSecondsRealtime( ResetIntervalSeconds );
            if( wait != null )
            {
               yield return wait;
            }
            else
            {
               yield return CoroutineHelper.CreateWaitForSeconds( ResetIntervalSeconds );
            }

            ClearIfConfigured( "periodic interval" );
         }
      }

      private static void OnSceneLoaded( int sceneId )
      {
         ClearIfConfigured( "scene load" );
      }

      private static void ClearIfConfigured( string reason )
      {
         try
         {
            FontCache.ClearOverrideFonts();
            XuaLogger.AutoTranslator.Debug( "OverrideFontAtlasResetHook: Cleared override font cache (" + reason + ")." );
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Error( e, "OverrideFontAtlasResetHook: Failed to clear override font cache (" + reason + ")." );
         }
      }
   }
}
