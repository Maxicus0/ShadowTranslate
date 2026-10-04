using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using XUnity.AutoTranslator.Plugin.Core.Constants;
using XUnity.AutoTranslator.Plugin.Core.Extensions;
using XUnity.Common.Constants;
using XUnity.Common.Extensions;
using XUnity.Common.Harmony;
using XUnity.Common.Logging;
using XUnity.Common.MonoMod;

namespace XUnity.AutoTranslator.Plugin.Core.Hooks
{
   internal static class ImageHooks
   {
      public static readonly Type[] All = new[] {
         typeof( MaskableGraphic_OnEnable_Hook ),
         typeof( Image_sprite_Hook ),
         typeof( Image_overrideSprite_Hook ),
         typeof( Image_material_Hook ),
         typeof( RawImage_texture_Hook ),
         typeof( Cursor_SetCursor_Hook ),

         typeof( Material_mainTexture_Hook ),

         typeof( CubismRenderer_MainTexture_Hook ),
         typeof( CubismRenderer_TryInitialize_Hook ),

         typeof( UIAtlas_spriteMaterial_Hook ),
         typeof( UISprite_OnInit_Hook ),
         typeof( UISprite_material_Hook ),
         typeof( UISprite_atlas_Hook ),
         typeof( UI2DSprite_sprite2D_Hook ),
         typeof( UI2DSprite_material_Hook ),
         typeof( UITexture_mainTexture_Hook ),
         typeof( UITexture_material_Hook ),
         typeof( UIPanel_clipTexture_Hook ),
         typeof( UIRect_OnInit_Hook ),

         typeof( DicingTextures_GetTexture_Hook ),
      };

      public static readonly Type[] Sprite = new[] {
         typeof( Sprite_texture_Hook )
      };

      public static readonly Type[] SpriteRenderer = new[] {
         typeof( SpriteRenderer_sprite_Hook ),
      };
   }

   internal static class DicingTextures_GetTexture_Hook
   {
      static bool Prepare( object instance )
      {
         return UnityTypes.DicingTextures != null;
      }

      static MethodBase TargetMethod( object instance )
      {
         return AccessToolsShim.Method( UnityTypes.DicingTextures?.ClrType, "GetTexture", new[] { typeof( string ) } );
      }

      public static void Postfix( object __instance, ref Texture2D __result )
      {
         AutoTranslationPlugin.Current.Hook_ImageChanged( ref __result, false );
      }

   }

   internal static class Sprite_texture_Hook
   {
      static bool Prepare( object instance )
      {
         return UnityTypes.Sprite != null;
      }

      static MethodBase TargetMethod( object instance )
      {
         return AccessToolsShim.Property( UnityTypes.Sprite?.ClrType, "texture" )?.GetGetMethod();
      }

      static void Postfix( ref Texture2D __result )
      {
         AutoTranslationPlugin.Current.Hook_ImageChanged( ref __result, true );
      }

   }

   internal static class SpriteRenderer_sprite_Hook
   {
      static bool Prepare( object instance )
      {
         return UnityTypes.SpriteRenderer != null;
      }

      static MethodBase TargetMethod( object instance )
      {
         return AccessToolsShim.Property( UnityTypes.SpriteRenderer?.ClrType, "sprite" )?.GetSetMethod();
      }

      public static void Prefix( SpriteRenderer __instance, ref Sprite value )
      {
         Texture2D texture;
         var prev = CallOrigin.ImageHooksEnabled;
         CallOrigin.ImageHooksEnabled = false;
         try
         {
            texture = value.texture;
         }
         finally
         {
            CallOrigin.ImageHooksEnabled = prev;
         }
         AutoTranslationPlugin.Current.Hook_ImageChangedOnComponent( __instance, ref value, ref texture, true, false );
      }

   }

   internal static class CubismRenderer_MainTexture_Hook
   {
      static bool Prepare( object instance )
      {
         return UnityTypes.CubismRenderer != null;
      }

      static MethodBase TargetMethod( object instance )
      {
         return AccessToolsShim.Property( UnityTypes.CubismRenderer.ClrType, "MainTexture" )?.GetSetMethod();
      }

      public static void Prefix( Component __instance, ref Texture2D value )
      {
         AutoTranslationPlugin.Current.Hook_ImageChangedOnComponent( __instance, ref value, true, false );
      }

   }

   internal static class CubismRenderer_TryInitialize_Hook
   {
      static bool Prepare( object instance )
      {
         return UnityTypes.CubismRenderer != null;
      }

      static MethodBase TargetMethod( object instance )
      {
         return AccessToolsShim.Method( UnityTypes.CubismRenderer.ClrType, "TryInitialize" );
      }

      public static void Prefix( Component __instance )
      {
         Texture2D _ = null;
         AutoTranslationPlugin.Current.Hook_ImageChangedOnComponent( __instance, ref _, true, true );
      }

   }

