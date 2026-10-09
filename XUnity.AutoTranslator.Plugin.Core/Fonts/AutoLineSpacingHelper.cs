using System;
using System.Collections.Generic;
using UnityEngine;
using XUnity.Common.Logging;

namespace XUnity.AutoTranslator.Plugin.Core.Fonts
{
   internal static class AutoLineSpacingHelper
   {
      private static readonly char[] TallCyrillicSample = { 'Й', 'Ё', 'Б', 'р', 'у', 'ф' };

      private static readonly char[] ReferenceLatinSample = { 'H', 'B', 'j', 'y', 'g' };

      private const float MinMultiplier = 1.0f;
      private const float MaxMultiplier = 1.25f;

      private static readonly Dictionary<long, float> _cache = new Dictionary<long, float>();

      private static readonly Dictionary<int, float> _lineEmCache = new Dictionary<int, float>();

      internal static float? GetFontLineEm( Font font )
      {
         if( font == null )
         {
            return null;
         }

         int id = font.GetInstanceID();
         if( _lineEmCache.TryGetValue( id, out float cached ) )
         {
            return cached > 0f ? (float?)cached : null;
         }

         float em = 0f;
         try
         {
            int size = font.fontSize;
            int lh = font.lineHeight;
            if( size > 0 && lh > 0 )
            {
               em = (float)lh / size;
            }
         }
         catch( Exception )
         {
         }

         _lineEmCache[ id ] = em;
         return em > 0f ? (float?)em : null;
      }

      internal const float MaxComfortableLineEm = 1.3f;
      internal const float TargetLineEm = 1.2f;
      internal const float MinLineSpacing = 0.45f;

      internal static float? ComputeAutoLineSpacingMultiplier( Font font, int fontSize, string translatedText )
      {
         if( font == null || fontSize <= 0 )
         {
            return null;
         }

         long key = ( (long)font.GetInstanceID() << 32 ) ^ (uint)fontSize;
         if( _cache.TryGetValue( key, out float cached ) )
         {
            return cached;
         }

         try
         {
            string combined = new string( TallCyrillicSample ) + new string( ReferenceLatinSample ) + ( translatedText ?? string.Empty );
            font.RequestCharactersInTexture( combined, fontSize );

            float? cyrillicSpan = MeasureVerticalSpan( font, TallCyrillicSample, fontSize );
            float? latinSpan = MeasureVerticalSpan( font, ReferenceLatinSample, fontSize );

            if( !cyrillicSpan.HasValue || !latinSpan.HasValue || latinSpan.Value <= 0f )
            {
               _cache[ key ] = 1.0f;
               return 1.0f;
            }

            float ratio = cyrillicSpan.Value / latinSpan.Value;
            if( ratio < MinMultiplier )
            {
               ratio = MinMultiplier;
            }
            else if( ratio > MaxMultiplier )
            {
               ratio = MaxMultiplier;
            }

            _cache[ key ] = ratio;
            return ratio;
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Warn( e, "AutoLineSpacingHelper: failed to compute automatic line spacing for font '" + font?.name + "' at size=" + fontSize + "." );
            return null;
         }
      }

      private static float? MeasureVerticalSpan( Font font, char[] sample, int fontSize )
      {
         IList<CharacterInfo> info = font.characterInfo;
         if( info == null || info.Count == 0 )
         {
            return null;
         }

         var wanted = new HashSet<char>( sample );
         float maxY = float.MinValue;
         float minY = float.MaxValue;
         bool any = false;

         for( int i = 0; i < info.Count; i++ )
         {
            var entry = info[ i ];
            char c = (char)entry.index;
            if( !wanted.Contains( c ) || entry.glyphHeight <= 0 || ( entry.size != 0 && entry.size != fontSize ) )

            {
               continue;
            }

            if( entry.maxY > maxY )
            {
               maxY = entry.maxY;
            }
            if( entry.minY < minY )
            {
               minY = entry.minY;
            }
            any = true;
         }

         return any ? (float?)( maxY - minY ) : null;
      }
   }
}
