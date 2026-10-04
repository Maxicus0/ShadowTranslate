using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using XUnity.AutoTranslator.Plugin.Core.Configuration;
using XUnity.AutoTranslator.Plugin.Core.Constants;
using XUnity.AutoTranslator.Plugin.Core.Extensions;
using XUnity.AutoTranslator.Plugin.Core.Text;
using XUnity.Common.Constants;
using XUnity.Common.Harmony;
using XUnity.Common.MonoMod;
using XUnity.Common.Utilities;

namespace XUnity.AutoTranslator.Plugin.Core.Hooks.TextMeshPro
{
   internal static class TextMeshProHooks
   {
      public static readonly Type[] All = new[] {
         typeof( TeshMeshProUGUI_OnEnable_Hook ),
         typeof( TeshMeshPro_OnEnable_Hook ),
         typeof( TMP_Text_text_Hook ),

         typeof( TMP_Text_SetText_Hook1 ),
         typeof( TMP_Text_SetText_Hook2 ),
         typeof( TMP_Text_SetText_Hook3 ),

         typeof( TMP_Text_SetCharArray_Hook1 ),
         typeof( TMP_Text_SetCharArray_Hook2 ),
         typeof( TMP_Text_SetCharArray_Hook3 ),

      };

      public static readonly Type[] DisableScrollInTmp = new[] {
         typeof( TMP_Text_maxVisibleCharacters_Hook ),
      };
   }

   [HookingHelperPriority( HookPriority.Last )]
   internal static class TMP_Text_SetText_Hook1
   {
      static bool Prepare( object instance )
      {
         return UnityTypes.TMP_Text != null;
      }

      static MethodBase TargetMethod( object instance )
      {
         return AccessToolsShim.Method( UnityTypes.TMP_Text?.ClrType, "SetText", new[] { typeof( Il2CppSystem.Text.StringBuilder ) } );
      }

      static void Postfix( Component __instance )
      {
         __instance = (Component)Il2CppUtilities.CreateProxyComponentWithDerivedType( __instance.Pointer, UnityTypes.TMP_Text.ClrType );

         AutoTranslationPlugin.Current.Hook_TextChanged( __instance, false );
      }

   }

   [HookingHelperPriority( HookPriority.Last )]
   internal static class TMP_Text_SetText_Hook2
   {
      static bool Prepare( object instance )
      {
         return UnityTypes.TMP_Text != null;
      }

      static MethodBase TargetMethod( object instance )
      {
         return AccessToolsShim.Method( UnityTypes.TMP_Text?.ClrType, "SetText", new[] { typeof( string ), typeof( bool ) } );
      }

      static void Postfix( Component __instance )
      {
         AutoTranslationPlugin.Current.Hook_TextChanged( __instance, false );
      }

   }

   [HookingHelperPriority( HookPriority.Last )]
   internal static class TMP_Text_SetText_Hook3
   {
      static bool Prepare( object instance )
      {
         return UnityTypes.TMP_Text != null;
      }

      static MethodBase TargetMethod( object instance )
      {
         return AccessToolsShim.Method( UnityTypes.TMP_Text.ClrType, "SetText", new[] { typeof( string ), typeof( float ), typeof( float ), typeof( float ) } );
      }

      static void Postfix( Component __instance )
      {
         AutoTranslationPlugin.Current.Hook_TextChanged( __instance, false );
      }

   }

   [HookingHelperPriority( HookPriority.Last )]
   internal static class TMP_Text_SetCharArray_Hook1
   {
      static bool Prepare( object instance )
      {
         return UnityTypes.TMP_Text != null;
      }

      static MethodBase TargetMethod( object instance )
      {
         return AccessToolsShim.Method( UnityTypes.TMP_Text?.ClrType, "SetCharArray", new[] { typeof( Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<char> ) } );
      }

      static void Postfix( Component __instance )
      {
         AutoTranslationPlugin.Current.Hook_TextChanged( __instance, false );
      }

   }

   [HookingHelperPriority( HookPriority.Last )]
   internal static class TMP_Text_SetCharArray_Hook2
   {
      static bool Prepare( object instance )
      {
         return UnityTypes.TMP_Text != null;
      }

      static MethodBase TargetMethod( object instance )
      {
         return AccessToolsShim.Method( UnityTypes.TMP_Text?.ClrType, "SetCharArray", new[] { typeof( Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<char> ), typeof( int ), typeof( int ) } );
      }

      static void Postfix( Component __instance )
      {
         AutoTranslationPlugin.Current.Hook_TextChanged( __instance, false );
      }

   }

   [HookingHelperPriority( HookPriority.Last )]
   internal static class TMP_Text_SetCharArray_Hook3
   {
      static bool Prepare( object instance )
      {
         return UnityTypes.TMP_Text != null;
      }

