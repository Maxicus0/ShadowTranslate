using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using XUnity.AutoTranslator.Plugin.Core.Fonts;
using XUnity.AutoTranslator.Plugin.Core.Configuration;
using XUnity.AutoTranslator.Plugin.Core.Extensions;
using XUnity.AutoTranslator.Plugin.Core.Utilities;
using XUnity.Common.Constants;
using XUnity.Common.Logging;
using XUnity.Common.Utilities;

namespace XUnity.AutoTranslator.Plugin.Core.Hooks
{

   internal static class NativeFontCyrillicSpacingFixHook
   {
      private const float SweepIntervalWithoutEventSeconds = 0.15f;
      private const float SweepIntervalWithEventSeconds = 2.0f;
      private const float RefreshMinIntervalSeconds = 0.4f;

      private static bool _applied;
      private static bool _subscribed;

      private sealed class FontState
      {
         public int LastGlyphCount = -1;
         public bool Initialized;
         public bool IsOverrideFont;
      }

      private static readonly Dictionary<int, FontState> _fontStates = new Dictionary<int, FontState>();

      private static FontState GetState( Font font )
      {
         int id = font.GetInstanceID();
         if( !_fontStates.TryGetValue( id, out var state ) )
         {
            if( _fontStates.Count >= 1024 )
            {
               _fontStates.Clear();
            }
            state = new FontState();
            _fontStates[ id ] = state;
         }
         return state;
      }

      private static readonly HashSet<int> _loggedFontIds = new HashSet<int>();

      private static readonly HashSet<long> _warmedUpFonts = new HashSet<long>();

      private static long WarmKey( Font font, int size )
      {
         return ( (long)font.GetInstanceID() << 32 ) | (uint)size;
      }

      private static string _cyrillicAlphabetString;

      private static readonly HashSet<char> _cyrillicLetters = new HashSet<char>
      {
         'а', 'А', 'б', 'Б', 'в', 'В', 'г', 'Г', 'д', 'Д',
         'е', 'Е', 'ё', 'Ё', 'ж', 'Ж', 'з', 'З', 'и', 'И',
         'й', 'Й', 'к', 'К', 'л', 'Л', 'м', 'М', 'н', 'Н',
         'о', 'О', 'п', 'П', 'р', 'Р', 'с', 'С', 'т', 'Т',
         'у', 'У', 'ф', 'Ф', 'х', 'Х', 'ц', 'Ц', 'ч', 'Ч',
         'ш', 'Ш', 'щ', 'Щ', 'ъ', 'Ъ', 'ы', 'Ы', 'ь', 'Ь',
         'э', 'Э', 'ю', 'Ю', 'я', 'Я',
      };

      private static readonly Dictionary<char, float> _knownCyrillicAdvanceRatio = new Dictionary<char, float>
      {
         { 'Ё', 0.7060f }, { 'А', 0.8580f }, { 'Б', 0.7450f }, { 'В', 0.7450f }, { 'Г', 0.7130f },
         { 'Д', 0.9010f }, { 'Е', 0.7060f }, { 'Ж', 0.9860f }, { 'З', 0.7440f }, { 'И', 0.8420f },
         { 'Й', 0.8420f }, { 'К', 0.7240f }, { 'Л', 0.8680f }, { 'М', 0.9430f }, { 'Н', 0.8420f },
         { 'О', 0.8620f }, { 'П', 0.8420f }, { 'Р', 0.7340f }, { 'С', 0.7750f }, { 'Т', 0.8180f },
         { 'У', 0.7920f }, { 'Ф', 0.8560f }, { 'Х', 0.8380f }, { 'Ц', 0.9020f }, { 'Ч', 0.8240f },
         { 'Ш', 0.9870f }, { 'Щ', 1.0050f }, { 'Ъ', 0.9680f }, { 'Ы', 0.9800f }, { 'Ь', 0.7450f },
         { 'Э', 0.7750f }, { 'Ю', 0.9700f }, { 'Я', 0.7840f }, { 'а', 0.5820f }, { 'б', 0.6210f },
         { 'в', 0.6120f }, { 'г', 0.5740f }, { 'д', 0.7210f }, { 'е', 0.5680f }, { 'ж', 0.8170f },
         { 'з', 0.5690f }, { 'и', 0.6890f }, { 'й', 0.6890f }, { 'к', 0.6000f }, { 'л', 0.7000f },
         { 'м', 0.7530f }, { 'н', 0.6870f }, { 'о', 0.6250f }, { 'п', 0.6900f }, { 'р', 0.6470f },
         { 'с', 0.5640f }, { 'т', 0.6410f }, { 'у', 0.6690f }, { 'ф', 0.9750f }, { 'х', 0.6150f },
         { 'ц', 0.7260f }, { 'ч', 0.6780f }, { 'ш', 0.8370f }, { 'щ', 0.8900f }, { 'ъ', 0.7810f },
         { 'ы', 0.8900f }, { 'ь', 0.6090f }, { 'э', 0.5640f }, { 'ю', 0.8490f }, { 'я', 0.6320f },
         { 'ё', 0.5680f },
      };

