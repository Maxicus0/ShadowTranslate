using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using XUnity.AutoTranslator.Plugin.Core.Configuration;
using XUnity.Common.Constants;
using XUnity.Common.Logging;
using XUnity.Common.Utilities;

namespace XUnity.AutoTranslator.Plugin.Core.Fonts
{
   internal static class FontCache
   {
      private static readonly Dictionary<int, Font> CachedFonts = new Dictionary<int, Font>();
      private static readonly Dictionary<(string FontName, int Size), Font> CachedNativeReplacementFonts = new Dictionary<(string, int), Font>();

      private static bool _hasLoadedNativeReplacementBundleFonts;
      private static Dictionary<string, Font> NativeReplacementBundleFonts = new Dictionary<string, Font>( StringComparer.OrdinalIgnoreCase );
      private static readonly HashSet<int> NativeReplacementBundleFontIds = new HashSet<int>();

      public static bool IsNativeReplacementBundleFont( Font font )
      {
         if( font == null || !_hasLoadedNativeReplacementBundleFonts ) return false;
         try
         {
            return NativeReplacementBundleFontIds.Contains( font.GetInstanceID() );
         }
         catch( Exception )
         {
            return false;
         }
      }

      private static string _nativeReplacementBundleRelativePath;

      private static string NativeReplacementBundleRelativePath
      {
         get
         {
            if( _nativeReplacementBundleRelativePath == null )
            {
               _nativeReplacementBundleRelativePath = ResolveNativeReplacementBundleRelativePath();
            }
            return _nativeReplacementBundleRelativePath;
         }
      }

      private static string ResolveNativeReplacementBundleRelativePath()
      {
         const string DefaultRelativePath = "UserLibs/sdw_fonts";
         const string FileName = "sdw_fonts";

         var gameRoot = Paths.GameRoot;
         try
         {
            var candidates = new[]
            {
               Path.Combine( "UserLibs", FileName ),
               Path.Combine( Path.Combine( "BepInEx", "UserLibs" ), FileName ),
               Path.Combine( Path.Combine( Path.Combine( "BepInEx", "plugins" ), "XUnity.AutoTranslator" ), FileName ),
               Path.Combine( Path.Combine( "BepInEx", "plugins" ), FileName ),
               Path.Combine( "BepInEx", FileName ),
               FileName,
            };
            foreach( var candidate in candidates )
            {
               if( File.Exists( Path.Combine( gameRoot, candidate ) ) )
               {
                  XuaLogger.AutoTranslator.Debug( "FontCache: found sdw_fonts bundle at well-known location '" + candidate + "'." );
                  return candidate;
               }
            }

            var skipDirs = new HashSet<string>( StringComparer.OrdinalIgnoreCase ) { "cache", "interop", "unity-libs", "core", "il2cpp_etc", "dotnet", "mono" };
            var queue = new Queue<KeyValuePair<string, int>>();
            var bepInEx = Path.Combine( gameRoot, "BepInEx" );
            queue.Enqueue( new KeyValuePair<string, int>( Directory.Exists( bepInEx ) ? bepInEx : gameRoot, 0 ) );
            int visited = 0;
            while( queue.Count > 0 && visited < 400 )
            {
               var current = queue.Dequeue();
               visited++;
               var file = Path.Combine( current.Key, FileName );
               if( File.Exists( file ) )
               {
                  XuaLogger.AutoTranslator.Debug( "FontCache: located sdw_fonts bundle by bounded search -> '" + file + "'." );
                  return Path.GetRelativePath( gameRoot, file );
               }

               if( current.Value >= 4 ) continue;
               foreach( var dir in Directory.GetDirectories( current.Key ) )
               {
                  if( skipDirs.Contains( Path.GetFileName( dir ) ) ) continue;
                  queue.Enqueue( new KeyValuePair<string, int>( dir, current.Value + 1 ) );
               }
            }

            XuaLogger.AutoTranslator.Info( "FontCache: no 'sdw_fonts' file found near '" + gameRoot + "'; will try the bundle embedded in the DLL (if any), otherwise native fonts stay untouched." );
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Warn( e, "FontCache: error while searching for the sdw_fonts bundle; defaulting to '" + DefaultRelativePath + "'." );
         }

         return DefaultRelativePath;
      }

      private static void EnsureNativeReplacementBundleFontsLoaded()
      {
         if( _hasLoadedNativeReplacementBundleFonts ) return;
         _hasLoadedNativeReplacementBundleFonts = true;

         try
         {
            NativeReplacementBundleFonts = FontHelper.LoadNativeReplacementFontsFromBundle( NativeReplacementBundleRelativePath );

            NativeReplacementBundleFontIds.Clear();
            foreach( var kv in NativeReplacementBundleFonts )
            {
               if( kv.Value != null )
               {
                  NativeReplacementBundleFontIds.Add( kv.Value.GetInstanceID() );
               }
            }
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Error( e, "FontCache: failed to load native-replacement font bundle - native fonts will be left untouched." );
            NativeReplacementBundleFonts = new Dictionary<string, Font>( StringComparer.OrdinalIgnoreCase );
         }
      }

      private static bool TryGetNativeReplacementBundleFont( string nativeFontName, out Font font )
      {
         EnsureNativeReplacementBundleFontsLoaded();
         return NativeReplacementBundleFonts.TryGetValue( nativeFontName, out font ) && font != null;
      }

      public static void Preload()
      {
         EnsureNativeReplacementBundleFontsLoaded();
      }

      public static bool HasNativeReplacementBundleFonts
      {
         get
         {
            EnsureNativeReplacementBundleFontsLoaded();
            return NativeReplacementBundleFonts.Count > 0;
         }
      }

