using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using UnityEngine;
using XUnity.Common.Logging;

namespace XUnity.AutoTranslator.Plugin.Core.Fonts
{
   public static class NativeFontReplacements
   {
      private const string PrivateAliasSuffix = " XUATCyr";

      private const bool ForceBoldStyleMetadata = false;

      private const int MaxOsFaceNameLength = 31;

      private static string BuildSafeAliasFamilyName( string realFamilyName )
      {
         var combined = realFamilyName + PrivateAliasSuffix;
         if( combined.Length <= MaxOsFaceNameLength )
         {
            return combined;
         }

         var maxRealNameLength = MaxOsFaceNameLength - PrivateAliasSuffix.Length;
         if( maxRealNameLength < 1 )
         {
            maxRealNameLength = 1;
         }

         var truncatedRealName = realFamilyName.Length > maxRealNameLength
            ? realFamilyName.Substring( 0, maxRealNameLength )
            : realFamilyName;

         return truncatedRealName + PrivateAliasSuffix;
      }

      private static readonly string[] TmpFontAssetNameSuffixes =
      {
         " SDF",
         " SDF Solid",
         " - SDF",
         " Font Asset",
      };

      private static readonly Dictionary<string, string> NativeNameToReplacementName =
         new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );

      private static readonly Dictionary<string, int> ReplacementNameToWeight =
         new Dictionary<string, int>( StringComparer.OrdinalIgnoreCase );

      private static readonly Dictionary<string, float> ReplacementNameToExpectedAdvanceRatio =
         new Dictionary<string, float>( StringComparer.OrdinalIgnoreCase );

      private static readonly Dictionary<string, bool> AliasConfirmedOsVisibleAtRegistration =
         new Dictionary<string, bool>( StringComparer.OrdinalIgnoreCase );

      private static readonly Dictionary<string, string> ReplacementNameToCacheFilePath =
         new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
      private static readonly Dictionary<string, bool> ReplacementNameToRegisteredViaPerUserRegistry =
         new Dictionary<string, bool>( StringComparer.OrdinalIgnoreCase );

      private static readonly List<string> RegisteredCacheFilePaths = new List<string>();
      private static bool _hasRegisteredShutdownCleanup;

      private static readonly List<string> RegisteredRegistryValueNames = new List<string>();

      [DllImport( "gdi32.dll", SetLastError = true, CharSet = CharSet.Unicode )]
      private static extern int AddFontResourceExW( string lpszFilename, uint fl, IntPtr pdv );

      [DllImport( "gdi32.dll", SetLastError = true, CharSet = CharSet.Unicode )]
      private static extern int RemoveFontResourceExW( string lpszFilename, uint fl, IntPtr pdv );

      [DllImport( "gdi32.dll", SetLastError = true )]
      private static extern IntPtr CreateFontIndirectW( ref LOGFONTW lplf );

      [DllImport( "gdi32.dll" )]
      private static extern IntPtr SelectObject( IntPtr hdc, IntPtr hgdiobj );

      [DllImport( "gdi32.dll" )]
      private static extern bool DeleteObject( IntPtr hObject );

