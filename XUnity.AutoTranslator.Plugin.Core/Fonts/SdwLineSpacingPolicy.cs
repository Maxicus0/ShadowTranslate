using System;
using System.Collections.Generic;
using UnityEngine;
using XUnity.AutoTranslator.Plugin.Core.Configuration;
using XUnity.AutoTranslator.Plugin.Core.Extensions;
using XUnity.Common.Constants;
using XUnity.Common.Extensions;
using XUnity.Common.Logging;

namespace XUnity.AutoTranslator.Plugin.Core.Fonts
{
   internal static class SdwLineSpacingPolicy
   {
      private const float Epsilon = 0.005f;
      private const float MinSpacing = 0.40f;
      private const float MaxSpacing = 1.60f;

      private static readonly Dictionary<int, float> _lineEmCache = new Dictionary<int, float>();

      public static void Apply( object ui, TextTranslationInfo info = null )
      {
         try
         {
            if( !Settings.EnableSdwLineSpacingPolicy || ui == null ) return;

            var textType = UnityTypes.Text;
            if( textType == null || UnityTypes.Text_Properties.Font == null || UnityTypes.Text_Properties.LineSpacing == null ) return;
            if( !textType.IsAssignableFrom( ui.GetUnityType() ) ) return;

            if( info == null ) info = ui.GetOrCreateTextTranslationInfo();
            if( info != null && info.HasLineSpacingOverride ) return;

            var font = UnityTypes.Text_Properties.Font.Get( ui ) as Font;
            if( font == null || !FontCache.IsNativeReplacementBundleFont( font ) ) return;

            float? fontLineEm = GetFontLineEm( font );
            if( !fontLineEm.HasValue ) return;

            float wanted = Mathf.Clamp( Settings.SdwTargetLineEm / fontLineEm.Value, MinSpacing, MaxSpacing );

            var current = UnityTypes.Text_Properties.LineSpacing.Get( ui );
            float currentValue = current is float f ? f : float.NaN;
            if( !float.IsNaN( currentValue ) && Mathf.Abs( currentValue - wanted ) <= Epsilon ) return;

            UnityTypes.Text_Properties.LineSpacing.Set( ui, wanted );

            if( Settings.EnableSdwDiagnostics )
            {
               XuaLogger.AutoTranslator.Debug( "[LINE-POLICY] '" + ( ui as Component )?.GetPath() + "' lineSpacing "
                  + currentValue.ToString( "0.000" ) + " -> " + wanted.ToString( "0.000" )
                  + " (fontLineEm=" + fontLineEm.Value.ToString( "0.00" ) + ", target=" + Settings.SdwTargetLineEm.ToString( "0.00" ) + ")" );
            }
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Warn( e, "SdwLineSpacingPolicy: failed to apply line spacing." );
         }
      }

      private static float? GetFontLineEm( Font font )
      {
         int id = font.GetInstanceID();
         if( _lineEmCache.TryGetValue( id, out float cached ) ) return cached;

         int size = font.fontSize;
         int lh = font.lineHeight;
         if( size <= 0 || lh <= 0 ) return null;

         float em = (float)lh / size;
         _lineEmCache[ id ] = em;
         return em;
      }

      public static void ClearCache() => _lineEmCache.Clear();
   }
}
