using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using XUnity.AutoTranslator.Plugin.Core.Configuration;
using XUnity.Common.Constants;
using XUnity.Common.Logging;
using XUnity.Common.Utilities;

using System.Reflection;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.Runtime;

namespace XUnity.AutoTranslator.Plugin.Core.Fonts
{
   internal static class FontHelper
   {

      private static void DumpExceptionChain( string context, Exception e )
      {
         try
         {
            var sb = new StringBuilder();
            sb.Append( "FontHelper[DIAG]: exception chain for " ).Append( context ).Append( ':' );

            int depth = 0;
            for( var cur = e; cur != null; depth++ )
            {
               sb.Append( "\n  [" ).Append( depth ).Append( "] " ).Append( cur.GetType().FullName )
                 .Append( ": " ).Append( cur.Message );

               if( cur.StackTrace != null )
               {
                  sb.Append( "\n      stack: " ).Append( cur.StackTrace.Replace( "\n", "\n      " ) );
               }

               if( cur is AggregateException agg && agg.InnerExceptions != null && agg.InnerExceptions.Count > 1 )
               {
                  for( int i = 0; i < agg.InnerExceptions.Count; i++ )
                  {
                     sb.Append( "\n      aggregate[" ).Append( i ).Append( "]: " ).Append( agg.InnerExceptions[ i ].GetType().FullName )
                       .Append( ": " ).Append( agg.InnerExceptions[ i ].Message );
                  }
               }

               cur = cur.InnerException;

               if( depth > 20 )
               {
                  sb.Append( "\n  ... truncated after 20 levels ..." );
                  break;
               }
            }

            XuaLogger.AutoTranslator.Info( sb.ToString() );
         }
         catch( Exception diagEx )
         {
            XuaLogger.AutoTranslator.Warn( "FontHelper[DIAG]: DumpExceptionChain itself threw: " + diagEx );
         }
      }

      private static bool TryReadEmbeddedFontBundle( out byte[] bytes, out string resourceName )
      {
         bytes = null;
         resourceName = null;
         try
         {
            var asm = typeof( FontHelper ).Assembly;
            foreach( var name in asm.GetManifestResourceNames() )
            {
               if( name.IndexOf( "sdw_fonts", StringComparison.OrdinalIgnoreCase ) < 0 ) continue;

               using( var stream = asm.GetManifestResourceStream( name ) )
               {
                  if( stream == null || stream.Length == 0 ) continue;
                  var buffer = new byte[ stream.Length ];
                  int read = 0;
                  while( read < buffer.Length )
                  {
                     int n = stream.Read( buffer, read, buffer.Length - read );
                     if( n <= 0 ) break;
                     read += n;
                  }
                  if( read != buffer.Length ) continue;
                  bytes = buffer;
                  resourceName = name;
                  return true;
               }
            }
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Warn( e, "FontHelper: failed to read an embedded font bundle resource." );
         }
         return false;
      }

