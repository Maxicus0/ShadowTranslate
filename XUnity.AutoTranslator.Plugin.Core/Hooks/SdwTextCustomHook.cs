using System;
using XUnity.AutoTranslator.Plugin.Core.Configuration;
using XUnity.AutoTranslator.Plugin.Core.Extensions;
using XUnity.Common.Logging;
using XUnity.Common.Utilities;
using XUnity.Common.Extensions;

namespace XUnity.AutoTranslator.Plugin.Core.Hooks
{
   internal static class SdwTextCustomHook
   {
      private const string HarmonyId = "com.xunity.autotranslator.hooks.sdwtextcustomhook";

      private static HarmonyLib.Harmony _harmony;
      private static bool _applied;
      private static bool _characterTablesPatched;

      private static System.Reflection.MethodInfo _updateTextMethod;
      private static System.Reflection.MethodInfo _setTextMethod;

      private sealed class GameEntryPointMarker
      {
         public string EntryPoint;
      }

      private sealed class SdwTextInstanceMarker
      {
         public int InstanceId;
      }

      private static bool MarkAsSdwTextInstance_Prefix( object __instance )
      {
         if( __instance == null )
         {
            return true;
         }

         var marker = __instance.GetOrCreateExtensionData<SdwTextInstanceMarker>();
         marker.InstanceId = GetUnityInstanceId( __instance );
         return true;
      }

      public static bool IsKnownSdwTextInstance( object instance )
      {
         if( instance == null )
         {
            return false;
         }

         if( IsForceBypassedPath( instance ) )
         {
            return false;
         }

         var marker = instance.GetExtensionData<SdwTextInstanceMarker>();
         if( marker == null )
         {
            return false;
         }

         var currentId = GetUnityInstanceId( instance );
         var isMatch = marker.InstanceId == currentId;

         if( isMatch )
         {
            XuaLogger.AutoTranslator.Debug( $"[SDWTEXT-MARKER-CHECK] '{instance.GetPath()}' matched known-SdwText marker (storedId={marker.InstanceId}, currentId={currentId})." );
         }

         return isMatch;
      }

      public static bool ShouldSkipOnEnableDispatch( object instance )
      {
         if( !IsKnownSdwTextInstance( instance ) )
         {
            return false;
         }

         try
         {
            var info = instance.GetTextTranslationInfo();
            if( info == null || !info.IsTranslated || string.IsNullOrEmpty( info.TranslatedText ) )
            {
               return false;
            }

            var current = instance.GetText( info );
            if( string.IsNullOrEmpty( current ) )
            {
               return false;
            }

            return string.Equals( StripWhitespace( current ), StripWhitespace( info.TranslatedText ), StringComparison.Ordinal );
         }
         catch
         {
            return false;
         }
      }

      public static bool IsWrappedOwnTranslation( object ui, string text, TextTranslationInfo info )
      {
         try
         {
            if( ui == null || info == null || !info.IsTranslated || string.IsNullOrEmpty( info.TranslatedText ) || string.IsNullOrEmpty( text ) )
            {
               return false;
            }

            if( !IsKnownSdwTextInstance( ui ) )
            {
               return false;
            }

            return string.Equals( StripWhitespace( text ), StripWhitespace( info.TranslatedText ), StringComparison.Ordinal );
         }
         catch
         {
            return false;
         }
      }

