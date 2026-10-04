using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using XUnity.AutoTranslator.Plugin.Core.Configuration;
using XUnity.AutoTranslator.Plugin.Core.Constants;
using XUnity.AutoTranslator.Plugin.Core.Extensions;
using XUnity.AutoTranslator.Plugin.Core.Fonts;
using XUnity.AutoTranslator.Plugin.Core.Hooks.TextMeshPro;
using XUnity.AutoTranslator.Plugin.Core.Hooks.UGUI;
using XUnity.Common.Logging;
using XUnity.Common.Utilities;

namespace XUnity.AutoTranslator.Plugin.Core.Hooks
{
   internal static class HooksSetup
   {
      public static void InstallTextGetterCompatHooks()
      {
         try
         {
            if( Settings.TextGetterCompatibilityMode )
            {
               throw new NotImplementedException("TextGetterCompatibilityMode is not supported in IL2CPP.");
            }
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Error( e, "An error occurred while setting up text getter compat hooks." );
         }
      }

      public static void InstallImageHooks()
      {
         try
         {
            if( Settings.EnableTextureTranslation || Settings.EnableTextureDumping )
            {
               HookingHelper.PatchAll( ImageHooks.All, Settings.ForceMonoModHooks );

               if( Settings.EnableLegacyTextureLoading || Settings.EnableSpriteHooking )
               {
                  HookingHelper.PatchAll( ImageHooks.Sprite, Settings.ForceMonoModHooks );
               }
            }
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Error( e, "An error occurred while setting up image hooks." );
         }
      }

      public static void InstallSpriteRendererHooks()
      {
         try
         {
            if( Settings.EnableSpriteRendererHooking && ( Settings.EnableTextureTranslation || Settings.EnableTextureDumping ) )
            {
               HookingHelper.PatchAll( ImageHooks.SpriteRenderer, Settings.ForceMonoModHooks );
            }
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Error( e, "An error occurred while setting up image hooks." );
         }
      }

      private static bool _textAssetHooksInstalled = false;
      public static void InstallTextAssetHooks()
      {
         try
         {
            if( !_textAssetHooksInstalled )
            {
               _textAssetHooksInstalled = true;
               HookingHelper.PatchAll( TextAssetHooks.All, Settings.ForceMonoModHooks );
            }
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Error( e, "An error occurred while setting up text asset hooks." );
         }
      }

      public static void InstallTextHooks()
      {
         try
         {
            HookingHelper.PatchAll( UGUIHooks.All, Settings.ForceMonoModHooks );
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Error( e, "An error occurred while setting up hooks for UGUI." );
         }

         try
         {
            HookingHelper.PatchAll( UtageHooks.All, Settings.ForceMonoModHooks );
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Error( e, "An error occurred while setting up hooks for Utage." );
         }

         try
         {
            if( Settings.EnableTextMeshPro )
            {
               HookingHelper.PatchAll( TextMeshProHooks.All, Settings.ForceMonoModHooks );
            }
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Error( e, "An error occurred while setting up hooks for TextMeshPro." );
         }

         try
         {
            SdwTextCustomHook.Apply();

            StoryTextPreTranslateHook.Apply();

            AdventureBacklogTranslateHook.Apply();

            MenuCharacterMessageBoxFontFixHook.Apply();

            AdventureScriptSourceSubstituteHook.Apply();
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Error( e, "An error occurred while setting up hooks for SdwText." );
         }

         try
         {
            OverrideFontAtlasResetHook.Apply();
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Error( e, "An error occurred while setting up the override font atlas reset hook." );
         }

         try
         {
            NativeFontCyrillicSpacingFixHook.Apply();
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Error( e, "An error occurred while setting up the native-font Cyrillic spacing fix hook." );
         }

         try
         {
            NativeFontReplacements.Initialize();
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Error( e, "An error occurred while registering native font replacements." );
         }

      }

      private static bool _installedPluginTranslationHooks = false;
      public static void InstallComponentBasedPluginTranslationHooks()
      {
         if( AutoTranslationPlugin.Current.PluginTextCaches.Count > 0 )
         {
            if( !_installedPluginTranslationHooks )
            {
               _installedPluginTranslationHooks = true;
               try
               {
                  throw new NotImplementedException("Plugin specific hooks are not supported in IL2CPP.");
               }
               catch( Exception e )
               {
                  XuaLogger.AutoTranslator.Error( e, "An error occurred while setting up hooks for Plugin translations." );
               }
            }
         }
      }

      private static HashSet<Assembly> _installedAssemblies = new HashSet<Assembly>();
      public static void InstallIMGUIBasedPluginTranslationHooks( Assembly assembly, bool final )
      {
         if( Settings.EnableIMGUI && !_installedAssemblies.Contains( assembly ) )
         {
            throw new NotImplementedException("Plugin specific hooks are not supported in IL2CPP.");
         }
      }
   }
}
