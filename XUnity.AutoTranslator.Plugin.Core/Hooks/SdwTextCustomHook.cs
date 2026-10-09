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
         using( XUnity.AutoTranslator.Plugin.Core.Utilities.HitchProfiler.Measure( "SetFontSetting" ) )
         {
            SetFontSettingCore( __instance );
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
      }

      private static void SetText_Postfix( object __instance )
      {
         MenuCharacterMessageBoxFontFixHook.EnforceMessageBoxAlignment( __instance );
         RememberGameEntryPoint( __instance, "SetText" );
         Dispatch( __instance, forceStabilizationReset: false );
      }

      private static void UpdateText_Postfix( object __instance )
      {
         MenuCharacterMessageBoxFontFixHook.EnforceMessageBoxAlignment( __instance );
         RememberGameEntryPoint( __instance, "UpdateText" );
         Dispatch( __instance, forceStabilizationReset: false );
      }

      private static void TextPropertySetter_Postfix( object __instance, string value )
      {
         MenuCharacterMessageBoxFontFixHook.EnforceMessageBoxAlignment( __instance );
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
         using( XUnity.AutoTranslator.Plugin.Core.Utilities.HitchProfiler.Measure( "Hook.SdwDispatch" ) )
         {
            DispatchCore( instance, forceStabilizationReset );
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

            if( IsNoWrapLabel( __instance ) || GetRectWidth( component ) <= 1f )
            {
               UnityTypes.Text_Properties.HorizontalOverflow?.Set( __instance, 1 );
               __result = msg;
               return false;
            }

            return true;
         }
         catch( System.Exception )
         {
            return true;
         }
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
}
