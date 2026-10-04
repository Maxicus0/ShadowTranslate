using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using XUnity.AutoTranslator.Plugin.Core.Configuration;
using XUnity.Common.Logging;

namespace XUnity.AutoTranslator.Plugin.Core
{
   public static class PluginLoader
   {
      internal static AutoTranslationPlugin Plugin;
      internal static MonoBehaviour MonoBehaviour;

      private static bool _loaded;

      public static IMonoBehaviour LoadWithConfig( IPluginEnvironment config )
      {
         if( !_loaded )
         {
            _loaded = true;
            PluginEnvironment.Current = config;

            Plugin = new AutoTranslationPlugin();
            var obj = new GameObject( "___XUnityAutoTranslator" );
            obj.hideFlags = HideFlags.HideAndDontSave;
            MonoBehaviour = obj.AddComponent<AutoTranslatorProxyBehaviour>();
            GameObject.DontDestroyOnLoad( obj );
         }
         return Plugin;
      }

      public static IMonoBehaviour Load()
      {
         return LoadWithConfig( new DefaultPluginEnvironment( true ) );
      }

      public static IMonoBehaviour Load( bool allowDefaultInitializeHarmonyDetourBridge )
      {
         return LoadWithConfig( new DefaultPluginEnvironment( allowDefaultInitializeHarmonyDetourBridge ) );
      }

      internal class AutoTranslatorProxyBehaviour : MonoBehaviour
      {
         static AutoTranslatorProxyBehaviour()
         {
            Il2CppInterop.Runtime.Injection.ClassInjector.RegisterTypeInIl2Cpp<AutoTranslatorProxyBehaviour>();
         }

         public AutoTranslatorProxyBehaviour( IntPtr value ) : base( value )
         {

         }

         void Update()
         {
            Plugin.Update();
         }

         void OnGUI()
         {
            Plugin.OnGUI();
         }

         void Start()
         {
            Plugin.Start();
         }
      }
   }
}
