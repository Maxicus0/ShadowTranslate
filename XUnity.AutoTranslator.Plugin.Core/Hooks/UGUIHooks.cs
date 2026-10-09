using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using XUnity.AutoTranslator.Plugin.Core.Constants;
using XUnity.AutoTranslator.Plugin.Core.Extensions;
using XUnity.AutoTranslator.Plugin.Core.Text;
using XUnity.Common.Constants;
using XUnity.Common.Extensions;
using XUnity.Common.Harmony;
using XUnity.Common.Logging;
using XUnity.Common.MonoMod;
using XUnity.Common.Utilities;

namespace XUnity.AutoTranslator.Plugin.Core.Hooks.UGUI
{
   internal static class UGUIHooks
   {
      public static readonly Type[] All = new[] {
         typeof( Text_text_Hook ),
         typeof( Text_OnEnable_Hook ),
      };
   }

   [HookingHelperPriority( HookPriority.Last )]
   internal static class Text_text_Hook
   {
      static bool Prepare( object instance )
      {
         return UnityTypes.Text != null;
      }

      static MethodBase TargetMethod( object instance )
      {
         return AccessToolsShim.Property( UnityTypes.Text?.ClrType, "text" )?.GetSetMethod();
      }

      static void _Postfix( Component __instance )
      {
         if( SdwTextCustomHook.IsKnownSdwTextInstance( __instance ) )
         {
            return;
         }

         if( StoryTextPreTranslateHook.IsPreTranslatedInstance( __instance ) )
         {
            return;
         }

         if( StoryTextPreTranslateHook.IsSuppressingSelfWrite )
         {
            return;
         }

         AutoTranslationPlugin.Current.Hook_TextChanged( __instance, false );

         StoryTextPreTranslateHook.TryApplyWrapSafetyNet( __instance );
      }

      static void Postfix( Component __instance )
      {
         __instance = (Component)Il2CppUtilities.CreateProxyComponentWithDerivedType( __instance.Pointer, UnityTypes.Text.ClrType );

         _Postfix( __instance );
      }

      static IntPtr TargetMethodPointer()
      {
         return UnityTypes.Text_Methods.IL2CPP.set_text;
      }

      static void ML_Detour( IntPtr instance, IntPtr value )
      {
         var __instance = (Component)Il2CppUtilities.CreateProxyComponentWithDerivedType( instance, UnityTypes.Text.ClrType );

         Il2CppUtilities.InvokeMethod( UnityTypes.Text_Methods.IL2CPP.set_text, instance, value );

         _Postfix( __instance );
      }

   }

   [HookingHelperPriority( HookPriority.Last )]
   internal static class Text_OnEnable_Hook
   {
      static bool Prepare( object instance )
      {
         return UnityTypes.Text != null;
      }
      static MethodBase TargetMethod( object instance )
      {
         return AccessToolsShim.Method( UnityTypes.Text?.ClrType, "OnEnable" );
      }

      static void _Postfix( Component __instance )
      {
         if( SdwTextCustomHook.ShouldSkipOnEnableDispatch( __instance ) )
         {
            return;
         }

         if( StoryTextPreTranslateHook.IsPreTranslatedInstance( __instance ) )
         {
            return;
         }

         AutoTranslationPlugin.Current.Hook_TextChanged( __instance, true );
      }

      static void Postfix( Component __instance )
      {
         if( UnityTypes.Text.IsAssignableFrom( __instance.GetUnityType() ) )
         {
            __instance = (Component)Il2CppUtilities.CreateProxyComponentWithDerivedType( __instance.Pointer, UnityTypes.Text.ClrType );

            _Postfix( __instance );
         }
      }

      static IntPtr TargetMethodPointer()
      {
         return UnityTypes.Text_Methods.IL2CPP.OnEnable;
      }

      static void ML_Detour( IntPtr instance )
      {
         Il2CppUtilities.InvokeMethod( UnityTypes.Text_Methods.IL2CPP.OnEnable, instance );

         if( instance.IsInstancePointerAssignableFrom( UnityTypes.Text.ClassPointer ) )
         {
            var __instance = (Component)Il2CppUtilities.CreateProxyComponentWithDerivedType( instance, UnityTypes.Text.ClrType );
            _Postfix( __instance );
         }
      }

   }
}