      static MethodBase TargetMethod( object instance )
      {
         return AccessToolsShim.Method( UnityTypes.TMP_Text?.ClrType, "SetCharArray", new[] { typeof( Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int> ), typeof( int ), typeof( int ) } );
      }

      static void Postfix( Component __instance )
      {
         AutoTranslationPlugin.Current.Hook_TextChanged( __instance, false );
      }

   }

   [HookingHelperPriority( HookPriority.Last )]
   internal static class TeshMeshProUGUI_OnEnable_Hook
   {
      static bool Prepare( object instance )
      {
         return UnityTypes.TextMeshProUGUI != null;
      }

      static MethodBase TargetMethod( object instance )
      {
         return AccessToolsShim.Method( UnityTypes.TextMeshProUGUI?.ClrType, "OnEnable" );
      }

      static void _Postfix( Component __instance )
      {
         AutoTranslationPlugin.Current.Hook_TextChanged( __instance, true );
      }

      static void Postfix( Component __instance )
      {
         __instance = (Component)Il2CppUtilities.CreateProxyComponentWithDerivedType( __instance.Pointer, UnityTypes.TextMeshProUGUI.ClrType );

         _Postfix( __instance );
      }

      static IntPtr TargetMethodPointer()
      {
         return UnityTypes.TextMeshProUGUI_Methods.IL2CPP.OnEnable;
      }

      static void ML_Detour( IntPtr instance )
      {
         Il2CppUtilities.InvokeMethod( UnityTypes.TextMeshProUGUI_Methods.IL2CPP.OnEnable, instance );

         var __instance = (Component)Il2CppUtilities.CreateProxyComponentWithDerivedType( instance, UnityTypes.TextMeshProUGUI.ClrType );
         _Postfix( __instance );
      }

   }

   [HookingHelperPriority( HookPriority.Last )]
   internal static class TeshMeshPro_OnEnable_Hook
   {
      static bool Prepare( object instance )
      {
         return UnityTypes.TextMeshPro != null;
      }

      static MethodBase TargetMethod( object instance )
      {
         return AccessToolsShim.Method( UnityTypes.TextMeshPro?.ClrType, "OnEnable" );
      }

      static void _Postfix( Component __instance )
      {
         AutoTranslationPlugin.Current.Hook_TextChanged( __instance, true );
      }

      static void Postfix( Component __instance )
      {
         __instance = (Component)Il2CppUtilities.CreateProxyComponentWithDerivedType( __instance.Pointer, UnityTypes.TextMeshPro.ClrType );

         _Postfix( __instance );
      }

      static IntPtr TargetMethodPointer()
      {
         return UnityTypes.TextMeshPro_Methods.IL2CPP.OnEnable;
      }

      static void ML_Detour( IntPtr instance )
      {
         Il2CppUtilities.InvokeMethod( UnityTypes.TextMeshPro_Methods.IL2CPP.OnEnable, instance );

         var __instance = (Component)Il2CppUtilities.CreateProxyComponentWithDerivedType( instance, UnityTypes.TextMeshPro.ClrType );
         _Postfix( __instance );
      }

   }

   [HookingHelperPriority( HookPriority.Last )]
   internal static class TMP_Text_text_Hook
   {
      static bool Prepare( object instance )
      {
         return UnityTypes.TMP_Text != null;
      }

      static MethodBase TargetMethod( object instance )
      {
         return AccessToolsShim.Property( UnityTypes.TMP_Text?.ClrType, "text" )?.GetSetMethod();
      }

      static void _Postfix( Component __instance )
      {
         AutoTranslationPlugin.Current.Hook_TextChanged( __instance, false );
      }

      static void Postfix( Component __instance )
      {
         __instance = __instance.CreateTextMeshProDerivedProxy();

         _Postfix( __instance );
      }

      static IntPtr TargetMethodPointer()
      {
         return UnityTypes.TMP_Text_Methods.IL2CPP.set_text;
      }

      static void ML_Detour( IntPtr instance, IntPtr value )
      {
         Il2CppUtilities.InvokeMethod( UnityTypes.TMP_Text_Methods.IL2CPP.set_text, instance, value );

         var __instance = instance.CreateTextMeshProDerivedProxy();
         _Postfix( __instance );
      }

   }

   [HookingHelperPriority( HookPriority.Last )]
   internal static class TMP_Text_maxVisibleCharacters_Hook
   {
      static bool Prepare( object instance )
      {
         return UnityTypes.TMP_Text != null;
      }

      static MethodBase TargetMethod( object instance )
      {
         return AccessToolsShim.Property( UnityTypes.TMP_Text?.ClrType, "maxVisibleCharacters" )?.GetSetMethod();
      }

      static void Prefix( Component __instance, ref int value )
      {
         var info = __instance.GetTextTranslationInfo();
         if( info != null && info.IsTranslated && 0 < value )
         {
            value = 99999;
         }
      }

   }
}
