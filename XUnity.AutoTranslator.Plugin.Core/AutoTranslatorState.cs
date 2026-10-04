using System;
using XUnity.AutoTranslator.Plugin.Core.Configuration;
using XUnity.Common.Logging;

namespace XUnity.AutoTranslator.Plugin.Core
{
   public static class AutoTranslatorState
   {
      public static int TranslationCount => Settings.TranslationCount;

      public static bool PluginInitialized { get; private set; }

      public static event Action PluginInitializationCompleted;

      internal static void OnPluginInitializationCompleted()
      {
         if( PluginInitialized ) return;

         PluginInitialized = true;

         if( PluginInitializationCompleted != null )
         {
            try
            {
               PluginInitializationCompleted();
            }
            catch( Exception e )
            {
               XuaLogger.AutoTranslator.Error( "Subscriber crash in PluginInitializationCompleted event: " + e );
            }
         }
      }
   }
}