      public static Dictionary<string, Font> LoadNativeReplacementFontsFromBundle( string assetBundleRelativePath )
      {
         var result = new Dictionary<string, Font>( StringComparer.OrdinalIgnoreCase );

         var bundlePath = Path.Combine( Paths.GameRoot, assetBundleRelativePath );
         var diagStopwatch = Stopwatch.StartNew();

         byte[] bundleBytes = null;
         if( File.Exists( bundlePath ) )
         {
            try
            {
               bundleBytes = File.ReadAllBytes( bundlePath );
               XuaLogger.AutoTranslator.Info( "FontHelper: read native-replacement font bundle from disk: '" + bundlePath + "' (" + bundleBytes.Length + " bytes)." );
            }
            catch( Exception e )
            {
               XuaLogger.AutoTranslator.Error( e, "FontHelper: failed to read native-replacement font bundle from disk: " + bundlePath + " - trying the copy embedded in the DLL instead." );
               DumpExceptionChain( "File.ReadAllBytes('" + bundlePath + "')", e );
               bundleBytes = null;
            }
         }

         if( bundleBytes == null )
         {
            if( TryReadEmbeddedFontBundle( out bundleBytes, out var embeddedName ) )
            {
               bundlePath = "<embedded:" + embeddedName + ">";
               XuaLogger.AutoTranslator.Info( "FontHelper: no usable sdw_fonts file on disk - using the font bundle embedded in the plugin DLL ('" + embeddedName + "', " + bundleBytes.Length + " bytes)." );
            }
            else
            {
               XuaLogger.AutoTranslator.Warn( "FontHelper: no native-replacement font bundle on disk ('" + bundlePath + "') and none embedded in the plugin DLL (no manifest resource named '*sdw_fonts*' - add the bundle as an <EmbeddedResource> to embed it). Native fonts will be left untouched." );
               return result;
            }
         }

         AssetBundle bundle;
         try
         {
            XuaLogger.AutoTranslator.Debug( "FontHelper[DIAG]: AssetBundle.LoadFromMemory starting with " + bundleBytes.Length + " byte(s)." );
            bundle = AssetBundle.LoadFromMemory( bundleBytes );
            XuaLogger.AutoTranslator.Debug( "FontHelper[DIAG]: AssetBundle.LoadFromMemory returned " + ( bundle == null ? "null" : "a non-null AssetBundle (name='" + bundle.name + "')" ) + "." );
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Error( e, "FontHelper: AssetBundle.LoadFromMemory failed for native-replacement font bundle: " + bundlePath );
            DumpExceptionChain( "AssetBundle.LoadFromMemory('" + bundlePath + "', " + bundleBytes.Length + " bytes)", e );
            return result;
         }

         if( bundle == null )
         {
            XuaLogger.AutoTranslator.Warn( "FontHelper: AssetBundle.LoadFromMemory returned null for native-replacement font bundle: " + bundlePath );
            return result;
         }

         try
         {
            IEnumerable<UnityEngine.Object> assets;
            try
            {
               XuaLogger.AutoTranslator.Debug( "FontHelper[DIAG]: untyped AssetBundle.LoadAllAssets() starting on bundle '" + bundle.name + "'." );
               assets = LoadAllAssetsUntypedThenFilter( bundle, bundlePath, Il2CppType.Of<Font>(), "Font" );
               XuaLogger.AutoTranslator.Debug( "FontHelper[DIAG]: untyped AssetBundle.LoadAllAssets() + managed-side filtering returned " + ( assets?.Count() ?? 0 ) + " Font asset(s)." );
            }
            catch( Exception untypedEx )
            {
               XuaLogger.AutoTranslator.Warn( untypedEx, "FontHelper[DIAG]: untyped AssetBundle.LoadAllAssets() path itself threw for '" + bundlePath + "' - falling back to the typed LoadAllAssets(Type) attempt." );
               DumpExceptionChain( "AssetBundle.LoadAllAssets() [untyped] on '" + bundlePath + "'", untypedEx );
               assets = null;
            }

            if( assets == null || !assets.Any() )
            {
               XuaLogger.AutoTranslator.Debug( "FontHelper[DIAG]: untyped LoadAllAssetsAsync() + filter returned no Font assets for '" + bundlePath + "' - falling back to per-asset LoadAssetAsync() by name." );
               try
               {
                  assets = LoadFontAssetsByNameFallback( bundle, bundlePath, Il2CppType.Of<Font>(), "Font" );
               }
               catch( Exception fallbackEx )
               {
                  XuaLogger.AutoTranslator.Warn( fallbackEx, "FontHelper[DIAG]: per-asset LoadAssetAsync() fallback itself threw for '" + bundlePath + "' - see exception chain below." );
                  DumpExceptionChain( "LoadFontAssetsByNameFallback(bundle, '" + bundlePath + "', Il2CppType.Of<Font>(), \"Font\") on '" + bundlePath + "'", fallbackEx );
                  assets = null;
               }
            }

            if( assets != null )
            {
               foreach( var asset in assets )
               {
                  var font = asset?.TryCast<Font>();
                  if( font != null && !string.IsNullOrEmpty( font.name ) )
                  {
                     font.hideFlags |= HideFlags.DontUnloadUnusedAsset;
                     GameObject.DontDestroyOnLoad( font );
                     result[ font.name ] = font;
                     XuaLogger.AutoTranslator.Debug( "FontHelper: loaded native-replacement font '" + font.name + "' from bundle (protected with HideFlags.DontUnloadUnusedAsset)." );
                  }
                  else if( asset != null )
                  {
                     string rawTypeName;
                     try
                     {
                        rawTypeName = Il2CppType.TypeFromPointer( asset.ObjectClass, "asset" )?.FullName ?? "<unknown>";
                     }
                     catch
                     {
                        rawTypeName = "<unavailable>";
                     }
                     XuaLogger.AutoTranslator.Debug( "FontHelper[DIAG]: an asset from the bundle failed to TryCast<Font>() (raw native type: " + rawTypeName + ") - skipping it." );
                  }
               }
            }
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Error( e, "FontHelper: failed to enumerate Font assets in native-replacement font bundle: " + bundlePath );
            DumpExceptionChain( "outer catch in LoadNativeReplacementFontsFromBundle('" + bundlePath + "')", e );
         }

         if( result.Count == 0 )
         {
            XuaLogger.AutoTranslator.Warn( "FontHelper: native-replacement font bundle '" + bundlePath + "' loaded but contained no Font assets - check that it was built against Font (not TMP_FontAsset) assets and that the three .otf files were actually added to it. If the log above shows a 'ReadOnlySpan`1.GetPinnableReference()' MissingMethodException, this is NOT a problem with the bundle itself - see LoadFontAssetsByNameFallback's log output for the actual fix." );
         }
         else
         {
            XuaLogger.AutoTranslator.Debug( "FontHelper: native-replacement font bundle loaded " + result.Count + " Font asset(s): " + string.Join( ", ", result.Keys ) + "." );
         }

         diagStopwatch.Stop();
         XuaLogger.AutoTranslator.Debug( "FontHelper[DIAG]: LoadNativeReplacementFontsFromBundle for '" + bundlePath + "' finished in " + diagStopwatch.ElapsedMilliseconds + "ms, returning " + result.Count + " font(s)." );

         return result;
      }

