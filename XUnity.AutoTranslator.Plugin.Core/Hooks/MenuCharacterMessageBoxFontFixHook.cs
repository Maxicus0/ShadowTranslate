using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Il2CppInterop.Runtime;
using UnityEngine;
using XUnity.AutoTranslator.Plugin.Core.Extensions;
using XUnity.AutoTranslator.Plugin.Core.Utilities;
using XUnity.Common.Constants;
using XUnity.Common.Extensions;
using XUnity.Common.Logging;

namespace XUnity.AutoTranslator.Plugin.Core.Hooks
{
   internal static class MenuCharacterMessageBoxFontFixHook
   {
      private const string HarmonyId = "XUnity.AutoTranslator.MenuCharacterMessageBoxFontFixHook";
      private static HarmonyLib.Harmony _harmony;
      private static bool _applied;

      private const string DiagBuildStamp = "diag-build-2";

      private static readonly string[] RelativePaths = new[]
      {
         "MessageBox/Base_Name/Text",
         "MessageBox/TextArea",
      };

      public static void Apply()
      {
         if( _applied )
         {
            return;
         }

         try
         {
            var viewType = ResolveType( "Sdw.Common.CommonMenuCharacterView" );
            if( viewType == null )
            {
               XuaLogger.AutoTranslator.Warn( "MenuCharacterMessageBoxFontFixHook: Sdw.Common.CommonMenuCharacterView could not be resolved via reflection; message-box font re-assert will be unavailable." );
               return;
            }

            var target = HarmonyLib.AccessTools.Method( viewType, "ConfigureMessageBox", new Type[] { typeof( string ), typeof( string ) } );
            if( target == null )
            {
               XuaLogger.AutoTranslator.Warn( "MenuCharacterMessageBoxFontFixHook: CommonMenuCharacterView.ConfigureMessageBox(string, string) could not be resolved via reflection." );
               return;
            }

            _harmony = new HarmonyLib.Harmony( HarmonyId );

            var postfix = typeof( MenuCharacterMessageBoxFontFixHook ).GetMethod(
               nameof( ConfigureMessageBox_Postfix ),
               BindingFlags.Static | BindingFlags.NonPublic );

            _harmony.Patch( target, postfix: new HarmonyLib.HarmonyMethod( postfix ) );

            _applied = true;
            XuaLogger.AutoTranslator.Debug( "MenuCharacterMessageBoxFontFixHook: patched " + target.DeclaringType?.FullName + "." + target + "." );
            XuaLogger.AutoTranslator.Debug( "MenuCharacterMessageBoxFontFixHook: [" + DiagBuildStamp + "] active - poll-based reassert + CHARBOX-DIAG logging present in this build." );
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Error( e, "MenuCharacterMessageBoxFontFixHook: Failed to apply hook." );
         }
      }

      private const float ReassertPollIntervalSeconds = 0.3f;
      private const float ReassertPollWindowSeconds = 6.0f;

      private static void ConfigureMessageBox_Postfix( object __instance )
      {
         try
         {
            var component = __instance as Component;
            if( component == null )
            {
               return;
            }

            XuaLogger.AutoTranslator.Debug( "MenuCharacterMessageBoxFontFixHook: [CHARBOX-DIAG] ConfigureMessageBox fired on '" + component.GetPath() + "'; starting " + ReassertPollWindowSeconds.ToString( "0.0" ) + "s reassert poll." );

            CoroutineHelper.Start( ReassertMessageBoxFonts( component.transform ) );
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Warn( e, "MenuCharacterMessageBoxFontFixHook: ConfigureMessageBox_Postfix failed." );
         }
      }

