using System;
using XUnity.AutoTranslator.Plugin.Core.Configuration;
using XUnity.AutoTranslator.Plugin.Core.Extensions;
using XUnity.Common.Constants;
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

         try
         {
            var marker = __instance.GetOrCreateExtensionData<SdwTextInstanceMarker>();
            marker.InstanceId = GetUnityInstanceId( __instance );
         }
         catch( System.Exception e )
         {
            LogHookError( "MarkAsSdwTextInstance", e );
         }
         return true;
      }

      public static bool IsKnownSdwTextInstance( object instance )
      {
         if( instance == null )
         {
            return false;
         }

         var marker = instance.GetExtensionData<SdwTextInstanceMarker>();
         if( marker == null )
         {
            return false;
         }

         var currentId = GetUnityInstanceId( instance );
         if( marker.InstanceId != currentId )
         {
            return false;
         }

         return !IsForceBypassedPathCached( instance, currentId );
      }

      private static int _bypassCacheFrame = -1;
      private static readonly System.Collections.Generic.Dictionary<int, bool> _bypassCache = new System.Collections.Generic.Dictionary<int, bool>();

      private static bool IsForceBypassedPathCached( object instance, int instanceId )
      {
         var groups = Settings.ForceBypassedSdwTextPaths;
         if( groups == null || groups.Length == 0 )
         {
            return false;
         }

         int frame = UnityEngine.Time.frameCount;
         if( frame != _bypassCacheFrame )
         {
            _bypassCacheFrame = frame;
            _bypassCache.Clear();
         }

         if( instanceId != 0 && _bypassCache.TryGetValue( instanceId, out var cached ) )
         {
            return cached;
         }

         var result = IsForceBypassedPath( instance );
         if( instanceId != 0 )
         {
            _bypassCache[ instanceId ] = result;
         }
         return result;
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

            return EqualsIgnoringWhitespace( current, info.TranslatedText );
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

            return EqualsIgnoringWhitespace( text, info.TranslatedText );
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

            if( Settings.EnableSdwDiagnostics )
            {
               XuaLogger.AutoTranslator.Debug( $"[SDWPATTERN] '{text.Replace( '\n', ' ' )}' -> '{translated.Replace( '\n', ' ' )}'  via {patternSource}  at '{ui.GetPath()}'" );
            }

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
         if( !Settings.EnableSdwDiagnostics )
         {
            return;
         }

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

      private const byte UnityTypeOther = 0;
      private const byte UnityTypeSdwText = 1;
      private const byte UnityTypePlainUiText = 2;

      private static readonly System.Collections.Generic.Dictionary<System.IntPtr, byte> _unityTypeKinds =
         new System.Collections.Generic.Dictionary<System.IntPtr, byte>();

      private static byte GetUnityTypeKind( object ui )
      {
         var type = ui.GetUnityType();
         if( type == null )
         {
            return UnityTypeOther;
         }

         var key = type.Pointer;
         if( !_unityTypeKinds.TryGetValue( key, out var kind ) )
         {
            var name = type.FullName;
            if( name != null && ( name == "SdwText" || name.EndsWith( ".SdwText", StringComparison.Ordinal ) ) )
            {
               kind = UnityTypeSdwText;
            }
            else if( name == "UnityEngine.UI.Text" )
            {
               kind = UnityTypePlainUiText;
            }
            else
            {
               kind = UnityTypeOther;
            }
            _unityTypeKinds[ key ] = kind;
         }
         return kind;
      }

      private static bool IsPlainUiTextComponent( object ui )
      {
         return GetUnityTypeKind( ui ) == UnityTypePlainUiText;
      }

      private static bool IsSdwTextComponent( object ui )
      {
         if( GetUnityTypeKind( ui ) == UnityTypeSdwText )
         {
            return true;
         }

         return IsKnownSdwTextInstance( ui );
      }

      private static bool EqualsIgnoringWhitespace( string a, string b )
      {
         if( ReferenceEquals( a, b ) ) return true;
         if( a == null || b == null ) return false;

         int i = 0, j = 0;
         while( true )
         {
            while( i < a.Length && char.IsWhiteSpace( a[ i ] ) ) i++;
            while( j < b.Length && char.IsWhiteSpace( b[ j ] ) ) j++;

            if( i >= a.Length || j >= b.Length )
            {
               return i >= a.Length && j >= b.Length;
            }

            if( a[ i ] != b[ j ] ) return false;
            i++;
            j++;
         }
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

            _sdwTextClrType = sdwTextType;
            PatchSetText( sdwTextType );
            PatchUpdateText( sdwTextType );
            PatchTextPropertySetter( sdwTextType );
            PatchConnectorOnEnable( connectorType );
            PatchSetFontSetting( sdwTextType );

            PatchCharacterTables( sdwTextType );

            PatchGetTextWidth( sdwTextType );
            PatchGetFormatedText( sdwTextType );

            _applied = true;
         }
         catch( System.Exception e )
         {
            XuaLogger.AutoTranslator.Error( e, "SdwTextCustomHook: Failed to apply hooks." );
         }
      }

      private static System.Collections.Generic.Dictionary<string, System.Type> ResolveTypesByName( params string[] simpleNames )
      {
         return XUnity.AutoTranslator.Plugin.Core.Utilities.TypeLookup.ResolveTypesByName( simpleNames );
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

         System.Reflection.MethodInfo prefix = typeof( SdwTextCustomHook ).GetMethod(
            nameof( MarkAsSdwTextInstance_Prefix ),
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic );

         System.Reflection.MethodInfo postfix = typeof( SdwTextCustomHook ).GetMethod(
            nameof( UpdateText_Postfix ),
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic );

         _updateTextMethod = target;
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

      private static void LogHookError( string name, System.Exception e )
      {
         XuaLogger.AutoTranslator.Warn( e, "SdwTextCustomHook: " + name + " failed." );
      }

      private static void SetFontSetting_Postfix( object __instance )
      {
         try
         {
            using( XUnity.AutoTranslator.Plugin.Core.Utilities.HitchProfiler.Measure( "SetFontSetting" ) )
            {
               SetFontSettingCore( __instance );
            }
         }
         catch( System.Exception e )
         {
            LogHookError( "SetFontSetting", e );
         }
      }

      private static void SetFontSettingCore( object __instance )
      {
         if( __instance == null || AutoTranslationPlugin.Current == null )
         {
            return;
         }

         MenuCharacterMessageBoxFontFixHook.EnforceMessageBoxAlignment( __instance );

         var info = __instance.GetOrCreateTextTranslationInfo();

         if( Settings.EnableFontDiagnostics )
         {
            XuaLogger.AutoTranslator.Debug(
               "SdwTextCustomHook.SetFontSetting_Postfix: fired for '" + __instance.GetUnityType()?.FullName
               + "', info=" + ( info == null ? "null" : "IsTranslated=" + info.IsTranslated ) + "." );
         }

         if( info != null && info.IsTranslated )
         {
            info.ChangeFont( __instance );
         }

         StoryTextPreTranslateHook.ReapplyStoryFontSize( __instance );

         Fonts.SdwLineSpacingPolicy.Apply( __instance, info );

         ReassertShrunkFont( __instance );
      }

      private static void SetText_Postfix( object __instance )
      {
         try
         {
            MenuCharacterMessageBoxFontFixHook.EnforceMessageBoxAlignment( __instance );
            RememberGameEntryPoint( __instance, "SetText" );
         }
         catch( System.Exception e )
         {
            LogHookError( "SetText", e );
         }
         Dispatch( __instance, forceStabilizationReset: false );
      }

      private static void UpdateText_Postfix( object __instance )
      {
         try
         {
            MenuCharacterMessageBoxFontFixHook.EnforceMessageBoxAlignment( __instance );
            RememberGameEntryPoint( __instance, "UpdateText" );
         }
         catch( System.Exception e )
         {
            LogHookError( "UpdateText", e );
         }
         Dispatch( __instance, forceStabilizationReset: false );
      }

      private static void TextPropertySetter_Postfix( object __instance, string value )
      {
         try
         {
            MenuCharacterMessageBoxFontFixHook.EnforceMessageBoxAlignment( __instance );
         }
         catch( System.Exception e )
         {
            LogHookError( "TextPropertySetter", e );
         }
         Dispatch( __instance, forceStabilizationReset: false );
      }

      private static readonly System.Collections.Generic.Dictionary<System.Type, System.Reflection.PropertyInfo> _cachedTargetProperties =
         new System.Collections.Generic.Dictionary<System.Type, System.Reflection.PropertyInfo>();

      private static void ConnectorOnEnable_Postfix( object __instance )
      {
         using( XUnity.AutoTranslator.Plugin.Core.Utilities.HitchProfiler.Measure( "Hook.ConnectorOnEnable" ) )
         {
            ConnectorOnEnable_PostfixCore( __instance );
         }
      }

      private static void ConnectorOnEnable_PostfixCore( object __instance )
      {
         if( __instance == null )
         {
            return;
         }

         var connectorType = __instance.GetType();
         if( !_cachedTargetProperties.TryGetValue( connectorType, out var cachedTargetProperty ) )
         {
            cachedTargetProperty = connectorType.GetProperty(
               "CachedTarget",
               System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public );
            _cachedTargetProperties[ connectorType ] = cachedTargetProperty;
         }

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
         try
         {
            using( XUnity.AutoTranslator.Plugin.Core.Utilities.HitchProfiler.Measure( "Hook.SdwDispatch" ) )
            {
               DispatchCore( instance, forceStabilizationReset );
            }
         }
         catch( System.Exception e )
         {
            LogHookError( "Dispatch", e );
         }
      }

      private static void DispatchCore( object instance, bool forceStabilizationReset )
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

         if( !_characterTablesPatched && !_characterTablesGaveUp )
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

      private const int MaxCharacterTableAttempts = 10;
      private const int CharacterTableRetryDelayMs = 3000;
      private static bool _characterTablesGaveUp;
      private static int _characterTableAttempts;
      private static int _characterTableLastAttemptTick;

      private static void PatchCharacterTables( System.Type sdwTextType )
      {
         if( _characterTablesPatched || _characterTablesGaveUp || sdwTextType == null )
         {
            return;
         }

         var now = System.Environment.TickCount;
         if( _characterTableAttempts > 0 && unchecked( now - _characterTableLastAttemptTick ) < CharacterTableRetryDelayMs )
         {
            return;
         }
         _characterTableLastAttemptTick = now;
         _characterTableAttempts++;

         try
         {
            bool ok = ExtendCharacterField( sdwTextType, "LatinCharacters", BuildCyrillicLetters(), IsLatinTable );
            ok &= ExtendCharacterField( sdwTextType, "NonLineStartingCharacters", new[] { '»', '—', '…', ')' }, IsNonLineStartingTable );
            ok &= ExtendCharacterField( sdwTextType, "NonLineEndingCharacters", new[] { '«', '(' }, IsNonLineEndingTable );

            if( ok )
            {
               _characterTablesPatched = true;
            }
            else if( _characterTableAttempts >= MaxCharacterTableAttempts )
            {
               _characterTablesGaveUp = true;
               XuaLogger.AutoTranslator.Warn( "SdwTextCustomHook: SdwText character tables were not ready after " + MaxCharacterTableAttempts + " attempts; giving up on the Cyrillic table extension." );
            }
         }
         catch( System.Exception e )
         {
            _characterTablesGaveUp = true;
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

         chars.Add( '-' );
         chars.Add( '\u2011' );
         return chars.ToArray();
      }

      private static bool IsLatinTable( char[] t )
      {
         return System.Array.IndexOf( t, 'a' ) >= 0 && System.Array.IndexOf( t, 'Z' ) >= 0 && System.Array.IndexOf( t, '5' ) >= 0;
      }

      private static bool IsNonLineStartingTable( char[] t )
      {
         return System.Array.IndexOf( t, '\u3001' ) >= 0 && System.Array.IndexOf( t, ',' ) >= 0;
      }

      private static bool IsNonLineEndingTable( char[] t )
      {
         return System.Array.IndexOf( t, '\uFF08' ) >= 0 && System.Array.IndexOf( t, '(' ) >= 0;
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

      private static readonly string[] NoGameWrapPathSuffixes = new string[ 0 ];

      private sealed class LabelCacheEntry
      {
         public bool Match;
         public int Frame;
      }

      private static readonly System.Collections.Generic.Dictionary<int, LabelCacheEntry> _noGameWrapCache = new System.Collections.Generic.Dictionary<int, LabelCacheEntry>();

      internal static bool IsNoWrapLabel( object ui )
      {
         if( NoGameWrapPathSuffixes.Length == 0 )
         {
            return false;
         }

         var component = ui as UnityEngine.Component;
         if( component == null )
         {
            return false;
         }

         int id = component.GetInstanceID();
         int frame = UnityEngine.Time.frameCount;
         if( _noGameWrapCache.TryGetValue( id, out var entry ) && ( entry.Match || frame - entry.Frame < 30 ) )
         {
            return entry.Match;
         }

         bool match = false;
         var path = component.GetPath();
         if( path != null )
         {
            foreach( var suffix in NoGameWrapPathSuffixes )
            {
               if( path.EndsWith( suffix, StringComparison.Ordinal ) )
               {
                  match = true;
                  break;
               }
            }
         }

         if( _noGameWrapCache.Count >= 4096 )
         {
            _noGameWrapCache.Clear();
         }
         _noGameWrapCache[ id ] = new LabelCacheEntry { Match = match, Frame = frame };

         return match;
      }

      private static void PatchGetFormatedText( System.Type sdwTextType )
      {
         System.Reflection.MethodInfo target = HarmonyLib.AccessTools.Method( sdwTextType, "GetFormatedText", new System.Type[] { typeof( string ) } );
         if( target == null )
         {
            XuaLogger.AutoTranslator.Warn( "SdwTextCustomHook: SdwText.GetFormatedText(string) could not be resolved via reflection." );
            return;
         }

         System.Reflection.MethodInfo prefix = typeof( SdwTextCustomHook ).GetMethod(
            nameof( GetFormatedText_Prefix ),
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic );

         System.Reflection.MethodInfo postfix = typeof( SdwTextCustomHook ).GetMethod(
            nameof( GetFormatedText_Postfix ),
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic );

         _harmony.Patch( target, prefix: new HarmonyLib.HarmonyMethod( prefix ), postfix: new HarmonyLib.HarmonyMethod( postfix ) );
         XuaLogger.AutoTranslator.Info( "SdwTextCustomHook: patched " + target.DeclaringType?.FullName + "." + target + " (WRAP-GUARD fix)." );
      }

      private static bool HasCyrillic( string text )
      {
         for( int i = 0; i < text.Length; i++ )
         {
            if( text[ i ] >= '\u0400' && text[ i ] <= '\u04FF' )
            {
               return true;
            }
         }
         return false;
      }

      private static float GetRectWidth( UnityEngine.Component component )
      {
         var rt = component.GetComponent<UnityEngine.RectTransform>();
         if( rt == null )
         {
            return float.MaxValue;
         }
         return rt.rect.width;
      }

      private static bool GetFormatedText_Prefix( object __instance, ref string msg, ref string __result )
      {
         using( XUnity.AutoTranslator.Plugin.Core.Utilities.HitchProfiler.Measure( "Hook.GetFormatedText" ) )
         {
            return GetFormatedText_PrefixCore( __instance, ref msg, ref __result );
         }
      }

      private static bool GetFormatedText_PrefixCore( object __instance, ref string msg, ref string __result )
      {
         try
         {
            if( __instance != null && !_suppressWrapMarker )
            {
               var wrapComponent = __instance as UnityEngine.Component;
               if( wrapComponent != null )
               {
                  if( _selfWrapIds.Count >= 16384 ) _selfWrapIds.Clear();
                  _selfWrapIds.Add( wrapComponent.GetInstanceID() );
               }
            }

            if( string.IsNullOrEmpty( msg ) || !HasCyrillic( msg ) )
            {
               return true;
            }

            var trimmed = msg.Trim( '\n', '\r' );
            if( trimmed.Length > 0 )
            {
               msg = trimmed;
            }

            var component = __instance as UnityEngine.Component;
            if( component == null )
            {
               return true;
            }

            bool noWrapLabel = IsNoWrapLabel( __instance );
            if( noWrapLabel || GetRectWidth( component ) <= 1f )
            {
               UnityTypes.Text_Properties.HorizontalOverflow?.Set( __instance, 1 );
               __result = msg;
               if( !noWrapLabel )
               {
                  ScheduleRewrap( component, msg, 1f );
               }
               return false;
            }

            return true;
         }
         catch( System.Exception )
         {
            return true;
         }
      }

      private static readonly System.Collections.Generic.HashSet<int> _selfWrapIds = new System.Collections.Generic.HashSet<int>();

      private static bool IsSelfWrapping( object ui )
      {
         var c = ui as UnityEngine.Component;
         return c != null && _selfWrapIds.Contains( c.GetInstanceID() );
      }

      private static readonly System.Text.RegularExpressions.Regex ReflowTagRegex =
         new System.Text.RegularExpressions.Regex( @"<[^<>]{1,200}>", System.Text.RegularExpressions.RegexOptions.CultureInvariant );

      private static double ReflowCharWidth( char c )
      {
         if( c == ' ' ) return 0.4;
         if( c >= '\u2E80' || ( c >= '\uFF00' && c <= '\uFFEF' ) ) return 2.0;
         if( c >= '\u0400' && c <= '\u04FF' ) return 1.1;
         return 1.0;
      }

      private static double ReflowWidth( string s )
      {
         if( string.IsNullOrEmpty( s ) ) return 0;
         if( s.IndexOf( '<' ) >= 0 ) s = ReflowTagRegex.Replace( s, string.Empty );
         double w = 0;
         foreach( var ch in s ) w += ReflowCharWidth( ch );
         return w;
      }

      internal static string ReflowLikeOriginal( object ui, string translated, string original )
      {
         try
         {
            var memoized = ApplyWrapMemo( ui, translated );
            if( memoized != null )
            {
               return memoized;
            }

            if( ui == null || string.IsNullOrEmpty( translated ) || string.IsNullOrEmpty( original ) || translated.IndexOf( '\n' ) >= 0 )
            {
               return translated;
            }

            var originalTrimmed = original.Trim( '\n', '\r' );
            if( originalTrimmed.IndexOf( '\n' ) < 0 || !IsSdwTextComponent( ui ) )
            {
               return translated;
            }

            if( IsSelfWrapping( ui ) )
            {
               return translated;
            }

            var originalLines = originalTrimmed.Split( '\n' );

            var words = translated.Split( new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries );
            if( words.Length < 2 )
            {
               return translated;
            }

            var widths = new double[ words.Length ];
            for( int i = 0; i < words.Length; i++ )
            {
               widths[ i ] = ReflowWidth( words[ i ] );
            }

            int targetLines = Math.Min( originalLines.Length, words.Length );
            double total = 0;
            foreach( var wd in widths ) total += wd;
            double idealLine = ( total + ReflowCharWidth( ' ' ) * ( words.Length - targetLines ) ) / targetLines;

            string best = ReflowBalanced( words, widths, targetLines, idealLine * 1.3 )
               ?? ReflowBalanced( words, widths, targetLines, double.MaxValue );

            if( best != null )
            {
               RememberSoftBreaks( ui, best, targetLines );
            }

            if( best != null && Settings.EnableSdwDiagnostics && DiagAllowed( ui, "reflow" ) )
            {
               XuaLogger.AutoTranslator.Debug( "[REFLOW] path='" + ui.GetPath() + "' originalLines=" + originalLines.Length
                  + " -> \"" + best.Replace( "\n", "\\n" ) + "\"" );
            }

            return best ?? translated;
         }
         catch( System.Exception e )
         {
            XuaLogger.AutoTranslator.Debug( "SdwTextCustomHook.ReflowLikeOriginal failed: " + e.Message );
            return translated;
         }
      }

      private static string ReflowBalanced( string[] words, double[] widths, int lines, double limit, double space = -1 )
      {
         if( space < 0 ) space = ReflowCharWidth( ' ' );
         var parts = SdwWrapPlanner.Balanced( words, widths, lines, limit, space, out _ );
         return parts == null ? null : string.Join( "\n", parts );
      }

      private sealed class WrapMemo
      {
         public string Key;
         public string Source;
         public int[] WordsPerLine;
         public bool Volatile;
      }

      private static readonly System.Collections.Generic.Dictionary<int, WrapMemo> _wrapMemos = new System.Collections.Generic.Dictionary<int, WrapMemo>();

      private static string MemoKey( string text )
      {
         var sb = new System.Text.StringBuilder( text.Length );
         foreach( var ch in text )
         {
            sb.Append( char.IsDigit( ch ) ? '0' : ( ch == '\n' || ch == '\r' ? ' ' : ch ) );
         }
         return sb.ToString();
      }

      private static string ApplyWrapMemo( object ui, string text )
      {
         try
         {
            var component = ui as UnityEngine.Component;
            if( component == null || string.IsNullOrEmpty( text ) || _wrapMemos.Count == 0
               || !_wrapMemos.TryGetValue( component.GetInstanceID(), out var memo ) )
            {
               return null;
            }

            if( memo.Volatile )
            {
               return null;
            }

            if( MemoKey( text ) != memo.Key )
            {
               _wrapMemos.Remove( component.GetInstanceID() );
               return null;
            }

            if( text.Replace( '\n', ' ' ) != memo.Source.Replace( '\n', ' ' ) )
            {
               memo.Volatile = true;
               return null;
            }

            var words = text.Replace( '\r', ' ' ).Replace( '\n', ' ' ).Split( new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries );
            int total = 0;
            foreach( var n in memo.WordsPerLine ) total += n;
            if( total != words.Length )
            {
               return null;
            }

            var lines = new string[ memo.WordsPerLine.Length ];
            int at = 0;
            for( int i = 0; i < lines.Length; i++ )
            {
               lines[ i ] = string.Join( " ", words, at, memo.WordsPerLine[ i ] );
               at += memo.WordsPerLine[ i ];
            }
            return string.Join( "\n", lines );
         }
         catch( System.Exception )
         {
            return null;
         }
      }

      private static bool IsVolatileLabel( int id )
      {
         return _wrapMemos.TryGetValue( id, out var memo ) && memo.Volatile;
      }

      private static void RememberWrap( int id, string source, string wrapped )
      {
         var lines = wrapped.Split( '\n' );
         var counts = new int[ lines.Length ];
         for( int i = 0; i < lines.Length; i++ )
         {
            counts[ i ] = lines[ i ].Split( new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries ).Length;
         }

         if( _wrapMemos.Count >= 4096 )
         {
            _wrapMemos.Clear();
         }
         _wrapMemos[ id ] = new WrapMemo { Key = MemoKey( source ), Source = source, WordsPerLine = counts };
      }

      private sealed class SoftBreakInfo
      {
         public string Text;
         public int Lines;
      }

      private static readonly System.Collections.Generic.Dictionary<int, SoftBreakInfo> _softBreaks = new System.Collections.Generic.Dictionary<int, SoftBreakInfo>();

      private static void RememberSoftBreaks( object ui, string text, int lines )
      {
         var component = ui as UnityEngine.Component;
         if( component == null || lines < 2 )
         {
            return;
         }

         if( _softBreaks.Count >= 4096 )
         {
            _softBreaks.Clear();
         }
         _softBreaks[ component.GetInstanceID() ] = new SoftBreakInfo { Text = text, Lines = lines };
      }

      private static System.Reflection.MethodInfo _updateTextMethod;
      private sealed class PendingRewrap
      {
         public string Text;
         public float MinWidth;
      }

      private static readonly System.Collections.Generic.Dictionary<int, PendingRewrap> _pendingRewrap = new System.Collections.Generic.Dictionary<int, PendingRewrap>();
      private const int RewrapMaxFrames = 120;

      private static bool _suppressWrapMarker;

      private static void InvokeUpdateText( UnityEngine.Component component, string text )
      {
         _suppressWrapMarker = true;
         try
         {
            _updateTextMethod.Invoke( component, new object[] { text } );
         }
         catch( System.Exception e )
         {
            XuaLogger.AutoTranslator.Debug( "InvokeUpdateText failed: " + e.Message );
         }
         finally
         {
            _suppressWrapMarker = false;
         }
      }

      private static void ScheduleRewrap( UnityEngine.Component component, string msg, float minWidth )
      {
         try
         {
            if( _updateTextMethod == null || string.IsNullOrEmpty( msg ) )
            {
               return;
            }

            int id = component.GetInstanceID();
            bool alreadyRunning = _pendingRewrap.ContainsKey( id );
            _pendingRewrap[ id ] = new PendingRewrap { Text = msg, MinWidth = minWidth };
            if( !alreadyRunning )
            {
               XUnity.AutoTranslator.Plugin.Core.Utilities.CoroutineHelper.Start( RewrapWhenLaidOut( component, id ) );
            }
         }
         catch( System.Exception e )
         {
            XuaLogger.AutoTranslator.Debug( "SdwTextCustomHook.ScheduleRewrap failed: " + e.Message );
         }
      }

      private static System.Collections.IEnumerator RewrapWhenLaidOut( UnityEngine.Component component, int id )
      {
         try
         {
            for( int frame = 0; frame < RewrapMaxFrames; frame++ )
            {
               yield return null;

               if( component.IsDestroyedUnityObject() )
               {
                  yield break;
               }

               if( !_pendingRewrap.TryGetValue( id, out var pending ) )
               {
                  yield break;
               }

               if( GetRectWidth( component ) < pending.MinWidth )
               {
                  continue;
               }

               var current = component.GetText( component.GetTextTranslationInfo() );
               if( EqualsIgnoringWhitespace( current, pending.Text ) )
               {
                  InvokeUpdateText( component, pending.Text );
                  if( Settings.EnableSdwDiagnostics )
                  {
                     XuaLogger.AutoTranslator.Debug( "[REWRAP] path='" + component.GetPath() + "' frame=" + frame
                        + " rectWidth=" + GetRectWidth( component ).ToString( "0.0" ) );
                  }
               }
               yield break;
            }
         }
         finally
         {
            _pendingRewrap.Remove( id );
         }
      }

      private const float OverflowWrapRatioOwnRect = 1.03f;
      private const float OriginalWidthTolerance = 1.1f;
      private const double OriginalEmPerWidthUnit = 0.5;
      private const int LayoutWaitFrames = 180;
      private const float OverflowWrapRatioWrapMode = 1.01f;
      private const float OverflowWrapRatioContainer = 1.03f;
      private const float OverflowWrapMinRoom = 100f;

      private const float FitMinRoom = 40f;
      private const int OverflowWrapMinChars = 25;

      private const int UnbreakableMinChars = 5;

      private static readonly string[] NoOverflowWrapPathParts = new[] { "/AlertArea/Text_Alert" };

      private const float ButtonRoomFactor = 0.92f;
      private const int ButtonWrapMinChars = 12;
      private const double RelaxedMaxLines = 2.0;

      private static readonly System.Collections.Generic.Dictionary<string, float> _diagLastLogged = new System.Collections.Generic.Dictionary<string, float>();

      private static bool DiagAllowed( object ui, string kind )
      {
         try
         {
            var component = ui as UnityEngine.Component;
            if( component == null )
            {
               return true;
            }

            string key = kind + ":" + component.GetInstanceID();
            float now = UnityEngine.Time.realtimeSinceStartup;
            if( _diagLastLogged.TryGetValue( key, out var last ) && now - last < 1f )
            {
               return false;
            }

            if( _diagLastLogged.Count >= 2048 )
            {
               _diagLastLogged.Clear();
            }
            _diagLastLogged[ key ] = now;
            return true;
         }
         catch( System.Exception )
         {
            return true;
         }
      }

      private static bool IsButtonLabelPath( string path )
      {
         if( string.IsNullOrEmpty( path ) )
         {
            return false;
         }

         int last = path.LastIndexOf( '/' );
         if( last <= 0 )
         {
            return false;
         }

         int start = path.LastIndexOf( '/', last - 1 ) + 1;
         var parent = path.Substring( start, last - start );
         return parent.IndexOf( "Button", StringComparison.OrdinalIgnoreCase ) >= 0
            && parent.IndexOf( "Object", StringComparison.OrdinalIgnoreCase ) < 0
            && parent.IndexOf( "Set", StringComparison.OrdinalIgnoreCase ) < 0
            && parent.IndexOf( "Group", StringComparison.OrdinalIgnoreCase ) < 0;
      }

      private static readonly string[][] ShrinkOnlyPathParts = new string[][] { new[] { "Download_SystemTips", "/Text_Title" } };

      private static string _singleLineSource;
      private static string[] _singleLineParts = new string[ 0 ];

      private static bool IsMinPowerPath( string path )
      {
         return path != null && path.IndexOf( "/DetailTable01/Text_Template", StringComparison.OrdinalIgnoreCase ) >= 0;
      }

      private static bool IsFitterRootChild( string path )
      {
         return path != null && path.IndexOf( "/ContentSizeFitterRoot/", StringComparison.OrdinalIgnoreCase ) >= 0;
      }

      private static bool IsShrinkOnlyPath( string path )
      {
         if( path == null )
         {
            return false;
         }

         foreach( var group in ShrinkOnlyPathParts )
         {
            bool all = true;
            foreach( var part in group )
            {
               if( path.IndexOf( part, StringComparison.OrdinalIgnoreCase ) < 0 )
               {
                  all = false;
                  break;
               }
            }
            if( all )
            {
               return true;
            }
         }

         var configured = Settings.SingleLinePathParts ?? string.Empty;
         if( !ReferenceEquals( configured, _singleLineSource ) )
         {
            _singleLineSource = configured;
            _singleLineParts = configured.Split( new[] { ';' }, StringSplitOptions.RemoveEmptyEntries );
         }

         foreach( var part in _singleLineParts )
         {
            var trimmed = part.Trim();
            if( trimmed.Length > 0 && path.IndexOf( trimmed, StringComparison.OrdinalIgnoreCase ) >= 0 )
            {
               return true;
            }
         }
         return false;
      }

      private static readonly System.Collections.Generic.HashSet<string> _loggedCyr = new System.Collections.Generic.HashSet<string>();
      private static readonly System.Text.RegularExpressions.Regex _cyrPathNorm = new System.Text.RegularExpressions.Regex( @"\(Clone\)| \(\d+\)|\d+", System.Text.RegularExpressions.RegexOptions.CultureInvariant );

      internal static void LogCyrillic( object ui, string translated, string original )
      {
         if( !Settings.LogCyrillicTranslations || string.IsNullOrEmpty( translated ) )
         {
            return;
         }

         try
         {
            bool cyr = false;
            foreach( var ch in translated )
            {
               if( ch >= '\u0400' && ch <= '\u04FF' ) { cyr = true; break; }
            }
            if( !cyr || _loggedCyr.Count >= 6000 )
            {
               return;
            }

            string path = ui.GetPath() ?? "?";
            string key = _cyrPathNorm.Replace( path, "#" ) + "|" + System.Text.RegularExpressions.Regex.Replace( translated, @"\d+", "#" );
            if( !_loggedCyr.Add( key ) )
            {
               return;
            }

            XuaLogger.AutoTranslator.Debug( "[CYR] '" + translated.Replace( "\r", string.Empty ).Replace( "\n", "\\n" ) + "'  <-  '"
               + ( original ?? string.Empty ).Replace( "\r", string.Empty ).Replace( "\n", "\\n" ) + "'  at '" + path + "'" );
         }
         catch
         {
         }
      }

      private static string _fixedScaleSource;
      private static System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, float>> _fixedScaleRules = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, float>>();

      private static float GetFixedScale( string path )
      {
         if( path == null )
         {
            return 0f;
         }

         var configured = Settings.LabelFixedScaleRules ?? string.Empty;
         if( !ReferenceEquals( configured, _fixedScaleSource ) )
         {
            _fixedScaleSource = configured;
            var rules = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, float>>();
            foreach( var entry in configured.Split( new[] { ';' }, StringSplitOptions.RemoveEmptyEntries ) )
            {
               int eq = entry.LastIndexOf( '=' );
               if( eq <= 0 )
               {
                  continue;
               }
               var key = entry.Substring( 0, eq ).Trim();
               if( key.Length > 0 && float.TryParse( entry.Substring( eq + 1 ).Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value ) && value > 0f )
               {
                  rules.Add( new System.Collections.Generic.KeyValuePair<string, float>( key, value ) );
               }
            }
            _fixedScaleRules = rules;
         }

         foreach( var rule in _fixedScaleRules )
         {
            if( path.IndexOf( rule.Key, StringComparison.OrdinalIgnoreCase ) >= 0 )
            {
               return rule.Value;
            }
         }
         return 0f;
      }

      private static string _widthRulesSource;
      private static System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, float>> _widthRules = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, float>>();

      private static float GetLabelWidthRule( string path )
      {
         if( path == null )
         {
            return 0f;
         }

         var configured = Settings.LabelWidthRules ?? string.Empty;
         if( !ReferenceEquals( configured, _widthRulesSource ) )
         {
            _widthRulesSource = configured;
            var rules = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, float>>();
            foreach( var entry in configured.Split( new[] { ';' }, StringSplitOptions.RemoveEmptyEntries ) )
            {
               int eq = entry.LastIndexOf( '=' );
               if( eq <= 0 )
               {
                  continue;
               }

               var key = entry.Substring( 0, eq ).Trim();
               if( key.Length > 0 && float.TryParse( entry.Substring( eq + 1 ).Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value ) && value > 0f )
               {
                  rules.Add( new System.Collections.Generic.KeyValuePair<string, float>( key, value ) );
               }
            }
            _widthRules = rules;
         }

         foreach( var rule in _widthRules )
         {
            if( path.IndexOf( rule.Key, StringComparison.OrdinalIgnoreCase ) >= 0 )
            {
               return rule.Value;
            }
         }
         return 0f;
      }

      private static System.Type _sdwTextClrType;
      private static System.Reflection.MethodInfo _getTextWidthMethodInfo;
      private static System.Reflection.MethodInfo _getSpaceWidthMethodInfo;

      private sealed class FitMemo
      {
         public string Text;
         public string Wrapped;
         public float Width;
         public int Shrunk;
      }

      private static readonly System.Collections.Generic.Dictionary<int, FitMemo> _fitMemos = new System.Collections.Generic.Dictionary<int, FitMemo>();

      private static void RememberFit( int id, string text, float width, string wrapped = null )
      {
         if( _fitMemos.Count >= 4096 )
         {
            _fitMemos.Clear();
         }
         int shrunkSize = _shrunkFonts.TryGetValue( id, out var shrunkRecord ) ? shrunkRecord.Shrunk : 0;
         _fitMemos[ id ] = new FitMemo { Text = text, Wrapped = wrapped, Width = width, Shrunk = shrunkSize };
      }

      private static bool IsFitUnchanged( UnityEngine.Component component, int id, string text )
      {
         return _fitMemos.Count > 0
            && _fitMemos.TryGetValue( id, out var memo )
            && ( memo.Text == text || memo.Wrapped == text )
            && !_pendingOverflowWrap.ContainsKey( id )
            && Math.Abs( memo.Width - GetRectWidth( component ) ) < 0.5f
            && ( memo.Shrunk <= 0 || ReadFontSize( component ) == memo.Shrunk );
      }

      private sealed class PendingOverflow
      {
         public string Text;
         public int SoftLines;
         public bool Immediate;
      }

      private static readonly System.Collections.Generic.Dictionary<int, int> _seenFrames = new System.Collections.Generic.Dictionary<int, int>();

      private static bool CanFitImmediately( UnityEngine.Component component, int id )
      {
         int frame = UnityEngine.Time.frameCount;
         bool seenLongAgo = _seenFrames.TryGetValue( id, out var first ) && frame - first >= 3;
         if( !_seenFrames.ContainsKey( id ) )
         {
            if( _seenFrames.Count >= 4096 )
            {
               _seenFrames.Clear();
            }
            _seenFrames[ id ] = frame;
         }

         if( GetRectWidth( component ) < 1f )
         {
            return false;
         }

         var settle = Settings.SettleFramePathParts;
         if( !string.IsNullOrEmpty( settle ) )
         {
            string cpath = component.GetPath();
            foreach( var part in settle.Split( new[] { ';' }, StringSplitOptions.RemoveEmptyEntries ) )
            {
               var t = part.Trim();
               if( t.Length > 0 && cpath != null && cpath.IndexOf( t, StringComparison.OrdinalIgnoreCase ) >= 0 )
               {
                  return false;
               }
            }
         }

         if( seenLongAgo )
         {
            return true;
         }

         return HasContentSizeFitter( component ) && GetContainerWidth( component ) >= OverflowWrapMinRoom;
      }

      private static readonly System.Collections.Generic.Dictionary<int, PendingOverflow> _pendingOverflowWrap = new System.Collections.Generic.Dictionary<int, PendingOverflow>();

      private static object AsSdwText( object ui )
      {
         if( _sdwTextClrType == null || _sdwTextClrType.IsInstanceOfType( ui ) )
         {
            return ui;
         }

         var pointer = ( (Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase)ui ).Pointer;
         return Il2CppUtilities.CreateProxyComponentWithDerivedType( pointer, _sdwTextClrType );
      }

      private static bool HasContentSizeFitter( UnityEngine.Component c )
      {
         try
         {
            foreach( var comp in c.GetComponents<UnityEngine.Component>() )
            {
               if( comp == null )
               {
                  continue;
               }

               var nativeName = comp.GetUnityType()?.FullName;
               if( ( nativeName != null && nativeName.EndsWith( "ContentSizeFitter", StringComparison.Ordinal ) )
                  || comp.GetType().Name == "ContentSizeFitter" )
               {
                  return true;
               }
            }
         }
         catch( System.Exception )
         {
         }
         return false;
      }

      private static System.Type _fitterClrType;
      private static bool _fitterTypeResolved;

      private static int GetHorizontalFitMode( UnityEngine.Component c )
      {
         try
         {
            foreach( var comp in c.GetComponents<UnityEngine.Component>() )
            {
               if( comp == null )
               {
                  continue;
               }

               var nativeName = comp.GetUnityType()?.FullName;
               bool isFitter = ( nativeName != null && nativeName.EndsWith( "ContentSizeFitter", StringComparison.Ordinal ) )
                  || comp.GetType().Name == "ContentSizeFitter";
               if( !isFitter )
               {
                  continue;
               }

               object typed = comp;
               var property = typed.GetType().GetProperty( "horizontalFit" );
               if( property == null )
               {
                  if( !_fitterTypeResolved )
                  {
                     _fitterTypeResolved = true;
                     ResolveTypesByName( "ContentSizeFitter" ).TryGetValue( "ContentSizeFitter", out _fitterClrType );
                  }

                  if( _fitterClrType == null )
                  {
                     return -1;
                  }

                  typed = Il2CppUtilities.CreateProxyComponentWithDerivedType( ( (Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase)comp ).Pointer, _fitterClrType );
                  property = _fitterClrType.GetProperty( "horizontalFit" );
               }

               if( property == null || typed == null )
               {
                  return -1;
               }

               return Convert.ToInt32( property.GetValue( typed, null ) );
            }
         }
         catch( System.Exception )
         {
         }
         return -1;
      }

      private static float GetContainerWidth( UnityEngine.Component c )
      {
         try
         {
            var t = c.transform.parent;
            for( int depth = 0; t != null && depth < 8; depth++, t = t.parent )
            {
               var rt = t.GetComponent<UnityEngine.RectTransform>();
               if( rt == null || HasContentSizeFitter( rt ) )
               {
                  continue;
               }
               float w = rt.rect.width;
               if( w >= OverflowWrapMinRoom )
               {
                  return w;
               }
            }
         }
         catch( System.Exception )
         {
         }
         return 0f;
      }

      private static float GetParentRectWidth( UnityEngine.Component c )
      {
         try
         {
            var parent = c.transform.parent;
            var rt = parent == null ? null : parent.GetComponent<UnityEngine.RectTransform>();
            return rt == null ? 0f : rt.rect.width;
         }
         catch( System.Exception )
         {
            return 0f;
         }
      }

      private static float GetRectHeight( UnityEngine.Component component )
      {
         var rt = component.GetComponent<UnityEngine.RectTransform>();
         return rt == null ? 0f : rt.rect.height;
      }

      private static bool IsHorizontalOverflowMode( object ui )
      {
         try
         {
            var value = UnityTypes.Text_Properties.HorizontalOverflow?.Get( ui );
            return value != null && Convert.ToInt32( value ) == 1;
         }
         catch( System.Exception )
         {
            return false;
         }
      }

      private static bool IsNoOverflowWrapPath( string path )
      {
         if( path == null )
         {
            return false;
         }

         foreach( var part in NoOverflowWrapPathParts )
         {
            if( path.IndexOf( part, StringComparison.OrdinalIgnoreCase ) >= 0 )
            {
               return true;
            }
         }
         return false;
      }

      internal static void ScheduleOverflowWrap( object ui, string translated )
      {
         try
         {
            if( !string.IsNullOrEmpty( translated ) && ui is UnityEngine.Component knownComponent
               && IsFitUnchanged( knownComponent, knownComponent.GetInstanceID(), translated ) )
            {
               return;
            }

            if( ui is UnityEngine.Component staleComponent )
            {
               _fitMemos.Remove( staleComponent.GetInstanceID() );
            }

            if( _shrunkFonts.Count > 0 && ui is UnityEngine.Component shrunkComponent )
            {
               RestoreShrunkFont( ui, shrunkComponent.GetInstanceID() );
            }

            if( ui == null || string.IsNullOrEmpty( translated ) )
            {
               return;
            }

            bool breakableText = translated.IndexOf( ' ' ) >= 0 || translated.IndexOf( '\n' ) >= 0;
            if( translated.Length < ( breakableText ? ButtonWrapMinChars : UnbreakableMinChars ) )
            {
               return;
            }

            if( _sdwTextClrType == null )
            {
               if( Settings.EnableSdwDiagnostics )
               {
                  XuaLogger.AutoTranslator.Debug( "[OVERFLOW-WRAP] skip: SdwText type is not resolved" );
               }
               return;
            }

            var raw = ui as UnityEngine.Component;
            if( raw == null || !IsSdwTextComponent( ui ) )
            {
               return;
            }

            if( StoryTextPreTranslateHook.IsPreTranslatedInstance( ui ) )
            {
               if( Settings.EnableSdwDiagnostics )
               {
                  XuaLogger.AutoTranslator.Debug( "[OVERFLOW-WRAP] skip path='" + raw.GetPath() + "': story text is fitted by StoryTextPreTranslateHook" );
               }
               return;
            }

            if( NoOverflowWrapPathParts.Length > 0 )
            {
               var path = raw.GetPath();
               if( IsNoOverflowWrapPath( path ) )
               {
                  if( Settings.EnableSdwDiagnostics )
                  {
                     XuaLogger.AutoTranslator.Debug( "[OVERFLOW-WRAP] skip path='" + path + "': listed in NoOverflowWrapPathParts" );
                  }
                  return;
               }
            }

            if( translated.Length < OverflowWrapMinChars && !IsButtonLabelPath( raw.GetPath() ) && GetLabelWidthRule( raw.GetPath() ) <= 0f && !IsShrinkOnlyPath( raw.GetPath() ) && !MayOverflowRect( raw, translated ) )
            {
               return;
            }

            int id = raw.GetInstanceID();
            if( IsVolatileLabel( id ) )
            {
               return;
            }

            int softLines = 0;
            if( _softBreaks.TryGetValue( id, out var soft ) && soft.Text == translated )
            {
               softLines = soft.Lines;
            }

            if( Settings.EnableSdwDiagnostics && DiagAllowed( ui, "scheduled" ) )
            {
               XuaLogger.AutoTranslator.Debug( "[OVERFLOW-WRAP] scheduled path='" + raw.GetPath() + "' selfWrapping=" + IsSelfWrapping( ui )
                  + " len=" + translated.Length + " softLines=" + softLines );
            }

            float knownWidth = GetRectWidth( raw );
            if( knownWidth >= 1f && _plannedWraps.Count > 0
               && _plannedWraps.TryGetValue( PlanKey( raw, translated, knownWidth, ui ), out var planned ) )
            {
               UnityTypes.Text_Properties.HorizontalOverflow?.Set( ui, 1 );
               if( _overflowSetByUs.Count >= 4096 )
               {
                  _overflowSetByUs.Clear();
               }
               _overflowSetByUs.Add( id );
               RememberWrap( id, translated, planned.Text );
               RememberFit( id, translated, knownWidth, planned.Text );
               AutoTranslationPlugin.Current?.ReapplyTranslatedText( ui, planned.Text );
               return;
            }

            bool alreadyRunning = _pendingOverflowWrap.ContainsKey( id );
            bool immediate = CanFitImmediately( raw, id );
            _pendingOverflowWrap[ id ] = new PendingOverflow { Text = translated, SoftLines = softLines, Immediate = immediate };
            string hidePath = raw.GetPath();
            bool hideWhileFitting = softLines > 0 || IsSelfWrapping( ui )
               || IsButtonLabelPath( hidePath ) || GetLabelWidthRule( hidePath ) > 0f
               || ( IsShrinkOnlyPath( hidePath ) && !IsFitterRootChild( hidePath ) ) || GetFixedScale( hidePath ) > 0f;
            if( hideWhileFitting && !immediate )
            {
               SetFitHidden( ui, id, true );
            }
            if( !alreadyRunning )
            {
               XUnity.AutoTranslator.Plugin.Core.Utilities.CoroutineHelper.Start( OverflowWrapWhenLaidOut( ui, id ) );
            }
         }
         catch( System.Exception e )
         {
            XuaLogger.AutoTranslator.Debug( "SdwTextCustomHook.ScheduleOverflowWrap failed: " + e.Message );
         }
      }

      private static readonly System.Collections.Generic.HashSet<int> _hiddenForFit = new System.Collections.Generic.HashSet<int>();

      private static UnityEngine.CanvasRenderer FindCanvasRenderer( UnityEngine.Component component )
      {
         try
         {
            var direct = component.GetComponent<UnityEngine.CanvasRenderer>();
            if( direct != null )
            {
               return direct;
            }
         }
         catch( System.Exception )
         {
         }

         try
         {
            var property = component.GetType().CachedProperty( "canvasRenderer" );
            var viaProperty = property?.Get( component ) as UnityEngine.CanvasRenderer;
            if( viaProperty != null )
            {
               return viaProperty;
            }
         }
         catch( System.Exception )
         {
         }

         return null;
      }

      internal static string TryGetPlannedText( object ui, string text )
      {
         try
         {
            if( _plannedWraps.Count == 0 || string.IsNullOrEmpty( text ) )
            {
               return null;
            }

            var raw = ui as UnityEngine.Component;
            if( raw == null || !IsSdwTextComponent( ui ) )
            {
               return null;
            }

            float width = GetRectWidth( raw );
            if( width < 1f )
            {
               return null;
            }

            int id = raw.GetInstanceID();
            if( _shrunkFonts.Count > 0 )
            {
               RestoreShrunkFont( ui, id );
            }

            if( !_plannedWraps.TryGetValue( PlanKey( raw, text, width, ui ), out var planned ) )
            {
               return null;
            }

            UnityTypes.Text_Properties.HorizontalOverflow?.Set( ui, 1 );
            if( _overflowSetByUs.Count >= 4096 )
            {
               _overflowSetByUs.Clear();
            }
            _overflowSetByUs.Add( id );
            RememberWrap( id, text, planned.Text );
            RememberFit( id, text, width, planned.Text );
            return planned.Text;
         }
         catch( System.Exception )
         {
            return null;
         }
      }

      private static void SetFitHidden( object ui, int id, bool hide )
      {
         try
         {
            if( !hide && !_hiddenForFit.Contains( id ) )
            {
               return;
            }

            var component = ui as UnityEngine.Component;
            var renderer = component == null ? null : FindCanvasRenderer( component );
            if( hide )
            {
               if( renderer != null )
               {
                  renderer.SetAlpha( 0f );
                  _hiddenForFit.Add( id );
               }
            }
            else
            {
               _hiddenForFit.Remove( id );
               if( renderer != null )
               {
                  renderer.SetAlpha( 1f );
               }
            }
         }
         catch( System.Exception )
         {
            _hiddenForFit.Remove( id );
         }
      }

      private sealed class PlannedWrap
      {
         public string Text;
      }

      private static readonly System.Collections.Generic.Dictionary<string, PlannedWrap> _plannedWraps = new System.Collections.Generic.Dictionary<string, PlannedWrap>();

      private static string PlanKey( UnityEngine.Component component, string text, float width, object ui )
      {
         return component.GetPath() + "|" + (int)Math.Round( width ) + "|" + (int)ReadFontSize( ui ) + "|" + text;
      }

      private static void RememberPlanned( UnityEngine.Component component, string text, float width, object ui, string wrapped )
      {
         if( _plannedWraps.Count >= 2048 )
         {
            _plannedWraps.Clear();
         }
         _plannedWraps[ PlanKey( component, text, width, ui ) ] = new PlannedWrap { Text = wrapped };
      }

      private static System.Collections.IEnumerator OverflowWrapWhenLaidOut( object ui, int id )
      {
         try
         {
            for( int frame = 0; frame < LayoutWaitFrames; frame++ )
            {
               bool firstPassNow = frame == 0 && _pendingOverflowWrap.TryGetValue( id, out var first ) && first.Immediate;
               if( !firstPassNow )
               {
                  yield return null;
               }

               var raw = ui as UnityEngine.Component;
               if( raw == null || ui.IsDestroyedUnityObject() || !_pendingOverflowWrap.TryGetValue( id, out var pending ) )
               {
                  yield break;
               }

               if( frame == 12 )
               {
                  SetFitHidden( ui, id, false );
               }

               if( ( !firstPassNow && frame < ( _hiddenForFit.Contains( id ) ? 1 : 3 ) ) || GetRectWidth( raw ) < 1f )
               {
                  if( frame == LayoutWaitFrames - 1 && Settings.EnableSdwDiagnostics )
                  {
                     XuaLogger.AutoTranslator.Debug( "[OVERFLOW-WRAP] gave up path='" + raw.GetPath() + "': the layout never settled" );
                  }
                  continue;
               }

               var expected = pending.Text;

               var current = raw.GetText( ui.GetTextTranslationInfo() );
               if( !EqualsIgnoringWhitespace( current, expected ) )
               {
                  if( Settings.EnableSdwDiagnostics )
                  {
                     XuaLogger.AutoTranslator.Debug( "[OVERFLOW-WRAP] gave up path='" + raw.GetPath() + "': text changed meanwhile (now \""
                        + ( current ?? "<null>" ).Replace( "\n", "\\n" ) + "\")" );
                  }
                  yield break;
               }

               var typed = AsSdwText( ui );
               var component = typed as UnityEngine.Component;
               if( component == null )
               {
                  yield break;
               }

               float own = GetRectWidth( component );
               bool fitter = HasContentSizeFitter( component );
               int fitMode = fitter ? GetHorizontalFitMode( component ) : 0;

               if( fitter && fitMode < 0 && frame < 3 )
               {
                  continue;
               }

               bool selfWrap = IsSelfWrapping( ui ) && !fitter;

               float room = fitter ? GetContainerWidth( component ) : own;
               if( fitter && room < 200f && own > room )
               {
                  room = own;
               }
               else if( fitter && own >= OverflowWrapMinRoom && own < room )
               {
                  if( fitMode == 0 )
                  {
                     room = own;
                  }
                  else if( fitMode < 0 )
                  {
                     var ownPreferredProperty = typed.GetType().CachedProperty( "preferredWidth" );
                     float ownPreferred = ownPreferredProperty == null ? 0f : (float)ownPreferredProperty.Get( typed );
                     if( Math.Abs( own - ownPreferred ) > 2f )
                     {
                        room = own;
                     }
                  }
               }

               var labelPath = raw.GetPath();
               bool buttonLabel = IsButtonLabelPath( labelPath );
               if( buttonLabel )
               {

                  float buttonWidth = GetParentRectWidth( component );
                  float limit = buttonWidth >= 40f ? Math.Min( room, buttonWidth ) : room;
                  room = limit * ButtonRoomFactor;
               }
               bool relaxHeight = buttonLabel;

               bool overflowMode = IsHorizontalOverflowMode( ui );
               float widthRule = GetLabelWidthRule( labelPath );
               if( widthRule > 0f )
               {
                  float ruleRoom = widthRule <= 3f ? own * widthRule : widthRule;
                  room = Math.Min( room, ruleRoom );
               }
               if( widthRule <= 0f && overflowMode && !selfWrap && !_overflowSetByUs.Contains( id ) )
               {
                  string originalText = ui.GetTextTranslationInfo()?.OriginalText;
                  double originalWidth = ReflowWidth( originalText ) * ReadFontSize( ui ) * OriginalEmPerWidthUnit;
                  if( originalWidth <= 0.0 )
                  {
                     if( Settings.EnableSdwDiagnostics )
                     {
                        XuaLogger.AutoTranslator.Debug( "[OVERFLOW-WRAP] skip path='" + raw.GetPath() + "': no original text to compare with" );
                     }
                     RememberFit( id, expected, own );
                     yield break;
                  }

                  if( originalWidth > own * 1.02 )
                  {
                     room = Math.Max( room, (float)( originalWidth * OriginalWidthTolerance ) );
                  }
               }
               float ratio = fitter ? OverflowWrapRatioContainer : ( selfWrap ? OverflowWrapRatioWrapMode : ( overflowMode ? OverflowWrapRatioOwnRect : OverflowWrapRatioWrapMode ) );
               if( widthRule > 0f )
               {
                  ratio = 1f;
               }

               var preferredProperty = typed.GetType().CachedProperty( "preferredWidth" );
               float preferred = preferredProperty == null ? 0f : (float)preferredProperty.Get( typed );

               float measured = selfWrap ? 0f : MeasureTextWidth( typed, expected );
               if( measured > preferred )
               {
                  preferred = measured;
               }

               bool heightOver = false;
               if( selfWrap && own >= 1f && IsVerticalTruncateMode( ui ) )
               {
                  float rectHeight = GetRectHeight( component );
                  double preferredHeight = ReadFloatProperty( typed, "preferredHeight" );
                  heightOver = rectHeight >= 1f && preferredHeight > rectHeight * 1.02 + 0.5;
               }

               bool wrap = room >= FitMinRoom && ( preferred > room * ratio || heightOver );
               if( Settings.EnableSdwDiagnostics )
               {
                  XuaLogger.AutoTranslator.Debug( "[OVERFLOW-WRAP] path='" + component.GetPath() + "' frame=" + frame + " ownRect=" + own.ToString( "0.0" )
                     + "x" + GetRectHeight( component ).ToString( "0.0" ) + " contentFitter=" + fitter + " overflowMode=" + overflowMode
                     + " room=" + room.ToString( "0.0" ) + " preferred=" + preferred.ToString( "0.0" ) + " selfWrap=" + selfWrap
                     + " heightOver=" + heightOver + " wrap=" + wrap );
               }

               float fixedScale = GetFixedScale( raw.GetPath() );
               if( fixedScale > 0f )
               {
                  UnityTypes.Text_Properties.HorizontalOverflow?.Set( ui, 1 );
                  if( _overflowSetByUs.Count >= 4096 )
                  {
                     _overflowSetByUs.Clear();
                  }
                  _overflowSetByUs.Add( id );
                  if( expected.IndexOf( '\n' ) >= 0 )
                  {
                     expected = expected.Replace( "\r", string.Empty ).Replace( '\n', ' ' );
                     AutoTranslationPlugin.Current?.ReapplyTranslatedText( ui, expected );
                  }
                  if( IsBalloonPath( raw.GetPath() ) && TryGrowBalloon( component, own, preferred ) )
                  {
                  }
                  else if( fixedScale < 1f )
                  {
                     ApplyFontShrink( ui, id, fixedScale );
                  }
                  if( Settings.EnableSdwDiagnostics )
                  {
                     XuaLogger.AutoTranslator.Debug( "[OVERFLOW-WRAP] fixed-scale path='" + raw.GetPath() + "' scale=" + fixedScale.ToString( "0.00" ) + " preferred=" + preferred.ToString( "0.0" ) + " room=" + room.ToString( "0.0" ) );
                  }
                  RememberFit( id, expected, own );
                  yield break;
               }

               if( !wrap )
               {
                  RememberFit( id, expected, own );
                  yield break;
               }

               if( IsFitterRootChild( raw.GetPath() ) )
               {
                  UnityTypes.Text_Properties.HorizontalOverflow?.Set( ui, 1 );
                  if( _overflowSetByUs.Count >= 4096 )
                  {
                     _overflowSetByUs.Clear();
                  }
                  _overflowSetByUs.Add( id );
                  RememberFit( id, expected, own );
                  yield break;
               }

               if( IsShrinkOnlyPath( raw.GetPath() ) && IsBalloonPath( raw.GetPath() ) && TryGrowBalloon( component, own, preferred ) )
               {
                  UnityTypes.Text_Properties.HorizontalOverflow?.Set( ui, 1 );
                  if( _overflowSetByUs.Count >= 4096 )
                  {
                     _overflowSetByUs.Clear();
                  }
                  _overflowSetByUs.Add( id );
                  if( expected.IndexOf( '\n' ) >= 0 )
                  {
                     expected = expected.Replace( "\r", string.Empty ).Replace( '\n', ' ' );
                     AutoTranslationPlugin.Current?.ReapplyTranslatedText( ui, expected );
                  }
                  if( Settings.EnableSdwDiagnostics )
                  {
                     XuaLogger.AutoTranslator.Debug( "[OVERFLOW-WRAP] balloon-grow path='" + raw.GetPath() + "' preferred=" + preferred.ToString( "0.0" ) + " own=" + own.ToString( "0.0" ) );
                  }
                  RememberFit( id, expected, own );
                  yield break;
               }

               if( IsShrinkOnlyPath( raw.GetPath() ) )
               {
                  double fit = Math.Max( room * SdwWrapPlanner.WidthSafety / preferred, 0.42 );
                  if( ( Settings.NameLineWrap || IsMinPowerPath( raw.GetPath() ) ) && fit < 0.7 && expected.IndexOf( ' ' ) >= 0 && !IsBalloonPath( raw.GetPath() ) )
                  {
                     string lined = BuildNameLines( typed, expected.Replace( "\r", string.Empty ).Replace( '\n', ' ' ), room, out double lineScale );
                     if( lined != null && lineScale > fit )
                     {
                        UnityTypes.Text_Properties.HorizontalOverflow?.Set( ui, 1 );
                        UnityTypes.Text_Properties.VerticalOverflow?.Set( ui, 1 );
                        if( _overflowSetByUs.Count >= 4096 )
                        {
                           _overflowSetByUs.Clear();
                        }
                        _overflowSetByUs.Add( id );
                        AutoTranslationPlugin.Current?.ReapplyTranslatedText( ui, lined );
                        RememberWrap( id, expected, lined );
                        double finalScale = Math.Max( lineScale, 0.6 );
                        if( finalScale < 0.99 )
                        {
                           ApplyFontShrink( ui, id, finalScale * 0.98 );
                        }
                        if( Settings.EnableSdwDiagnostics )
                        {
                           XuaLogger.AutoTranslator.Debug( "[OVERFLOW-WRAP] name-lines path='" + raw.GetPath() + "' scale=" + finalScale.ToString( "0.00" ) + " font=" + ReadFontSize( ui ) + " text=" + lined.Replace( "\n", "\\n" ) );
                        }
                        RememberFit( id, expected, own );
                        yield break;
                     }
                  }
                  UnityTypes.Text_Properties.HorizontalOverflow?.Set( ui, 1 );
                  if( _overflowSetByUs.Count >= 4096 )
                  {
                     _overflowSetByUs.Clear();
                  }
                  _overflowSetByUs.Add( id );
                  if( expected.IndexOf( '\n' ) >= 0 )
                  {
                     expected = expected.Replace( "\r", string.Empty ).Replace( '\n', ' ' );
                  }
                  AutoTranslationPlugin.Current?.ReapplyTranslatedText( ui, expected );
                  ApplyFontShrink( ui, id, fit * 0.98 );
                  StartShrinkVerify( ui, id, room, expected );
                  if( fit <= 0.42 + 0.001 )
                  {
                     UnityTypes.Text_Properties.VerticalOverflow?.Set( ui, 1 );
                  }
                  if( Settings.EnableSdwDiagnostics )
                  {
                     XuaLogger.AutoTranslator.Debug( "[OVERFLOW-WRAP] shrink-only path='" + raw.GetPath() + "' scale=" + fit.ToString( "0.00" ) + " font=" + ReadFontSize( ui ) );
                  }
                  RememberFit( id, expected, own );
                  yield break;
               }

               string diag;
               var plan = TryBuildWrapPlan( ui, typed, component, expected, room, fitter, pending.SoftLines, relaxHeight, out diag );

               if( plan == null )
               {

                  bool unbreakable = expected.IndexOf( ' ' ) < 0 && expected.IndexOf( '\n' ) < 0;
                  double floorScale = unbreakable ? SdwWrapPlanner.MinUnbreakableScale : SdwWrapPlanner.MinReadableScale;
                  double fitScale = preferred > room * ratio ? Math.Max( room * SdwWrapPlanner.WidthSafety / preferred, floorScale ) : 1.0;
                  if( fitScale < 1.0 )
                  {
                     ApplyFontShrink( ui, id, fitScale * 0.98 );
                  }

                  AutoTranslationPlugin.Current?.ReapplyTranslatedText( ui, expected );

                  if( heightOver || fitScale <= floorScale + 0.001 )
                  {
                     UnityTypes.Text_Properties.VerticalOverflow?.Set( ui, 1 );
                  }

                  if( Settings.EnableSdwDiagnostics )
                  {
                     XuaLogger.AutoTranslator.Debug( "[OVERFLOW-WRAP] no plan path='" + raw.GetPath() + "' scale=" + fitScale.ToString( "0.00" ) + " " + diag );
                  }
                  RememberFit( id, expected, own );
                  yield break;
               }

               AutoTranslationPlugin.Current?.ReapplyTranslatedText( ui, plan.Text );

               if( plan != null )
               {
                  UnityTypes.Text_Properties.HorizontalOverflow?.Set( ui, 1 );
                  if( _overflowSetByUs.Count >= 4096 )
                  {
                     _overflowSetByUs.Clear();
                  }
                  _overflowSetByUs.Add( id );

                  RememberWrap( id, expected, plan.Text );

                  bool widthOver = plan.MaxWidth > room * SdwWrapPlanner.WidthSafety + 0.5;
                  bool shrink = plan.Scale < SdwWrapPlanner.OkScale || widthOver;
                  if( shrink )
                  {
                     ApplyFontShrink( ui, id, Math.Max( plan.Scale, SdwWrapPlanner.MinReadableScale ) * 0.98 );
                  }
                  else if( plan.Lines == 1 && plan.Scale < SdwWrapPlanner.OkScale )
                  {
                     AlignLeft( ui );
                  }

                  if( plan.HeightIgnored || plan.Scale < SdwWrapPlanner.MinReadableScale )
                  {
                     UnityTypes.Text_Properties.VerticalOverflow?.Set( ui, 1 );
                  }

                  RememberFit( id, expected, own, plan.Text );

                  if( !shrink && !plan.HeightIgnored )
                  {
                     RememberPlanned( raw, expected, own, ui, plan.Text );
                  }

                  if( Settings.EnableSdwDiagnostics )
                  {
                     XuaLogger.AutoTranslator.Debug( "[OVERFLOW-WRAP] applied -> \"" + plan.Text.Replace( "\n", "\\n" ) + "\" lines=" + plan.Lines
                        + " scale=" + plan.Scale.ToString( "0.00" ) + " shrink=" + shrink + " heightIgnored=" + plan.HeightIgnored + " " + diag );
                  }
               }
               yield break;
            }
         }
         finally
         {
            _pendingOverflowWrap.Remove( id );
            SetFitHidden( ui, id, false );
         }
      }

      private sealed class ShrunkFont
      {
         public int Original;
         public int Shrunk;
      }

      private static readonly System.Collections.Generic.Dictionary<int, ShrunkFont> _shrunkFonts = new System.Collections.Generic.Dictionary<int, ShrunkFont>();

      private static readonly System.Collections.Generic.HashSet<int> _overflowSetByUs = new System.Collections.Generic.HashSet<int>();

      private static void AlignLeft( object ui )
      {
         try
         {
            var property = ui.GetType().GetProperty( "alignment", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public );
            if( property == null || !property.CanRead || !property.CanWrite )
            {
               return;
            }

            var current = property.GetValue( ui, null );
            if( current == null )
            {
               return;
            }

            int left = Convert.ToInt32( current ) / 3 * 3;
            object value = property.PropertyType.IsEnum ? Enum.ToObject( property.PropertyType, left ) : (object)left;
            property.SetValue( ui, value, null );
         }
         catch( System.Exception e )
         {
            XuaLogger.AutoTranslator.Debug( "SdwTextCustomHook.AlignLeft failed: " + e.Message );
         }
      }

      private static int ReadFontSize( object ui )
      {
         return (int?)UnityTypes.Text_Properties.FontSize?.Get( ui ) ?? 0;
      }

      private static bool IsBalloonPath( string path )
      {
         return path != null
            && ( path.IndexOf( "/BalloonRoot/", StringComparison.OrdinalIgnoreCase ) >= 0
               || path.IndexOf( "/CommonPopBalloon", StringComparison.OrdinalIgnoreCase ) >= 0
               || path.IndexOf( "/BoostIcon/", StringComparison.OrdinalIgnoreCase ) >= 0
               || path.IndexOf( "/Text_Limit", StringComparison.OrdinalIgnoreCase ) >= 0 );
      }

      private const float BalloonPadding = 16f;
      private const float BalloonMaxWidth = 460f;

      private static bool TryGrowBalloon( UnityEngine.Component label, float own, float preferred )
      {
         try
         {
            if( preferred <= 0f )
            {
               return false;
            }

            float need = Math.Min( preferred * 1.04f + BalloonPadding, BalloonMaxWidth );
            if( need <= own + 0.5f )
            {
               return true;
            }

            float delta = need - own;
            var rt = label.GetComponent<UnityEngine.RectTransform>();
            if( rt == null )
            {
               return false;
            }

            bool stretched = Math.Abs( rt.anchorMax.x - rt.anchorMin.x ) > 0.001f;
            if( !stretched )
            {
               rt.SetSizeWithCurrentAnchors( UnityEngine.RectTransform.Axis.Horizontal, own + delta );
            }

            var current = rt.parent == null ? null : rt.parent.GetComponent<UnityEngine.RectTransform>();
            for( int level = 0; level < 3 && current != null; level++ )
            {
               string name = current.name ?? string.Empty;
               current.SetSizeWithCurrentAnchors( UnityEngine.RectTransform.Axis.Horizontal, current.rect.width + delta );
               if( name.IndexOf( "Balloon", StringComparison.OrdinalIgnoreCase ) >= 0
                  || name.IndexOf( "Base", StringComparison.OrdinalIgnoreCase ) >= 0
                  || name.IndexOf( "BoostIcon", StringComparison.OrdinalIgnoreCase ) >= 0 )
               {
                  break;
               }
               current = current.parent == null ? null : current.parent.GetComponent<UnityEngine.RectTransform>();
            }
            return true;
         }
         catch( System.Exception e )
         {
            XuaLogger.AutoTranslator.Debug( "SdwTextCustomHook.TryGrowBalloon failed: " + e.Message );
            return false;
         }
      }

      private static string BuildNameLines( object typed, string text, float room, out double scale )
      {
         scale = 1.0;
         var words = text.Split( new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries );
         if( words.Length < 2 )
         {
            return null;
         }

         string best = null;
         double bestScale = -1.0;
         int maxLines = Math.Min( 3, words.Length );
         for( int lines = 2; lines <= maxLines; lines++ )
         {
            string candidate = null;
            float candidateWidth = float.MaxValue;
            if( lines == 2 )
            {
               for( int i = 1; i < words.Length; i++ )
               {
                  var text2 = string.Join( " ", words, 0, i ) + "\n" + string.Join( " ", words, i, words.Length - i );
                  float w = MeasureTextWidth( typed, text2 );
                  if( w > 0f && w < candidateWidth ) { candidateWidth = w; candidate = text2; }
               }
            }
            else
            {
               for( int i = 1; i < words.Length - 1; i++ )
               {
                  for( int j = i + 1; j < words.Length; j++ )
                  {
                     var text3 = string.Join( " ", words, 0, i ) + "\n" + string.Join( " ", words, i, j - i ) + "\n" + string.Join( " ", words, j, words.Length - j );
                     float w = MeasureTextWidth( typed, text3 );
                     if( w > 0f && w < candidateWidth ) { candidateWidth = w; candidate = text3; }
                  }
               }
            }

            if( candidate == null )
            {
               continue;
            }

            double candidateScale = Math.Min( 1.0, room * SdwWrapPlanner.WidthSafety / candidateWidth );
            if( candidateScale > bestScale )
            {
               best = candidate;
               bestScale = candidateScale;
            }
            if( candidateScale >= 0.8 )
            {
               break;
            }
         }

         scale = bestScale;
         return best;
      }

      private static readonly System.Collections.Generic.HashSet<int> _shrinkVerifiers = new System.Collections.Generic.HashSet<int>();

      private static void StartShrinkVerify( object ui, int id, float room, string expected )
      {
         if( room < 1f || _shrinkVerifiers.Count >= 256 )
         {
            return;
         }
         if( _shrinkVerifiers.Add( id ) )
         {
            XUnity.AutoTranslator.Plugin.Core.Utilities.CoroutineHelper.Start( VerifyShrinkFit( ui, id, room, expected ) );
         }
      }

      private static System.Collections.IEnumerator VerifyShrinkFit( object ui, int id, float room, string expected )
      {
         try
         {
            double target = room * SdwWrapPlanner.WidthSafety;
            for( int pass = 0; pass < 6; pass++ )
            {
               yield return null;
               yield return null;

               var unityObject = ui as UnityEngine.Object;
               if( unityObject == null )
               {
                  yield break;
               }
               if( !_shrunkFonts.TryGetValue( id, out var record ) )
               {
                  yield break;
               }

               var preferredProperty = ui.GetType().CachedProperty( "preferredWidth" );
               if( preferredProperty == null )
               {
                  yield break;
               }
               float preferred = (float)preferredProperty.Get( ui );
               int current = ReadFontSize( ui );
               if( preferred < 1f || current <= 0 )
               {
                  continue;
               }

               int next = current;
               if( preferred > target + 1.0 )
               {
                  next = Math.Max( 8, (int)Math.Floor( current * target / preferred ) );
               }
               else if( preferred < target * 0.9 && current < record.Original )
               {
                  next = Math.Min( record.Original, (int)Math.Floor( current * target / preferred ) );
               }

               if( next == current )
               {
                  yield break;
               }
               if( next >= record.Original )
               {
                  UnityTypes.Text_Properties.FontSize.Set( ui, record.Original );
                  _shrunkFonts.Remove( id );
               }
               else
               {
                  UnityTypes.Text_Properties.FontSize.Set( ui, next );
                  record.Shrunk = next;
               }
               ForceTextRebuild( ui );
               if( Settings.EnableSdwDiagnostics )
               {
                  XuaLogger.AutoTranslator.Debug( "[OVERFLOW-WRAP] verify id=" + id + " pass=" + pass + " preferred=" + preferred.ToString( "0" ) + " target=" + target.ToString( "0" ) + " font " + current + " -> " + next );
               }
            }
         }
         finally
         {
            _shrinkVerifiers.Remove( id );
         }
      }

      private static void ApplyFontShrink( object ui, int id, double scale )
      {
         try
         {
            int fontSize = ReadFontSize( ui );
            if( fontSize <= 0 )
            {
               return;
            }

            int shrunk = Math.Max( 1, (int)Math.Floor( fontSize * scale ) );
            if( shrunk >= fontSize )
            {
               return;
            }

            if( _shrunkFonts.Count >= 4096 )
            {
               _shrunkFonts.Clear();
            }
            _shrunkFonts[ id ] = new ShrunkFont { Original = fontSize, Shrunk = shrunk };
            UnityTypes.Text_Properties.FontSize.Set( ui, shrunk );
            ForceTextRebuild( ui );
            StartFontWatch( ui, id );
         }
         catch( System.Exception e )
         {
            XuaLogger.AutoTranslator.Debug( "SdwTextCustomHook.ApplyFontShrink failed: " + e.Message );
         }
      }

      private static readonly System.Collections.Generic.HashSet<int> _fontWatchers = new System.Collections.Generic.HashSet<int>();
      private const int FontWatchFrames = 150;

      private static void StartFontWatch( object ui, int id )
      {
         if( _fontWatchers.Count >= 1024 )
         {
            return;
         }
         if( _fontWatchers.Add( id ) )
         {
            XUnity.AutoTranslator.Plugin.Core.Utilities.CoroutineHelper.Start( WatchShrunkFont( ui, id ) );
         }
      }

      private static System.Collections.IEnumerator WatchShrunkFont( object ui, int id )
      {
         for( int frame = 1; frame <= FontWatchFrames; frame++ )
         {
            yield return null;
            if( !CheckShrunkFont( ui, id, frame ) )
            {
               break;
            }
         }
         _fontWatchers.Remove( id );
      }

      private static readonly string[] DirtyMethodNames = { "SetAllDirty", "SetVerticesDirty", "SetLayoutDirty", "SetMaterialDirty" };

      private static void ForceTextRebuild( object ui )
      {
         try
         {
            var type = ui.GetType();
            foreach( var name in DirtyMethodNames )
            {
               var method = type.GetMethod( name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public, null, System.Type.EmptyTypes, null );
               method?.Invoke( ui, null );
            }
         }
         catch( System.Exception )
         {
         }
      }

      private static bool CheckShrunkFont( object ui, int id, int frame )
      {
         try
         {
            var component = ui as UnityEngine.Component;
            if( component == null || ui.IsDestroyedUnityObject() || !_shrunkFonts.TryGetValue( id, out var record ) )
            {
               return false;
            }

            if( frame == 1 || frame == 2 || frame == 4 || frame == 8 )
            {
               ForceTextRebuild( ui );
            }

            int current = ReadFontSize( ui );
            if( current > 0 && current != record.Shrunk )
            {
               UnityTypes.Text_Properties.FontSize.Set( ui, record.Shrunk );
               if( Settings.EnableSdwDiagnostics )
               {
                  XuaLogger.AutoTranslator.Debug( "[OVERFLOW-WRAP] font-reset path='" + component.GetPath() + "' frame=" + frame + " was=" + current + " back-to=" + record.Shrunk );
               }
            }
            return true;
         }
         catch( System.Exception )
         {
            return false;
         }
      }

      private static void RestoreShrunkFont( object ui, int id )
      {
         try
         {
            if( _shrunkFonts.TryGetValue( id, out var record ) )
            {
               _shrunkFonts.Remove( id );
               if( ReadFontSize( ui ) == record.Shrunk )
               {
                  UnityTypes.Text_Properties.FontSize.Set( ui, record.Original );
               }
            }
         }
         catch( System.Exception )
         {
         }
      }

      private static void ReassertShrunkFont( object ui )
      {
         try
         {
            var component = ui as UnityEngine.Component;
            if( component != null && _shrunkFonts.Count > 0 && _shrunkFonts.TryGetValue( component.GetInstanceID(), out var record )
               && ReadFontSize( ui ) == record.Original )
            {
               UnityTypes.Text_Properties.FontSize.Set( ui, record.Shrunk );
            }
         }
         catch( System.Exception )
         {
         }
      }

      private static double ReadFloatProperty( object typed, string name )
      {
         var property = typed.GetType().CachedProperty( name );
         if( property == null )
         {
            return 0;
         }
         return property.Get( typed ) is float f ? f : 0;
      }

      private static System.Reflection.PropertyInfo _rawTextProperty;
      private static System.Reflection.FieldInfo _rawTextField;
      private static bool _rawTextResolved;

      private static void ResolveRawText( object typed )
      {
         if( _rawTextResolved || typed == null )
         {
            return;
         }

         _rawTextResolved = true;
         try
         {
            var type = typed.GetType();
            var property = HarmonyLib.AccessTools.Property( type, "m_Text" );
            if( property != null && property.CanRead && property.CanWrite )
            {
               _rawTextProperty = property;
            }
            else
            {
               _rawTextField = HarmonyLib.AccessTools.Field( type, "m_Text" );
            }
         }
         catch( System.Exception )
         {
         }

         if( _rawTextProperty == null && _rawTextField == null )
         {
            XuaLogger.AutoTranslator.Warn( "SdwTextCustomHook: Text.m_Text could not be resolved; measuring text may leave the last measured word in a label." );
         }
      }

      internal static string ReadRawText( object typed )
      {
         try
         {
            ResolveRawText( typed );
            if( _rawTextProperty != null )
            {
               return _rawTextProperty.GetValue( typed, null ) as string;
            }
            if( _rawTextField != null )
            {
               return _rawTextField.GetValue( typed ) as string;
            }
         }
         catch( System.Exception )
         {
         }
         return null;
      }

      internal static void RestoreRawText( object typed, string saved )
      {
         if( saved == null || typed == null )
         {
            return;
         }

         try
         {
            ResolveRawText( typed );
            if( string.Equals( ReadRawText( typed ), saved, StringComparison.Ordinal ) )
            {
               return;
            }

            if( _rawTextProperty != null )
            {
               _rawTextProperty.SetValue( typed, saved, null );
            }
            else if( _rawTextField != null )
            {
               _rawTextField.SetValue( typed, saved );
            }
         }
         catch( System.Exception e )
         {
            XuaLogger.AutoTranslator.Debug( "SdwTextCustomHook.RestoreRawText failed: " + e.Message );
         }
      }

      private static bool IsVerticalTruncateMode( object ui )
      {
         try
         {
            var value = UnityTypes.Text_Properties.VerticalOverflow?.Get( ui );
            return value != null && Convert.ToInt32( value ) == 0;
         }
         catch( System.Exception )
         {
            return false;
         }
      }

      private static bool MayOverflowRect( UnityEngine.Component component, string text )
      {
         try
         {
            float width = GetRectWidth( component );
            int fontSize = ReadFontSize( component );
            if( width <= 1f || width >= float.MaxValue || fontSize <= 0 )
            {
               return true;
            }

            double widest = 0;
            foreach( var line in text.Split( '\n' ) )
            {
               widest = Math.Max( widest, ReflowWidth( line ) );
            }
            return widest * fontSize * 0.55 > width * 0.7;
         }
         catch( System.Exception )
         {
            return true;
         }
      }

      private static float MeasureTextWidth( object typed, string text )
      {
         string rawBefore = ReadRawText( typed );
         try
         {
            if( _getTextWidthMethodInfo == null )
            {
               _getTextWidthMethodInfo = HarmonyLib.AccessTools.Method( _sdwTextClrType, "GetTextWidth", new System.Type[] { typeof( string ) } );
               if( _getTextWidthMethodInfo == null )
               {
                  return 0f;
               }
            }

            float widest = 0f;
            foreach( var line in text.Split( '\n' ) )
            {
               var clean = line.IndexOf( '<' ) >= 0 ? ReflowTagRegex.Replace( line, string.Empty ) : line;
               if( clean.Length == 0 )
               {
                  continue;
               }
               widest = Math.Max( widest, Convert.ToSingle( _getTextWidthMethodInfo.Invoke( typed, new object[] { clean } ) ) );
            }
            return widest;
         }
         catch( System.Exception )
         {
            return 0f;
         }
         finally
         {
            RestoreRawText( typed, rawBefore );
         }
      }

      private static SdwWrapPlan TryBuildWrapPlan( object ui, object typed, UnityEngine.Component component, string text, float room, bool fitter, int softLines, bool relaxHeight, out string diag )
      {
         diag = string.Empty;
         string rawBefore = ReadRawText( typed );
         try
         {
            if( _getTextWidthMethodInfo == null || _getSpaceWidthMethodInfo == null )
            {
               _getTextWidthMethodInfo = HarmonyLib.AccessTools.Method( _sdwTextClrType, "GetTextWidth", new System.Type[] { typeof( string ) } );
               _getSpaceWidthMethodInfo = HarmonyLib.AccessTools.Method( _sdwTextClrType, "GetSpaceWidth", System.Type.EmptyTypes );
               if( _getTextWidthMethodInfo == null || _getSpaceWidthMethodInfo == null )
               {
                  return null;
               }
            }

            var separators = new[] { ' ' };
            var paragraphs = new System.Collections.Generic.List<string[]>();
            var minLines = new System.Collections.Generic.List<int>();
            if( softLines > 0 )
            {
               paragraphs.Add( text.Replace( '\r', ' ' ).Replace( '\n', ' ' ).Split( separators, StringSplitOptions.RemoveEmptyEntries ) );
               minLines.Add( softLines );
            }
            else
            {
               foreach( var paragraph in text.Split( '\n' ) )
               {
                  paragraphs.Add( paragraph.Replace( '\r', ' ' ).Split( separators, StringSplitOptions.RemoveEmptyEntries ) );
                  minLines.Add( 1 );
               }
            }

            bool breakable = false;
            foreach( var words in paragraphs )
            {
               if( words.Length >= 2 )
               {
                  breakable = true;
               }
            }
            if( !breakable )
            {
               return null;
            }

            double space = Convert.ToDouble( _getSpaceWidthMethodInfo.Invoke( typed, null ) );
            space += Math.Max( 0.0, Convert.ToDouble( _getTextWidthMethodInfo.Invoke( typed, new object[] { " " } ) ) );
            var known = new System.Collections.Generic.Dictionary<string, double>();
            var widths = new double[ paragraphs.Count ][];
            for( int p = 0; p < paragraphs.Count; p++ )
            {
               var words = paragraphs[ p ];
               widths[ p ] = new double[ words.Length ];
               for( int i = 0; i < words.Length; i++ )
               {
                  if( !known.TryGetValue( words[ i ], out var width ) )
                  {

                     var measured = words[ i ].IndexOf( '<' ) >= 0 ? ReflowTagRegex.Replace( words[ i ], string.Empty ) : words[ i ];
                     width = measured.Length == 0
                        ? 0.0
                        : Convert.ToDouble( _getTextWidthMethodInfo.Invoke( typed, new object[] { measured } ) );
                     known[ words[ i ] ] = width;
                  }
                  widths[ p ][ i ] = width;
               }
            }

            _getTextWidthMethodInfo.Invoke( typed, new object[] { "M" } );
            double lineHeight = ReadFloatProperty( typed, "preferredHeight" );
            double rectHeight = GetRectHeight( component );
            double spacing = UnityTypes.Text_Properties.LineSpacing?.Get( ui ) is float ls && ls > 0.05f ? ls : 1.0;

            double maxLines = double.MaxValue;
            if( !fitter && lineHeight > 1.0 && rectHeight >= lineHeight * 0.5 )
            {
               maxLines = Math.Max( 1.0, 1.0 + Math.Floor( ( rectHeight - lineHeight ) / ( lineHeight * spacing ) + 0.1 ) );
            }

            if( relaxHeight )
            {

               maxLines = Math.Max( maxLines, RelaxedMaxLines );
            }

            diag = "lineH=" + lineHeight.ToString( "0.0" ) + " rectH=" + rectHeight.ToString( "0.0" ) + " lineSpacing=" + spacing.ToString( "0.000" )
               + " maxLines=" + ( maxLines == double.MaxValue ? "any" : maxLines.ToString( "0" ) ) + " soft=" + softLines;

            var plan = SdwWrapPlanner.Plan( paragraphs.ToArray(), widths, space, room, maxLines, minLines.ToArray() );
            if( Settings.EnableSdwDiagnostics && plan != null )
            {
               double total = 0;
               int wordCount = 0;
               foreach( var w in widths ) { foreach( var x in w ) { total += x; wordCount++; } }
               diag += " paras=" + paragraphs.Count + " words=" + wordCount + " sumW=" + total.ToString( "0" ) + " space=" + space.ToString( "0.0" ) + " room=" + room.ToString( "0" );
            }
            return plan;
         }
         catch( System.Exception e )
         {
            XuaLogger.AutoTranslator.Debug( "SdwTextCustomHook.TryBuildWrapPlan failed: " + e.Message );
         }
         finally
         {
            RestoreRawText( typed, rawBefore );
         }
         return null;
      }

      internal static bool IsDegenerateWrap( string wrapped, int minLines = 4 )
      {
         if( string.IsNullOrEmpty( wrapped ) )
         {
            return false;
         }

         var lines = wrapped.Split( '\n' );
         if( lines.Length < minLines )
         {
            return false;
         }

         int words = 0;
         int nonEmptyLines = 0;
         foreach( var line in lines )
         {
            int w = line.Split( new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries ).Length;
            if( w > 0 )
            {
               nonEmptyLines++;
               words += w;
            }
         }

         return words >= 5 && nonEmptyLines * 10 >= words * 6;
      }

      private static readonly System.Text.RegularExpressions.Regex BlankLineRegex = new System.Text.RegularExpressions.Regex( "[ \\t]*\\r?\\n(?:[ \\t]*\\r?\\n)+", System.Text.RegularExpressions.RegexOptions.Compiled );

      private static void GetFormatedText_Postfix( object __instance, string msg, ref string __result )
      {
         using( XUnity.AutoTranslator.Plugin.Core.Utilities.HitchProfiler.Measure( "Hook.GetFormatedTextPost" ) )
         {
            GetFormatedText_PostfixCore( __instance, msg, ref __result );
         }
      }

      private static void GetFormatedText_PostfixCore( object __instance, string msg, ref string __result )
      {
         Fonts.SdwLineSpacingPolicy.Apply( __instance );

         try
         {
            if( string.IsNullOrEmpty( __result ) || string.IsNullOrEmpty( msg ) || !HasCyrillic( __result ) )
            {
               return;
            }

            var cleaned = __result.Trim( '\n', '\r' );
            if( cleaned.Length > 0 )
            {
               __result = cleaned;
            }

            if( __result.IndexOf( '\n' ) >= 0 )
            {
               var labelForPath = __instance as UnityEngine.Component;
               if( msg.IndexOf( '\n' ) < 0 && labelForPath != null && IsShrinkOnlyPath( labelForPath.GetPath() ) )
               {
                  __result = BlankLineRegex.Replace( __result, " " ).Replace( '\n', ' ' );
               }
               else if( !BlankLineRegex.IsMatch( msg ) )
               {
                  __result = BlankLineRegex.Replace( __result, "\n" );
               }
            }

            var labelComponent = __instance as UnityEngine.Component;
            if( labelComponent != null && IsDegenerateWrap( __result ) )
            {
               float labelWidth = GetRectWidth( labelComponent );
               int labelFont = (int?)UnityTypes.Text_Properties.FontSize?.Get( __instance ) ?? 0;
               if( labelWidth < System.Math.Max( 100f, labelFont * 4f ) )
               {
                  var unwrapped = msg.Trim( '\n', '\r' );
                  if( unwrapped.Length > 0 )
                  {
                     UnityTypes.Text_Properties.HorizontalOverflow?.Set( __instance, 1 );
                     __result = unwrapped;
                     ScheduleRewrap( labelComponent, unwrapped, 100f );
                     return;
                  }
               }
            }

            if( Settings.EnableSdwDiagnostics )
            {
               var c = __instance as UnityEngine.Component;
               XuaLogger.AutoTranslator.Debug( "[WRAP-DIAG] path='" + ( c == null ? "?" : c.GetPath() )
                  + "' rectWidth=" + ( c == null ? 0f : GetRectWidth( c ) ).ToString( "0.0" )
                  + " fontSize=" + UnityTypes.Text_Properties.FontSize?.Get( __instance )
                  + " lineSpacing=" + UnityTypes.Text_Properties.LineSpacing?.Get( __instance )
                  + " in=\"" + msg.Replace( "\n", "\\n" ) + "\" out=\"" + __result.Replace( "\n", "\\n" ) + "\"" );
            }
         }
         catch( System.Exception )
         {
         }
      }

      private static bool GetTextWidth_Prefix( ref string message )
      {
         using( XUnity.AutoTranslator.Plugin.Core.Utilities.HitchProfiler.Measure( "Hook.GetTextWidth" ) )
         {
            return GetTextWidth_PrefixCore( ref message );
         }
      }

      private static bool GetTextWidth_PrefixCore( ref string message )
      {
         if( !string.IsNullOrEmpty( message ) && message.IndexOf( "<size=", System.StringComparison.OrdinalIgnoreCase ) >= 0 )
         {
            message = SizeOpenTagRegex.Replace( message, string.Empty );
         }

         return true;
      }

      private static char[] ReadCharTable( System.Reflection.PropertyInfo property )
      {
         try
         {
            if( property == null || !property.CanRead || !property.CanWrite || property.GetIndexParameters().Length != 0 )
            {
               return null;
            }

            if( property.GetValue( null, null ) is System.Collections.Generic.IList<char> list && list.Count > 0 )
            {
               var result = new char[ list.Count ];
               for( int i = 0; i < result.Length; i++ )
               {
                  result[ i ] = list[ i ];
               }
               return result;
            }
         }
         catch( System.Exception )
         {
         }
         return null;
      }

      private static System.Reflection.PropertyInfo FindCharTable( System.Type sdwTextType, string fieldName, System.Func<char[], bool> identify )
      {
         System.Reflection.PropertyInfo byName = HarmonyLib.AccessTools.Property( sdwTextType, fieldName );
         if( byName != null )
         {
            return byName;
         }

         const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
         foreach( var candidate in sdwTextType.GetProperties( flags ) )
         {
            if( !typeof( System.Collections.Generic.IList<char> ).IsAssignableFrom( candidate.PropertyType ) )
            {
               continue;
            }

            var content = ReadCharTable( candidate );
            if( content != null && identify( content ) )
            {
               XuaLogger.AutoTranslator.Info( "SdwTextCustomHook: SdwText." + fieldName + " not found by name; using '" + candidate.Name + "' (matched by contents)." );
               return candidate;
            }
         }
         return null;
      }

      private static bool ExtendCharacterField( System.Type sdwTextType, string fieldName, char[] additions, System.Func<char[], bool> identify )
      {
         System.Reflection.PropertyInfo property = FindCharTable( sdwTextType, fieldName, identify );
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

   internal sealed class SdwWrapPlan
   {
      public string Text;
      public int Lines;
      public double MaxWidth;
      public double Scale;
      public bool HeightIgnored;
   }

   internal static class SdwWrapPlanner
   {
      public const double OkScale = 0.95;
      public const double WidthSafety = 0.98;
      public const double MinReadableScale = 0.55;
      public const double MinUnbreakableScale = 0.5;
      private const int MaxLinesPerParagraph = 8;
      private const int HardMaxLinesPerParagraph = 40;

      private static readonly System.Collections.Generic.HashSet<string> WeakLineEnders = new System.Collections.Generic.HashSet<string>(
         ( "в во на и а но да с со к ко о об обо от до по за из изо у не ни как что чтобы если когда же ли бы я ты он она мы вы они "
         + "это этот эта эти то для при про без над под перед между через или либо так уже еще ещё мой моя мои твой свой своя своё его её их "
         + "наш ваш был была было были будет быть очень всё там тут вот только даже тоже также среди возле около вокруг после мимо кроме "
         + "вместо против ради сквозь благодаря a an the of to and in on at for or but" ).Split( ' ' ) );

      private static readonly System.Collections.Generic.HashSet<string> WeakLineStarters = new System.Collections.Generic.HashSet<string>(
         ( "бы ли же был была было были будет — – - ... …" ).Split( ' ' ) );

      private static readonly System.Text.RegularExpressions.Regex AdjectiveEndingRegex =
         new System.Text.RegularExpressions.Regex( "(ый|ий|ой|ая|яя|ое|ее|ые|ие|ого|его|ому|ему|ым|им|ую|юю|ых|их|ом|ем)$", System.Text.RegularExpressions.RegexOptions.CultureInvariant );

      private static string BreakCore( string w )
      {
         return w.Trim( '»', '”', '"', '\'', ')', ']', '(', '[', '«', '“' ).ToLowerInvariant();
      }

      private static bool IsSentenceEndBeforeCapital( string core, string after )
      {
         if( core.Length < 2 || string.IsNullOrEmpty( after ) )
         {
            return false;
         }

         bool hasDigit = false;
         foreach( var ch in core ) if( char.IsDigit( ch ) ) { hasDigit = true; break; }
         if( !hasDigit && core.Length < 3 )
         {
            return false;
         }

         var rest = after.TrimStart( '«', '“', '"', '(', '[', '\'' );
         while( rest.StartsWith( "<" ) )
         {
            int close = rest.IndexOf( '>' );
            if( close < 0 ) break;
            rest = rest.Substring( close + 1 );
         }
         return rest.Length > 0 && char.IsUpper( rest[ 0 ] );
      }

      private static double BreakAdjustment( string before, string after )
      {
         double adj = 0;
         var t = before.TrimEnd( '»', '”', '"', '\'', ')', ']' );
         if( t.Length == 0 ) return 0;

         char last = t[ t.Length - 1 ];
         var core = BreakCore( t ).TrimEnd( '.', ',', ';', ':', '!', '?', '…' );
         if( last == '!' || last == '?' || last == '…' || ( last == '.' && ( core.Length >= 4 || IsSentenceEndBeforeCapital( core, after ) ) ) )
         {
            adj = -0.45;
         }
         else if( last == ',' || last == ';' || last == ':' )
         {
            adj = -0.18;
         }
         else if( WeakLineEnders.Contains( core ) )
         {
            adj = 0.25;
         }
         else if( core.Length >= 5 && AdjectiveEndingRegex.IsMatch( core ) )
         {
            adj = 0.15;
         }

         if( adj >= 0 && WeakLineStarters.Contains( BreakCore( after ) ) )
         {
            adj += 0.20;
         }
         return adj;
      }

      public static string[] Balanced( string[] words, double[] widths, int lines, double limit, double space, out double maxLineWidth )
      {
         maxLineWidth = 0;
         int n = words.Length;
         if( lines < 1 || lines > n ) return null;

         var prefix = new double[ n + 1 ];
         for( int i = 0; i < n; i++ ) prefix[ i + 1 ] = prefix[ i ] + widths[ i ];

         System.Func<int, int, double> lineWidth = ( from, to ) => prefix[ to ] - prefix[ from ] + space * ( to - from - 1 );

         var insideQuote = new bool[ n + 1 ];
         int depth = 0;
         for( int i = 0; i < n; i++ )
         {
            foreach( var ch in words[ i ] )
            {
               if( ch == '«' ) depth++;
               else if( ch == '»' && depth > 0 ) depth--;
            }
            insideQuote[ i + 1 ] = depth > 0;
         }

         double ideal = ( prefix[ n ] + space * ( n - lines ) ) / lines;
         var cost = new double[ lines + 1, n + 1 ];
         var prev = new int[ lines + 1, n + 1 ];
         for( int k = 0; k <= lines; k++ )
            for( int i = 0; i <= n; i++ )
               cost[ k, i ] = double.MaxValue;
         cost[ 0, 0 ] = 0;

         for( int k = 1; k <= lines; k++ )
         {
            for( int i = k; i <= n; i++ )
            {
               for( int j = k - 1; j < i; j++ )
               {
                  if( cost[ k - 1, j ] == double.MaxValue ) continue;
                  double w = lineWidth( j, i );
                  if( w > limit && i - j > 1 ) continue;
                  double d = w - ideal;
                  double c = cost[ k - 1, j ] + d * d;
                  if( i - j == 1 ) c += 0.20 * ideal * ideal;
                  if( i < n ) c += BreakAdjustment( words[ i - 1 ], words[ i ] ) * ideal * ideal;
                  if( i < n && insideQuote[ i ] ) c += 0.60 * ideal * ideal;
                  if( c < cost[ k, i ] )
                  {
                     cost[ k, i ] = c;
                     prev[ k, i ] = j;
                  }
               }
            }
         }

         if( cost[ lines, n ] == double.MaxValue ) return null;

         var parts = new string[ lines ];
         int end = n;
         double widest = 0;
         for( int k = lines; k >= 1; k-- )
         {
            int start = prev[ k, end ];
            parts[ k - 1 ] = string.Join( " ", words, start, end - start );
            widest = System.Math.Max( widest, lineWidth( start, end ) );
            end = start;
         }
         maxLineWidth = widest;
         return parts;
      }

      private sealed class Option
      {
         public string[] Lines;
         public double MaxW;
      }

      private static SdwWrapPlan Evaluate( Option[][] options, int[] counts, double room, double maxLinesByHeight )
      {
         int total = 0;
         double widest = 0;
         var sb = new System.Text.StringBuilder();
         for( int p = 0; p < options.Length; p++ )
         {
            var option = options[ p ][ counts[ p ] - 1 ];
            total += option.Lines.Length;
            widest = System.Math.Max( widest, option.MaxW );
            if( p > 0 ) sb.Append( '\n' );
            sb.Append( string.Join( "\n", option.Lines ) );
         }

         double scaleWidth = widest > room * WidthSafety ? room * WidthSafety / widest : 1.0;
         double scaleHeight = maxLinesByHeight < total ? maxLinesByHeight / total : 1.0;
         return new SdwWrapPlan
         {
            Text = sb.ToString(),
            Lines = total,
            MaxWidth = widest,
            Scale = System.Math.Min( scaleWidth, scaleHeight ),
         };
      }

      public static SdwWrapPlan Plan( string[][] paragraphs, double[][] widths, double space, double room, double maxLinesByHeight, int[] minLines )
      {
         int count = paragraphs.Length;

         double fitLimit = room * WidthSafety;
         var options = new Option[ count ][];
         var counts = new int[ count ];

         for( int p = 0; p < count; p++ )
         {
            var words = paragraphs[ p ];
            if( words.Length == 0 )
            {
               options[ p ] = new[] { new Option { Lines = new[] { string.Empty }, MaxW = 0 } };
               counts[ p ] = 1;
               continue;
            }

            double totalWidth = space * System.Math.Max( 0, words.Length - 1 );
            foreach( var w in widths[ p ] ) totalWidth += w;
            int needed = fitLimit > 1 ? (int)System.Math.Ceiling( totalWidth / fitLimit ) + 2 : MaxLinesPerParagraph;
            int kmax = System.Math.Min( words.Length, System.Math.Min( HardMaxLinesPerParagraph, System.Math.Max( MaxLinesPerParagraph, needed ) ) );
            options[ p ] = new Option[ kmax ];
            int firstFit = -1;
            int narrowestK = kmax;
            double narrowest = double.MaxValue;
            for( int k = 1; k <= kmax; k++ )
            {
               double widest;
               var lines = Balanced( words, widths[ p ], k, fitLimit, space, out widest )
                  ?? Balanced( words, widths[ p ], k, double.MaxValue, space, out widest );
               options[ p ][ k - 1 ] = new Option { Lines = lines, MaxW = widest };
               if( firstFit < 0 && widest <= fitLimit )
               {
                  firstFit = k;
               }
               if( lines != null && widest < narrowest - 1e-6 )
               {
                  narrowest = widest;
                  narrowestK = k;
               }
            }

            int wanted = minLines != null && p < minLines.Length ? minLines[ p ] : 1;
            counts[ p ] = System.Math.Max( firstFit > 0 ? firstFit : narrowestK, System.Math.Max( 1, System.Math.Min( wanted, kmax ) ) );
         }

         var first = Evaluate( options, counts, room, maxLinesByHeight );
         var best = first;
         if( first.Scale < OkScale )
         {
            var work = (int[])counts.Clone();
            while( true )
            {
               int pick = -1;
               SdwWrapPlan pickPlan = null;
               for( int p = 0; p < count; p++ )
               {
                  if( work[ p ] <= 1 ) continue;
                  work[ p ]--;
                  var candidate = Evaluate( options, work, room, maxLinesByHeight );
                  work[ p ]++;
                  if( pickPlan == null || candidate.Scale > pickPlan.Scale )
                  {
                     pick = p;
                     pickPlan = candidate;
                  }
               }

               if( pick < 0 ) break;

               work[ pick ]--;
               if( pickPlan.Scale > best.Scale + 1e-9 )
               {
                  best = pickPlan;
               }
               if( pickPlan.Scale >= OkScale ) break;
            }

            if( best.Scale < MinReadableScale )
            {
               first.HeightIgnored = true;
               first.Scale = first.MaxWidth > room * WidthSafety ? System.Math.Max( room * WidthSafety / first.MaxWidth, MinUnbreakableScale ) : 1.0;
               best = first;
            }
         }
         return best;
      }
   }
}