      internal static bool TryApplyPatternTranslation( object ui, TextTranslationInfo info, bool isComponentActive )
      {
         try
         {
            if( ui == null || !isComponentActive || !SdwPatternTranslator.HasPatterns )
            {
               return false;
            }

            if( info != null && info.IsCurrentlySettingText )
            {
               return false;
            }

            var isSdwText = IsSdwTextComponent( ui );
            var isPlainUiText = !isSdwText && IsPlainUiTextComponent( ui );
            if( !( isSdwText || isPlainUiText ) || StoryTextPreTranslateHook.IsPreTranslatedInstance( ui ) )
            {
               return false;
            }

            var text = ui.GetText( info );
            if( string.IsNullOrWhiteSpace( text ) )
            {
               return false;
            }

            if( info != null && info.IsTranslated && ( text == info.TranslatedText || IsWrappedOwnTranslation( ui, text, info ) ) )
            {
               return false;
            }

            if( SdwPatternTranslator.TryExactDirect( text, out _ ) )
            {
               return false;
            }

            string translated;
            string patternSource;
            if( isSdwText )
            {
               if( !SdwPatternTranslator.TryResolve( text, out translated, out patternSource ) )
               {
                  LogPatternMiss( text, ui );
                  return false;
               }
            }
            else
            {
               if( !SdwPatternTranslator.TryTranslate( text, out translated, out var src ) )
               {
                  return false;
               }
               patternSource = "pattern '" + src + "' (UI.Text)";
            }

            XuaLogger.AutoTranslator.Debug( $"[SDWPATTERN] '{text.Replace( '\n', ' ' )}' -> '{translated.Replace( '\n', ' ' )}'  via {patternSource}  at '{ui.GetPath()}'" );

            if( info != null )
            {
               info.Reset( text );
               if( info.ShouldIgnore )
               {
                  return false;
               }
            }

            AutoTranslationPlugin.Current.SetTranslatedText( ui, translated, text, info );
            return true;
         }
         catch( System.Exception e )
         {
            XuaLogger.AutoTranslator.Warn( e, "SdwTextCustomHook: pattern translation failed; falling back to the normal pipeline." );
            return false;
         }
      }

      private static readonly System.Collections.Generic.HashSet<string> _loggedMisses = new System.Collections.Generic.HashSet<string>();

      private static bool ContainsLetter( string s )
      {
         foreach( var ch in s ) if( char.IsLetter( ch ) ) return true;
         return false;
      }

      private static void LogPatternMiss( string text, object ui )
      {
         try
         {
            if( text.Length < 2 || !ContainsLetter( text ) || _loggedMisses.Count > 500 || !_loggedMisses.Add( text ) )
            {
               return;
            }

            var sb = new System.Text.StringBuilder();
            foreach( var c in text )
            {
               if( c == '\n' ) sb.Append( "\\n" );
               else if( c == '\r' ) sb.Append( "\\r" );
               else if( c == '=' ) sb.Append( "\\=" );
               else if( c < 32 || c > 126 ) sb.Append( "\\u" ).Append( ( (int)c ).ToString( "X4" ) );
               else sb.Append( c );
            }
            XuaLogger.AutoTranslator.Debug( $"[SDW-MISS] no exact/pattern translation for '{sb}' at '{ui.GetPath()}'" );
         }
         catch
         {
         }
      }

      private static bool IsPlainUiTextComponent( object ui )
      {
         var name = ui.GetUnityType()?.FullName;
         return name == "UnityEngine.UI.Text";
      }

      private static bool IsSdwTextComponent( object ui )
      {
         var name = ui.GetUnityType()?.FullName;
         if( name != null && ( name == "SdwText" || name.EndsWith( ".SdwText", StringComparison.Ordinal ) ) )
         {
            return true;
         }

         return IsKnownSdwTextInstance( ui );
      }

      private static string StripWhitespace( string value )
      {
         var sb = new System.Text.StringBuilder( value.Length );
         foreach( var c in value )
         {
            if( !char.IsWhiteSpace( c ) )
            {
               sb.Append( c );
            }
         }
         return sb.ToString();
      }

      private static bool IsForceBypassedPath( object instance )
      {
         var groups = Settings.ForceBypassedSdwTextPaths;
         if( groups == null || groups.Length == 0 )
         {
            return false;
         }

         string path;
         try
         {
            path = instance.GetPath();
         }
         catch
         {
            return false;
         }

         if( path == null )
         {
            return false;
         }

         foreach( var fragments in groups )
         {
            var allMatch = true;
            foreach( var fragment in fragments )
            {
               if( path.IndexOf( fragment, StringComparison.OrdinalIgnoreCase ) < 0 )
               {
                  allMatch = false;
                  break;
               }
            }

            if( allMatch )
            {
               return true;
            }
         }

         return false;
      }

      private static int GetUnityInstanceId( object instance )
      {
         return ( instance as UnityEngine.Object )?.GetInstanceID() ?? 0;
      }

      private static void RememberGameEntryPoint( object instance, string entryPoint )
      {
         if( instance == null )
         {
            return;
         }

         instance.GetOrCreateExtensionData<GameEntryPointMarker>().EntryPoint = entryPoint;
      }

