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

         if( Settings.EnableSdwDiagnostics )
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
            bool ok = ExtendCharacterField( sdwTextType, "LatinCharacters", BuildCyrillicLetters() );
            ok &= ExtendCharacterField( sdwTextType, "NonLineStartingCharacters", new[] { '»', '—', '…', ')' } );
            ok &= ExtendCharacterField( sdwTextType, "NonLineEndingCharacters", new[] { '«', '(' } );

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

            if( best != null && Settings.EnableSdwDiagnostics )
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

      private const float OverflowWrapRatioOwnRect = 1.15f;
      private const float OverflowWrapRatioWrapMode = 1.01f;
      private const float OverflowWrapRatioContainer = 1.03f;
      private const float OverflowWrapMinRoom = 100f;
      private const int OverflowWrapMinChars = 25;

      private static readonly string[] NoOverflowWrapPathParts = new string[ 0 ];

      private static readonly string[][] ShrinkOnlyPathParts = new string[][] { new[] { "Download_SystemTips", "/Text_Title" } };

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
         return false;
      }

      private static System.Type _sdwTextClrType;
      private static System.Reflection.MethodInfo _getTextWidthMethodInfo;
      private static System.Reflection.MethodInfo _getSpaceWidthMethodInfo;

      private sealed class PendingOverflow
      {
         public string Text;
         public int SoftLines;
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
            if( _shrunkFonts.Count > 0 && ui is UnityEngine.Component shrunkComponent )
            {
               RestoreShrunkFont( ui, shrunkComponent.GetInstanceID() );
            }

            if( ui == null || string.IsNullOrEmpty( translated ) || translated.Length < OverflowWrapMinChars
               || ( translated.IndexOf( ' ' ) < 0 && translated.IndexOf( '\n' ) < 0 ) )
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

            if( IsSelfWrapping( ui ) && !HasContentSizeFitter( raw ) )
            {
               if( Settings.EnableSdwDiagnostics )
               {
                  XuaLogger.AutoTranslator.Debug( "[OVERFLOW-WRAP] skip path='" + raw.GetPath() + "': the game wraps this label itself" );
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

            if( Settings.EnableSdwDiagnostics )
            {
               XuaLogger.AutoTranslator.Debug( "[OVERFLOW-WRAP] scheduled path='" + raw.GetPath() + "' selfWrapping=" + IsSelfWrapping( ui )
                  + " len=" + translated.Length + " softLines=" + softLines );
            }

            bool alreadyRunning = _pendingOverflowWrap.ContainsKey( id );
            _pendingOverflowWrap[ id ] = new PendingOverflow { Text = translated, SoftLines = softLines };
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

      private static System.Collections.IEnumerator OverflowWrapWhenLaidOut( object ui, int id )
      {
         try
         {
            for( int frame = 0; frame < 60; frame++ )
            {
               yield return null;

               var raw = ui as UnityEngine.Component;
               if( raw == null || ui.IsDestroyedUnityObject() || !_pendingOverflowWrap.TryGetValue( id, out var pending ) )
               {
                  yield break;
               }

               if( frame < 3 || GetRectWidth( raw ) < 1f )
               {
                  if( frame == 59 && Settings.EnableSdwDiagnostics )
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
               if( IsSelfWrapping( ui ) && !fitter )
               {
                  yield break;
               }

               float room = fitter ? GetContainerWidth( component ) : own;
               if( fitter && room < 200f && own > room )
               {
                  room = own;
               }

               bool overflowMode = IsHorizontalOverflowMode( ui );
               if( overflowMode && !_overflowSetByUs.Contains( id ) )
               {
                  if( Settings.EnableSdwDiagnostics )
                  {
                     XuaLogger.AutoTranslator.Debug( "[OVERFLOW-WRAP] skip path='" + raw.GetPath() + "': the game lets this label overflow (rect "
                        + own.ToString( "0" ) + "px is not a limit)" );
                  }
                  yield break;
               }
               float ratio = fitter ? OverflowWrapRatioContainer : ( overflowMode ? OverflowWrapRatioOwnRect : OverflowWrapRatioWrapMode );

               var preferredProperty = typed.GetType().CachedProperty( "preferredWidth" );
               float preferred = preferredProperty == null ? 0f : (float)preferredProperty.Get( typed );

               bool wrap = room >= OverflowWrapMinRoom && preferred > room * ratio;
               if( Settings.EnableSdwDiagnostics )
               {
                  XuaLogger.AutoTranslator.Debug( "[OVERFLOW-WRAP] path='" + component.GetPath() + "' frame=" + frame + " ownRect=" + own.ToString( "0.0" )
                     + "x" + GetRectHeight( component ).ToString( "0.0" ) + " contentFitter=" + fitter + " overflowMode=" + overflowMode
                     + " room=" + room.ToString( "0.0" ) + " preferred=" + preferred.ToString( "0.0" ) + " wrap=" + wrap );
               }

               if( !wrap )
               {
                  yield break;
               }

               if( IsShrinkOnlyPath( raw.GetPath() ) )
               {
                  double fit = Math.Max( room / preferred, SdwWrapPlanner.MinReadableScale );
                  ApplyFontShrink( ui, id, fit * 0.98 );
                  if( Settings.EnableSdwDiagnostics )
                  {
                     XuaLogger.AutoTranslator.Debug( "[OVERFLOW-WRAP] shrink-only path='" + raw.GetPath() + "' scale=" + fit.ToString( "0.00" ) );
                  }
                  yield break;
               }

               string diag;
               var plan = TryBuildWrapPlan( ui, typed, component, expected, room, fitter, pending.SoftLines, out diag );

               AutoTranslationPlugin.Current?.ReapplyTranslatedText( ui, plan != null ? plan.Text : expected );

               if( plan != null )
               {
                  UnityTypes.Text_Properties.HorizontalOverflow?.Set( ui, 1 );
                  if( _overflowSetByUs.Count >= 4096 )
                  {
                     _overflowSetByUs.Clear();
                  }
                  _overflowSetByUs.Add( id );

                  RememberWrap( id, expected, plan.Text );

                  bool shrink = plan.Scale < SdwWrapPlanner.OkScale && plan.Scale >= SdwWrapPlanner.MinReadableScale;
                  if( shrink )
                  {
                     ApplyFontShrink( ui, id, plan.Scale * 0.98 );
                  }
                  else if( plan.Lines == 1 && plan.Scale < SdwWrapPlanner.OkScale )
                  {
                     AlignLeft( ui );
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
         }
         catch( System.Exception e )
         {
            XuaLogger.AutoTranslator.Debug( "SdwTextCustomHook.ApplyFontShrink failed: " + e.Message );
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

      private static SdwWrapPlan TryBuildWrapPlan( object ui, object typed, UnityEngine.Component component, string text, float room, bool fitter, int softLines, out string diag )
      {
         diag = string.Empty;
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
                     width = Convert.ToDouble( _getTextWidthMethodInfo.Invoke( typed, new object[] { words[ i ] } ) );
                     known[ words[ i ] ] = width;
                  }
                  widths[ p ][ i ] = width;
               }
            }

            double lineHeight = ReadFloatProperty( typed, "preferredHeight" );
            double rectHeight = GetRectHeight( component );
            double spacing = UnityTypes.Text_Properties.LineSpacing?.Get( ui ) is float ls && ls > 0.05f ? ls : 1.0;

            double maxLines = double.MaxValue;
            if( !fitter && lineHeight > 1.0 && rectHeight >= lineHeight * 0.5 )
            {
               maxLines = Math.Max( 1.0, 1.0 + Math.Floor( ( rectHeight - lineHeight ) / ( lineHeight * spacing ) + 0.1 ) );
            }

            diag = "lineH=" + lineHeight.ToString( "0.0" ) + " rectH=" + rectHeight.ToString( "0.0" ) + " lineSpacing=" + spacing.ToString( "0.000" )
               + " maxLines=" + ( maxLines == double.MaxValue ? "any" : maxLines.ToString( "0" ) ) + " soft=" + softLines;

            var plan = SdwWrapPlanner.Plan( paragraphs.ToArray(), widths, space, room, maxLines, minLines.ToArray() );
            if( maxLines <= 1.0 && plan.HeightIgnored )
            {
               diag += " one-line-unfittable";
               return null;
            }
            return plan;
         }
         catch( System.Exception e )
         {
            XuaLogger.AutoTranslator.Debug( "SdwTextCustomHook.TryBuildWrapPlan failed: " + e.Message );
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
      public const double MinReadableScale = 0.6;
      private const int MaxLinesPerParagraph = 8;

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

      private static double BreakAdjustment( string before, string after )
      {
         double adj = 0;
         var t = before.TrimEnd( '»', '”', '"', '\'', ')', ']' );
         if( t.Length == 0 ) return 0;

         char last = t[ t.Length - 1 ];
         var core = BreakCore( t ).TrimEnd( '.', ',', ';', ':', '!', '?', '…' );
         if( last == '!' || last == '?' || last == '…' || ( last == '.' && core.Length >= 4 ) )
         {
            adj = -0.30;
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

         double scaleWidth = widest > room ? room / widest : 1.0;
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
         double fitLimit = room / OkScale;
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

            int kmax = System.Math.Min( words.Length, MaxLinesPerParagraph );
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
               first.Scale = first.MaxWidth > room ? room / first.MaxWidth : 1.0;
               best = first;
            }
         }
         return best;
      }
   }
}