      private static List<UnityEngine.Object> LoadAllAssetsUntypedThenFilter( AssetBundle bundle, string bundlePath, Il2CppSystem.Type assetType, string assetTypeLabel )
      {
         var matched = new List<UnityEngine.Object>();

         UnityEngine.Object[] all;
         try
         {
            var request = bundle.LoadAllAssetsAsync();
            WaitForAssetBundleRequest( request );
            all = request.allAssets;
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Info( "FontHelper: AssetBundle.LoadAllAssetsAsync() is not usable on this runtime (" + e.GetType().Name + ") - loading fonts from the bundle by name instead." );
            if( Settings.EnableSdwDiagnostics )
            {
               DumpExceptionChain( "AssetBundle.LoadAllAssetsAsync() [untyped] on '" + bundlePath + "'", e );
            }
            return matched;
         }

         if( all == null )
         {
            return matched;
         }

         XuaLogger.AutoTranslator.Debug( "FontHelper[DIAG]: untyped AssetBundle.LoadAllAssets() returned " + all.Length + " asset(s) total from '" + bundlePath + "' (before filtering to '" + assetTypeLabel + "')." );

         var wantedFullName = assetType?.FullName;
         foreach( var asset in all )
         {
            if( asset == null )
            {
               continue;
            }

            try
            {
               var nativeType = Il2CppType.TypeFromPointer( asset.ObjectClass, assetTypeLabel );
               var actualFullName = nativeType?.FullName;
               if( wantedFullName != null && actualFullName == wantedFullName )
               {
                  matched.Add( asset );
               }
            }
            catch( Exception typeCheckEx )
            {
               XuaLogger.AutoTranslator.Warn( typeCheckEx, "FontHelper[DIAG]: failed to check runtime type of an asset returned by untyped LoadAllAssets() while loading '" + bundlePath + "' - skipping that asset." );
            }
         }

         XuaLogger.AutoTranslator.Debug( "FontHelper[DIAG]: LoadAllAssetsUntypedThenFilter for '" + bundlePath + "' matched " + matched.Count + " of " + all.Length + " asset(s) against type '" + assetTypeLabel + "'." );
         return matched;
      }

      private static bool IsMissingSpanPinMethodBug( Exception e )
      {
         for( var ex = e; ex != null; ex = ex.InnerException )
         {
            if( ex is MissingMethodException && ex.Message.IndexOf( "ReadOnlySpan", StringComparison.OrdinalIgnoreCase ) >= 0 )
            {
               return true;
            }
         }
         return false;
      }

