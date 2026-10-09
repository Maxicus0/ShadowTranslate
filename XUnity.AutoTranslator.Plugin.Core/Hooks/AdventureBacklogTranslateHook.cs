using System;
using XUnity.AutoTranslator.Plugin.Core.Extensions;
using XUnity.Common.Extensions;
using XUnity.Common.Logging;
using XUnity.Common.Utilities;

namespace XUnity.AutoTranslator.Plugin.Core.Hooks
{
   internal static class AdventureBacklogTranslateHook
   {
      private const string HarmonyId = "com.xunity.autotranslator.hooks.adventurebacklogtranslatehook";

      private static HarmonyLib.Harmony _harmony;
      private static bool _applied;

      private static System.Reflection.PropertyInfo _logEntryTextProperty;
      private static System.Reflection.PropertyInfo _logItemTextComponentProperty;

      private sealed class BacklogTextWriter
      {
         public System.Reflection.MethodInfo SetText;
         public System.Reflection.PropertyInfo TextProperty;
      }

      private static readonly System.Collections.Generic.Dictionary<System.Type, BacklogTextWriter> _textWriterCache =
         new System.Collections.Generic.Dictionary<System.Type, BacklogTextWriter>();

      private static readonly System.Collections.Generic.Dictionary<string, System.Type> _nativeTypeCache =
         new System.Collections.Generic.Dictionary<string, System.Type>();

      public static void Apply()
      {
         if( _applied ) return;

         try
         {
            var resolved = ResolveTypesByName( "AdventureLogItem", "IAdventureLogEntry" );
            resolved.TryGetValue( "AdventureLogItem", out System.Type logItemType );
            resolved.TryGetValue( "IAdventureLogEntry", out System.Type logEntryType );

            if( logItemType == null || logEntryType == null )
            {
               XuaLogger.AutoTranslator.Warn( "AdventureBacklogTranslateHook: AdventureLogItem/IAdventureLogEntry type could not be resolved via reflection." );
               return;
            }

            var target = HarmonyLib.AccessTools.Method(
               logItemType,
               "Configure",
               new System.Type[] { logEntryType } );

            if( target == null )
            {
               XuaLogger.AutoTranslator.Warn( "AdventureBacklogTranslateHook: AdventureLogItem.Configure(IAdventureLogEntry) could not be resolved via reflection." );
               return;
            }

            _logEntryTextProperty = HarmonyLib.AccessTools.Property( logEntryType, "Text" );
            _logItemTextComponentProperty = HarmonyLib.AccessTools.Property( logItemType, "Text" );

            if( _logEntryTextProperty == null || _logItemTextComponentProperty == null )
            {
               XuaLogger.AutoTranslator.Warn( "AdventureBacklogTranslateHook: 'Text' member could not be resolved on IAdventureLogEntry/AdventureLogItem." );
               return;
            }

            _harmony = new HarmonyLib.Harmony( HarmonyId );

            var postfix = typeof( AdventureBacklogTranslateHook ).GetMethod(
               nameof( Configure_Postfix ),
               System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic );

            _harmony.Patch( target, postfix: new HarmonyLib.HarmonyMethod( postfix ) );

            _applied = true;
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Error( e, "AdventureBacklogTranslateHook: Failed to apply hook." );
         }
      }

      private static System.Collections.Generic.Dictionary<string, System.Type> ResolveTypesByName( params string[] simpleNames )
      {
         return XUnity.AutoTranslator.Plugin.Core.Utilities.TypeLookup.ResolveTypesByName( simpleNames );
      }

      private static void Configure_Postfix( object __instance, object logEntry )
      {
         try
         {
            if( __instance == null || logEntry == null )
            {
               return;
            }

            string original = _logEntryTextProperty.GetValue( logEntry, null ) as string;
            if( string.IsNullOrEmpty( original ) )
            {
               return;
            }

            if( !StoryTextPreTranslateHook.TryGetStoryTranslation( original, out var translated ) )
            {
               XuaLogger.AutoTranslator.Debug( "AdventureBacklogTranslateHook: no cached translation for backlog line: " + original );
               return;
            }

            object textComponent = _logItemTextComponentProperty.GetValue( __instance, null );
            if( textComponent == null )
            {
               return;
            }

            System.Type declaredType = textComponent.GetType();
            System.Type componentType = declaredType;

            Il2CppSystem.Type nativeType = textComponent.GetUnityType();
            string nativeTypeName = nativeType?.FullName;

            if( !string.IsNullOrEmpty( nativeTypeName ) )
            {
               if( !_nativeTypeCache.TryGetValue( nativeTypeName, out System.Type resolvedType ) )
               {
                  ResolveTypesByName( nativeTypeName ).TryGetValue( nativeTypeName, out resolvedType );
                  _nativeTypeCache[ nativeTypeName ] = resolvedType;
               }

               if( resolvedType != null )
               {
                  componentType = resolvedType;
               }
            }

            if( componentType != declaredType )
            {
               try
               {
                  var pointer = ( (Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase)textComponent ).Pointer;
                  textComponent = Il2CppUtilities.CreateProxyComponentWithDerivedType( pointer, componentType );
               }
               catch( Exception e )
               {
                  XuaLogger.AutoTranslator.Warn( e, "AdventureBacklogTranslateHook: failed to re-type backlog row component as its real native type; falling back to plain text write." );
                  componentType = declaredType;
               }
            }

            var info = textComponent.GetOrCreateTextTranslationInfo();
            info?.SetTranslatedText( translated );

            if( !_textWriterCache.TryGetValue( componentType, out var writer ) )
            {
               writer = new BacklogTextWriter
               {
                  SetText = HarmonyLib.AccessTools.Method( componentType, "SetText", new System.Type[] { typeof( string ) } ),
               };
               if( writer.SetText == null )
               {
                  writer.TextProperty = HarmonyLib.AccessTools.Property( componentType, "text" );
               }
               _textWriterCache[ componentType ] = writer;
            }

            if( writer.SetText != null )
            {
               writer.SetText.Invoke( textComponent, new object[] { translated } );
            }
            else
            {
               writer.TextProperty?.SetValue( textComponent, translated, null );
            }
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Warn( e, "AdventureBacklogTranslateHook: Configure_Postfix failed; backlog row left untranslated." );
         }
      }
   }
}
