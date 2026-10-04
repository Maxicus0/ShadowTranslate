using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using XUnity.AutoTranslator.Plugin.Core.Configuration;
using XUnity.AutoTranslator.Plugin.Core.Extensions;
using XUnity.Common.Extensions;

namespace XUnity.AutoTranslator.Plugin.Core.Utilities
{
   public static class LanguageHelper
   {
      internal static bool HasRedirectedTexts = false;

      private const string MogolianVowelSeparatorString = "\u180e";
      private const char MogolianVowelSeparatorCharacter = '\u180e';

      private static Func<string, bool> DefaultSymbolCheck;
      private static readonly Dictionary<string, Func<string, bool>> LanguageSymbolChecks = new Dictionary<string, Func<string, bool>>( StringComparer.OrdinalIgnoreCase )
      {
         { "ja", ContainsJapaneseSymbols },
         { "ru", ContainsRussianSymbols },
         { "zh-CN", ContainsChineseSymbols },
         { "zh-TW", ContainsChineseSymbols },
         { "zh-Hans", ContainsChineseSymbols },
         { "zh-Hant", ContainsChineseSymbols },
         { "zh", ContainsChineseSymbols },
         { "ko", ContainsKoreanSymbols },
         { "en", ContainsStandardLatinSymbols },
         { "auto", text => true },
      };

      private static readonly HashSet<string> LanguagesNotUsingWhitespaceBetweenWords = new HashSet<string>
      {
         "ja", "zh", "zh-CN", "zh-TW", "zh-Hans", "zh-Hant"
      };

      internal static bool RequiresWhitespaceUponLineMerging( string code )
      {
         return !LanguagesNotUsingWhitespaceBetweenWords.Contains( code );
      }

      internal static Func<string, bool> GetSymbolCheck( string language )
      {
         if( LanguageSymbolChecks.TryGetValue( language, out Func<string, bool> check ) )
         {
            return check;
         }
         return text => true;
      }

      internal static bool ContainsLanguageSymbolsForSourceLanguage( string text )
      {
         if( DefaultSymbolCheck == null )
         {
            DefaultSymbolCheck = GetSymbolCheck( Settings.FromLanguage );
         }

         return DefaultSymbolCheck( text );
      }

      public static bool ContainsVariableSymbols(string text)
      {
         var fidx = text.IndexOf( '{' );
         return fidx > -1
            && text.IndexOf( '}', fidx ) > fidx;
      }

      public static bool IsRedirected( this string text )
      {
         if( text.Length > 0 )
         {
            switch( Settings.RedirectedResourceDetectionStrategy )
            {
               case RedirectedResourceDetection.AppendMongolianVowelSeparator:
               case RedirectedResourceDetection.AppendMongolianVowelSeparatorAndRemoveAppended:
               case RedirectedResourceDetection.AppendMongolianVowelSeparatorAndRemoveAll:
                  return text.Contains( MogolianVowelSeparatorString );
               case RedirectedResourceDetection.None:
               default:
                  return false;
            }
         }
         return false;
      }

      public static string FixRedirected( this string text )
      {
         switch( Settings.RedirectedResourceDetectionStrategy )
         {
            case RedirectedResourceDetection.AppendMongolianVowelSeparatorAndRemoveAppended:
               if( text.Length > 0 && text[ text.Length - 1 ] == MogolianVowelSeparatorCharacter )
               {
                  return text.Substring( 0, text.Length - 1 );
               }
               break;
            case RedirectedResourceDetection.AppendMongolianVowelSeparatorAndRemoveAll:
               return text.Replace( MogolianVowelSeparatorString, string.Empty );
            case RedirectedResourceDetection.AppendMongolianVowelSeparator:
            case RedirectedResourceDetection.None:
            default:
               return text;
         }

         return text;
      }

      public static string MakeRedirected( this string text )
      {
         switch( Settings.RedirectedResourceDetectionStrategy )
         {
            case RedirectedResourceDetection.AppendMongolianVowelSeparator:
            case RedirectedResourceDetection.AppendMongolianVowelSeparatorAndRemoveAppended:
            case RedirectedResourceDetection.AppendMongolianVowelSeparatorAndRemoveAll:
               if( ContainsLanguageSymbolsForSourceLanguage( text ) || ContainsVariableSymbols( text ) )
               {
                  HasRedirectedTexts = Settings.RedirectedResourceDetectionStrategy != RedirectedResourceDetection.None;

                  return text + MogolianVowelSeparatorCharacter;
               }
               break;
            case RedirectedResourceDetection.None:
            default:
               break;
         }
         return text;
      }

      public static bool IsTranslatable( string text )
      {
         return ContainsLanguageSymbolsForSourceLanguage( text )
            && !Settings.IgnoreTextStartingWith.Any( x => text.StartsWithStrict( x ) );
      }

      internal static bool ContainsJapaneseSymbols( string text )
      {
         foreach( var c in text )
         {
            if( ( c >= '\u3021' && c <= '\u3029' )
               || ( c >= '\u3031' && c <= '\u3035' )
               || ( c >= '\u3041' && c <= '\u3096' )
               || ( c >= '\u30a1' && c <= '\u30fa' )
               || ( c >= '\uff66' && c <= '\uff9d' )
               || ( c >= '\u4e00' && c <= '\u9faf' )
               || ( c >= '\u3400' && c <= '\u4dbf' )
               || ( c >= '\uf900' && c <= '\ufaff' ) )
            {
               return true;
            }
         }
         return false;
      }

      internal static bool ContainsKoreanSymbols( string text )
      {
         foreach( var c in text )
         {
            if( ( c >= '\uac00' && c <= '\ud7af' ) )
            {
               return true;
            }
         }
         return false;
      }

      internal static bool ContainsChineseSymbols( string text )
      {
         foreach( var c in text )
         {
            if( ( c >= '\u4e00' && c <= '\u9faf' )
               || ( c >= '\u3400' && c <= '\u4dbf' )
               || ( c >= '\uf900' && c <= '\ufaff' ) )
            {
               return true;
            }
         }
         return false;
      }

      internal static bool ContainsRussianSymbols( string text )
      {
         foreach( var c in text )
         {
            if( ( c >= '\u0400' && c <= '\u04ff' )
               || ( c >= '\u0500' && c <= '\u052f' )
               || ( c >= '\u2de0' && c <= '\u2dff' )
               || ( c >= '\ua640' && c <= '\ua69f' )
               || ( c >= '\u1c80' && c <= '\u1c88' )
               || ( c >= '\ufe2e' && c <= '\ufe2f' )
               || ( c == '\u1d2b' || c == '\u1d78' ) )
            {
               return true;
            }
         }
         return false;
      }

      internal static bool ContainsStandardLatinSymbols( string text )
      {
         foreach( var c in text )
         {
            if( ( c >= '\u0041' && c <= '\u005a' )
               || ( c >= '\u0061' && c <= '\u007a' ) )
            {
               return true;
            }
         }
         return false;
      }
   }
}
