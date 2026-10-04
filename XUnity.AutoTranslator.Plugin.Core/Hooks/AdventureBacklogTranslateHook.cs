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

      private static System.Collections.Generic.Dictionary<string, System.Type> ResolveTypesByName(
         params string[] simpleNames )
      {
         var result = new System.Collections.Generic.Dictionary<string, System.Type>();
         var remaining = new System.Collections.Generic.HashSet<string>( simpleNames );

         foreach( var name in simpleNames )
         {
            System.Type direct = System.Type.GetType( name, false );
            if( direct != null )
            {
               result[ name ] = direct;
               remaining.Remove( name );
            }
         }

         if( remaining.Count == 0 )
         {
            return result;
         }

         foreach( var assembly in System.AppDomain.CurrentDomain.GetAssemblies() )
         {
            if( remaining.Count == 0 )
            {
               break;
            }

            System.Type[] types;
            try
            {
               types = assembly.GetTypes();
            }
            catch( System.Reflection.ReflectionTypeLoadException ex )
            {
               var loaded = new System.Collections.Generic.List<System.Type>();
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

            foreach( var type in types )
            {
               if( type == null )
               {
                  continue;
               }

               if( remaining.Contains( type.FullName ) )
               {
                  result[ type.FullName ] = type;
                  remaining.Remove( type.FullName );
               }
               else if( remaining.Contains( type.Name ) )
               {
                  result[ type.Name ] = type;
                  remaining.Remove( type.Name );
               }
            }
         }

         return result;
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

            System.Reflection.MethodInfo setTextMethod = HarmonyLib.AccessTools.Method(
               componentType, "SetText", new System.Type[] { typeof( string ) } );

            if( setTextMethod != null )
            {
               setTextMethod.Invoke( textComponent, new object[] { translated } );
            }
            else
            {
               System.Reflection.PropertyInfo textProperty = HarmonyLib.AccessTools.Property( componentType, "text" );
               textProperty?.SetValue( textComponent, translated, null );
            }
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Warn( e, "AdventureBacklogTranslateHook: Configure_Postfix failed; backlog row left untranslated." );
         }
      }
   }
}