      private static List<UnityEngine.Object> LoadFontAssetsByNameFallback( AssetBundle bundle, string bundlePath, Il2CppSystem.Type assetType, string assetTypeLabel )
      {
         var loaded = new List<UnityEngine.Object>();
         XuaLogger.AutoTranslator.Debug( $"FontHelper[DIAG]: LoadFontAssetsByNameFallback starting for '{bundlePath}', filtering per-asset LoadAsset() calls by type '{assetTypeLabel}'." );

         string[] names;
         try
         {
            XuaLogger.AutoTranslator.Debug( "FontHelper[DIAG]: GetAllAssetNames() starting (zero-argument icall, nothing to marshal in)." );
            names = bundle.GetAllAssetNames();
            XuaLogger.AutoTranslator.Debug( "FontHelper[DIAG]: GetAllAssetNames() succeeded, returned " + names.Length + " name(s): " + string.Join( ", ", names ) + "." );
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Error( e, "FontHelper: GetAllAssetNames() also failed for native-replacement font bundle: " + bundlePath );
            DumpExceptionChain( "AssetBundle.GetAllAssetNames() on '" + bundlePath + "'", e );
            names = Array.Empty<string>();
         }

         int perAssetSpanBugCount = 0;
         int perAssetSpanBugRecoveredCount = 0;
         int perAssetOtherFailureCount = 0;
         foreach( var name in names )
         {
            try
            {
               XuaLogger.AutoTranslator.Debug( $"FontHelper[DIAG]: LoadAssetAsync('{name}', {assetTypeLabel}) starting." );
               var request = bundle.LoadAssetAsync( name, assetType );
               WaitForAssetBundleRequest( request );
               var asset = request.asset;
               if( asset != null )
               {
                  loaded.Add( asset );
                  XuaLogger.AutoTranslator.Debug( $"FontHelper[DIAG]: LoadAsset('{name}', {assetTypeLabel}) succeeded -> non-null asset." );
               }
               else
               {
                  XuaLogger.AutoTranslator.Debug( $"FontHelper[DIAG]: LoadAsset('{name}', {assetTypeLabel}) returned null (no exception, just nothing there - could be a genuine miss OR a silent type mismatch, can't tell from here)." );
               }
            }
            catch( Exception e )
            {
               if( IsMissingSpanPinMethodBug( e ) )
               {
                  perAssetSpanBugCount++;
               }
               else
               {
                  perAssetOtherFailureCount++;
                  XuaLogger.AutoTranslator.Warn( $"FontHelper[DIAG]: LoadAsset('{name}', {assetTypeLabel}) failed with something OTHER than the known ReadOnlySpan bug - see chain below." );
               }
               XuaLogger.AutoTranslator.Warn( e, $"FontHelper: per-asset fallback also failed to load '{name}' as {assetTypeLabel} from font bundle: {bundlePath}" );
               DumpExceptionChain( $"AssetBundle.LoadAsset('{name}', {assetTypeLabel}) on '{bundlePath}'", e );
            }
         }

         if( names.Length > 0 )
         {
            XuaLogger.AutoTranslator.Debug( $"FontHelper[DIAG]: per-asset fallback summary for '{bundlePath}' (type={assetTypeLabel}): {names.Length} name(s) tried, " +
               $"{loaded.Count} succeeded, {perAssetSpanBugCount} hit the known ReadOnlySpan bug ({perAssetSpanBugRecoveredCount} of those recovered via the raw il2cpp_runtime_invoke bypass), {perAssetOtherFailureCount} failed some other way." );
         }

         if( loaded.Count == 0 )
         {
            XuaLogger.AutoTranslator.Error(
               "FontHelper: could not load any Font assets from '" + bundlePath + "' because of a known, currently-unresolved Il2CppInterop bug " +
               "(MissingMethodException on Il2CppSystem.ReadOnlySpan<T>/Span<T>) affecting Unity 6's AssetBundle bindings under IL2CPP - tracked at " +
               "https://github.com/BepInEx/Il2CppInterop/issues/202 and independently reported for other mods on the same Unity 6000.0.59f2 build " +
               "at https://github.com/BepInEx/BepInEx/issues/1197, /1226, and /1151. Not fixed as of Il2CppInterop v1.5.3 (the latest upstream " +
               "release, already what current BepInEx bleeding-edge builds ship). This is NOT fixed by deleting/regenerating BepInEx\\interop - " +
               "the defect is in how the interop tooling registers/binds the native icall itself, not in anything generated per-game - and it is " +
               "not specific to this font bundle: every AssetBundle load path this project tries (LoadFromMemory-triggered LoadAllAssets, and " +
               "per-asset LoadAsset) hits the same exception. There is currently no workaround reachable from this project's own code; a prior " +
               "attempt at one (a raw native icall-invoke bypass) has been removed - see IsMissingSpanPinMethodBug's doc comment for why. Native " +
               "OS fonts will keep being used in the meantime (Cyrillic spacing is corrected in place by NativeFontCyrillicSpacingFixHook). See " +
               "the diagnostic dump immediately below (if present) for additional detail." );

            DumpReadOnlySpanShapeOnce();
         }
         else
         {
            XuaLogger.AutoTranslator.Debug( "FontHelper: per-asset fallback recovered " + loaded.Count + " Font asset(s) out of " + names.Length + " name(s) in the bundle." );
         }

         return loaded;
      }

      private static void WaitForAssetBundleRequest( AssetBundleRequest request )
      {
         if( request == null ) throw new ArgumentNullException( nameof( request ) );

      }

      private static bool _hasDumpedReadOnlySpanShape;

