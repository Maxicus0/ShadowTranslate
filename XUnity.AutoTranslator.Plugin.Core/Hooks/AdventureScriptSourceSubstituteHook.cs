using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using XUnity.AutoTranslator.Plugin.Core.Configuration;
using XUnity.Common.Logging;

namespace XUnity.AutoTranslator.Plugin.Core.Hooks
{
   internal static class AdventureScriptSourceSubstituteHook
   {
      private const string HarmonyId = "com.xunity.autotranslator.hooks.adventurescriptsourcesubstitutehook";

      private static HarmonyLib.Harmony _harmony;
      private static bool _applied;

      private static PropertyInfo _textMemberProperty;

      private static readonly HashSet<string> _loggedSignatures = new HashSet<string>( StringComparer.Ordinal );

      public static void Apply()
      {
         if( _applied ) return;

         try
         {
            var resolved = ResolveTypesByName( "AdventurePlayable", "AdventureScriptMaster" );
            resolved.TryGetValue( "AdventurePlayable", out Type adventurePlayableType );
            resolved.TryGetValue( "AdventureScriptMaster", out Type scriptMasterType );

            if( adventurePlayableType == null || scriptMasterType == null )
            {
               XuaLogger.AutoTranslator.Warn( "AdventureScriptSourceSubstituteHook: AdventurePlayable/AdventureScriptMaster type could not be resolved via reflection; skipping." );
               return;
            }

            var target = HarmonyLib.AccessTools.Method(
               adventurePlayableType,
               "PlayScriptAndWait",
               new Type[] { scriptMasterType } );

            if( target == null )
            {
               XuaLogger.AutoTranslator.Warn( "AdventureScriptSourceSubstituteHook: AdventurePlayable.PlayScriptAndWait(AdventureScriptMaster) could not be resolved via reflection; skipping." );
               return;
            }

            if( !string.IsNullOrEmpty( Settings.StoryScriptMasterTextMember ) )
            {
               _textMemberProperty = HarmonyLib.AccessTools.Property( scriptMasterType, Settings.StoryScriptMasterTextMember );

               if( _textMemberProperty == null )
               {
                  XuaLogger.AutoTranslator.Warn( "AdventureScriptSourceSubstituteHook: configured StoryScriptMasterTextMember '" + Settings.StoryScriptMasterTextMember + "' is not a property on " + scriptMasterType.FullName + "; falling back to discovery mode -- check the DISCOVERY log lines for the real name." );
               }
               else if( _textMemberProperty.PropertyType != typeof( string ) )
               {
                  XuaLogger.AutoTranslator.Warn( "AdventureScriptSourceSubstituteHook: configured StoryScriptMasterTextMember '" + Settings.StoryScriptMasterTextMember + "' exists but is type " + _textMemberProperty.PropertyType.Name + ", not string; falling back to discovery mode." );
                  _textMemberProperty = null;
               }
            }

            _harmony = _harmony ?? new HarmonyLib.Harmony( HarmonyId );

            var prefix = typeof( AdventureScriptSourceSubstituteHook ).GetMethod(
               nameof( PlayScriptAndWait_Prefix ),
               BindingFlags.Static | BindingFlags.NonPublic );

            _harmony.Patch( target, prefix: new HarmonyLib.HarmonyMethod( prefix ) );

            _applied = true;

            XuaLogger.AutoTranslator.Info( _textMemberProperty != null
               ? "AdventureScriptSourceSubstituteHook: patched AdventurePlayable.PlayScriptAndWait(AdventureScriptMaster); substituting via configured member '" + Settings.StoryScriptMasterTextMember + "'."
               : "AdventureScriptSourceSubstituteHook: patched AdventurePlayable.PlayScriptAndWait(AdventureScriptMaster) in DISCOVERY mode -- play a recognizable story line, then check the log for candidate property names and set StoryScriptMasterTextMember under [Behaviour] in AutoTranslatorConfig.ini." );
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Error( e, "AdventureScriptSourceSubstituteHook: failed to apply hook." );
         }
      }

      private static void PlayScriptAndWait_Prefix( object nextPlayScript )
      {
         try
         {
            if( nextPlayScript == null )
            {
               return;
            }

            if( _textMemberProperty != null )
            {
               SubstituteConfigured( nextPlayScript );
            }
            else
            {
               LogCandidates( nextPlayScript );
            }
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Warn( e, "AdventureScriptSourceSubstituteHook: PlayScriptAndWait_Prefix failed; script record left unmodified." );
         }
      }

      private static void SubstituteConfigured( object script )
      {
         string original = _textMemberProperty.GetValue( script, null ) as string;
         if( string.IsNullOrEmpty( original ) )
         {
            return;
         }

         if( !StoryTextPreTranslateHook.TryGetStoryTranslation( original, out var translated ) )
         {
            XuaLogger.AutoTranslator.Debug( "AdventureScriptSourceSubstituteHook: no cached translation for script line: " + original );
            return;
         }

         _textMemberProperty.SetValue( script, translated, null );
      }

      private static void LogCandidates( object script )
      {
         Type scriptType = script.GetType();

         var stringProperties = scriptType
            .GetProperties( BindingFlags.Public | BindingFlags.Instance )
            .Where( p => p.PropertyType == typeof( string ) && p.GetIndexParameters().Length == 0 )
            .ToArray();

         if( stringProperties.Length == 0 )
         {
            XuaLogger.AutoTranslator.Warn( "AdventureScriptSourceSubstituteHook: no public string properties found on " + scriptType.FullName + " -- the text member may not be a simple string property on this exact runtime type. Story text will still be translated at the display layer by StoryTextPreTranslateHook as before; source substitution just won't apply." );
            return;
         }

         string signature = string.Join( ",", stringProperties.Select( p => p.Name ) );
         if( !_loggedSignatures.Add( signature ) )
         {
            return;
         }

         XuaLogger.AutoTranslator.Info( "AdventureScriptSourceSubstituteHook: DISCOVERY -- candidate string properties on " + scriptType.FullName + ":" );

         foreach( var prop in stringProperties )
         {
            string value;
            try
            {
               value = prop.GetValue( script, null ) as string;
            }
            catch( Exception e )
            {
               XuaLogger.AutoTranslator.Info( "   " + prop.Name + " = <threw " + e.GetType().Name + " reading value>" );
               continue;
            }

            string preview = value == null
               ? "<null>"
               : ( value.Length > 60 ? value.Substring( 0, 60 ) + "..." : value );

            XuaLogger.AutoTranslator.Info( "   " + prop.Name + " = \"" + preview + "\"" );
         }

         XuaLogger.AutoTranslator.Info( "AdventureScriptSourceSubstituteHook: find the property above whose value matches the line you just saw on screen, then set StoryScriptMasterTextMember=<that name> under [Behaviour] in AutoTranslatorConfig.ini and restart." );
      }

      private static Dictionary<string, Type> ResolveTypesByName( params string[] simpleNames )
      {
         var result = new Dictionary<string, Type>();
         var remaining = new HashSet<string>( simpleNames );

         foreach( var name in simpleNames )
         {
            Type direct = Type.GetType( name, false );
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

         foreach( var assembly in AppDomain.CurrentDomain.GetAssemblies() )
         {
            if( remaining.Count == 0 )
            {
               break;
            }

            Type[] types;
            try
            {
               types = assembly.GetTypes();
            }
            catch( ReflectionTypeLoadException ex )
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
   }
}