      private static readonly Dictionary<string, UnityEngine.Object> CachedNativeReplacementTextMeshProFonts = new Dictionary<string, UnityEngine.Object>();
      private static bool _hasReadOverrideFontTextMeshPro = false;
      private static UnityEngine.Object OverrideFontTextMeshPro;
      private static bool _hasReadFallbackFontTextMeshPro = false;
      private static UnityEngine.Object FallbackFontTextMeshPro;

      public static Font GetOrCreate( int size )
      {
         if( !CachedFonts.TryGetValue( size, out Font font ) || font == null )
         {
            font = FontHelper.GetTextFont( size );
            CachedFonts[ size ] = font;
         }
         return font;
      }

      public static Font GetOrCreateNativeReplacement( string nativeFontName, int size )
      {
         var key = (nativeFontName, size);
         if( CachedNativeReplacementFonts.TryGetValue( key, out Font font ) && font != null )
         {
            return font;
         }

         TryGetNativeReplacementBundleFont( nativeFontName, out font );
         if( font != null )
         {
            CachedNativeReplacementFonts[ key ] = font;
         }
         return font;
      }

      public static UnityEngine.Object GetOrCreateNativeReplacementTextMeshPro( string nativeFontAssetName )
      {
         if( !NativeFontReplacements.TryGetReplacementFontNameForTextMeshPro( nativeFontAssetName, out var replacementFontName ) )
         {
            return null;
         }

         if( !CachedNativeReplacementTextMeshProFonts.TryGetValue( replacementFontName, out var font ) || font == null )
         {
            if( UnityTypes.TMP_FontAsset_Methods.CreateFontAsset == null )
            {
               XuaLogger.AutoTranslator.Warn( "FontCache: TMP_FontAsset.CreateFontAsset not found (TextMeshPro version might be below 3.2.0) -- cannot build a native TMP replacement for '" + replacementFontName + "'." );
               return null;
            }

            try
            {
               font = (UnityEngine.Object)UnityTypes.TMP_FontAsset_Methods.CreateFontAsset.Invoke( null, new object[] { replacementFontName, "", 90 } );
               if( font != null )
               {
                  UnityEngine.Object.DontDestroyOnLoad( font );
               }
               else
               {
                  XuaLogger.AutoTranslator.Warn( "FontCache: TMP_FontAsset.CreateFontAsset('" + replacementFontName + "') returned null while building a native TMP replacement." );
               }
            }
            catch( Exception e )
            {
               XuaLogger.AutoTranslator.Warn( e, "FontCache: failed to build a native TMP replacement font asset from OS font '" + replacementFontName + "'." );
               font = null;
            }

            CachedNativeReplacementTextMeshProFonts[ replacementFontName ] = font;
         }
         return font;
      }

      public static void ClearOverrideFonts()
      {
         CachedFonts.Clear();
         CachedNativeReplacementFonts.Clear();
         CachedNativeReplacementTextMeshProFonts.Clear();
      }

      public static object GetOrCreateOverrideFontTextMeshPro()
      {
         if( !_hasReadOverrideFontTextMeshPro )
         {
            try
            {
               _hasReadOverrideFontTextMeshPro = true;
               OverrideFontTextMeshPro = FontHelper.GetTextMeshProFont( Settings.OverrideFontTextMeshPro );
            }
            catch( Exception e ) when( e.ToString().ToLowerInvariant().Contains( "missing" ) || e.ToString().ToLowerInvariant().Contains( "not found" ) )
            {
               XuaLogger.AutoTranslator.Warn( e, "An error occurred while loading text mesh pro override font. Retrying load with custom proxies..." );

               try
               {
                  OverrideFontTextMeshPro = FontHelper.GetTextMeshProFontByCustomProxies( Settings.OverrideFontTextMeshPro );
               }
               catch( Exception ex )
               {
                  XuaLogger.AutoTranslator.Error( ex, "An error occurred while loading text mesh pro override font: " + Settings.OverrideFontTextMeshPro );
               }
            }
            catch( Exception e )
            {
               XuaLogger.AutoTranslator.Error( e, "An error occurred while loading text mesh pro override font: " + Settings.OverrideFontTextMeshPro );
            }
         }

         return OverrideFontTextMeshPro;
      }

      public static UnityEngine.Object GetOrCreateFallbackFontTextMeshPro()
      {
         if( !_hasReadFallbackFontTextMeshPro )
         {
            try
            {
               _hasReadFallbackFontTextMeshPro = true;
               FallbackFontTextMeshPro = FontHelper.GetTextMeshProFont( Settings.FallbackFontTextMeshPro );
            }
            catch( Exception e ) when( e.ToString().ToLowerInvariant().Contains( "missing" ) || e.ToString().ToLowerInvariant().Contains( "not found" ) )
            {
               XuaLogger.AutoTranslator.Warn( e, "An error occurred while loading text mesh pro fallback font. Retrying load with custom proxies..." );

               try
               {
                  FallbackFontTextMeshPro = FontHelper.GetTextMeshProFontByCustomProxies( Settings.FallbackFontTextMeshPro );
               }
               catch( Exception ex )
               {
                  XuaLogger.AutoTranslator.Error( ex, "An error occurred while loading text mesh pro fallback font: " + Settings.FallbackFontTextMeshPro );
               }
            }
            catch( Exception e )
            {
               XuaLogger.AutoTranslator.Error( e, "An error occurred while loading text mesh pro fallback font: " + Settings.FallbackFontTextMeshPro );
            }
         }

         return FallbackFontTextMeshPro;
      }
   }
}
