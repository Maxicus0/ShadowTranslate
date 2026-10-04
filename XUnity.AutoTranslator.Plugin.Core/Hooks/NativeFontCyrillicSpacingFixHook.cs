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
      private const float SweepIntervalSeconds = 0.15f;

      private static bool _applied;

      private static readonly HashSet<string> _loggedFontNames = new HashSet<string>();

      private static readonly HashSet<string> _warmedUpFonts = new HashSet<string>();

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
            XuaLogger.AutoTranslator.Debug( "NativeFontCyrillicSpacingFixHook: started periodic backstop sweep (every " + SweepIntervalSeconds + "s)." );
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

      private static void OnFontTextureRebuilt( Font font )
      {
         if( font == null )
         {
            return;
         }
         TryCorrect( font );
      }

      private static IEnumerator PeriodicSweepLoop()
      {
         while( true )
         {
            var wait = CoroutineHelper.CreateWaitForSecondsRealtime( SweepIntervalSeconds );
            if( wait != null )
            {
               yield return wait;
            }
            else
            {
               yield return CoroutineHelper.CreateWaitForSeconds( SweepIntervalSeconds );
            }

            Font[] fonts = null;
            try
            {
               fonts = ComponentHelper.FindObjectsOfType<Font>();
            }
            catch( Exception e )
            {
               XuaLogger.AutoTranslator.Warn( e, "NativeFontCyrillicSpacingFixHook: failed to enumerate loaded fonts for the periodic sweep." );
            }

            if( fonts == null )
            {
               continue;
            }

            for( int i = 0; i < fonts.Length; i++ )
            {
               var font = fonts[ i ];
               if( font == null )
               {
                  continue;
               }
               TryCorrect( font );
            }
         }
      }

      public static void TryCorrect( Font font )
      {
         if( FontCache.IsNativeReplacementBundleFont( font ) )
         {
            WarmUpCyrillicGlyphs( font );
            return;
         }

         LogFontInUse( font );

         if( !string.IsNullOrEmpty( Settings.OverrideFont ) && string.Equals( font?.name, Settings.OverrideFont, StringComparison.Ordinal ) )
         {
            return;
         }

         WarmUpCyrillicGlyphs( font );

         try
         {
            CorrectFont( font );
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Warn( e, "NativeFontCyrillicSpacingFixHook: failed to normalize Cyrillic advance widths on font '" + font?.name + "'." );
         }
      }

      public static void PrewarmForSize( Font font, int size, string extraText )
      {
         if( font == null || size <= 0 )
         {
            return;
         }

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

            string key = font.GetInstanceID() + ":" + size;
            if( _warmedUpFonts.Add( key ) )
            {
               XuaLogger.AutoTranslator.Debug( "NativeFontCyrillicSpacingFixHook: [PREWARM] baking " + _cyrillicLetters.Count + " Cyrillic glyph(s) on '" + font.name + "' (id=" + font.GetInstanceID() + ") at component size " + size + "." );
               font.RequestCharactersInTexture( _cyrillicAlphabetString, size );
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
         string key = font.GetInstanceID() + ":" + size;
         if( !_warmedUpFonts.Add( key ) )
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

         string name = font.name;
         if( _loggedFontNames.Add( name ) )
         {
            XuaLogger.AutoTranslator.Debug( "NativeFontCyrillicSpacingFixHook: font in use -> '" + name + "' (dynamic=" + font.dynamic + ", size=" + font.fontSize + ")." );
         }
      }

      private static void CorrectFont( Font font )
      {
         IList<CharacterInfo> info = font.characterInfo;
         if( info == null || info.Count == 0 )
         {
            return;
         }

         bool hasCyrillic = false;
         for( int i = 0; i < info.Count; i++ )
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

         bool changed = false;
         var merged = new CharacterInfo[ info.Count ];
         for( int i = 0; i < info.Count; i++ )
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
                  entry.advance = correctedAdvance;
                  changed = true;
               }
            }

            merged[ i ] = entry;
         }

         if( changed )
         {
            font.characterInfo = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<CharacterInfo>( merged );
            XuaLogger.AutoTranslator.Debug( "NativeFontCyrillicSpacingFixHook: corrected Cyrillic advance width(s) on font '" + font.name + "'." );

            RefreshComponentsUsingFont( font );
         }
      }

      private static void RefreshComponentsUsingFont( Font font )
      {
         if( font == null || UnityTypes.Text == null )
         {
            return;
         }

         UnityEngine.Object[] components;
         try
         {
            components = UnityEngine.Object.FindObjectsOfType( UnityTypes.Text.UnityType );
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Warn( e, "NativeFontCyrillicSpacingFixHook: failed to enumerate Text component(s) to refresh after correcting font '" + font.name + "'." );
            return;
         }

         if( components == null )
         {
            return;
         }

         int refreshed = 0;
         for( int i = 0; i < components.Length; i++ )
         {
            var component = components[ i ];
            if( component == null )
            {
               continue;
            }

            var typedComponent = Il2CppUtilities.CreateProxyComponentWithDerivedType( component.Pointer, UnityTypes.Text.ClrType );

            Font componentFont;
            try
            {
               componentFont = UnityTypes.Text_Properties.Font.Get( typedComponent ) as Font;
            }
            catch( Exception e )
            {
               XuaLogger.AutoTranslator.Warn( e, "NativeFontCyrillicSpacingFixHook: failed to read the font off a component while refreshing after correcting font '" + font.name + "'." );
               continue;
            }

            if( !UnityObjectReferenceComparer.Default.Equals( componentFont, font ) )
            {
               continue;
            }

            try
            {
               typedComponent.SetAllDirtyEx();
               refreshed++;
            }
            catch( Exception e )
            {
               XuaLogger.AutoTranslator.Warn( e, "NativeFontCyrillicSpacingFixHook: failed to force a redraw on a component using corrected font '" + font.name + "'." );
            }
         }

         if( refreshed > 0 )
         {
            XuaLogger.AutoTranslator.Debug( "NativeFontCyrillicSpacingFixHook: refreshed " + refreshed + " already-rendered component(s) using font '" + font.name + "' so the corrected Cyrillic spacing actually shows up." );
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