      public static void Apply()
      {
         if( !Settings.EnableCyrillicSpacingFix ) return;
         if( _applied )
         {
            return;
         }
         _applied = true;

         try
         {
            var handlerMethod = typeof( NativeFontCyrillicSpacingFixHook ).GetMethod( nameof( OnFontTextureRebuilt ), BindingFlags.NonPublic | BindingFlags.Static );
            bool subscribed = false;

            if( handlerMethod != null )
            {
               var textureRebuiltEvent = typeof( Font ).GetEvent( "textureRebuilt", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static );
               if( textureRebuiltEvent != null )
               {
                  var addMethod = textureRebuiltEvent.GetAddMethod( true );
                  if( addMethod != null )
                  {
                     var handler = CreateCompatibleHandler( textureRebuiltEvent.EventHandlerType, handlerMethod );
                     if( handler != null )
                     {
                        addMethod.Invoke( null, new object[] { handler } );
                        subscribed = true;
                     }
                  }
               }

               if( !subscribed )
               {
                  var addMethod = typeof( Font ).GetMethod( "add_textureRebuilt", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static );
                  if( addMethod != null )
                  {
                     var paramType = addMethod.GetParameters()[ 0 ].ParameterType;
                     var handler = CreateCompatibleHandler( paramType, handlerMethod );
                     if( handler != null )
                     {
                        addMethod.Invoke( null, new object[] { handler } );
                        subscribed = true;
                     }
                  }
               }

               if( !subscribed )
               {
                  var field = typeof( Font ).GetField( "textureRebuilt", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static );
                  if( field != null )
                  {
                     var handler = CreateCompatibleHandler( field.FieldType, handlerMethod );
                     if( handler != null )
                     {
                        if( typeof( Delegate ).IsAssignableFrom( field.FieldType ) )
                        {
                           var combined = Delegate.Combine( field.GetValue( null ) as Delegate, handler as Delegate );
                           field.SetValue( null, combined );
                        }
                        else
                        {
                           field.SetValue( null, handler );
                        }
                        subscribed = true;
                     }
                  }
               }
            }

            _subscribed = subscribed;

            if( subscribed )
            {
               XuaLogger.AutoTranslator.Debug( "NativeFontCyrillicSpacingFixHook: subscribed to Font.textureRebuilt." );
            }
            else
            {
               XuaLogger.AutoTranslator.Warn( "NativeFontCyrillicSpacingFixHook: could not locate or bind to Font.textureRebuilt (event, add_ method, or field) via reflection on this build. Falling back to the periodic sweep alone." );
            }
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Warn( e, "NativeFontCyrillicSpacingFixHook: failed to subscribe to Font.textureRebuilt. Falling back to the periodic sweep alone." );
         }

         try
         {
            CoroutineHelper.Start( PeriodicSweepLoop() );
            XuaLogger.AutoTranslator.Debug( "NativeFontCyrillicSpacingFixHook: started periodic backstop sweep (every " + ( _subscribed ? SweepIntervalWithEventSeconds : SweepIntervalWithoutEventSeconds ) + "s)." );
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Error( e, "NativeFontCyrillicSpacingFixHook: failed to start periodic backstop sweep." );
         }
      }