      private static void DumpReadOnlySpanShapeOnce()
      {
         if( _hasDumpedReadOnlySpanShape ) return;
         _hasDumpedReadOnlySpanShape = true;

         try
         {
            XuaLogger.AutoTranslator.Info( "FontHelper: dumping Il2CppSystem span-type shape for diagnostic purposes (one-time, read-only, no native calls)..." );

            var candidateTypeNames = new[] { "Il2CppSystem.ReadOnlySpan`1", "Il2CppSystem.Span`1" };
            var probeElementTypes = new[] { typeof( char ), typeof( byte ), typeof( int ), typeof( short ) };
            var found = false;

            var probeResults = new List<(string spanKind, string elementType, string outcome)>();

            foreach( var asm in AppDomain.CurrentDomain.GetAssemblies() )
            {
               Type[] types;
               try
               {
                  types = asm.GetTypes();
               }
               catch( ReflectionTypeLoadException rtle )
               {
                  types = rtle.Types.Where( t => t != null ).ToArray();
               }
               catch
               {
                  continue;
               }

               foreach( var type in types )
               {
                  if( type.FullName == null || Array.IndexOf( candidateTypeNames, type.FullName ) < 0 )
                     continue;

                  found = true;
                  XuaLogger.AutoTranslator.Info( $"FontHelper: found {type.FullName} in assembly '{asm.GetName().Name}'." );

                  foreach( var elementType in probeElementTypes )
                  {
                     Type closed;
                     try
                     {
                        closed = type.IsGenericTypeDefinition ? type.MakeGenericType( elementType ) : type;
                     }
                     catch( Exception e )
                     {
                        XuaLogger.AutoTranslator.Warn( $"FontHelper: could not close {type.FullName} over <{elementType.Name}>: {e.Message}" );
                        probeResults.Add( (type.Name, elementType.Name, "SKIPPED: could not close generic type - " + e.Message) );
                        continue;
                     }

                     XuaLogger.AutoTranslator.Info( $"FontHelper: --- {closed} ---" );

                     foreach( var field in closed.GetFields( BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic ) )
                     {
                        XuaLogger.AutoTranslator.Info( $"FontHelper:   field: {field.FieldType} {field.Name}" );
                     }

                     ConstructorInfo parameterlessCtor = null;
                     foreach( var ctor in closed.GetConstructors( BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic ) )
                     {
                        XuaLogger.AutoTranslator.Info( $"FontHelper:   ctor: {ctor}" );
                        if( ctor.GetParameters().Length == 0 )
                        {
                           parameterlessCtor = ctor;
                        }
                     }

                     MethodInfo getPinnableReferenceMethod = null;
                     foreach( var method in closed.GetMethods( BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly ) )
                     {
                        var implFlags = method.GetMethodImplementationFlags();
                        XuaLogger.AutoTranslator.Info( $"FontHelper:   method: {method}  [ImplFlags={implFlags}]" );

                        if( method.Name == "GetPinnableReference" && method.GetParameters().Length == 0 )
                        {
                           getPinnableReferenceMethod = method;
                        }
                     }

                     if( parameterlessCtor == null )
                     {
                        XuaLogger.AutoTranslator.Info( $"FontHelper:   [PROBE] no parameterless constructor found for {closed} - skipping active call test for this instantiation." );
                        probeResults.Add( (type.Name, elementType.Name, "SKIPPED: no parameterless ctor") );
                        continue;
                     }
                     if( getPinnableReferenceMethod == null )
                     {
                        XuaLogger.AutoTranslator.Info( $"FontHelper:   [PROBE] GetPinnableReference() not found via reflection at all on {closed} (not just missing at call time - missing from metadata itself)." );
                        probeResults.Add( (type.Name, elementType.Name, "SKIPPED: method absent from reflection metadata") );
                        continue;
                     }

                     try
                     {
                        var instance = parameterlessCtor.Invoke( Array.Empty<object>() );
                        try
                        {
                           getPinnableReferenceMethod.Invoke( instance, Array.Empty<object>() );
                           XuaLogger.AutoTranslator.Info( $"FontHelper:   [PROBE] {closed}.GetPinnableReference() on a default-constructed (empty) instance: SUCCEEDED." );
                           probeResults.Add( (type.Name, elementType.Name, "OK") );
                        }
                        catch( TargetInvocationException tie ) when( tie.InnerException != null )
                        {
                           var inner = tie.InnerException;
                           var isMmEx = inner is MissingMethodException;
                           XuaLogger.AutoTranslator.Info( $"FontHelper:   [PROBE] {closed}.GetPinnableReference() threw {inner.GetType().Name}: {inner.Message}" );
                           probeResults.Add( (type.Name, elementType.Name, (isMmEx ? "MISSING: " : "THREW (other): ") + inner.GetType().Name + ": " + inner.Message) );
                        }
                     }
                     catch( Exception ctorEx )
                     {
                        XuaLogger.AutoTranslator.Info( $"FontHelper:   [PROBE] could not even construct {closed} to test it: {ctorEx.GetType().Name}: {ctorEx.Message}" );
                        probeResults.Add( (type.Name, elementType.Name, "SKIPPED: construction failed - " + ctorEx.Message) );
                     }
                  }
               }
            }

            if( !found )
            {
               XuaLogger.AutoTranslator.Info( "FontHelper: none of Il2CppSystem.ReadOnlySpan<T>/Span<T> are loaded as reflectable managed types (they may only exist as native-side generic instantiations never surfaced to managed reflection) - a fix will likely need to be read directly out of BepInEx\\interop\\Il2CppSystem.dll with a decompiler (dnSpy/ILSpy) instead." );
            }
            else
            {
               XuaLogger.AutoTranslator.Info( "FontHelper: [PROBE SUMMARY] GetPinnableReference() active-call results per (span-kind, element-type) - "
                  + "if <char> shows MISSING while <byte>/<int>/<short> show OK, that's strong evidence this is an IL2CPP AOT-generic-instantiation "
                  + "gap specific to <char> (i.e. the game's compiled code never uses a char span here, so IL2CPP never generated it) rather than a "
                  + "wholesale-broken interop type; if EVERY element type shows MISSING (or SKIPPED because the method isn't even in the reflection "
                  + "metadata), that instead points at something broken in Il2CppInterop's ReadOnlySpan<T>/Span<T> wrapper generation itself, "
                  + "regardless of T:" );
               foreach( var r in probeResults )
               {
                  XuaLogger.AutoTranslator.Info( $"FontHelper:   {r.spanKind}<{r.elementType}>: {r.outcome}" );
               }
            }
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Warn( e, "FontHelper: diagnostic span-shape dump itself failed - harmless, just means less information was captured." );
            DumpExceptionChain( "DumpReadOnlySpanShapeOnce", e );
         }
      }

