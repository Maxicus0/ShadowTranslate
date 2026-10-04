using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using UnityEngine;
using XUnity.AutoTranslator.Plugin.Core.Constants;
using XUnity.AutoTranslator.Plugin.Core.Extensions;
using XUnity.AutoTranslator.Plugin.Core.Text;
using XUnity.Common.Constants;
using XUnity.Common.Harmony;
using XUnity.Common.Extensions;
using XUnity.Common.MonoMod;
using XUnity.Common.Utilities;

namespace XUnity.AutoTranslator.Plugin.Core.Hooks.NGUI
{
   internal static class NGUIHooks
   {
      public static readonly Type[] All = new[] {
         typeof( UILabel_text_Hook ),
         typeof( UILabel_OnEnable_Hook )
      };
   }

   [HookingHelperPriority( HookPriority.Last )]
   internal static class UILabel_text_Hook
   {
      static bool Prepare( object instance )
      {
         return UnityTypes.UILabel != null;
      }

      static MethodBase TargetMethod( object instance )
      {
         return AccessToolsShim.Property( UnityTypes.UILabel?.ClrType, "text" )?.GetSetMethod();
      }

      public static void _Postfix( Component __instance )
      {
         AutoTranslationPlugin.Current.Hook_TextChanged( __instance, false );
      }

      static void Postfix( Component __instance )
      {
         __instance = (Component)Il2CppUtilities.CreateProxyComponentWithDerivedType( __instance.Pointer, UnityTypes.UILabel.ClrType );

         _Postfix( __instance );
      }

      static IntPtr TargetMethodPointer()
      {
         return UnityTypes.UILabel_Methods.IL2CPP.set_text;
      }

      static void ML_Detour( IntPtr instance, IntPtr value )
      {
         Il2CppUtilities.InvokeMethod( UnityTypes.UILabel_Methods.IL2CPP.set_text, instance, value );

         var __instance = (Component)Il2CppUtilities.CreateProxyComponentWithDerivedType( instance, UnityTypes.UILabel.ClrType );
         _Postfix( __instance );
      }

   }

   [HookingHelperPriority( HookPriority.Last )]
   internal static class UILabel_OnEnable_Hook
   {
      static bool Prepare( object instance )
      {
         return UnityTypes.UIRect != null;
      }

      static MethodBase TargetMethod( object instance )
      {
         return AccessToolsShim.Method( UnityTypes.UIRect?.ClrType, "OnEnable" );
      }

      public static void _Postfix( Component __instance )
      {
         AutoTranslationPlugin.Current.Hook_TextChanged( __instance, true );
      }

      static void Postfix( Component __instance )
      {
         __instance = __instance.GetOrCreateNGUIDerivedProxy();
         if( __instance != null )
         {
            _Postfix( __instance );
         }
      }

      static IntPtr TargetMethodPointer()
      {
         return UnityTypes.UIRect_Methods.IL2CPP.OnEnable;
      }

      static void ML_Detour( IntPtr instance )
      {
         Il2CppUtilities.InvokeMethod( UnityTypes.UIRect_Methods.IL2CPP.OnEnable, instance );

         var __instance = instance.CreateNGUIDerivedProxy();
         if( __instance != null )
         {
            _Postfix( __instance );
         }
      }

   }
}