      private delegate void ClrFontTextureRebuiltHandler( Font font );

      private static object CreateCompatibleHandler( Type targetType, MethodInfo handlerMethod )
      {
         if( targetType == null || handlerMethod == null )
         {
            return null;
         }

         if( typeof( Delegate ).IsAssignableFrom( targetType ) )
         {
            try
            {
               return Delegate.CreateDelegate( targetType, handlerMethod );
            }
            catch( Exception e )
            {
               XuaLogger.AutoTranslator.Warn( e, "NativeFontCyrillicSpacingFixHook: found a real Delegate-derived type for Font.textureRebuilt (" + targetType.FullName + ") but could not bind OnFontTextureRebuilt to it directly." );
               return null;
            }
         }

         try
         {
            var clrHandler = Delegate.CreateDelegate( typeof( ClrFontTextureRebuiltHandler ), handlerMethod );

            MethodInfo convertDelegateDefinition = null;
            var candidates = typeof( Il2CppInterop.Runtime.DelegateSupport ).GetMethods( BindingFlags.Public | BindingFlags.Static );
            for( int i = 0; i < candidates.Length; i++ )
            {
               var candidate = candidates[ i ];
               if( candidate.Name != "ConvertDelegate" || !candidate.IsGenericMethodDefinition )
               {
                  continue;
               }
               var parameters = candidate.GetParameters();
               if( parameters.Length == 1 && parameters[ 0 ].ParameterType == typeof( Delegate ) )
               {
                  convertDelegateDefinition = candidate;
                  break;
               }
            }

            if( convertDelegateDefinition == null )
            {
               XuaLogger.AutoTranslator.Warn( "NativeFontCyrillicSpacingFixHook: Font.textureRebuilt's handler type (" + targetType.FullName + ") is not a real Delegate, and Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<T>(Delegate) could not be located via reflection on this Il2CppInterop version to bridge it." );
               return null;
            }

            var converted = convertDelegateDefinition.MakeGenericMethod( targetType ).Invoke( null, new object[] { clrHandler } );
            if( converted == null || !targetType.IsInstanceOfType( converted ) )
            {
               XuaLogger.AutoTranslator.Warn( "NativeFontCyrillicSpacingFixHook: ConvertDelegate<" + targetType.FullName + "> did not produce a usable handler for Font.textureRebuilt." );
               return null;
            }
            return converted;
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Warn( e, "NativeFontCyrillicSpacingFixHook: Font.textureRebuilt's handler type (" + targetType.FullName + ") is not a real Delegate, and bridging it through Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate failed." );
            return null;
         }
      }

      private const float DirtyCoalesceSeconds = 0.1f;
      private static readonly Dictionary<int, Font> _dirtyFonts = new Dictionary<int, Font>();
      private static bool _dirtyDrainScheduled;

      private static void OnFontTextureRebuilt( Font font )
      {
         if( font == null )
         {
            return;
         }

         _dirtyFonts[ font.GetInstanceID() ] = font;
         if( _dirtyDrainScheduled )
         {
            return;
         }

         _dirtyDrainScheduled = true;
         try
         {
            CoroutineHelper.Start( DrainDirtyFonts() );
         }
         catch( Exception e )
         {
            _dirtyDrainScheduled = false;
            _dirtyFonts.Clear();
            XuaLogger.AutoTranslator.Warn( e, "NativeFontCyrillicSpacingFixHook: failed to schedule the coalesced font correction; correcting immediately." );
            TryCorrect( font, force: true );
         }
      }