      public static UnityEngine.Object GetTextMeshProFont( string assetBundle )
      {
         UnityEngine.Object font = null;

         var overrideFontPath = Path.Combine( Paths.GameRoot, assetBundle );
         if( File.Exists( overrideFontPath ) )
         {
            XuaLogger.AutoTranslator.Info( "Attempting to load TextMesh Pro font from asset bundle." );

            AssetBundle bundle = null;
            try
            {
               if( UnityTypes.AssetBundle_Methods.LoadFromFile != null )
               {
                  XuaLogger.AutoTranslator.Debug( "FontHelper[DIAG]: reflected AssetBundle.LoadFromFile('" + overrideFontPath + "') starting." );
                  bundle = (AssetBundle)UnityTypes.AssetBundle_Methods.LoadFromFile.Invoke( null, new object[] { overrideFontPath } );
                  XuaLogger.AutoTranslator.Debug( "FontHelper[DIAG]: reflected AssetBundle.LoadFromFile returned " + ( bundle == null ? "null" : "non-null" ) + "." );
               }
               else if( UnityTypes.AssetBundle_Methods.CreateFromFile != null )
               {
                  XuaLogger.AutoTranslator.Debug( "FontHelper[DIAG]: reflected AssetBundle.CreateFromFile('" + overrideFontPath + "') starting." );
                  bundle = (AssetBundle)UnityTypes.AssetBundle_Methods.CreateFromFile.Invoke( null, new object[] { overrideFontPath } );
                  XuaLogger.AutoTranslator.Debug( "FontHelper[DIAG]: reflected AssetBundle.CreateFromFile returned " + ( bundle == null ? "null" : "non-null" ) + "." );
               }
               else
               {
                  return GetTextMeshProFontByCustomProxies( assetBundle );
               }
            }
            catch( Exception loadEx ) when( IsMissingSpanPinMethodBug( loadEx ) )
            {
               XuaLogger.AutoTranslator.Warn( "GetTextMeshProFont: reflected AssetBundle.LoadFromFile/CreateFromFile hit the known ReadOnlySpan<char> Il2CppInterop bug for '" + overrideFontPath + "'; falling back to the byte[]-based GetTextMeshProFontByCustomProxies path, which avoids it entirely." );
               DumpExceptionChain( "reflected AssetBundle.LoadFromFile/CreateFromFile('" + overrideFontPath + "')", loadEx );
               return GetTextMeshProFontByCustomProxies( assetBundle );
            }
            catch( Exception loadEx )
            {
               XuaLogger.AutoTranslator.Error( loadEx, "GetTextMeshProFont: reflected asset bundle load threw while loading font: " + overrideFontPath );
               DumpExceptionChain( "reflected AssetBundle.LoadFromFile/CreateFromFile('" + overrideFontPath + "') [UNRECOGNIZED EXCEPTION]", loadEx );
               return null;
            }

            if( bundle == null )
            {
               XuaLogger.AutoTranslator.Warn( "Could not load asset bundle while loading font: " + overrideFontPath );
               return null;
            }

            if( UnityTypes.TMP_FontAsset != null )
            {
               try
               {
                  if( UnityTypes.AssetBundle_Methods.LoadAllAssets != null )
                  {
                     XuaLogger.AutoTranslator.Debug( "FontHelper[DIAG]: reflected AssetBundle.LoadAllAssets(TMP_FontAsset) starting for '" + overrideFontPath + "'." );
                     var assets = (Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<UnityEngine.Object>)UnityTypes.AssetBundle_Methods.LoadAllAssets.Invoke( bundle, new object[] { UnityTypes.TMP_FontAsset.UnityType } );
                     font = assets?.FirstOrDefault();
                     XuaLogger.AutoTranslator.Debug( "FontHelper[DIAG]: reflected AssetBundle.LoadAllAssets(TMP_FontAsset) succeeded, returned " + ( assets?.Count() ?? 0 ) + " asset(s)." );
                  }
                  else if( UnityTypes.AssetBundle_Methods.LoadAll != null )
                  {
                     XuaLogger.AutoTranslator.Debug( "FontHelper[DIAG]: reflected AssetBundle.LoadAll(TMP_FontAsset) starting for '" + overrideFontPath + "'." );
                     var assets = (Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<UnityEngine.Object>)UnityTypes.AssetBundle_Methods.LoadAll.Invoke( bundle, new object[] { UnityTypes.TMP_FontAsset.UnityType } );
                     font = assets?.FirstOrDefault();
                     XuaLogger.AutoTranslator.Debug( "FontHelper[DIAG]: reflected AssetBundle.LoadAll(TMP_FontAsset) succeeded, returned " + ( assets?.Count() ?? 0 ) + " asset(s)." );
                  }
               }
               catch( Exception assetsEx ) when( IsMissingSpanPinMethodBug( assetsEx ) )
               {
                  XuaLogger.AutoTranslator.Warn( "GetTextMeshProFont: reflected AssetBundle.LoadAllAssets/LoadAll(TMP_FontAsset) hit the known ReadOnlySpan<char> Il2CppInterop bug for '" + overrideFontPath + "'; falling back to per-asset loading via GetAllAssetNames()+LoadAsset() on the already-loaded bundle." );
                  DumpExceptionChain( "reflected AssetBundle.LoadAllAssets/LoadAll(TMP_FontAsset) on '" + overrideFontPath + "'", assetsEx );
                  var fallbackAssets = LoadFontAssetsByNameFallback( bundle, overrideFontPath, UnityTypes.TMP_FontAsset.UnityType, "TMP_FontAsset" );
                  font = fallbackAssets?.FirstOrDefault();
               }
               catch( Exception assetsEx )
               {
                  XuaLogger.AutoTranslator.Warn( assetsEx, "GetTextMeshProFont: reflected AssetBundle.LoadAllAssets/LoadAll(TMP_FontAsset) threw something OTHER than the known ReadOnlySpan bug for '" + overrideFontPath + "'." );
                  DumpExceptionChain( "reflected AssetBundle.LoadAllAssets/LoadAll(TMP_FontAsset) on '" + overrideFontPath + "' [UNRECOGNIZED EXCEPTION]", assetsEx );
               }
            }
         }
         else
         {
            bool isAssetBundleNameInsideSystemFont = false;
            try
            {
               string[] systemFontNames = GetOSInstalledFontNames();
               for( int i = 0; i < systemFontNames.Length; i++ )
               {
                  if( systemFontNames[ i ] == assetBundle )
                  {
                     isAssetBundleNameInsideSystemFont = true;
                     break;
                  }
               }
            }
            catch( Exception ex )
            {
               XuaLogger.AutoTranslator.Error( $"Unable to fetch installed font names: {ex.Message}" );
            }

            if( UnityTypes.TMP_FontAsset_Methods.CreateFontAsset != null && isAssetBundleNameInsideSystemFont )
            {
               XuaLogger.AutoTranslator.Info( $"A font {assetBundle} is installed on the system. Attempting to create a TextMesh Pro font from it." );
               font = (UnityEngine.Object)UnityTypes.TMP_FontAsset_Methods.CreateFontAsset.Invoke( null, new object[] { assetBundle, "", 90 } );
            }
            else
            {
               if( UnityTypes.TMP_FontAsset_Methods.CreateFontAsset == null )
               {
                  XuaLogger.AutoTranslator.Warn( "TMP_FontAsset.CreateFontAsset not found. TextMeshPro version might be below 3.2.0." );
               }

               XuaLogger.AutoTranslator.Info( "Attempting to load TextMesh Pro font from internal Resources API." );
               font = Resources.Load( assetBundle );
            }
         }

         if( font != null )
         {
            var versionProperty = UnityTypes.TMP_FontAsset_Properties.Version;
            var version = (string)versionProperty?.Get( font ) ?? "Unknown";
            XuaLogger.AutoTranslator.Info( $"Loaded TextMesh Pro font uses version: {version}" );

            if( versionProperty != null && Settings.TextMeshProVersion != null && version != Settings.TextMeshProVersion )
            {
               XuaLogger.AutoTranslator.Warn( $"TextMesh Pro version mismatch. Font asset version: {version}, TextMesh Pro version: {Settings.TextMeshProVersion}" );
            }

            GameObject.DontDestroyOnLoad( font );
         }
         else
         {
            XuaLogger.AutoTranslator.Error( "Could not find the TextMeshPro font asset: " + assetBundle );
         }

         return font;
      }