      private static IEnumerator ReassertMessageBoxFonts( Transform root )
      {
         var wait = CoroutineHelper.CreateWaitForSeconds( ReassertPollIntervalSeconds );
         float elapsed = 0f;
         int tick = 0;

         while( elapsed <= ReassertPollWindowSeconds )
         {
            if( wait != null )
            {
               yield return wait;
            }
            else
            {
               yield return null;
            }
            elapsed += ReassertPollIntervalSeconds;
            tick++;

            if( root == null )
            {
               XuaLogger.AutoTranslator.Debug( "MenuCharacterMessageBoxFontFixHook: [CHARBOX-DIAG] tick=" + tick + " root destroyed; stopping poll early." );
               yield break;
            }

            foreach( var relativePath in RelativePaths )
            {
               Transform child;
               try
               {
                  child = root.Find( relativePath );
               }
               catch( Exception e )
               {
                  XuaLogger.AutoTranslator.Warn( e, "MenuCharacterMessageBoxFontFixHook: Transform.Find('" + relativePath + "') threw." );
                  continue;
               }

               if( child == null )
               {
                  XuaLogger.AutoTranslator.Debug( "MenuCharacterMessageBoxFontFixHook: [CHARBOX-DIAG] tick=" + tick + " path='" + relativePath + "' under '" + root.GetPath() + "' -> not found." );
                  continue;
               }

               object textComponent = FindTextComponent( child.gameObject );
               if( textComponent == null )
               {
                  XuaLogger.AutoTranslator.Debug( "MenuCharacterMessageBoxFontFixHook: [CHARBOX-DIAG] tick=" + tick + " path='" + relativePath + "' -> GameObject found but no Text-derived component on it." );
                  continue;
               }

               if( relativePath == "MessageBox/TextArea" )
               {
                  ForceMiddleCenter( textComponent );
               }

               var info = textComponent.GetOrCreateTextTranslationInfo();
               bool isTranslated = info != null && info.IsTranslated;
               int translatedLength = info?.TranslatedText?.Length ?? -1;

               if( isTranslated )
               {
                  info.ChangeFont( textComponent );
               }

               XuaLogger.AutoTranslator.Debug(
                  "MenuCharacterMessageBoxFontFixHook: [CHARBOX-DIAG] tick=" + tick + " path='" + relativePath
                  + "' found=True isTranslated=" + isTranslated + " translatedLength=" + translatedLength
                  + " reasserted=" + isTranslated + "." );
            }
         }
      }

      private static readonly Dictionary<int, bool> _isMessageBoxTextArea = new Dictionary<int, bool>();

      internal static void EnforceMessageBoxAlignment( object ui )
      {
         try
         {
            var component = ui as Component;
            if( component == null ) return;

            int id = component.GetInstanceID();
            if( !_isMessageBoxTextArea.TryGetValue( id, out bool isTarget ) )
            {
               var path = component.GetPath();
               isTarget = path != null && path.EndsWith( "/MessageBox/TextArea", StringComparison.Ordinal );
               _isMessageBoxTextArea[ id ] = isTarget;
            }

            if( isTarget )
            {
               ForceMiddleCenter( ui );
            }
         }
         catch( Exception )
         {
         }
      }

      private const int TextAnchorMiddleCenter = 4;

      internal static void ForceMiddleCenter( object textComponent )
      {
         if( textComponent == null ) return;

         try
         {
            var prop = textComponent.GetType().GetProperty( "alignment", BindingFlags.Instance | BindingFlags.Public );
            if( prop == null || !prop.CanRead || !prop.CanWrite ) return;

            var current = prop.GetValue( textComponent, null );
            if( current != null && Convert.ToInt32( current ) == TextAnchorMiddleCenter ) return;

            object middleCenter = prop.PropertyType.IsEnum
               ? Enum.ToObject( prop.PropertyType, TextAnchorMiddleCenter )
               : (object)TextAnchorMiddleCenter;
            prop.SetValue( textComponent, middleCenter, null );
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Debug( "MenuCharacterMessageBoxFontFixHook: ForceMiddleCenter failed: " + e.Message );
         }
      }

      private static object FindTextComponent( GameObject go )
      {
         if( go == null || UnityTypes.Text == null )
         {
            return null;
         }

         try
         {
            var comps = go.GetComponents( Il2CppType.Of<Component>() );
            foreach( var c in comps )
            {
               if( c == null )
               {
                  continue;
               }

               Il2CppSystem.Type resolvedType;
               try
               {
                  resolvedType = c.GetUnityType();
               }
               catch( Exception )
               {
                  continue;
               }

               if( resolvedType != null && UnityTypes.Text.IsAssignableFrom( resolvedType ) )
               {
                  return c;
               }
            }
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Warn( e, "MenuCharacterMessageBoxFontFixHook: failed to enumerate components on '" + go.name + "'." );
         }

         return null;
      }

      private static Type ResolveType( string fullName )
      {
         Type direct = Type.GetType( fullName, false );
         if( direct != null )
         {
            return direct;
         }

         foreach( var assembly in AppDomain.CurrentDomain.GetAssemblies() )
         {
            Type[] types;
            try
            {
               types = assembly.GetTypes();
            }
            catch( System.Reflection.ReflectionTypeLoadException ex )
            {
               var loaded = new List<Type>();
               foreach( var t in ex.Types )
               {
                  if( t != null )
                  {
                     loaded.Add( t );
                  }
               }
               types = loaded.ToArray();
            }
            catch
            {
               continue;
            }

            foreach( var t in types )
            {
               if( t != null && t.FullName == fullName )
               {
                  return t;
               }
            }
         }

         return null;
      }
   }
}