      private static IEnumerator DrainDirtyFonts()
      {
         var wait = CoroutineHelper.CreateWaitForSecondsRealtime( DirtyCoalesceSeconds );
         if( wait != null )
         {
            yield return wait;
         }
         else
         {
            yield return CoroutineHelper.CreateWaitForSeconds( DirtyCoalesceSeconds );
         }

         _dirtyDrainScheduled = false;
         if( _dirtyFonts.Count == 0 )
         {
            yield break;
         }

         var batch = new List<Font>( _dirtyFonts.Values );
         _dirtyFonts.Clear();

         for( int i = 0; i < batch.Count; i++ )
         {
            var font = batch[ i ];
            if( font == null )
            {
               continue;
            }

            try
            {
               using( HitchProfiler.Measure( "FontDrain" ) )
               {
                  TryCorrect( font, force: true );
               }
            }
            catch( Exception e )
            {
               XuaLogger.AutoTranslator.Warn( e, "NativeFontCyrillicSpacingFixHook: coalesced font correction failed." );
            }
         }
      }

      private static IEnumerator PeriodicSweepLoop()
      {
         while( true )
         {
            float interval = _subscribed ? SweepIntervalWithEventSeconds : SweepIntervalWithoutEventSeconds;
            var wait = CoroutineHelper.CreateWaitForSecondsRealtime( interval );
            if( wait != null )
            {
               yield return wait;
            }
            else
            {
               yield return CoroutineHelper.CreateWaitForSeconds( interval );
            }

            SweepOnce();
         }
      }

      private static void SweepOnce()
      {
         try
         {
            using( HitchProfiler.Measure( "FontSweep" ) )
            {
               SweepFonts();
            }
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Warn( e, "NativeFontCyrillicSpacingFixHook: periodic sweep failed." );
         }
      }

      private static void SweepFonts()
      {
         try
         {
            Font[] fonts = ComponentHelper.FindObjectsOfType<Font>();
            if( fonts == null )
            {
               return;
            }

            for( int i = 0; i < fonts.Length; i++ )
            {
               var font = fonts[ i ];
               if( font == null || !font.dynamic )
               {
                  continue;
               }

               try
               {
                  TryCorrect( font );
               }
               catch( Exception e )
               {
                  XuaLogger.AutoTranslator.Warn( e, "NativeFontCyrillicSpacingFixHook: periodic sweep failed on a font." );
               }
            }
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Warn( e, "NativeFontCyrillicSpacingFixHook: periodic sweep failed." );
         }
      }

      public static void TryCorrect( Font font, bool force = false )
      {
         if( !Settings.EnableCyrillicSpacingFix ) return;
         if( font == null )
         {
            return;
         }

         if( FontCache.IsNativeReplacementBundleFont( font ) )
         {
            WarmUpCyrillicGlyphs( font );
            return;
         }

         LogFontInUse( font );

         var state = GetState( font );
         if( !state.Initialized )
         {
            state.Initialized = true;
            state.IsOverrideFont = !string.IsNullOrEmpty( Settings.OverrideFont ) && string.Equals( font.name, Settings.OverrideFont, StringComparison.Ordinal );
         }

         if( state.IsOverrideFont )
         {
            return;
         }

         WarmUpCyrillicGlyphs( font );

         try
         {
            CorrectFont( font, state, force || !_subscribed );
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Warn( e, "NativeFontCyrillicSpacingFixHook: failed to normalize Cyrillic advance widths on font '" + font.name + "'." );
         }
      }

      private const string PrebakeMainFontName = "UIMainFont";
      private const string PrebakePunctuation = " .,:;!?-\u2014\u2013\u2026\u00AB\u00BB()[]%+/'\"\u2116\u00D7*&#=<>~\u00B7\u2022";
      private static readonly HashSet<int> _prebakeScheduled = new HashSet<int>();
      private static string _prebakeChars;

