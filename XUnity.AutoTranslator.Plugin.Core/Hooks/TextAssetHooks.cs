using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using XUnity.AutoTranslator.Plugin.Core.AssetRedirection;
using XUnity.Common.Constants;
using XUnity.Common.Harmony;
using XUnity.Common.MonoMod;
using XUnity.Common.Utilities;

namespace XUnity.AutoTranslator.Plugin.Core.Hooks
{
   internal static class TextAssetHooks
   {
      public static readonly Type[] All = new[]
      {
         typeof( TextAsset_bytes_Hook ),
         typeof( TextAsset_text_Hook ),
      };
   }

   internal static class TextAsset_bytes_Hook
   {
      static bool Prepare( object instance )
      {
         return UnityTypes.TextAsset != null;
      }

      static MethodBase TargetMethod( object instance )
      {
         return AccessToolsShim.Property( UnityTypes.TextAsset?.ClrType, "bytes" )?.GetGetMethod();
      }

      static void Postfix( TextAsset __instance, ref Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte> __result )
      {
         if( __result == null ) return;

         var ext = __instance.GetExtensionData<TextAssetExtensionData>();
         if( ext != null )
         {
            __result = ext.Data;
         }
      }

   }

   internal static class TextAsset_text_Hook
   {
      static bool Prepare( object instance )
      {
         return UnityTypes.TextAsset != null;
      }

      static MethodBase TargetMethod( object instance )
      {
         return AccessToolsShim.Property( UnityTypes.TextAsset?.ClrType, "text" )?.GetGetMethod();
      }

      static void Postfix( TextAsset __instance, ref string __result )
      {
         if( __result == null ) return;

         var ext = __instance.GetExtensionData<TextAssetExtensionData>();
         if( ext != null )
         {
            __result = ext.Text;
         }
      }

   }
}
