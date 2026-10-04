using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using XUnity.Common.Constants;
using XUnity.Common.Utilities;

namespace XUnity.AutoTranslator.Plugin.Core.Utilities
{
   public static class CoroutineHelper
   {
      public static WaitForSeconds CreateWaitForSeconds( float seconds )
      {
         return new WaitForSeconds( seconds );
      }

      public static object CreateWaitForSecondsRealtime( float delay )
      {
         if( UnityFeatures.SupportsWaitForSecondsRealtime )
         {
            return GetWaitForSecondsRealtimeInternal( delay );
         }
         return null;
      }

      private static object GetWaitForSecondsRealtimeInternal( float delay )
      {
         return new WaitForSecondsRealtime( delay );
      }

      public static Coroutine Start( IEnumerator coroutine )
      {
         var wrapper = new Il2CppSystem.Collections.IEnumerator( new Il2CppManagedEnumerator( coroutine ).Pointer );
         return PluginLoader.MonoBehaviour.StartCoroutine( wrapper );

      }

      public static void Stop( Coroutine coroutine )
      {
         PluginLoader.MonoBehaviour.StopCoroutine( coroutine );
      }
   }
}