      public static void SchedulePrebake( Font font )
      {
         if( !Settings.EnableCyrillicSpacingFix || font == null )
         {
            return;
         }

         int id = font.GetInstanceID();
         if( !_prebakeScheduled.Add( id ) )
         {
            return;
         }

         var sizes = Settings.SdwPrebakeFontSizes;
         if( sizes == null || sizes.Length == 0 )
         {
            return;
         }

         if( !string.Equals( font.name, PrebakeMainFontName, StringComparison.Ordinal ) )
         {
            return;
         }

         try
         {
            CoroutineHelper.Start( PrebakeRoutine( font, sizes ) );
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Warn( e, "NativeFontCyrillicSpacingFixHook: failed to schedule the background glyph pre-bake." );
         }
      }

      private static string GetPrebakeChars()
      {
         if( _prebakeChars == null )
         {
            var sb = new System.Text.StringBuilder( 160 );
            foreach( var c in _cyrillicLetters )
            {
               sb.Append( c );
            }
            sb.Append( "0123456789" );
            sb.Append( PrebakePunctuation );
            _prebakeChars = sb.ToString();
         }
         return _prebakeChars;
      }

      private static IEnumerator PrebakeRoutine( Font font, int[] sizes )
      {
         var wait = CoroutineHelper.CreateWaitForSecondsRealtime( 0.75f );
         if( wait != null )
         {
            yield return wait;
         }
         else
         {
            yield return CoroutineHelper.CreateWaitForSeconds( 0.75f );
         }

         var chars = GetPrebakeChars();
         int baked = 0;

         for( int i = 0; i < sizes.Length; i++ )
         {
            if( font == null )
            {
               yield break;
            }

            bool failed = false;
            using( HitchProfiler.Measure( "PrebakeGlyphs" ) )
            {
               try
               {
                  font.RequestCharactersInTexture( chars, sizes[ i ] );
                  baked++;
               }
               catch( Exception e )
               {
                  XuaLogger.AutoTranslator.Warn( e, "NativeFontCyrillicSpacingFixHook: background pre-bake failed on font '" + font.name + "' at size " + sizes[ i ] + "; stopping." );
                  failed = true;
               }
            }

            if( failed )
            {
               yield break;
            }

            yield return null;
         }

         XuaLogger.AutoTranslator.Debug( "NativeFontCyrillicSpacingFixHook: background pre-bake of font '" + font.name + "' finished (" + baked + " size(s))." );
      }

      public static void PrewarmForSize( Font font, int size, string extraText )
      {
         if( !Settings.EnableCyrillicSpacingFix ) return;
         if( font == null || size <= 0 )
         {
            return;
         }

         SchedulePrebake( font );

         try
         {
            if( _cyrillicAlphabetString == null )
            {
               var sb = new System.Text.StringBuilder( _cyrillicLetters.Count );
               foreach( var c in _cyrillicLetters )
               {
                  sb.Append( c );
               }
               _cyrillicAlphabetString = sb.ToString();
            }

            if( !string.IsNullOrEmpty( extraText ) )
            {
               font.RequestCharactersInTexture( extraText, size );
            }
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Warn( e, "NativeFontCyrillicSpacingFixHook: [PREWARM] failed on font '" + font?.name + "' at size " + size + "." );
         }
      }

      private static void WarmUpCyrillicGlyphs( Font font )
      {
         if( font == null )
         {
            return;
         }

         int size = font.fontSize;
         if( !_warmedUpFonts.Add( WarmKey( font, size ) ) )
         {
            return;
         }

         if( _cyrillicAlphabetString == null )
         {
            var sb = new System.Text.StringBuilder( _cyrillicLetters.Count );
            foreach( var c in _cyrillicLetters )
            {
               sb.Append( c );
            }
            _cyrillicAlphabetString = sb.ToString();
         }

         try
         {
            XuaLogger.AutoTranslator.Debug( "NativeFontCyrillicSpacingFixHook: warming up " + _cyrillicLetters.Count + " Cyrillic glyph(s) on font '" + font.name + "' (size=" + size + ") ahead of dialogue actually needing them." );
            font.RequestCharactersInTexture( _cyrillicAlphabetString, size );
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Warn( e, "NativeFontCyrillicSpacingFixHook: failed to warm up Cyrillic glyphs on font '" + font.name + "'." );
         }
      }