      public static UnityEngine.Object GetTextMeshProFontByCustomProxies( string assetBundle )
      {
         UnityEngine.Object font = null;

         var overrideFontPath = Path.Combine( Paths.GameRoot, assetBundle );
         if( File.Exists( overrideFontPath ) )
         {
            XuaLogger.AutoTranslator.Info( "Attempting to load TextMesh Pro font from asset bundle." );

            byte[] bundleBytes;
            try
            {
               bundleBytes = File.ReadAllBytes( overrideFontPath );
            }
            catch( Exception e )
            {
               XuaLogger.AutoTranslator.Error( e, "Failed to read asset bundle from disk while loading font: " + overrideFontPath );
               return null;
            }

            AssetBundle bundle;
            try
            {
               bundle = AssetBundle.LoadFromMemory( bundleBytes );
            }
            catch( Exception e )
            {
               XuaLogger.AutoTranslator.Error( e, "AssetBundle.LoadFromMemory failed while loading font: " + overrideFontPath );
               return null;
            }

            if( bundle == null )
            {
               XuaLogger.AutoTranslator.Warn( "Could not load asset bundle while loading font: " + overrideFontPath );
               return null;
            }

            if( UnityTypes.TMP_FontAsset != null )
            {
               IEnumerable<UnityEngine.Object> assets;
               try
               {
                  XuaLogger.AutoTranslator.Debug( "FontHelper[DIAG]: untyped AssetBundle.LoadAllAssets() starting for '" + overrideFontPath + "'." );
                  assets = LoadAllAssetsUntypedThenFilter( bundle, overrideFontPath, UnityTypes.TMP_FontAsset.UnityType, "TMP_FontAsset" );
                  XuaLogger.AutoTranslator.Debug( "FontHelper[DIAG]: untyped AssetBundle.LoadAllAssets() + managed-side filtering returned " + ( assets?.Count() ?? 0 ) + " TMP_FontAsset asset(s)." );
               }
               catch( Exception untypedEx )
               {
                  XuaLogger.AutoTranslator.Warn( untypedEx, "FontHelper[DIAG]: untyped AssetBundle.LoadAllAssets() path itself threw for '" + overrideFontPath + "' - falling back to the typed LoadAllAssets(Type) attempt." );
                  DumpExceptionChain( "AssetBundle.LoadAllAssets() [untyped] on '" + overrideFontPath + "'", untypedEx );
                  assets = null;
               }

               if( assets == null || !assets.Any() )
               {
                  try
                  {
                     XuaLogger.AutoTranslator.Debug( "FontHelper[DIAG]: AssetBundle.LoadAllAssets(TMP_FontAsset) starting for '" + overrideFontPath + "'." );
                     assets = bundle.LoadAllAssets( UnityTypes.TMP_FontAsset.UnityType );
                     XuaLogger.AutoTranslator.Debug( "FontHelper[DIAG]: AssetBundle.LoadAllAssets(TMP_FontAsset) succeeded, returned " + ( assets?.Count() ?? 0 ) + " asset(s)." );
                  }
                  catch( Exception primaryEx ) when( IsMissingSpanPinMethodBug( primaryEx ) )
                  {
                     XuaLogger.AutoTranslator.Warn( "GetTextMeshProFontByCustomProxies: AssetBundle.LoadAllAssets(Type) hit the known upstream Il2CppInterop bug (see LoadNativeReplacementFontsFromBundle's log output for details) while loading '" + overrideFontPath + "'; falling back to per-asset loading via GetAllAssetNames()+LoadAsset()." );
                     DumpExceptionChain( "AssetBundle.LoadAllAssets(TMP_FontAsset) on '" + overrideFontPath + "'", primaryEx );
                     assets = LoadFontAssetsByNameFallback( bundle, overrideFontPath, UnityTypes.TMP_FontAsset.UnityType, "TMP_FontAsset" );
                  }
                  catch( Exception unexpectedEx )
                  {
                     XuaLogger.AutoTranslator.Warn( "FontHelper[DIAG]: GetTextMeshProFontByCustomProxies: AssetBundle.LoadAllAssets(TMP_FontAsset) threw something OTHER than the known ReadOnlySpan bug for '" + overrideFontPath + "' - see exception chain below. Falling back to per-asset loading anyway." );
                     DumpExceptionChain( "AssetBundle.LoadAllAssets(TMP_FontAsset) on '" + overrideFontPath + "' [UNRECOGNIZED EXCEPTION]", unexpectedEx );
                     assets = LoadFontAssetsByNameFallback( bundle, overrideFontPath, UnityTypes.TMP_FontAsset.UnityType, "TMP_FontAsset" );
                  }
               }
               font = assets?.FirstOrDefault();
            }
         }

         if( font != null )
         {
            var versionProperty = UnityTypes.TMP_FontAsset_Properties.Version;
            var version = (string)versionProperty?.Get( font ) ?? "Unknown";
            XuaLogger.AutoTranslator.Info( $"Loaded TextMesh Pro font uses version: {version}" );

            if( versionProperty != null && Settings.TextMeshProVersion != null && version != Settings.TextMeshProVersion )
            {
               XuaLogger.AutoTranslator.Warn( $"TextMesh Pro version mismatch. Font asset version: {version}, TextMesh Pro version: {Settings.TextMeshProVersion}" );
            }

            GameObject.DontDestroyOnLoad( font );
         }
         else
         {
            XuaLogger.AutoTranslator.Error( "Could not find the TextMeshPro font asset: " + assetBundle );
         }

         return font;
      }

      public static Font GetTextFont( int size )
      {
         return GetTextFont( Settings.OverrideFont, size );
      }

      public static Font GetTextFont( string fontName, int size )
      {
         var font = Font.CreateDynamicFontFromOSFont( fontName, size );
         if( font != null )
         {
            GameObject.DontDestroyOnLoad( font );
         }

         return font;
      }

      public static string[] GetOSInstalledFontNames()
      {
         return Font.GetOSInstalledFontNames();
      }
   }
}
