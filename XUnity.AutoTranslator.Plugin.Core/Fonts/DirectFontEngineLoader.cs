using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using XUnity.Common.Logging;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace XUnity.AutoTranslator.Plugin.Core.Fonts
{
   internal static class DirectFontEngineLoader
   {
      private static string ResourceNameFor( string fontFileName ) => "Fonts.sdwfonts." + fontFileName + ".otf";

      private static bool _hasRun;

      public static byte[] TryReadEmbeddedOtfBytes( string nativeFontName )
      {
         try
         {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = ResourceNameFor( nativeFontName );
            using( var stream = assembly.GetManifestResourceStream( resourceName ) )
            {
               if( stream == null )
               {
                  XuaLogger.AutoTranslator.Warn( "DirectFontEngineLoader: no embedded resource '" + resourceName + "'." );
                  return null;
               }

               using( var ms = new MemoryStream() )
               {
                  stream.CopyTo( ms );
                  return ms.ToArray();
               }
            }
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Warn( e, "DirectFontEngineLoader: failed to read embedded OTF bytes for '" + nativeFontName + "'." );
            return null;
         }
      }

      public static void RunDiagnosticsOnce()
      {
         if( _hasRun ) return;
         _hasRun = true;

         XuaLogger.AutoTranslator.Info( "DirectFontEngineLoader: starting byte-direct FontEngine diagnostic (bypasses OS font table and AssetBundle entirely - see this class's doc comment for why those two are dead ends on this build)." );

         if( !FontEngineReflection.IsAvailable )
         {
            XuaLogger.AutoTranslator.Warn( "DirectFontEngineLoader: could not find UnityEngine.TextCore.LowLevel.FontEngine (or its LoadFontFace(byte[], int) overload) in any loaded assembly - this game's TMPro/TextCore build may not expose it the way Unity 6000.0's public docs describe it. Aborting diagnostic; native-font-mode falls back to whatever FontCache/NativeFontReplacements already do (currently: nothing, on this build)." );
            return;
         }

         foreach( var nativeFontName in new[] { "UIMainFont", "UIMainFont_Subset", "UISubFont" } )
         {
            var bytes = TryReadEmbeddedOtfBytes( nativeFontName );
            if( bytes == null ) continue;

            TryLoadAndProbe( nativeFontName, bytes );
         }

         FontAssetSurfaceScan.LogAvailableSurface();
      }

      private static void TryLoadAndProbe( string debugName, byte[] otfBytes )
      {
         try
         {
            FontEngineReflection.InitializeFontEngine();

            var loadResult = FontEngineReflection.LoadFontFaceFromBytes( otfBytes, 90 );
            if( !FontEngineReflection.IsSuccess( loadResult ) )
            {
               XuaLogger.AutoTranslator.Warn( "DirectFontEngineLoader: FontEngine.LoadFontFace(byte[" + otfBytes.Length + "], 90) FAILED for '" + debugName + "' - result: " + loadResult + ". Either the bytes aren't a face FontEngine recognises, or this build's LoadFontFace(byte[], int) behaves differently than documented." );
               return;
            }

            XuaLogger.AutoTranslator.Info( "DirectFontEngineLoader: FontEngine successfully parsed '" + debugName + "' (" + otfBytes.Length + " byte(s)) directly from memory - no OS font table, no AssetBundle involved." );

            var faceInfoDump = FontEngineReflection.DescribeFaceInfo();
            if( faceInfoDump != null )
            {
               XuaLogger.AutoTranslator.Info( "DirectFontEngineLoader: '" + debugName + "' face info: " + faceInfoDump );
            }

            ProbeGlyph( debugName, 'A', "Latin 'A'" );
            ProbeGlyph( debugName, '\u0416', "Cyrillic 'Ж' (U+0416)" );
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Warn( e, "DirectFontEngineLoader: diagnostic probe threw for '" + debugName + "'." );
         }
      }

      private static void ProbeGlyph( string debugName, char c, string label )
      {
         try
         {
            var hasGlyph = FontEngineReflection.TryGetGlyphIndex( c, out var glyphIndex );
            XuaLogger.AutoTranslator.Info( "DirectFontEngineLoader: '" + debugName + "' " + label + " -> " +
               ( hasGlyph && glyphIndex != 0
                  ? "glyph index " + glyphIndex + " (present)"
                  : "NO GLYPH (font does not cover this character)" ) );
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Warn( e, "DirectFontEngineLoader: glyph probe for " + label + " threw for '" + debugName + "'." );
         }
      }

      private static class FontEngineReflection
      {
         private static readonly Type FontEngineType;
         private static readonly MethodInfo InitializeMethod;
         private static readonly MethodInfo LoadFontFaceBytesMethod;
         private static readonly int LoadFontFaceBytesArgCount;
         private static readonly Type LoadFontFaceBytesParamType;
         private static readonly MethodInfo SetFaceSizeMethod;
         private static readonly MethodInfo GetFaceInfoMethod;
         private static readonly MethodInfo TryGetGlyphIndexMethod;

         public static bool IsAvailable => FontEngineType != null && LoadFontFaceBytesMethod != null;

         static FontEngineReflection()
         {
            try
            {
               FontEngineType = FindType( "UnityEngine.TextCore.LowLevel.FontEngine" );
               if( FontEngineType == null )
               {
                  XuaLogger.AutoTranslator.Debug( "DirectFontEngineLoader: type 'UnityEngine.TextCore.LowLevel.FontEngine' not found in any loaded assembly." );
                  return;
               }

               InitializeMethod = FontEngineType.GetMethod( "InitializeFontEngine", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null );

               var allLoadFontFaceOverloads = FontEngineType
                  .GetMethods( BindingFlags.Public | BindingFlags.Static )
                  .Where( m => m.Name == "LoadFontFace" )
                  .ToList();

               foreach( var overload in allLoadFontFaceOverloads )
               {
                  var shape = string.Join( ", ", overload.GetParameters().Select( p => p.ParameterType.Name ) );
                  XuaLogger.AutoTranslator.Info( "DirectFontEngineLoader: FontEngine.LoadFontFace overload found on this build: (" + shape + ")." );
               }

               if( allLoadFontFaceOverloads.Count == 0 )
               {
                  XuaLogger.AutoTranslator.Warn( "DirectFontEngineLoader: FontEngine was found but exposes NO 'LoadFontFace' overload at all on this build - likely stripped by IL2CPP's managed-code stripping because nothing in the game's own compiled code calls it directly." );
               }

               var bytesOverload = allLoadFontFaceOverloads
                  .Where( m => m.GetParameters().Length >= 1 && IsByteArrayShaped( m.GetParameters()[ 0 ].ParameterType ) )
                  .OrderByDescending( m => m.GetParameters().Length == 2 && m.GetParameters()[ 1 ].ParameterType == typeof( int ) )
                  .FirstOrDefault();

               if( bytesOverload != null )
               {
                  LoadFontFaceBytesMethod = bytesOverload;
                  LoadFontFaceBytesArgCount = bytesOverload.GetParameters().Length;
                  LoadFontFaceBytesParamType = bytesOverload.GetParameters()[ 0 ].ParameterType;
               }

               SetFaceSizeMethod = FontEngineType
                  .GetMethods( BindingFlags.Public | BindingFlags.Static )
                  .FirstOrDefault( m =>
                  {
                     if( m.Name != "SetFaceSize" ) return false;
                     var p = m.GetParameters();
                     return p.Length == 1 && p[ 0 ].ParameterType == typeof( int );
                  } );

               GetFaceInfoMethod = FontEngineType.GetMethod( "GetFaceInfo", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null );

               TryGetGlyphIndexMethod = FontEngineType
                  .GetMethods( BindingFlags.Public | BindingFlags.Static )
                  .FirstOrDefault( m =>
                  {
                     if( m.Name != "TryGetGlyphIndex" ) return false;
                     var p = m.GetParameters();
                     return p.Length == 2 && p[ 1 ].IsOut;
                  } );

               XuaLogger.AutoTranslator.Debug( "DirectFontEngineLoader: resolved FontEngine from assembly '" + FontEngineType.Assembly.GetName().Name +
                  "' - InitializeFontEngine:" + ( InitializeMethod != null ) +
                  " LoadFontFace(byte[]...):" + ( LoadFontFaceBytesMethod != null ? "(" + LoadFontFaceBytesArgCount + " arg(s))" : "false" ) +
                  " SetFaceSize:" + ( SetFaceSizeMethod != null ) +
                  " GetFaceInfo:" + ( GetFaceInfoMethod != null ) +
                  " TryGetGlyphIndex:" + ( TryGetGlyphIndexMethod != null ) + "." );
            }
            catch( Exception e )
            {
               XuaLogger.AutoTranslator.Warn( e, "DirectFontEngineLoader: failed resolving UnityEngine.TextCore.LowLevel.FontEngine via reflection." );
            }
         }

         public static void InitializeFontEngine()
         {
            InitializeMethod?.Invoke( null, null );
         }

         public static object LoadFontFaceFromBytes( byte[] bytes, int pointSize )
         {
            if( LoadFontFaceBytesMethod == null )
            {
               throw new InvalidOperationException( "FontEngine.LoadFontFace(byte-array...) was not resolved on this build." );
            }

            var arrayArg = BuildByteArrayArgument( LoadFontFaceBytesParamType, bytes );

            if( arrayArg is Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase il2CppObj )
            {
               XuaLogger.AutoTranslator.Info( "DirectFontEngineLoader[DIAG]: arrayArg.WasCollected immediately after construction (before any native call) = " + il2CppObj.WasCollected + "." );
            }

            object result;
            try
            {
               if( LoadFontFaceBytesArgCount == 2 )
               {
                  result = LoadFontFaceBytesMethod.Invoke( null, new object[] { arrayArg, pointSize } );
               }
               else
               {
                  result = LoadFontFaceBytesMethod.Invoke( null, new object[] { arrayArg } );
                  if( IsSuccess( result ) && SetFaceSizeMethod != null )
                  {
                     SetFaceSizeMethod.Invoke( null, new object[] { pointSize } );
                  }
                  else if( SetFaceSizeMethod == null )
                  {
                     XuaLogger.AutoTranslator.Warn( "DirectFontEngineLoader: loaded the face via the byte-array-only LoadFontFace overload but FontEngine.SetFaceSize(int) wasn't found - face info/glyph metrics below may be at whatever default point size LoadFontFace(byte[]) uses, not the requested " + pointSize + "." );
                  }
               }
            }
            finally
            {
               GC.KeepAlive( arrayArg );
            }
            return result;
         }

         private static bool IsByteArrayShaped( Type parameterType )
         {
            if( parameterType == typeof( byte[] ) ) return true;

            if( parameterType.IsGenericType )
            {
               var genericArgs = parameterType.GetGenericArguments();
               if( genericArgs.Length == 1 && genericArgs[ 0 ] == typeof( byte ) )
               {
                  var defName = parameterType.GetGenericTypeDefinition().Name;
                  if( defName.IndexOf( "Il2CppStructArray", StringComparison.Ordinal ) >= 0
                     || defName.IndexOf( "Il2CppReferenceArray", StringComparison.Ordinal ) >= 0
                     || defName.IndexOf( "Il2CppArrayBase", StringComparison.Ordinal ) >= 0 )
                  {
                     return true;
                  }
               }
            }
            return false;
         }

         private static object BuildByteArrayArgument( Type parameterType, byte[] bytes )
         {
            if( parameterType != typeof( byte[] ) )
            {
               return new Il2CppStructArray<byte>( bytes );
            }
            return bytes;
         }

         public static bool IsSuccess( object fontEngineErrorResult )
         {
            if( fontEngineErrorResult == null ) return false;
            return Convert.ToInt32( fontEngineErrorResult ) == 0;
         }

         public static bool TryGetGlyphIndex( char c, out uint glyphIndex )
         {
            glyphIndex = 0;
            if( TryGetGlyphIndexMethod == null ) return false;

            var args = new object[] { (uint)c, (uint)0 };
            var found = (bool)TryGetGlyphIndexMethod.Invoke( null, args );
            glyphIndex = (uint)args[ 1 ];
            return found;
         }

         public static string DescribeFaceInfo()
         {
            if( GetFaceInfoMethod == null ) return null;

            try
            {
               var faceInfo = GetFaceInfoMethod.Invoke( null, null );
               if( faceInfo == null ) return null;

               var sb = new StringBuilder();
               var faceInfoType = faceInfo.GetType();

               foreach( var field in faceInfoType.GetFields( BindingFlags.Public | BindingFlags.Instance ) )
               {
                  AppendMember( sb, field.Name, SafeToString( field.GetValue( faceInfo ) ) );
               }
               foreach( var prop in faceInfoType.GetProperties( BindingFlags.Public | BindingFlags.Instance ) )
               {
                  if( prop.GetIndexParameters().Length > 0 ) continue;
                  AppendMember( sb, prop.Name, SafeToString( SafeGet( prop, faceInfo ) ) );
               }

               return sb.Length > 0 ? sb.ToString() : null;
            }
            catch( Exception e )
            {
               XuaLogger.AutoTranslator.Warn( e, "DirectFontEngineLoader: failed to describe FaceInfo." );
               return null;
            }
         }

         private static void AppendMember( StringBuilder sb, string name, string value )
         {
            if( sb.Length > 0 ) sb.Append( ", " );
            sb.Append( name ).Append( '=' ).Append( value );
         }

         private static object SafeGet( PropertyInfo prop, object instance )
         {
            try { return prop.GetValue( instance ); }
            catch { return "<threw>"; }
         }

         private static string SafeToString( object value ) => value?.ToString() ?? "null";

         private static Type FindType( string fullName )
         {
            foreach( var assembly in AppDomain.CurrentDomain.GetAssemblies() )
            {
               Type type;
               try { type = assembly.GetType( fullName, throwOnError: false ); }
               catch { continue; }

               if( type != null ) return type;
            }
            return null;
         }
      }

      private static class FontAssetSurfaceScan
      {
         private static readonly string[] CandidateTypeNames =
         {
            "TMPro.TMP_FontAsset",
            "UnityEngine.TextCore.Text.FontAsset",
         };

         private static readonly string[] InterestingSubstrings =
         {
            "Glyph", "Atlas", "Character", "Texture", "Populate", "AddCharacter", "AddGlyph",
            "Create"
         };

         public static void LogAvailableSurface()
         {
            foreach( var typeName in CandidateTypeNames )
            {
               Type type = null;
               foreach( var assembly in AppDomain.CurrentDomain.GetAssemblies() )
               {
                  try { type = assembly.GetType( typeName, throwOnError: false ); }
                  catch { type = null; }
                  if( type != null ) break;
               }

               if( type == null )
               {
                  XuaLogger.AutoTranslator.Debug( "DirectFontEngineLoader: type '" + typeName + "' not present in this game's loaded assemblies." );
                  continue;
               }

               XuaLogger.AutoTranslator.Info( "DirectFontEngineLoader: found '" + typeName + "' in assembly '" + type.Assembly.GetName().Name + "' - relevant members:" );

               var methods = type.GetMethods( BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly )
                  .Where( m => InterestingSubstrings.Any( s => m.Name.IndexOf( s, StringComparison.OrdinalIgnoreCase ) >= 0 ) )
                  .OrderBy( m => m.Name );

               foreach( var method in methods )
               {
                  var parms = string.Join( ", ", method.GetParameters().Select( p => p.ParameterType.Name + " " + p.Name ) );
                  XuaLogger.AutoTranslator.Info( "DirectFontEngineLoader:   " + ( method.IsStatic ? "static " : "" ) + method.ReturnType.Name + " " + method.Name + "(" + parms + ")" );
               }
            }
         }
      }
   }
}
