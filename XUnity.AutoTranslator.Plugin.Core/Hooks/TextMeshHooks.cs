using System;
using System.Reflection;
using UnityEngine;
using XUnity.AutoTranslator.Plugin.Core.Constants;
using XUnity.AutoTranslator.Plugin.Core.Extensions;
using XUnity.AutoTranslator.Plugin.Core.Text;
using XUnity.Common.Constants;
using XUnity.Common.Harmony;
using XUnity.Common.Logging;
using XUnity.Common.MonoMod;
using XUnity.Common.Utilities;

namespace XUnity.AutoTranslator.Plugin.Core.Hooks.UGUI
{
   internal static class TextMeshHooks
   {
      public static readonly Type[] All = new[] {
         typeof( TextMesh_text_Hook ),
         typeof( GameObject_SetActive_Hook ),
      };
   }

   [HookingHelperPriority( HookPriority.Last )]
   internal static class TextMesh_text_Hook
   {
      static bool Prepare( object instance )
      {
         return UnityTypes.TextMesh != null;
      }

      static MethodBase TargetMethod( object instance )
      {
         return AccessToolsShim.Property( UnityTypes.TextMesh?.ClrType, "text" )?.GetSetMethod();
      }

      static void _Postfix( Component __instance )
      {
         AutoTranslationPlugin.Current.Hook_TextChanged( __instance, false );
      }

      static void Postfix( Component __instance )
      {
         __instance = (Component)Il2CppUtilities.CreateProxyComponentWithDerivedType( __instance.Pointer, UnityTypes.TextMesh.ClrType );

         _Postfix( __instance );
      }

      static IntPtr TargetMethodPointer()
      {
         return UnityTypes.TextMesh_Methods.IL2CPP.set_text;
      }

      static void ML_Detour( IntPtr instance, IntPtr value )
      {
         Il2CppUtilities.InvokeMethod( UnityTypes.TextMesh_Methods.IL2CPP.set_text, instance, value );

         var __instance = (Component)Il2CppUtilities.CreateProxyComponentWithDerivedType( instance, UnityTypes.TextMesh.ClrType );
         _Postfix( __instance );
      }

   }

   [HookingHelperPriority( HookPriority.Last )]
   internal static class GameObject_SetActive_Hook
   {
      static bool Prepare( object instance )
      {
         return UnityTypes.GameObject != null && UnityTypes.TextMesh != null;
      }

      static MethodBase TargetMethod( object instance )
      {
         return AccessToolsShim.Method( UnityTypes.GameObject.ClrType, "SetActive", typeof( bool ) );
      }

      static void _Postfix( GameObject __instance, bool value )
      {
         if( value )
         {
            var tms = __instance.GetComponentsInChildren( UnityTypes.TextMesh.UnityType );
            foreach( var tm in tms )
            {
               var comp = Il2CppUtilities.CreateProxyComponentWithDerivedType( tm.Pointer, UnityTypes.TextMesh.ClrType );
               AutoTranslationPlugin.Current.Hook_TextChanged( comp, true );
            }
         }
      }

      static void Postfix( GameObject __instance, bool value )
      {
         _Postfix( __instance, value );
      }

      static IntPtr TargetMethodPointer()
      {
         return UnityTypes.GameObject_Methods.IL2CPP.SetActive;
      }

      unsafe static void ML_Detour( IntPtr instance, bool active )
      {
         var __instance = new GameObject( instance );
         __instance.SetActive( active );

         _Postfix( __instance, active );
      }

   }
}
