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

      private const float MinMultiplier = 0.95f;
      private const float MaxMultiplier = 1.6f;

      internal static float? ComputeAutoLineSpacingMultiplier( Font font, int fontSize, string translatedText )
      {
         if( font == null || fontSize <= 0 )
         {
            return null;
         }

         try
         {
            string combined = new string( TallCyrillicSample ) + new string( ReferenceLatinSample ) + ( translatedText ?? string.Empty );
            font.RequestCharactersInTexture( combined, fontSize );

            float? cyrillicSpan = MeasureVerticalSpan( font, TallCyrillicSample );
            float? latinSpan = MeasureVerticalSpan( font, ReferenceLatinSample );

            if( !cyrillicSpan.HasValue || !latinSpan.HasValue || latinSpan.Value <= 0f )
            {
               return null;
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

            return ratio;
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Warn( e, "AutoLineSpacingHelper: failed to compute automatic line spacing for font '" + font?.name + "' at size=" + fontSize + "." );
            return null;
         }
      }

      private static float? MeasureVerticalSpan( Font font, char[] sample )
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
            if( !wanted.Contains( c ) || entry.glyphHeight <= 0 )
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