   internal static class Material_mainTexture_Hook
   {
      static bool Prepare( object instance )
      {
         return true;
      }

      static MethodBase TargetMethod( object instance )
      {
         return AccessToolsShim.Property( typeof( Material ), "mainTexture" )?.GetSetMethod();
      }

      public static void Prefix( Material __instance, ref Texture value )
      {
         if( value.TryCastTo<Texture2D>( out var texture2d ) )
         {
            AutoTranslationPlugin.Current.Hook_ImageChangedOnComponent( __instance, ref texture2d, true, false );
            value = texture2d;
         }
      }

   }

   internal static class MaskableGraphic_OnEnable_Hook
   {
      static bool Prepare( object instance )
      {
         return UnityTypes.MaskableGraphic != null;
      }

      static MethodBase TargetMethod( object instance )
      {
         return AccessToolsShim.Method( UnityTypes.MaskableGraphic?.ClrType, "OnEnable" );
      }

      public static void Postfix( Component __instance )
      {
         var type = __instance.GetUnityType();
         if( ( UnityTypes.Image != null && UnityTypes.Image.IsAssignableFrom( type ) )
            || ( UnityTypes.RawImage != null && UnityTypes.RawImage.IsAssignableFrom( type ) ) )
         {
            Texture2D _ = null;
            AutoTranslationPlugin.Current.Hook_ImageChangedOnComponent( __instance, ref _, false, true );
         }
      }

   }

   internal static class Image_sprite_Hook
   {
      static bool Prepare( object instance )
      {
         return UnityTypes.Image != null;
      }

      static MethodBase TargetMethod( object instance )
      {
         return AccessToolsShim.Property( UnityTypes.Image?.ClrType, "sprite" )?.GetSetMethod();
      }

      public static void Postfix( Component __instance )
      {
         Texture2D _ = null;
         AutoTranslationPlugin.Current.Hook_ImageChangedOnComponent( __instance, ref _, false, false );
      }

   }

   internal static class Image_overrideSprite_Hook
   {
      static bool Prepare( object instance )
      {
         return UnityTypes.Image != null;
      }

      static MethodBase TargetMethod( object instance )
      {
         return AccessToolsShim.Property( UnityTypes.Image?.ClrType, "overrideSprite" )?.GetSetMethod();
      }

      public static void Postfix( Component __instance )
      {
         Texture2D _ = null;
         AutoTranslationPlugin.Current.Hook_ImageChangedOnComponent( __instance, ref _, false, false );
      }

   }

   internal static class Image_material_Hook
   {
      static bool Prepare( object instance )
      {
         return UnityTypes.Image != null;
      }

      static MethodBase TargetMethod( object instance )
      {
         return AccessToolsShim.Property( UnityTypes.Image?.ClrType, "material" )?.GetSetMethod();
      }

      public static void Postfix( Component __instance )
      {
         Texture2D _ = null;
         AutoTranslationPlugin.Current.Hook_ImageChangedOnComponent( __instance, ref _, false, false );
      }

   }

   internal static class RawImage_texture_Hook
   {
      static bool Prepare( object instance )
      {
         return UnityTypes.RawImage != null;
      }

      static MethodBase TargetMethod( object instance )
      {
         return AccessToolsShim.Property( UnityTypes.RawImage?.ClrType, "texture" )?.GetSetMethod();
      }

      public static void Prefix( Component __instance, ref Texture value )
      {
         if( value.TryCastTo<Texture2D>( out var texture2d ) )
         {
            AutoTranslationPlugin.Current.Hook_ImageChangedOnComponent( __instance, ref texture2d, true, false );
            value = texture2d;
         }
      }

   }

   internal static class Cursor_SetCursor_Hook
   {
      static bool Prepare( object instance )
      {
         return true;
      }

      static MethodBase TargetMethod( object instance )
      {
         return AccessToolsShim.Method( typeof( Cursor ), "SetCursor", new[] { typeof( Texture2D ), typeof( Vector2 ), typeof( CursorMode ) } );
      }

      public static void Prefix( ref Texture2D texture )
      {
         AutoTranslationPlugin.Current.Hook_ImageChanged( ref texture, true );
      }

   }

   internal static class UIAtlas_spriteMaterial_Hook
   {
      static bool Prepare( object instance )
      {
         return UnityTypes.UIAtlas != null;
      }

      static MethodBase TargetMethod( object instance )
      {
         return AccessToolsShim.Property( UnityTypes.UIAtlas?.ClrType, "spriteMaterial" )?.GetSetMethod();
      }

      public static void Postfix( object __instance )
      {
         Texture2D _ = null;
         AutoTranslationPlugin.Current.Hook_ImageChangedOnComponent( __instance, ref _, false, false );
      }

   }

