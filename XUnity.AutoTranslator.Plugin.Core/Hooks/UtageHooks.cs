using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using XUnity.AutoTranslator.Plugin.Core.Configuration;
using XUnity.AutoTranslator.Plugin.Core.Constants;
using XUnity.AutoTranslator.Plugin.Core.Extensions;
using XUnity.AutoTranslator.Plugin.Core.Utilities;
using XUnity.Common.Constants;
using XUnity.Common.Harmony;
using XUnity.Common.MonoMod;
using XUnity.Common.Utilities;

namespace XUnity.AutoTranslator.Plugin.Core.Hooks
{
   internal static class UtageHooks
   {
      public static readonly Type[] All = new[] {
         typeof( AdvEngine_JumpScenario_Hook ),
         typeof( UguiNovelTextGenerator_LengthOfView_Hook ),

      };
   }

   internal static class AdvEngine_JumpScenario_Hook
   {
      static bool Prepare( object instance )
      {
         return UnityTypes.AdvEngine != null;
      }

      static MethodBase TargetMethod( object instance )
      {
         return AccessToolsShim.Method( UnityTypes.AdvEngine?.ClrType, "JumpScenario", new Type[] { typeof( string ) } );
      }

      static void Prefix( ref string label )
      {
         UtageHelper.FixLabel( ref label );
      }

   }

   internal static class UguiNovelTextGenerator_LengthOfView_Hook
   {
      static bool Prepare( object instance )
      {
         return UnityTypes.UguiNovelTextGenerator != null;
      }

      static MethodBase TargetMethod( object instance )
      {
         return AccessToolsShim.Property( UnityTypes.UguiNovelTextGenerator?.ClrType, "LengthOfView" ).GetSetMethod( true );
      }

      static void Prefix( ref int value )
      {
         value = -1;
      }

   }

   [HookingHelperPriority( HookPriority.Last )]
   internal static class TextArea2D_text_Hook
   {
      static bool Prepare( object instance )
      {
         return UnityTypes.TextArea2D != null;
      }

      static MethodBase TargetMethod( object instance )
      {
         return AccessToolsShim.Property( UnityTypes.TextArea2D?.ClrType, "text" )?.GetSetMethod();
      }

      static void Postfix( Component __instance )
      {
         __instance = (Component)Il2CppUtilities.CreateProxyComponentWithDerivedType( __instance.Pointer, UnityTypes.TextArea2D.ClrType );

         _Postfix( __instance );
      }

      static void _Postfix( Component __instance )
      {
         AutoTranslationPlugin.Current.Hook_TextChanged( __instance, false );
      }

   }

   [HookingHelperPriority( HookPriority.Last )]
   internal static class TextArea2D_TextData_Hook
   {
      static bool Prepare( object instance )
      {
         return UnityTypes.TextArea2D != null;
      }

      static MethodBase TargetMethod( object instance )
      {
         return AccessToolsShim.Property( UnityTypes.TextArea2D.ClrType, "TextData" )?.GetSetMethod();
      }

      static void Postfix( Component __instance )
      {
         __instance = (Component)Il2CppUtilities.CreateProxyComponentWithDerivedType( __instance.Pointer, UnityTypes.TextArea2D.ClrType );

         _Postfix( __instance );
      }

      static void _Postfix( Component __instance )
      {
         AutoTranslationPlugin.Current.Hook_TextChanged( __instance, false );
      }

   }

   [HookingHelperPriority( HookPriority.Last )]
   internal static class TextData_ctor_Hook
   {
      static bool Prepare( object instance )
      {
         return UnityTypes.TextData != null;
      }

      static MethodBase TargetMethod( object instance )
      {
         return UnityTypes.TextData.ClrType.GetConstructor( new[] { typeof( string ) } );
      }

      static void Postfix( object __instance, string text )
      {
         __instance.SetExtensionData( text );
      }

   }
}