      public static void Apply()
      {
         if( _applied )
         {
            return;
         }

         try
         {
            var resolved = ResolveTypesByName( "SdwText", "Sdw.UI.StringTableTextConnector" );
            resolved.TryGetValue( "SdwText", out System.Type sdwTextType );
            resolved.TryGetValue( "Sdw.UI.StringTableTextConnector", out System.Type connectorType );

            _harmony = new HarmonyLib.Harmony( HarmonyId );

            PatchSetText( sdwTextType );
            PatchUpdateText( sdwTextType );
            PatchTextPropertySetter( sdwTextType );
            PatchConnectorOnEnable( connectorType );
            PatchSetFontSetting( sdwTextType );

            PatchCharacterTables( sdwTextType );

            PatchGetTextWidth( sdwTextType );

            _applied = true;
         }
         catch( System.Exception e )
         {
            XuaLogger.AutoTranslator.Error( e, "SdwTextCustomHook: Failed to apply hooks." );
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

      private static void PatchSetText( System.Type sdwTextType )
      {
         System.Reflection.MethodInfo target = HarmonyLib.AccessTools.Method(
            sdwTextType,
            "SetText",
            new System.Type[] { typeof( string ) } );

         if( target == null )
         {
            XuaLogger.AutoTranslator.Warn( "SdwTextCustomHook: SdwText.SetText(string) could not be resolved via reflection." );
            return;
         }

         _setTextMethod = target;

         System.Reflection.MethodInfo prefix = typeof( SdwTextCustomHook ).GetMethod(
            nameof( MarkAsSdwTextInstance_Prefix ),
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic );

         System.Reflection.MethodInfo postfix = typeof( SdwTextCustomHook ).GetMethod(
            nameof( SetText_Postfix ),
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic );

         _harmony.Patch( target, prefix: new HarmonyLib.HarmonyMethod( prefix ), postfix: new HarmonyLib.HarmonyMethod( postfix ) );
      }

      private static void PatchUpdateText( System.Type sdwTextType )
      {
         System.Reflection.MethodInfo target = HarmonyLib.AccessTools.Method(
            sdwTextType,
            "UpdateText",
            new System.Type[] { typeof( string ) } );

         if( target == null )
         {
            XuaLogger.AutoTranslator.Warn( "SdwTextCustomHook: SdwText.UpdateText(string) could not be resolved via reflection." );
            return;
         }

         _updateTextMethod = target;

         System.Reflection.MethodInfo prefix = typeof( SdwTextCustomHook ).GetMethod(
            nameof( MarkAsSdwTextInstance_Prefix ),
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic );

         System.Reflection.MethodInfo postfix = typeof( SdwTextCustomHook ).GetMethod(
            nameof( UpdateText_Postfix ),
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic );

         _harmony.Patch( target, prefix: new HarmonyLib.HarmonyMethod( prefix ), postfix: new HarmonyLib.HarmonyMethod( postfix ) );
      }

      private static void PatchTextPropertySetter( System.Type sdwTextType )
      {
         System.Reflection.PropertyInfo textProperty = HarmonyLib.AccessTools.Property( sdwTextType, "text" );
         System.Reflection.MethodInfo target = textProperty?.GetSetMethod();

         if( target == null )
         {
            XuaLogger.AutoTranslator.Warn( "SdwTextCustomHook: SdwText.text (set_text) could not be resolved via reflection." );
            return;
         }

         System.Reflection.MethodInfo prefix = typeof( SdwTextCustomHook ).GetMethod(
            nameof( MarkAsSdwTextInstance_Prefix ),
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic );

         System.Reflection.MethodInfo postfix = typeof( SdwTextCustomHook ).GetMethod(
            nameof( TextPropertySetter_Postfix ),
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic );

         _harmony.Patch( target, prefix: new HarmonyLib.HarmonyMethod( prefix ), postfix: new HarmonyLib.HarmonyMethod( postfix ) );
      }

      private static void PatchConnectorOnEnable( System.Type connectorType )
      {
         System.Reflection.MethodInfo target = HarmonyLib.AccessTools.Method(
            connectorType,
            "OnEnable",
            System.Type.EmptyTypes );

         if( target == null )
         {
            XuaLogger.AutoTranslator.Warn( "SdwTextCustomHook: Sdw.UI.StringTableTextConnector.OnEnable() could not be resolved via reflection." );
            return;
         }

         System.Reflection.MethodInfo postfix = typeof( SdwTextCustomHook ).GetMethod(
            nameof( ConnectorOnEnable_Postfix ),
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic );

         _harmony.Patch( target, postfix: new HarmonyLib.HarmonyMethod( postfix ) );
      }

      private static void PatchSetFontSetting( System.Type sdwTextType )
      {
         System.Reflection.MethodInfo target = HarmonyLib.AccessTools.Method(
            sdwTextType,
            "SetFontSetting" );

         if( target == null )
         {
            XuaLogger.AutoTranslator.Warn( "SdwTextCustomHook: SdwText.SetFontSetting(Language) could not be resolved via reflection." );
            return;
         }

         System.Reflection.MethodInfo postfix = typeof( SdwTextCustomHook ).GetMethod(
            nameof( SetFontSetting_Postfix ),
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic );

         _harmony.Patch( target, postfix: new HarmonyLib.HarmonyMethod( postfix ) );

         XuaLogger.AutoTranslator.Info( "SdwTextCustomHook: patched " + target.DeclaringType?.FullName + "." + target + " (FONT-REASSERT fix)." );
      }

      private static void SetFontSetting_Postfix( object __instance )
      {
         if( __instance == null || AutoTranslationPlugin.Current == null )
         {
            return;
         }

         var info = __instance.GetOrCreateTextTranslationInfo();

         XuaLogger.AutoTranslator.Debug(
            "SdwTextCustomHook.SetFontSetting_Postfix: fired for '" + __instance.GetUnityType()?.FullName
            + "', info=" + ( info == null ? "null" : "IsTranslated=" + info.IsTranslated ) + "." );

         if( info != null && info.IsTranslated )
         {
            info.ChangeFont( __instance );
         }

         StoryTextPreTranslateHook.ReapplyStoryFontSize( __instance );
      }

      private static void SetText_Postfix( object __instance )
      {
         RememberGameEntryPoint( __instance, "SetText" );
         Dispatch( __instance, forceStabilizationReset: false );
      }

      private static void UpdateText_Postfix( object __instance )
      {
         RememberGameEntryPoint( __instance, "UpdateText" );
         Dispatch( __instance, forceStabilizationReset: false );
      }

      private static void TextPropertySetter_Postfix( object __instance, string value )
      {
         Dispatch( __instance, forceStabilizationReset: false );
      }

      private static void ConnectorOnEnable_Postfix( object __instance )
      {
         if( __instance == null )
         {
            return;
         }

         System.Reflection.PropertyInfo cachedTargetProperty = __instance.GetType().GetProperty(
            "CachedTarget",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public );

         if( cachedTargetProperty == null )
         {
            XuaLogger.AutoTranslator.Warn( "SdwTextCustomHook: CachedTarget property not found on StringTableTextConnector." );
            return;
         }

         object textInstance;
         try
         {
            textInstance = cachedTargetProperty.GetValue( __instance, null );
         }
         catch( System.Exception e )
         {
            XuaLogger.AutoTranslator.Warn( e, "SdwTextCustomHook: Failed to read CachedTarget via reflection." );
            return;
         }

         Dispatch( textInstance, forceStabilizationReset: true );
      }

      private static void Dispatch( object instance, bool forceStabilizationReset )
      {
         if( instance == null )
         {
            return;
         }

         if( AutoTranslationPlugin.Current == null )
         {
            return;
         }

         if( StoryTextPreTranslateHook.IsPreTranslatedInstance( instance ) )
         {
            return;
         }

         if( StoryTextPreTranslateHook.IsSuppressingSelfWrite )
         {
            return;
         }

         if( !_characterTablesPatched )
         {
            PatchCharacterTables( instance.GetType() );
         }

         if( forceStabilizationReset )
         {
            var info = instance.GetOrCreateTextTranslationInfo();
            if( info != null )
            {
               info.IsStabilizingText = false;
            }
         }

         AutoTranslationPlugin.Current.Hook_TextChanged( instance, false );
      }

      private static void PatchCharacterTables( System.Type sdwTextType )
      {
         if( _characterTablesPatched || sdwTextType == null )
         {
            return;
         }

         try
         {
            bool ok = ExtendCharacterField( sdwTextType, "LatinCharacters", BuildCyrillicLetters() );

            ExtendCharacterField( sdwTextType, "NonLineStartingCharacters", new[] { '»', '—', '…', ')' } );
            ExtendCharacterField( sdwTextType, "NonLineEndingCharacters", new[] { '«', '(' } );

            if( ok )
            {
               _characterTablesPatched = true;
            }
         }
         catch( System.Exception e )
         {
            XuaLogger.AutoTranslator.Error( e, "SdwTextCustomHook: Failed to extend SdwText character tables for Cyrillic support." );
         }
      }

      private static char[] BuildCyrillicLetters()
      {
         var chars = new System.Collections.Generic.List<char>( 66 );
         for( char c = '\u0410'; c <= '\u044F'; c++ )
         {
            chars.Add( c );
         }
         chars.Add( '\u0401' );
         chars.Add( '\u0451' );
         return chars.ToArray();
      }

      private static readonly System.Text.RegularExpressions.Regex SizeOpenTagRegex =
         new System.Text.RegularExpressions.Regex( "<size=[^>]*>", AutoTranslationPlugin.RegexCompiledSupportedFlag );

      private static void PatchGetTextWidth( System.Type sdwTextType )
      {
         System.Reflection.MethodInfo target = HarmonyLib.AccessTools.Method(
            sdwTextType,
            "GetTextWidth",
            new System.Type[] { typeof( string ) } );

         if( target == null )
         {
            XuaLogger.AutoTranslator.Warn( "SdwTextCustomHook: SdwText.GetTextWidth(string) could not be resolved via reflection." );
            return;
         }

         System.Reflection.MethodInfo prefix = typeof( SdwTextCustomHook ).GetMethod(
            nameof( GetTextWidth_Prefix ),
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic );

         _harmony.Patch( target, prefix: new HarmonyLib.HarmonyMethod( prefix ) );

         XuaLogger.AutoTranslator.Info( "SdwTextCustomHook: patched " + target.DeclaringType?.FullName + "." + target + " (SIZE-TAG WIDTH fix)." );
      }

      private static bool GetTextWidth_Prefix( ref string message )
      {
         if( !string.IsNullOrEmpty( message ) && message.IndexOf( "<size=", System.StringComparison.OrdinalIgnoreCase ) >= 0 )
         {
            message = SizeOpenTagRegex.Replace( message, string.Empty );
         }

         return true;
      }

      private static bool ExtendCharacterField( System.Type sdwTextType, string fieldName, char[] additions )
      {
         System.Reflection.PropertyInfo property = HarmonyLib.AccessTools.Property( sdwTextType, fieldName );
         if( property == null )
         {
            XuaLogger.AutoTranslator.Warn( "SdwTextCustomHook: SdwText." + fieldName + " could not be resolved via reflection." );
            return false;
         }

         object rawValue = property.GetValue( null, null );

         char[] existing = null;
         if( rawValue is System.Collections.Generic.IList<char> il2cppList )
         {
            existing = new char[ il2cppList.Count ];
            for( int i = 0; i < il2cppList.Count; i++ )
            {
               existing[ i ] = il2cppList[ i ];
            }
         }

         if( existing == null || existing.Length == 0 )
         {
            XuaLogger.AutoTranslator.Warn( "SdwTextCustomHook: SdwText." + fieldName + " is not yet initialized; will retry once a translation is dispatched." );
            return false;
         }

         var merged = new System.Collections.Generic.List<char>( existing );
         bool changed = false;
         foreach( char c in additions )
         {
            if( !merged.Contains( c ) )
            {
               merged.Add( c );
               changed = true;
            }
         }

         if( !changed )
         {
            return true;
         }

         char[] mergedArray = merged.ToArray();

         property.SetValue( null, new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<char>( mergedArray ), null );

         XuaLogger.AutoTranslator.Info( "SdwTextCustomHook: Extended SdwText." + fieldName + " with " + additions.Length + " character(s); table now has " + mergedArray.Length + " entries." );
         return true;
      }
   }
}