   internal static class UISprite_OnInit_Hook
   {
      static bool Prepare( object instance )
      {
         return UnityTypes.UISprite != null;
      }

      static MethodBase TargetMethod( object instance )
      {
         return AccessToolsShim.Method( UnityTypes.UISprite?.ClrType, "OnInit" );
      }

      public static void Postfix( object __instance )
      {
         Texture2D _ = null;
         AutoTranslationPlugin.Current.Hook_ImageChangedOnComponent( __instance, ref _, false, true );
      }

   }

   internal static class UISprite_material_Hook
   {
      static bool Prepare( object instance )
      {
         return UnityTypes.UISprite != null;
      }

      static MethodBase TargetMethod( object instance )
      {
         return AccessToolsShim.Property( UnityTypes.UISprite?.ClrType, "material" )?.GetSetMethod();
      }

      public static void Postfix( object __instance )
      {
         Texture2D _ = null;
         AutoTranslationPlugin.Current.Hook_ImageChangedOnComponent( __instance, ref _, false, false );
      }

   }

   internal static class UISprite_atlas_Hook
   {
      static bool Prepare( object instance )
      {
         return UnityTypes.UISprite != null;
      }

      static MethodBase TargetMethod( object instance )
      {
         return AccessToolsShim.Property( UnityTypes.UISprite?.ClrType, "atlas" )?.GetSetMethod();
      }

      public static void Postfix( object __instance )
      {
         Texture2D _ = null;
         AutoTranslationPlugin.Current.Hook_ImageChangedOnComponent( __instance, ref _, false, false );
      }

   }

   internal static class UITexture_mainTexture_Hook
   {
      static bool Prepare( object instance )
      {
         return UnityTypes.UITexture != null;
      }

      static MethodBase TargetMethod( object instance )
      {
         return AccessToolsShim.Property( UnityTypes.UITexture?.ClrType, "mainTexture" )?.GetSetMethod();
      }

      public static void Postfix( object __instance )
      {
         Texture2D _ = null;
         AutoTranslationPlugin.Current.Hook_ImageChangedOnComponent( __instance, ref _, false, false );
      }

   }

   internal static class UITexture_material_Hook
   {
      static bool Prepare( object instance )
      {
         return UnityTypes.UITexture != null;
      }

      static MethodBase TargetMethod( object instance )
      {
         return AccessToolsShim.Property( UnityTypes.UITexture?.ClrType, "material" )?.GetSetMethod();
      }

      public static void Postfix( object __instance )
      {
         Texture2D _ = null;
         AutoTranslationPlugin.Current.Hook_ImageChangedOnComponent( __instance, ref _, false, false );
      }

   }

   internal static class UIRect_OnInit_Hook
   {
      static bool Prepare( object instance )
      {
         return UnityTypes.UIRect != null;
      }

      static MethodBase TargetMethod( object instance )
      {
         return AccessToolsShim.Method( UnityTypes.UIRect?.ClrType, "OnInit" );
      }

      public static void Postfix( object __instance )
      {
         Texture2D _ = null;
         AutoTranslationPlugin.Current.Hook_ImageChangedOnComponent( __instance, ref _, false, true );
      }

   }

   internal static class UI2DSprite_sprite2D_Hook
   {
      static bool Prepare( object instance )
      {
         return UnityTypes.UI2DSprite != null;
      }

      static MethodBase TargetMethod( object instance )
      {
         return AccessToolsShim.Property( UnityTypes.UI2DSprite?.ClrType, "sprite2D" )?.GetSetMethod();
      }

      public static void Postfix( object __instance )
      {
         Texture2D _ = null;
         AutoTranslationPlugin.Current.Hook_ImageChangedOnComponent( __instance, ref _, false, false );
      }

   }

   internal static class UI2DSprite_material_Hook
   {
      static bool Prepare( object instance )
      {
         return UnityTypes.UI2DSprite != null;
      }

      static MethodBase TargetMethod( object instance )
      {
         return AccessToolsShim.Property( UnityTypes.UI2DSprite?.ClrType, "material" )?.GetSetMethod();
      }

      public static void Postfix( object __instance )
      {
         Texture2D _ = null;
         AutoTranslationPlugin.Current.Hook_ImageChangedOnComponent( __instance, ref _, false, false );
      }

   }

   internal static class UIPanel_clipTexture_Hook
   {
      static bool Prepare( object instance )
      {
         return UnityTypes.UIPanel != null;
      }

      static MethodBase TargetMethod( object instance )
      {
         return AccessToolsShim.Property( UnityTypes.UIPanel.ClrType, "clipTexture" )?.GetSetMethod();
      }

      public static void Postfix( object __instance )
      {
         Texture2D _ = null;
         AutoTranslationPlugin.Current.Hook_ImageChangedOnComponent( __instance, ref _, false, false );
      }

   }
}