      private static void LogFontInUse( Font font )
      {
         if( font == null )
         {
            return;
         }

         if( _loggedFontIds.Add( font.GetInstanceID() ) )
         {
            XuaLogger.AutoTranslator.Debug( "NativeFontCyrillicSpacingFixHook: font in use -> '" + font.name + "' (dynamic=" + font.dynamic + ", size=" + font.fontSize + ")." );
         }
      }

      private static void CorrectFont( Font font, FontState state, bool force )
      {
         IList<CharacterInfo> info = font.characterInfo;
         if( info == null || info.Count == 0 )
         {
            return;
         }

         int count = info.Count;
         if( !force && state.LastGlyphCount == count )
         {
            return;
         }
         state.LastGlyphCount = count;

         bool hasCyrillic = false;
         for( int i = 0; i < count; i++ )
         {
            if( _cyrillicLetters.Contains( (char)info[ i ].index ) )
            {
               hasCyrillic = true;
               break;
            }
         }
         if( !hasCyrillic )
         {
            return;
         }

         bool isKnownFontFamily = IsUIMainFontFamily( font.name );

         CharacterInfo[] merged = null;
         for( int i = 0; i < count; i++ )
         {
            var entry = info[ i ];
            char current = (char)entry.index;

            if( _cyrillicLetters.Contains( current ) )
            {
               int correctedAdvance;
               if( !( isKnownFontFamily && TryGetKnownRatioAdvance( entry, current, out correctedAdvance ) ) )
               {
                  correctedAdvance = ComputeGeometryBasedAdvance( entry );
               }

               if( entry.advance != correctedAdvance )
               {
                  if( merged == null )
                  {
                     merged = new CharacterInfo[ count ];
                     for( int k = 0; k < i; k++ )
                     {
                        merged[ k ] = info[ k ];
                     }
                  }
                  entry.advance = correctedAdvance;
               }
            }

            if( merged != null )
            {
               merged[ i ] = entry;
            }
         }

         if( merged != null )
         {
            font.characterInfo = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<CharacterInfo>( merged );
            XuaLogger.AutoTranslator.Debug( "NativeFontCyrillicSpacingFixHook: corrected Cyrillic advance width(s) on font '" + font.name + "'." );

            RefreshComponentsUsingFont( font );
         }
      }

      private static readonly Dictionary<int, Font> _pendingRefreshFonts = new Dictionary<int, Font>();
      private static float _lastRefreshTime = -100f;
      private static bool _refreshScheduled;
      private const double RefreshBudgetMs = 3.0;

      private static void RefreshComponentsUsingFont( Font font )
      {
         if( font == null || UnityTypes.Text == null )
         {
            return;
         }

         _pendingRefreshFonts[ font.GetInstanceID() ] = font;
         if( _refreshScheduled )
         {
            return;
         }

         float since = Time.realtimeSinceStartup - _lastRefreshTime;
         float delay = since >= RefreshMinIntervalSeconds ? 0f : RefreshMinIntervalSeconds - since;

         _refreshScheduled = true;
         try
         {
            CoroutineHelper.Start( RefreshCoroutine( delay ) );
         }
         catch( Exception e )
         {
            _refreshScheduled = false;
            XuaLogger.AutoTranslator.Warn( e, "NativeFontCyrillicSpacingFixHook: failed to schedule the deferred component refresh." );
         }
      }

