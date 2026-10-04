using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using XUnity.Common.Logging;

namespace XUnity.AutoTranslator.Plugin.Core.Hooks
{
   internal static class CyrillicAdvanceWidthFixHook
   {
      private const string HarmonyId = "com.xunity.autotranslator.hooks.cyrillicadvancewidthfixhook";
      private static bool _applied;

      private static readonly Dictionary<char, char> _widthAnalog = new Dictionary<char, char>
      {
         ['ш'] = 'm', ['Ш'] = 'M', ['щ'] = 'm', ['Щ'] = 'M',
         ['м'] = 'm', ['М'] = 'M', ['ю'] = 'm', ['Ю'] = 'M',
         ['ж'] = 'w', ['Ж'] = 'W', ['ы'] = 'w', ['Ы'] = 'W',
         ['ъ'] = 'w', ['Ъ'] = 'W', ['ф'] = 'w', ['Ф'] = 'W',
         ['а'] = 'a', ['А'] = 'A', ['б'] = 'b', ['Б'] = 'B',
         ['в'] = 'b', ['В'] = 'B', ['г'] = 'r', ['Г'] = 'F',
         ['д'] = 'd', ['Д'] = 'D', ['е'] = 'e', ['Е'] = 'E',
         ['з'] = 'e', ['З'] = 'E', ['и'] = 'n', ['И'] = 'N',
         ['й'] = 'n', ['Й'] = 'N', ['к'] = 'k', ['К'] = 'K',
         ['н'] = 'n', ['Н'] = 'H', ['о'] = 'o', ['О'] = 'O',
         ['п'] = 'n', ['П'] = 'N', ['р'] = 'p', ['Р'] = 'P',
         ['с'] = 'c', ['С'] = 'C', ['т'] = 'r', ['Т'] = 'T',
         ['у'] = 'y', ['У'] = 'Y', ['х'] = 'x', ['Х'] = 'X',
         ['ц'] = 'u', ['Ц'] = 'U', ['ч'] = 'u', ['Ч'] = 'U',
         ['э'] = 'e', ['Э'] = 'E', ['я'] = 'n', ['Я'] = 'N',
         ['л'] = 'n', ['Л'] = 'N',
         ['ь'] = 'l', ['Ь'] = 'L', ['ё'] = 'e', ['Ё'] = 'E',
      };

      public static void Apply()
      {
         if( _applied )
         {
            return;
         }

         try
         {
            var harmony = new HarmonyLib.Harmony( HarmonyId );
            var postfix = new HarmonyLib.HarmonyMethod( typeof( CyrillicAdvanceWidthFixHook ), nameof( RequestCharactersInTexture_Postfix ) );

            int patched = 0;
            foreach( var method in HarmonyLib.AccessTools.GetDeclaredMethods( typeof( Font ) ) )
            {
               if( method.Name != "RequestCharactersInTexture" )
               {
                  continue;
               }

               try
               {
                  harmony.Patch( method, postfix: postfix );
                  patched++;
               }
               catch( Exception e )
               {
                  XuaLogger.AutoTranslator.Warn( e, "CyrillicAdvanceWidthFixHook: Failed to patch one Font.RequestCharactersInTexture overload." );
               }
            }

            if( patched == 0 )
            {
               XuaLogger.AutoTranslator.Warn( "CyrillicAdvanceWidthFixHook: Could not find Font.RequestCharactersInTexture to patch." );
            }
            else
            {
               XuaLogger.AutoTranslator.Info( "CyrillicAdvanceWidthFixHook: patched " + patched + " Font.RequestCharactersInTexture overload(s)." );
            }

            _applied = true;
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Error( e, "CyrillicAdvanceWidthFixHook: Failed to apply." );
         }
      }

      [ThreadStatic]
      private static bool _isPriming;

      private static void RequestCharactersInTexture_Postfix( Font __instance, string characters )
      {
         if( _isPriming || __instance == null || string.IsNullOrEmpty( characters ) )
         {
            return;
         }

         try
         {
            bool hasCyrillic = false;
            foreach( char c in characters )
            {
               if( _widthAnalog.ContainsKey( c ) )
               {
                  hasCyrillic = true;
                  break;
               }
            }
            if( !hasCyrillic )
            {
               return;
            }

            XuaLogger.AutoTranslator.Debug(
               "CyrillicAdvanceWidthFixHook: RequestCharactersInTexture seen with Cyrillic characters on font '"
               + __instance.name + "' (dynamic=" + __instance.dynamic + "): " + characters );

            _isPriming = true;
            try
            {
               __instance.RequestCharactersInTexture( "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ", 0 );
            }
            finally
            {
               _isPriming = false;
            }

            System.Collections.Generic.IList<CharacterInfo> info = __instance.characterInfo;
            if( info == null || info.Count == 0 )
            {
               return;
            }

            var advanceByChar = new Dictionary<int, int>( info.Count );
            for( int i = 0; i < info.Count; i++ )
            {
               advanceByChar[ info[ i ].index ] = info[ i ].advance;
            }

            foreach( char c in characters )
            {
               if( !_widthAnalog.TryGetValue( c, out char analog ) )
               {
                  continue;
               }

               bool foundSelf = advanceByChar.TryGetValue( (int)c, out int selfAdvance );
               bool foundAnalog = advanceByChar.TryGetValue( (int)analog, out int analogAdvanceDiag );

               XuaLogger.AutoTranslator.Debug(
                  "CyrillicAdvanceWidthFixHook: DIAG char '" + c + "' (U+" + ( (int)c ).ToString( "X4" ) + ") "
                  + ( foundSelf ? "advance=" + selfAdvance : "NOT in characterInfo (" + info.Count + " entries total)" )
                  + "; analog '" + analog + "' (U+" + ( (int)analog ).ToString( "X4" ) + ") "
                  + ( foundAnalog ? "advance=" + analogAdvanceDiag : "also not found" ) + "." );
            }

            bool changed = false;
            var mergedArray = new CharacterInfo[ info.Count ];
            for( int i = 0; i < info.Count; i++ )
            {
               var entry = info[ i ];
               char current = (char)entry.index;
               if( _widthAnalog.TryGetValue( current, out char analog ) &&
                   advanceByChar.TryGetValue( (int)analog, out int analogAdvance ) &&
                   entry.advance != analogAdvance )
               {
                  entry.advance = analogAdvance;
                  changed = true;
               }
               mergedArray[ i ] = entry;
            }

            if( changed )
            {
               __instance.characterInfo = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<CharacterInfo>( mergedArray );
               XuaLogger.AutoTranslator.Debug( "CyrillicAdvanceWidthFixHook: corrected Cyrillic advance width(s) on font '" + __instance.name + "'." );
            }
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Warn( e, "CyrillicAdvanceWidthFixHook: Failed to normalize Cyrillic advance widths." );
         }
      }
   }
}