      [DllImport( "gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true )]
      private static extern int GetTextFaceW( IntPtr hdc, int nCount, StringBuilder lpszFacename );

      [DllImport( "gdi32.dll", SetLastError = true )]
      private static extern IntPtr CreateCompatibleDC( IntPtr hdc );

      [DllImport( "gdi32.dll", SetLastError = true )]
      private static extern bool DeleteDC( IntPtr hdc );

      [DllImport( "user32.dll", SetLastError = true )]
      private static extern IntPtr SendMessageTimeoutW( IntPtr hWnd, uint Msg, UIntPtr wParam, IntPtr lParam, uint fuFlags, uint uTimeout, out UIntPtr lpdwResult );

      [StructLayout( LayoutKind.Sequential, CharSet = CharSet.Unicode )]
      private struct LOGFONTW
      {
         public int lfHeight;
         public int lfWidth;
         public int lfEscapement;
         public int lfOrientation;
         public int lfWeight;
         public byte lfItalic;
         public byte lfUnderline;
         public byte lfStrikeOut;
         public byte lfCharSet;
         public byte lfOutPrecision;
         public byte lfClipPrecision;
         public byte lfQuality;
         public byte lfPitchAndFamily;
         [MarshalAs( UnmanagedType.ByValTStr, SizeConst = 32 )]
         public string lfFaceName;
      }

      private const byte DEFAULT_CHARSET = 1;
      private const byte OUT_TT_ONLY_PRECIS = 7;
      private const byte CLIP_DEFAULT_PRECIS = 0;
      private const byte DEFAULT_QUALITY = 0;
      private const byte DEFAULT_PITCH = 0;
      private const int FW_DONTCARE = 0;
      private const uint WM_FONTCHANGE = 0x001D;
      private const uint SMTO_ABORTIFHUNG = 0x0002;
      private static readonly IntPtr HWND_BROADCAST = new IntPtr( 0xffff );

      private const uint FR_PRIVATE = 0x10;

      private const uint FR_NONE = 0x0;

      private static void RegisterShutdownCleanup()
      {
         if( _hasRegisteredShutdownCleanup ) return;
         _hasRegisteredShutdownCleanup = true;

         try
         {
            AppDomain.CurrentDomain.ProcessExit += ( _, __ ) => Shutdown();
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Warn( e, "NativeFontReplacements: failed to subscribe to AppDomain.ProcessExit for font cleanup." );
         }

         try
         {
            UnityEngine.Application.quitting += new System.Action( Shutdown );
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Warn( e, "NativeFontReplacements: failed to subscribe to Application.quitting for font cleanup." );
         }
      }

      internal static void Shutdown()
      {
         foreach( var path in RegisteredCacheFilePaths )
         {
            try
            {
               RemoveFontResourceExW( path, FR_NONE, IntPtr.Zero );
            }
            catch
            {
            }
         }

         if( OperatingSystem.IsWindows() && RegisteredRegistryValueNames.Count > 0 )
         {
            try
            {
               using( var fontsKey = Registry.CurrentUser.OpenSubKey( PerUserFontsRegistryKeyPath, writable: true ) )
               {
                  if( fontsKey != null )
                  {
                     foreach( var valueName in RegisteredRegistryValueNames )
                     {
                        try
                        {
                           fontsKey.DeleteValue( valueName, throwOnMissingValue: false );
                        }
                        catch
                        {
                        }
                     }
                  }
               }
            }
            catch
            {
            }
         }

         if( RegisteredCacheFilePaths.Count == 0 && RegisteredRegistryValueNames.Count == 0 ) return;

         try
         {
            BroadcastFontChange();
         }
         catch
         {
         }
      }

      private static bool TryRegisterOneFromBytes( byte[] fontBytes, string fileName, out string registeredFamilyNameOut, out string realFamilyNameOut, out string cacheFilePathOut, out bool registeredViaPerUserRegistryOut )
      {
         registeredFamilyNameOut = null;
         realFamilyNameOut = null;
         cacheFilePathOut = null;
         registeredViaPerUserRegistryOut = false;

         var realFamilyName = TryExtractFamilyName( fontBytes );
         if( string.IsNullOrEmpty( realFamilyName ) )
         {
            XuaLogger.AutoTranslator.Warn(
               "NativeFontReplacements: could not read a family name from '" + fileName + ".otf's own 'name' table. "
               + "Falling back to using the file name itself ('" + fileName + "') as the OS family name, which is a guess "
               + "that previously proved WRONG for these exact files - expect this replacement to silently render as a "
               + "default OS font (e.g. Arial) rather than the intended replacement until this is fixed properly." );
            realFamilyName = fileName;
         }

         var usWeightClass = TryReadUsWeightClass( fontBytes );
         var isBold = usWeightClass.HasValue && usWeightClass.Value >= 700;

         var aliasFamilyName = BuildSafeAliasFamilyName( realFamilyName );
         if( aliasFamilyName.Length != realFamilyName.Length + PrivateAliasSuffix.Length )
         {
            XuaLogger.AutoTranslator.Info(
               "NativeFontReplacements: '" + fileName + "'s real family name ('" + realFamilyName + "', "
               + realFamilyName.Length + " chars) plus the private alias suffix would exceed Windows' "
               + MaxOsFaceNameLength + "-character LOGFONT face name limit - truncated the real-name portion "
               + "to fit ('" + aliasFamilyName + "'). See \"FIFTH ISSUE\" in this class's doc comment." );
         }
         byte[] bytesToRegister;
         string registeredFamilyName;
         try
         {
            bytesToRegister = RewriteFamilyName( fontBytes, aliasFamilyName, ForceBoldStyleMetadata );
            registeredFamilyName = aliasFamilyName;
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Warn( e,
               "NativeFontReplacements: failed to rewrite '" + fileName + "' to a private alias family name - "
               + "registering under its real name '" + realFamilyName + "' instead, which risks colliding with a "
               + "same-named font the game itself may already have loaded (see this class's doc comment)." );
            bytesToRegister = fontBytes;
            registeredFamilyName = realFamilyName;
         }

         if( ForceBoldStyleMetadata && isBold )
         {
            try
            {
               var beforeFsSelection = TryReadFsSelection( bytesToRegister );
               var beforeMacStyle = TryReadMacStyle( bytesToRegister );

               bytesToRegister = NormalizeBoldStyleFlags( bytesToRegister );

               XuaLogger.AutoTranslator.Info(
                  "NativeFontReplacements: '" + fileName + "' has OS/2.usWeightClass=" + usWeightClass.Value
                  + " (bold/heavy) but declared itself style 'Regular' (fsSelection=" + FormatHex16( beforeFsSelection )
                  + ", macStyle=" + FormatHex16( beforeMacStyle ) + ") - normalized to declare BOLD "
                  + "(fsSelection BOLD bit set/REGULAR bit cleared, macStyle BOLD bit set, legacy subfamily "
                  + "'Regular' -> 'Bold') so OS-level style matching for our alias no longer disagrees with this "
                  + "font's own actual design. This corrects a confirmed inconsistency; it is not yet confirmed to "
                  + "be the (full) explanation for a reported 'too thin' rendering - see class doc comment." );
            }
            catch( Exception e )
            {
               XuaLogger.AutoTranslator.Warn( e,
                  "NativeFontReplacements: failed to normalize bold style flags for '" + fileName + "' - "
                  + "registering with its as-shipped (possibly self-contradictory) style metadata instead." );
            }
         }
         else if( isBold )
         {
            XuaLogger.AutoTranslator.Info(
               "NativeFontReplacements: '" + fileName + "' has OS/2.usWeightClass=" + usWeightClass.Value
               + " (bold/heavy) but ForceBoldStyleMetadata is disabled (see class doc comment, \"THIRTEENTH ISSUE\") "
               + "- registering with its as-shipped, self-contradictory-but-proven-to-render-correctly style metadata "
               + "('Regular' legacy subfamily kept) so FontStyle.Normal requests (what this project and the game "
               + "itself always issue) keep matching this alias's one and only face." );
         }

         try
         {
            if( TryComputeExpectedAdvanceRatio( bytesToRegister, ProbeCharacter, out var expectedRatio ) )
            {
               ReplacementNameToExpectedAdvanceRatio[ registeredFamilyName ] = expectedRatio;
            }
            else
            {
               XuaLogger.AutoTranslator.Warn(
                  "NativeFontReplacements: could not extract an advance-width fingerprint for '" + registeredFamilyName
                  + "' from its own 'cmap'/'hmtx' tables (either the probe glyph isn't mapped, or those tables are missing/"
                  + "malformed) - the new fingerprint-based diagnostic (see TryVerifyAdvanceWidthFingerprint, class doc "
                  + "comment 'NINTH ISSUE') will report 'nothing to compare' for this alias; osEnumerable/faceResolves/"
                  + "HasCharacter remain the only checks available for it." );
            }
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Warn( e, "NativeFontReplacements: advance-width fingerprint extraction threw for '" + fileName + "'." );
         }

         string cacheFilePath;
         try
         {
            cacheFilePath = GetOrCreateFontCacheFilePath( fileName, bytesToRegister );
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Warn( e, "NativeFontReplacements: failed to write a private cache file for '" + fileName + "' - cannot register it via AddFontResourceExW." );
            return false;
         }

         var isCffOutline = HasCffOutlines( bytesToRegister );
         var registeredViaPerUserRegistry = TryRegisterPerUserFontRegistryEntry(
            registeredFamilyName, Path.GetFileName( cacheFilePath ), isCffOutline );

         var fontCount = AddFontResourceExW( cacheFilePath, FR_NONE, IntPtr.Zero );
         if( fontCount == 0 )
         {
            XuaLogger.AutoTranslator.Warn( "NativeFontReplacements: AddFontResourceExW failed for '" + fileName + "' (Win32 error " + Marshal.GetLastWin32Error() + ", file '" + cacheFilePath + "')." );
            return false;
         }
         RegisteredCacheFilePaths.Add( cacheFilePath );

         NativeNameToReplacementName[ fileName ] = registeredFamilyName;
         ReplacementNameToWeight[ registeredFamilyName ] = isBold ? ( usWeightClass ?? 700 ) : 400;

         BroadcastFontChange();

         XuaLogger.AutoTranslator.Info(
            "NativeFontReplacements: registered '" + fileName + "' (" + fontCount + " face(s)) under alias '"
            + registeredFamilyName + "' via AddFontResourceExW (non-private - see class doc comment, \"EIGHTH BUG\", "
            + "for why FR_PRIVATE was dropped) (real OS family name '" + realFamilyName
            + "' deliberately NOT reused - see class doc comment on why that collides with the game's own copy). "
            + "Per-user Fonts registry entry (see class doc comment, \"TENTH ISSUE\"): "
            + ( registeredViaPerUserRegistry
               ? "added under HKEY_CURRENT_USER\\" + PerUserFontsRegistryKeyPath + "."
               : "NOT added (see preceding warning) - this alias only has the session-level AddFontResourceExW "
                  + "registration, which is what the NINTH-ISSUE fingerprint check already showed failing." ) );

         ReplacementNameToCacheFilePath[ registeredFamilyName ] = cacheFilePath;
         ReplacementNameToRegisteredViaPerUserRegistry[ registeredFamilyName ] = registeredViaPerUserRegistry;

         registeredFamilyNameOut = registeredFamilyName;
         realFamilyNameOut = realFamilyName;
         cacheFilePathOut = cacheFilePath;
         registeredViaPerUserRegistryOut = registeredViaPerUserRegistry;
         return true;
      }

      public static bool TryRegisterOverrideFontFile( string relativePath, out string registeredFamilyName )
      {
         registeredFamilyName = null;

         if( string.IsNullOrEmpty( relativePath ) )
         {
            return false;
         }

         var fullPath = Path.Combine( Path.Combine( XUnity.Common.Utilities.Paths.GameRoot, "BepInEx" ), relativePath );
         if( !File.Exists( fullPath ) )
         {
            XuaLogger.AutoTranslator.Warn( "NativeFontReplacements: OverrideFontFile configured but not found: '" + fullPath + "'. OverrideFont left unchanged." );
            return false;
         }

         byte[] fontBytes;
         try
         {
            fontBytes = File.ReadAllBytes( fullPath );
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Warn( e, "NativeFontReplacements: OverrideFontFile: failed to read '" + fullPath + "'. OverrideFont left unchanged." );
            return false;
         }

         var logicalName = Path.GetFileNameWithoutExtension( fullPath );

         if( !TryRegisterOneFromBytes( fontBytes, logicalName, out var alias, out var realName, out _, out _ ) )
         {
            XuaLogger.AutoTranslator.Warn( "NativeFontReplacements: OverrideFontFile: registration of '" + fullPath + "' failed (see preceding warning). OverrideFont left unchanged." );
            return false;
         }

         RegisterShutdownCleanup();

         registeredFamilyName = alias;
         XuaLogger.AutoTranslator.Info( "NativeFontReplacements: OverrideFontFile: registered '" + fullPath + "' (real family name '" + realName + "') under OS alias '" + alias + "'." );
         return true;
      }

      private static bool IsNameInOsInstalledFonts( string name )
      {
         var installedNames = FontHelper.GetOSInstalledFontNames();
         for( int i = 0; i < installedNames.Length; i++ )
         {
            if( string.Equals( installedNames[ i ], name, StringComparison.OrdinalIgnoreCase ) )
            {
               return true;
            }
         }
         return false;
      }

      internal static bool TryVerifyAliasEnumerable( string registeredFamilyName )
      {
         if( string.IsNullOrEmpty( registeredFamilyName ) )
         {
            return false;
         }

         try
         {
            var isEnumerable = IsNameInOsInstalledFonts( registeredFamilyName );
            AliasConfirmedOsVisibleAtRegistration[ registeredFamilyName ] = isEnumerable;
            return isEnumerable;
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Warn( e, "NativeFontReplacements: TryVerifyAliasEnumerable threw for '" + registeredFamilyName + "' - falling back to the last known result from registration time." );
            return AliasConfirmedOsVisibleAtRegistration.TryGetValue( registeredFamilyName, out var cached ) && cached;
         }
      }

      internal static bool TryVerifyFontFaceResolves( string requestedFaceName, int weight, out string actualFaceName )
      {
         actualFaceName = null;
         if( string.IsNullOrEmpty( requestedFaceName ) )
         {
            return false;
         }

         IntPtr hdc = IntPtr.Zero;
         IntPtr hFont = IntPtr.Zero;
         IntPtr hOldFont = IntPtr.Zero;
         try
         {
            hdc = CreateCompatibleDC( IntPtr.Zero );
            if( hdc == IntPtr.Zero )
            {
               return false;
            }

            var logFont = new LOGFONTW
            {
               lfHeight = -32,
               lfWidth = 0,
               lfEscapement = 0,
               lfOrientation = 0,
               lfWeight = weight > 0 ? weight : FW_DONTCARE,
               lfItalic = 0,
               lfUnderline = 0,
               lfStrikeOut = 0,
               lfCharSet = DEFAULT_CHARSET,
               lfOutPrecision = OUT_TT_ONLY_PRECIS,
               lfClipPrecision = CLIP_DEFAULT_PRECIS,
               lfQuality = DEFAULT_QUALITY,
               lfPitchAndFamily = DEFAULT_PITCH,
               lfFaceName = requestedFaceName,
            };

            hFont = CreateFontIndirectW( ref logFont );
            if( hFont == IntPtr.Zero )
            {
               return false;
            }

            hOldFont = SelectObject( hdc, hFont );

            var sb = new StringBuilder( 64 );
            var len = GetTextFaceW( hdc, sb.Capacity, sb );
            if( len <= 0 )
            {
               return false;
            }

            actualFaceName = sb.ToString();
            return string.Equals( actualFaceName, requestedFaceName, StringComparison.OrdinalIgnoreCase );
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Warn( e, "NativeFontReplacements: TryVerifyFontFaceResolves threw for '" + requestedFaceName + "'." );
            return false;
         }
         finally
         {
            try
            {
               if( hdc != IntPtr.Zero && hOldFont != IntPtr.Zero )
               {
                  SelectObject( hdc, hOldFont );
               }
               if( hFont != IntPtr.Zero )
               {
                  DeleteObject( hFont );
               }
               if( hdc != IntPtr.Zero )
               {
                  DeleteDC( hdc );
               }
            }
            catch
            {
            }
         }
      }

      private static void BroadcastFontChange()
      {
         try
         {
            SendMessageTimeoutW( HWND_BROADCAST, WM_FONTCHANGE, UIntPtr.Zero, IntPtr.Zero, SMTO_ABORTIFHUNG, 1000, out _ );
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Warn( e, "NativeFontReplacements: WM_FONTCHANGE broadcast threw (non-fatal)." );
         }
      }

      internal static bool TryGetReplacementWeight( string replacementFontName, out int weight )
      {
         if( !string.IsNullOrEmpty( replacementFontName ) && ReplacementNameToWeight.TryGetValue( replacementFontName, out weight ) )
         {
            return true;
         }
         weight = FW_DONTCARE;
         return false;
      }

      private static string GetOrCreateFontCacheFilePath( string fileName, byte[] bytes )
      {
         var contentHash = ComputeShortContentHash( bytes );

         try
         {
            var perUserFontsDir = Path.Combine(
               Environment.GetFolderPath( Environment.SpecialFolder.LocalApplicationData ),
               "Microsoft", "Windows", "Fonts" );
            Directory.CreateDirectory( perUserFontsDir );

            var generationPrefix = PerUserFontFilePrefix + fileName + "_";
            var hashedFileName = generationPrefix + contentHash + ".otf";
            var perUserPath = Path.Combine( perUserFontsDir, hashedFileName );

            if( !IsUpToDateOnDisk( perUserPath, bytes.Length ) )
            {
               File.WriteAllBytes( perUserPath, bytes );
            }

            TryCleanupStaleGenerations( perUserFontsDir, generationPrefix, hashedFileName );
            return perUserPath;
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Warn( e,
               "NativeFontReplacements: could not write '" + fileName + "' into the per-user Fonts folder "
               + "(%LocalAppData%\\Microsoft\\Windows\\Fonts) - falling back to a %TEMP% cache file. This "
               + "replacement will only ever get a plain AddFontResourceExW/session-level registration, not "
               + "the registry-listed install \"TENTH ISSUE\" (class doc comment) added, so it is likely to hit "
               + "the same substitution failure that fix targets." );

            var cacheDir = Path.Combine( Path.GetTempPath(), "XUnityAutoTranslator.NativeFontReplacements" );
            Directory.CreateDirectory( cacheDir );

            var tempGenerationPrefix = fileName + "_";
            var hashedFileName = tempGenerationPrefix + contentHash + ".otf";
            var path = Path.Combine( cacheDir, hashedFileName );

            if( !IsUpToDateOnDisk( path, bytes.Length ) )
            {
               File.WriteAllBytes( path, bytes );
            }

            TryCleanupStaleGenerations( cacheDir, tempGenerationPrefix, hashedFileName );
            return path;
         }
      }

      private static bool IsUpToDateOnDisk( string path, int expectedLength )
      {
         try
         {
            var info = new FileInfo( path );
            return info.Exists && info.Length == expectedLength;
         }
         catch
         {
            return false;
         }
      }

      private static void TryCleanupStaleGenerations( string directory, string prefix, string currentFileName )
      {
         try
         {
            foreach( var path in Directory.GetFiles( directory, prefix + "*.otf" ) )
            {
               var name = Path.GetFileName( path );
               if( string.Equals( name, currentFileName, StringComparison.OrdinalIgnoreCase ) ) continue;

               try
               {
                  File.Delete( path );
               }
               catch
               {
               }
            }
         }
         catch
         {
         }
      }

      private static string ComputeShortContentHash( byte[] bytes )
      {
         unchecked
         {
            const uint fnvOffsetBasis = 2166136261;
            const uint fnvPrime = 16777619;

            var hash = fnvOffsetBasis;
            for( var i = 0; i < bytes.Length; i++ )
            {
               hash ^= bytes[ i ];
               hash *= fnvPrime;
            }
            return hash.ToString( "x8" );
         }
      }

      private const string PerUserFontFilePrefix = "XUAT_";

      private const string PerUserFontsRegistryKeyPath = "Software\\Microsoft\\Windows NT\\CurrentVersion\\Fonts";

      private static bool TryRegisterPerUserFontRegistryEntry( string registeredFamilyName, string fileNameOnDisk, bool isCffOutline )
      {
         if( !OperatingSystem.IsWindows() )
         {
            return false;
         }

         try
         {
            var outlineKind = isCffOutline ? "OpenType" : "TrueType";
            var valueName = registeredFamilyName + " (" + outlineKind + ")";

            using( var fontsKey = Registry.CurrentUser.CreateSubKey( PerUserFontsRegistryKeyPath, writable: true ) )
            {
               if( fontsKey == null )
               {
                  XuaLogger.AutoTranslator.Warn(
                     "NativeFontReplacements: Registry.CurrentUser.CreateSubKey returned null for '" + PerUserFontsRegistryKeyPath
                     + "' - could not add a per-user font registry entry for '" + registeredFamilyName + "'." );
                  return false;
               }

               fontsKey.SetValue( valueName, fileNameOnDisk, RegistryValueKind.String );
            }

            RegisteredRegistryValueNames.Add( valueName );
            return true;
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Warn( e,
               "NativeFontReplacements: failed to add a per-user Fonts registry entry for '" + registeredFamilyName
               + "' - this alias will fall back to a plain AddFontResourceExW/session-level registration (same as "
               + "before \"TENTH ISSUE\" in the class doc comment), which is what has been failing the fingerprint "
               + "check." );
            return false;
         }
      }

      private static bool HasCffOutlines( byte[] data )
      {
         return TryFindTable( data, Tag_CFF, out _, out _, out _ );
      }

      private static string TryExtractFamilyName( byte[] data )
      {
         try
         {
            int baseOffset = 0;
            if( data.Length >= 16 && ReadU32( data, 0 ) == 0x74746366u )
            {
               baseOffset = (int)ReadU32( data, 12 );
            }

            if( baseOffset + 12 > data.Length )
            {
               return null;
            }

            int numTables = ReadU16( data, baseOffset + 4 );
            int nameTableOffset = -1;
            for( int i = 0; i < numTables; i++ )
            {
               int recordOffset = baseOffset + 12 + i * 16;
               if( recordOffset + 16 > data.Length )
               {
                  break;
               }
               if( ReadU32( data, recordOffset ) == 0x6E616D65u )
               {
                  nameTableOffset = (int)ReadU32( data, recordOffset + 8 );
                  break;
               }
            }

            if( nameTableOffset < 0 || nameTableOffset + 6 > data.Length )
            {
               return null;
            }

            int count = ReadU16( data, nameTableOffset + 2 );
            int stringStorageOffset = nameTableOffset + ReadU16( data, nameTableOffset + 4 );

            string anyNameId1 = null;
            string windowsAnyLanguage = null;
            string windowsEnUs = null;
            string fullNameFallback = null;

            for( int i = 0; i < count; i++ )
            {
               int recordOffset = nameTableOffset + 6 + i * 12;
               if( recordOffset + 12 > data.Length )
               {
                  break;
               }

               int platformID = ReadU16( data, recordOffset );
               int languageID = ReadU16( data, recordOffset + 4 );
               int nameID = ReadU16( data, recordOffset + 6 );
               int length = ReadU16( data, recordOffset + 8 );
               int stringOffset = ReadU16( data, recordOffset + 10 );

               if( nameID != 1 && nameID != 4 )
               {
                  continue;
               }

               int absoluteOffset = stringStorageOffset + stringOffset;
               if( absoluteOffset < 0 || length <= 0 || absoluteOffset + length > data.Length )
               {
                  continue;
               }

               string value = platformID == 1
                  ? Encoding.ASCII.GetString( data, absoluteOffset, length )
                  : Encoding.BigEndianUnicode.GetString( data, absoluteOffset, length );

               if( string.IsNullOrEmpty( value ) )
               {
                  continue;
               }

               if( nameID == 4 )
               {
                  if( fullNameFallback == null ) fullNameFallback = value;
                  continue;
               }

               if( anyNameId1 == null ) anyNameId1 = value;
               if( platformID == 3 )
               {
                  if( windowsAnyLanguage == null ) windowsAnyLanguage = value;
                  if( languageID == 0x0409 ) windowsEnUs = value;
               }
            }

            return windowsEnUs ?? windowsAnyLanguage ?? anyNameId1 ?? fullNameFallback;
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Warn( e, "NativeFontReplacements: failed to parse the 'name' table while extracting a font family name." );
            return null;
         }
      }

      private static ushort ReadU16( byte[] data, int offset )
      {
         return (ushort)( ( data[ offset ] << 8 ) | data[ offset + 1 ] );
      }

      private static uint ReadU32( byte[] data, int offset )
      {
         return (uint)( ( data[ offset ] << 24 ) | ( data[ offset + 1 ] << 16 ) | ( data[ offset + 2 ] << 8 ) | data[ offset + 3 ] );
      }

      private static void WriteU32( byte[] data, int offset, uint value )
      {
         data[ offset ] = (byte)( ( value >> 24 ) & 0xFF );
         data[ offset + 1 ] = (byte)( ( value >> 16 ) & 0xFF );
         data[ offset + 2 ] = (byte)( ( value >> 8 ) & 0xFF );
         data[ offset + 3 ] = (byte)( value & 0xFF );
      }

      private static void AppendU16( List<byte> buffer, ushort value )
      {
         buffer.Add( (byte)( ( value >> 8 ) & 0xFF ) );
         buffer.Add( (byte)( value & 0xFF ) );
      }

      private static uint ComputeTableChecksum( byte[] data, int offset, int length )
      {
         uint sum = 0;
         int paddedLength = ( length + 3 ) & ~3;
         for( int i = 0; i < paddedLength; i += 4 )
         {
            uint word = ( (uint)data[ offset + i ] << 24 ) | ( (uint)data[ offset + i + 1 ] << 16 ) | ( (uint)data[ offset + i + 2 ] << 8 ) | data[ offset + i + 3 ];
            sum = unchecked(sum + word);
         }
         return sum;
      }

      private const string LegacySubfamily_Regular = "Regular";
      private const string LegacySubfamily_Bold = "Bold";

      private static byte[] RewriteFamilyName( byte[] data, string newFamilyName, bool markBold )
      {
         int baseOffset = 0;
         if( data.Length >= 16 && ReadU32( data, 0 ) == 0x74746366u )
         {
            baseOffset = (int)ReadU32( data, 12 );
         }

         int numTables = ReadU16( data, baseOffset + 4 );
         int nameDirEntryOffset = -1;
         int nameTableOffset = -1;
         for( int i = 0; i < numTables; i++ )
         {
            int recordOffset = baseOffset + 12 + i * 16;
            if( ReadU32( data, recordOffset ) == 0x6E616D65u )
            {
               nameDirEntryOffset = recordOffset;
               nameTableOffset = (int)ReadU32( data, recordOffset + 8 );
               break;
            }
         }

         if( nameDirEntryOffset < 0 )
         {
            throw new InvalidOperationException( "no 'name' table directory entry found." );
         }

         int format = ReadU16( data, nameTableOffset );
         if( format != 0 )
         {
            throw new NotSupportedException( "'name' table format " + format + " is not supported (only format 0)." );
         }

         int count = ReadU16( data, nameTableOffset + 2 );
         int stringStorageOffset = nameTableOffset + ReadU16( data, nameTableOffset + 4 );

         var records = new List<(ushort platformID, ushort platEncID, ushort languageID, ushort nameID, byte[] value)>( count );
         for( int i = 0; i < count; i++ )
         {
            int recordOffset = nameTableOffset + 6 + i * 12;
            ushort platformID = ReadU16( data, recordOffset );
            ushort platEncID = ReadU16( data, recordOffset + 2 );
            ushort languageID = ReadU16( data, recordOffset + 4 );
            ushort nameID = ReadU16( data, recordOffset + 6 );
            int length = ReadU16( data, recordOffset + 8 );
            int stringOffset = ReadU16( data, recordOffset + 10 );
            int absoluteOffset = stringStorageOffset + stringOffset;

            byte[] value;
            if( nameID == 1 )
            {
               value = platformID == 1
                  ? Encoding.ASCII.GetBytes( newFamilyName )
                  : Encoding.BigEndianUnicode.GetBytes( newFamilyName );
            }
            else if( markBold && nameID == 2 && platformID == 3
               && absoluteOffset >= 0 && length > 0 && absoluteOffset + length <= data.Length
               && Encoding.BigEndianUnicode.GetString( data, absoluteOffset, length ) == LegacySubfamily_Regular )
            {
               value = Encoding.BigEndianUnicode.GetBytes( LegacySubfamily_Bold );
            }
            else
            {
               value = new byte[ length ];
               Array.Copy( data, absoluteOffset, value, 0, length );
            }

            records.Add( (platformID, platEncID, languageID, nameID, value) );
         }

         var newNameTable = new List<byte>( 6 + records.Count * 12 + records.Count * ( newFamilyName.Length * 2 + 8 ) );
         AppendU16( newNameTable, 0 );
         AppendU16( newNameTable, (ushort)records.Count );
         AppendU16( newNameTable, (ushort)( 6 + records.Count * 12 ) );

         int runningStringOffset = 0;
         foreach( var record in records )
         {
            AppendU16( newNameTable, record.platformID );
            AppendU16( newNameTable, record.platEncID );
            AppendU16( newNameTable, record.languageID );
            AppendU16( newNameTable, record.nameID );
            AppendU16( newNameTable, (ushort)record.value.Length );
            AppendU16( newNameTable, (ushort)runningStringOffset );
            runningStringOffset += record.value.Length;
         }
         foreach( var record in records )
         {
            newNameTable.AddRange( record.value );
         }

         int newNameTableOffset = ( data.Length + 3 ) & ~3;
         int paddedNewTableLength = ( newNameTable.Count + 3 ) & ~3;

         var result = new byte[ newNameTableOffset + paddedNewTableLength ];
         Array.Copy( data, result, data.Length );
         for( int i = 0; i < newNameTable.Count; i++ )
         {
            result[ newNameTableOffset + i ] = newNameTable[ i ];
         }

         WriteU32( result, nameDirEntryOffset + 8, (uint)newNameTableOffset );
         WriteU32( result, nameDirEntryOffset + 12, (uint)newNameTable.Count );
         WriteU32( result, nameDirEntryOffset + 4, ComputeTableChecksum( result, newNameTableOffset, newNameTable.Count ) );

         RecomputeHeadCheckSumAdjustment( result, baseOffset, numTables );

         return result;
      }

      private static void RecomputeHeadCheckSumAdjustment( byte[] result, int baseOffset, int numTables )
      {
         int headTableOffset = -1;
         for( int i = 0; i < numTables; i++ )
         {
            int recordOffset = baseOffset + 12 + i * 16;
            if( ReadU32( result, recordOffset ) == 0x68656164u )
            {
               headTableOffset = (int)ReadU32( result, recordOffset + 8 );
               break;
            }
         }

         if( headTableOffset < 0 )
         {
            return;
         }

         WriteU32( result, headTableOffset + 8, 0 );

         uint totalChecksum = 0;
         for( int i = 0; i < numTables; i++ )
         {
            int recordOffset = baseOffset + 12 + i * 16;
            int tableOffset = (int)ReadU32( result, recordOffset + 8 );
            int tableLength = (int)ReadU32( result, recordOffset + 12 );
            totalChecksum = unchecked(totalChecksum + ComputeTableChecksum( result, tableOffset, tableLength ));
         }

         WriteU32( result, headTableOffset + 8, unchecked(0xB1B0AFBAu - totalChecksum) );
      }

      private const int OS2_UsWeightClassOffset = 4;
      private const int OS2_FsSelectionOffset = 62;
      private const int Head_MacStyleOffset = 44;
      private const ushort FsSelection_Bold = 0x0020;
      private const ushort FsSelection_Regular = 0x0040;
      private const ushort MacStyle_Bold = 0x0001;
      private const uint Tag_OS2 = 0x4F532F32u;
      private const uint Tag_head = 0x68656164u;

      private const uint Tag_cmap = 0x636D6170u;
      private const uint Tag_hhea = 0x68686561u;
      private const uint Tag_hmtx = 0x686D7478u;

      private const uint Tag_CFF = 0x43464620u;
      private const int Head_UnitsPerEmOffset = 18;
      private const int Hhea_NumberOfHMetricsOffset = 34;

      internal const char ProbeCharacter = 'А';

      private static bool TryFindTable( byte[] data, uint tag, out int recordOffset, out int tableOffset, out int tableLength )
      {
         recordOffset = -1;
         tableOffset = -1;
         tableLength = -1;

         int baseOffset = 0;
         if( data.Length >= 16 && ReadU32( data, 0 ) == 0x74746366u )
         {
            baseOffset = (int)ReadU32( data, 12 );
         }

         if( baseOffset + 12 > data.Length )
         {
            return false;
         }

         int numTables = ReadU16( data, baseOffset + 4 );
         for( int i = 0; i < numTables; i++ )
         {
            int candidateRecordOffset = baseOffset + 12 + i * 16;
            if( candidateRecordOffset + 16 > data.Length )
            {
               break;
            }
            if( ReadU32( data, candidateRecordOffset ) == tag )
            {
               recordOffset = candidateRecordOffset;
               tableOffset = (int)ReadU32( data, candidateRecordOffset + 8 );
               tableLength = (int)ReadU32( data, candidateRecordOffset + 12 );
               return true;
            }
         }

         return false;
      }

      private static bool TryComputeExpectedAdvanceRatio( byte[] data, char probeChar, out float ratio )
      {
         ratio = 0f;
         try
         {
            if( !TryFindTable( data, Tag_head, out _, out var headOffset, out _ )
               || headOffset + Head_UnitsPerEmOffset + 2 > data.Length )
            {
               return false;
            }
            var unitsPerEm = ReadU16( data, headOffset + Head_UnitsPerEmOffset );
            if( unitsPerEm == 0 )
            {
               return false;
            }

            if( !TryFindTable( data, Tag_cmap, out _, out var cmapOffset, out var cmapLength ) )
            {
               return false;
            }
            if( !TryGetGlyphIdForChar( data, cmapOffset, cmapLength, probeChar, out var glyphId ) || glyphId == 0 )
            {
               return false;
            }

            if( !TryFindTable( data, Tag_hhea, out _, out var hheaOffset, out _ )
               || hheaOffset + Hhea_NumberOfHMetricsOffset + 2 > data.Length )
            {
               return false;
            }
            var numberOfHMetrics = ReadU16( data, hheaOffset + Hhea_NumberOfHMetricsOffset );
            if( numberOfHMetrics == 0 )
            {
               return false;
            }

            if( !TryFindTable( data, Tag_hmtx, out _, out var hmtxOffset, out _ ) )
            {
               return false;
            }

            var metricIndex = glyphId < numberOfHMetrics ? glyphId : numberOfHMetrics - 1;
            var advanceOffset = hmtxOffset + metricIndex * 4;
            if( advanceOffset + 2 > data.Length )
            {
               return false;
            }

            var advanceWidth = ReadU16( data, advanceOffset );
            if( advanceWidth == 0 )
            {
               return false;
            }

            ratio = advanceWidth / (float)unitsPerEm;
            return true;
         }
         catch
         {
            return false;
         }
      }

      private static bool TryGetGlyphIdForChar( byte[] data, int cmapTableOffset, int cmapTableLength, char ch, out int glyphId )
      {
         glyphId = 0;
         if( cmapTableOffset + 4 > data.Length )
         {
            return false;
         }

         var numTables = ReadU16( data, cmapTableOffset + 2 );
         for( int i = 0; i < numTables; i++ )
         {
            var recordOffset = cmapTableOffset + 4 + i * 8;
            if( recordOffset + 8 > data.Length )
            {
               break;
            }

            var subtableOffset = cmapTableOffset + (int)ReadU32( data, recordOffset + 4 );
            if( subtableOffset + 2 > data.Length )
            {
               continue;
            }

            var format = ReadU16( data, subtableOffset );
            int candidate;
            if( format == 4 && TryLookupCmapFormat4( data, subtableOffset, ch, out candidate ) && candidate != 0 )
            {
               glyphId = candidate;
               return true;
            }
            if( format == 12 && TryLookupCmapFormat12( data, subtableOffset, ch, out candidate ) && candidate != 0 )
            {
               glyphId = candidate;
               return true;
            }
         }

         return false;
      }

      private static bool TryLookupCmapFormat4( byte[] data, int subtableOffset, char ch, out int glyphId )
      {
         glyphId = 0;
         var code = (int)ch;

         if( subtableOffset + 14 > data.Length )
         {
            return false;
         }

         var segCountX2 = ReadU16( data, subtableOffset + 6 );
         var segCount = segCountX2 / 2;
         var endCodeOffset = subtableOffset + 14;
         var startCodeOffset = endCodeOffset + segCountX2 + 2;
         var idDeltaOffset = startCodeOffset + segCountX2;
         var idRangeOffsetOffset = idDeltaOffset + segCountX2;

         if( idRangeOffsetOffset + segCountX2 > data.Length )
         {
            return false;
         }

         for( int i = 0; i < segCount; i++ )
         {
            var endCode = ReadU16( data, endCodeOffset + i * 2 );
            if( code > endCode )
            {
               continue;
            }

            var startCode = ReadU16( data, startCodeOffset + i * 2 );
            if( code < startCode )
            {
               return true;
            }

            var idDelta = (short)ReadU16( data, idDeltaOffset + i * 2 );
            var idRangeOffset = ReadU16( data, idRangeOffsetOffset + i * 2 );

            if( idRangeOffset == 0 )
            {
               glyphId = (ushort)( code + idDelta );
            }
            else
            {
               var glyphIndexAddress = idRangeOffsetOffset + i * 2 + idRangeOffset + ( code - startCode ) * 2;
               if( glyphIndexAddress + 2 > data.Length )
               {
                  return false;
               }
               var rawGlyphId = ReadU16( data, glyphIndexAddress );
               glyphId = rawGlyphId == 0 ? 0 : (ushort)( rawGlyphId + idDelta );
            }
            return true;
         }

         return true;
      }

      private static bool TryLookupCmapFormat12( byte[] data, int subtableOffset, char ch, out int glyphId )
      {
         glyphId = 0;
         var code = (uint)ch;

         if( subtableOffset + 16 > data.Length )
         {
            return false;
         }

         var nGroups = ReadU32( data, subtableOffset + 12 );
         var groupsOffset = subtableOffset + 16;

         for( uint i = 0; i < nGroups; i++ )
         {
            var groupOffset = groupsOffset + (int)i * 12;
            if( groupOffset + 12 > data.Length )
            {
               return false;
            }

            var startCharCode = ReadU32( data, groupOffset );
            var endCharCode = ReadU32( data, groupOffset + 4 );
            if( code < startCharCode || code > endCharCode )
            {
               continue;
            }

            var startGlyphId = ReadU32( data, groupOffset + 8 );
            glyphId = (int)( startGlyphId + ( code - startCharCode ) );
            return true;
         }

         return true;
      }

      internal static bool TryVerifyAdvanceWidthFingerprint( string registeredFamilyName, Font font, int size, out float expectedPx, out float measuredPx )
      {
         expectedPx = 0f;
         measuredPx = 0f;

         if( font == null || string.IsNullOrEmpty( registeredFamilyName ) || size <= 0 )
         {
            return true;
         }

         if( !ReplacementNameToExpectedAdvanceRatio.TryGetValue( registeredFamilyName, out var ratio ) || ratio <= 0f )
         {
            return true;
         }

         expectedPx = ratio * size;

         try
         {
            var probe = ProbeCharacter.ToString();
            font.RequestCharactersInTexture( probe, size, FontStyle.Normal );
            if( !font.GetCharacterInfo( ProbeCharacter, out var info, size, FontStyle.Normal ) )
            {
               return false;
            }

            measuredPx = info.advance;
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Warn( e, "NativeFontReplacements: TryVerifyAdvanceWidthFingerprint threw measuring '" + registeredFamilyName + "' at size " + size + "." );
            return false;
         }

         const float ToleranceInPixels = 1.5f;
         return Math.Abs( measuredPx - expectedPx ) <= ToleranceInPixels;
      }

      private static ushort? TryReadUsWeightClass( byte[] data )
      {
         if( !TryFindTable( data, Tag_OS2, out _, out var os2Offset, out _ )
            || os2Offset + OS2_UsWeightClassOffset + 2 > data.Length )
         {
            return null;
         }
         return ReadU16( data, os2Offset + OS2_UsWeightClassOffset );
      }

      private static ushort? TryReadFsSelection( byte[] data )
      {
         if( !TryFindTable( data, Tag_OS2, out _, out var os2Offset, out _ )
            || os2Offset + OS2_FsSelectionOffset + 2 > data.Length )
         {
            return null;
         }
         return ReadU16( data, os2Offset + OS2_FsSelectionOffset );
      }

      private static ushort? TryReadMacStyle( byte[] data )
      {
         if( !TryFindTable( data, Tag_head, out _, out var headOffset, out _ )
            || headOffset + Head_MacStyleOffset + 2 > data.Length )
         {
            return null;
         }
         return ReadU16( data, headOffset + Head_MacStyleOffset );
      }

      private static string FormatHex16( ushort? value )
      {
         return value.HasValue ? "0x" + value.Value.ToString( "X4" ) : "<unknown>";
      }

      private static byte[] NormalizeBoldStyleFlags( byte[] data )
      {
         if( !TryFindTable( data, Tag_OS2, out var os2RecordOffset, out var os2Offset, out var os2Length ) )
         {
            throw new InvalidOperationException( "no 'OS/2' table directory entry found." );
         }
         if( !TryFindTable( data, Tag_head, out _, out var headOffset, out _ ) )
         {
            throw new InvalidOperationException( "no 'head' table directory entry found." );
         }
         if( os2Offset + OS2_FsSelectionOffset + 2 > data.Length || headOffset + Head_MacStyleOffset + 2 > data.Length )
         {
            throw new InvalidOperationException( "OS/2 or head table shorter than the expected fixed offsets - refusing to patch out of bounds." );
         }

         var result = (byte[])data.Clone();

         var fsSelection = ReadU16( result, os2Offset + OS2_FsSelectionOffset );
         fsSelection = (ushort)( ( fsSelection | FsSelection_Bold ) & ~FsSelection_Regular );
         result[ os2Offset + OS2_FsSelectionOffset ] = (byte)( ( fsSelection >> 8 ) & 0xFF );
         result[ os2Offset + OS2_FsSelectionOffset + 1 ] = (byte)( fsSelection & 0xFF );

         var macStyle = ReadU16( result, headOffset + Head_MacStyleOffset );
         macStyle = (ushort)( macStyle | MacStyle_Bold );
         result[ headOffset + Head_MacStyleOffset ] = (byte)( ( macStyle >> 8 ) & 0xFF );
         result[ headOffset + Head_MacStyleOffset + 1 ] = (byte)( macStyle & 0xFF );

         WriteU32( result, os2RecordOffset + 4, ComputeTableChecksum( result, os2Offset, os2Length ) );

         int baseOffset = 0;
         if( result.Length >= 16 && ReadU32( result, 0 ) == 0x74746366u )
         {
            baseOffset = (int)ReadU32( result, 12 );
         }
         int numTables = ReadU16( result, baseOffset + 4 );
         RecomputeHeadCheckSumAdjustment( result, baseOffset, numTables );

         return result;
      }

      public static bool TryGetReplacementFontNameForTextMeshPro( string tmpFontAssetName, out string replacementFontName )
      {
         replacementFontName = null;
         if( string.IsNullOrEmpty( tmpFontAssetName ) )
         {
            return false;
         }

         if( NativeNameToReplacementName.TryGetValue( tmpFontAssetName, out replacementFontName ) )
         {
            return true;
         }

         for( int i = 0; i < TmpFontAssetNameSuffixes.Length; i++ )
         {
            var suffix = TmpFontAssetNameSuffixes[ i ];
            if( tmpFontAssetName.EndsWith( suffix, StringComparison.OrdinalIgnoreCase ) )
            {
               var candidate = tmpFontAssetName.Substring( 0, tmpFontAssetName.Length - suffix.Length ).TrimEnd();
               if( candidate.Length > 0 && NativeNameToReplacementName.TryGetValue( candidate, out replacementFontName ) )
               {
                  return true;
               }
            }
         }

         XuaLogger.AutoTranslator.Debug(
            "NativeFontReplacements: no TMP replacement match for font asset name '" + tmpFontAssetName
            + "' (tried the exact name and with common TMP naming suffixes stripped)." );
         return false;
      }
   }
}