      private static IEnumerator RefreshCoroutine( float delay )
      {
         if( delay > 0f )
         {
            var wait = CoroutineHelper.CreateWaitForSecondsRealtime( delay );
            if( wait != null )
            {
               yield return wait;
            }
            else
            {
               yield return CoroutineHelper.CreateWaitForSeconds( delay );
            }
         }
         else
         {
            yield return null;
         }

         if( _pendingRefreshFonts.Count == 0 || UnityTypes.Text == null )
         {
            _pendingRefreshFonts.Clear();
            _lastRefreshTime = Time.realtimeSinceStartup;
            _refreshScheduled = false;
            yield break;
         }

         var pendingIds = new HashSet<int>( _pendingRefreshFonts.Keys );
         _pendingRefreshFonts.Clear();

         UnityEngine.Object[] components = null;
         try
         {
            components = UnityEngine.Object.FindObjectsOfType( UnityTypes.Text.UnityType );
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Warn( e, "NativeFontCyrillicSpacingFixHook: failed to enumerate Text component(s) to refresh after correcting font(s)." );
         }

         int refreshed = 0;
         if( components != null )
         {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            for( int i = 0; i < components.Length; i++ )
            {
               if( clock.Elapsed.TotalMilliseconds > RefreshBudgetMs )
               {
                  yield return null;
                  clock.Reset();
                  clock.Start();
               }

               var component = components[ i ];
               if( component == null )
               {
                  continue;
               }

               try
               {
                  var typedComponent = Il2CppUtilities.CreateProxyComponentWithDerivedType( component.Pointer, UnityTypes.Text.ClrType );
                  var componentFont = UnityTypes.Text_Properties.Font.Get( typedComponent ) as Font;
                  if( componentFont == null || !pendingIds.Contains( componentFont.GetInstanceID() ) )
                  {
                     continue;
                  }

                  typedComponent.SetAllDirtyEx();
                  refreshed++;
               }
               catch( Exception e )
               {
                  XuaLogger.AutoTranslator.Warn( e, "NativeFontCyrillicSpacingFixHook: failed to refresh a component using a corrected font." );
               }
            }
         }

         if( refreshed > 0 )
         {
            XuaLogger.AutoTranslator.Debug( "NativeFontCyrillicSpacingFixHook: refreshed " + refreshed + " already-rendered component(s) so the corrected Cyrillic spacing actually shows up." );
         }

         _lastRefreshTime = Time.realtimeSinceStartup;
         _refreshScheduled = false;

         if( _pendingRefreshFonts.Count > 0 )
         {
            _refreshScheduled = true;
            try
            {
               CoroutineHelper.Start( RefreshCoroutine( RefreshMinIntervalSeconds ) );
            }
            catch( Exception )
            {
               _refreshScheduled = false;
            }
         }
      }

      private static bool IsUIMainFontFamily( string fontName )
      {
         return !string.IsNullOrEmpty( fontName ) && fontName.StartsWith( "UIMainFont", StringComparison.Ordinal );
      }

      private static bool TryGetKnownRatioAdvance( CharacterInfo entry, char current, out int correctedAdvance )
      {
         float ratio;
         if( !_knownCyrillicAdvanceRatio.TryGetValue( current, out ratio ) )
         {
            correctedAdvance = 0;
            return false;
         }

         int referenceSize = GetReferenceSize( entry );
         float bugSignatureLow = referenceSize * 0.85f;
         float bugSignatureHigh = referenceSize * 1.15f;
         if( entry.advance < bugSignatureLow || entry.advance > bugSignatureHigh )
         {
            correctedAdvance = 0;
            return false;
         }

         correctedAdvance = (int)Math.Round( ratio * referenceSize );
         return true;
      }

      private static int ComputeGeometryBasedAdvance( CharacterInfo entry )
      {
         int trailingBearing = Math.Max( entry.bearing, MinimumTrailingBearing( entry ) );
         return entry.glyphWidth + entry.bearing + trailingBearing;
      }

      private static int MinimumTrailingBearing( CharacterInfo entry )
      {
         int referenceSize = GetReferenceSize( entry );
         int pad = referenceSize / 12;
         return pad > 0 ? pad : 1;
      }

      private static int GetReferenceSize( CharacterInfo entry )
      {
         return entry.size > 0 ? entry.size : ( entry.glyphHeight > 0 ? entry.glyphHeight : 16 );
      }
   }
}
