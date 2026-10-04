using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using XUnity.AutoTranslator.Plugin.Core.Configuration;
using XUnity.AutoTranslator.Plugin.Core.Extensions;
using XUnity.AutoTranslator.Plugin.Core.Utilities;
using XUnity.Common.Constants;
using XUnity.Common.Extensions;
using XUnity.Common.Logging;
using XUnity.Common.Utilities;

namespace XUnity.AutoTranslator.Plugin.Core.Hooks
{
   internal static class StoryTextPreTranslateHook
   {
      private const string HarmonyId = "com.xunity.autotranslator.hooks.storytextpretranslatehook";

      private static HarmonyLib.Harmony _harmony;
      private static bool _applied;

      private static readonly Dictionary<string, string> _translations =
         new Dictionary<string, string>( StringComparer.Ordinal );
      private static bool _dictionaryLoaded;
      private static string _loadedFromPath;

      private static readonly HashSet<string> _translatedValues =
         new HashSet<string>( StringComparer.Ordinal );

      private static readonly Dictionary<string, string> _normalizedTranslations =
         new Dictionary<string, string>( StringComparer.Ordinal );

      private static System.Reflection.MethodInfo _getTextWidthMethod;
      private static System.Reflection.MethodInfo _getSpaceWidthMethod;
      private static System.Reflection.MethodInfo _dividePatternCharacterListMethod;
      private static System.Reflection.MethodInfo _isNonLineStartingCharacterMethod;
      private static bool _wrapPrimitivesResolved;
      private static bool _wrapPrimitivesAvailable;

      private static System.Reflection.MethodInfo _canvasForceUpdateCanvasesMethod;
      private static bool _canvasForceUpdateResolved;

      private static readonly Dictionary<int, float> _cachedBoxWidths =
         new Dictionary<int, float>();

      private static readonly HashSet<int> _loggedAncestryFor = new HashSet<int>();

      private static System.Reflection.PropertyInfo _textValuePropertyInfo;

      internal static bool IsSuppressingSelfWrite;

      private static System.Type _sdwTextType;

      private static Il2CppSystem.Type _sdwTextIl2CppType;

      private sealed class PreTranslatedStoryTextMarker
      {
         public string SubstitutedText;

         public int InstanceId;
      }

      public static bool IsPreTranslatedInstance( object sdwTextInstance )
      {
         if( sdwTextInstance == null )
         {
            return false;
         }

         var marker = sdwTextInstance.GetExtensionData<PreTranslatedStoryTextMarker>();
         return marker != null && marker.InstanceId == GetUnityInstanceId( sdwTextInstance );
      }

      private static int GetUnityInstanceId( object instance )
      {
         return ( instance as UnityEngine.Object )?.GetInstanceID() ?? 0;
      }

      internal static bool TryGetStoryTranslation( string original, out string translated )
      {
         EnsureDictionaryLoaded();
         return _translations.TryGetValue( original, out translated ) && !string.IsNullOrEmpty( translated );
      }

      private static System.Reflection.PropertyInfo _textPropertyInfo;

      private static System.Reflection.PropertyInfo _boxViewPropertyInfo;

      public static void Apply()
      {
         if( _applied ) return;

         bool primaryOk = ApplyPlayAsObservablePatch();
         bool secondaryOk = ApplyTextStreamPatch();

         bool genericOk = ApplyGenericIAdventureTextBoxPatch();

         _applied = primaryOk || secondaryOk || genericOk;

         if( !_applied )
         {
            XuaLogger.AutoTranslator.Warn( "StoryTextPreTranslateHook: neither AdventureTextBox.PlayAsObservable, AdventureTextView.TextStream, nor any other IAdventureTextBox implementer could be patched; story text will not be pre-translated." );
         }
      }

      private static bool ApplyPlayAsObservablePatch()
      {
         try
         {
            var resolved = ResolveTypesByName( "AdventureTextBox", "AdventureTextView" );
            resolved.TryGetValue( "AdventureTextBox", out System.Type adventureTextBoxType );
            resolved.TryGetValue( "AdventureTextView", out System.Type adventureTextViewType );

            if( adventureTextBoxType == null || adventureTextViewType == null )
            {
               XuaLogger.AutoTranslator.Warn( "StoryTextPreTranslateHook: AdventureTextBox/AdventureTextView type could not be resolved via reflection; skipping primary (PlayAsObservable) patch." );
               return false;
            }

            var target = HarmonyLib.AccessTools.Method(
               adventureTextBoxType,
               "PlayAsObservable",
               new System.Type[] { typeof( string ) } );

            if( target == null )
            {
               XuaLogger.AutoTranslator.Warn( "StoryTextPreTranslateHook: AdventureTextBox.PlayAsObservable(string) could not be resolved via reflection; skipping primary patch." );
               return false;
            }

            _boxViewPropertyInfo = HarmonyLib.AccessTools.Property( adventureTextBoxType, "Text" );
            _textPropertyInfo = HarmonyLib.AccessTools.Property( adventureTextViewType, "Text" );

            if( _boxViewPropertyInfo == null || _textPropertyInfo == null )
            {
               XuaLogger.AutoTranslator.Warn( "StoryTextPreTranslateHook: 'Text' property could not be resolved on AdventureTextBox/AdventureTextView; skipping primary patch." );
               return false;
            }

            _harmony = _harmony ?? new HarmonyLib.Harmony( HarmonyId );

            var prefix = typeof( StoryTextPreTranslateHook ).GetMethod(
               nameof( PlayAsObservable_Prefix ),
               System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic );

            _harmony.Patch( target, prefix: new HarmonyLib.HarmonyMethod( prefix ) );

            XuaLogger.AutoTranslator.Info( "StoryTextPreTranslateHook: patched AdventureTextBox.PlayAsObservable(string) (primary)." );
            return true;
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Error( e, "StoryTextPreTranslateHook: failed to apply primary (PlayAsObservable) patch." );
            return false;
         }
      }

      private static bool ApplyTextStreamPatch()
      {
         try
         {
            var resolved = ResolveTypesByName( "AdventureTextView" );
            resolved.TryGetValue( "AdventureTextView", out System.Type adventureTextViewType );

            if( adventureTextViewType == null )
            {
               XuaLogger.AutoTranslator.Warn( "StoryTextPreTranslateHook: AdventureTextView type could not be resolved via reflection; skipping secondary (TextStream) patch." );
               return false;
            }

            var target = HarmonyLib.AccessTools.Method(
               adventureTextViewType,
               "TextStream",
               new System.Type[] { typeof( string ) } );

            if( target == null )
            {
               XuaLogger.AutoTranslator.Warn( "StoryTextPreTranslateHook: AdventureTextView.TextStream(string) could not be resolved via reflection; skipping secondary patch." );
               return false;
            }

            if( _textPropertyInfo == null )
            {
               _textPropertyInfo = HarmonyLib.AccessTools.Property( adventureTextViewType, "Text" );
            }

            if( _textPropertyInfo == null )
            {
               XuaLogger.AutoTranslator.Warn( "StoryTextPreTranslateHook: AdventureTextView.Text property could not be resolved via reflection; skipping secondary patch." );
               return false;
            }

            _harmony = _harmony ?? new HarmonyLib.Harmony( HarmonyId );

            var prefix = typeof( StoryTextPreTranslateHook ).GetMethod(
               nameof( TextStream_Prefix ),
               System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic );

            _harmony.Patch( target, prefix: new HarmonyLib.HarmonyMethod( prefix ) );

            XuaLogger.AutoTranslator.Info( "StoryTextPreTranslateHook: patched AdventureTextView.TextStream(string) (secondary)." );
            return true;
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Error( e, "StoryTextPreTranslateHook: failed to apply secondary (TextStream) patch." );
            return false;
         }
      }

      private static bool ApplyGenericIAdventureTextBoxPatch()
      {
         try
         {
            var resolved = ResolveTypesByName( "IAdventureTextBox" );
            resolved.TryGetValue( "IAdventureTextBox", out System.Type iAdventureTextBoxType );

            if( iAdventureTextBoxType == null )
            {
               XuaLogger.AutoTranslator.Warn( "StoryTextPreTranslateHook: IAdventureTextBox type could not be resolved via reflection; skipping generalized box-type patch." );
               return false;
            }

            var implementers = FindConcreteImplementers( iAdventureTextBoxType );
            if( implementers.Count == 0 )
            {
               XuaLogger.AutoTranslator.Warn( "StoryTextPreTranslateHook: no concrete IAdventureTextBox implementers found; skipping generalized box-type patch." );
               return false;
            }

            _harmony = _harmony ?? new HarmonyLib.Harmony( HarmonyId );

            var prefix = typeof( StoryTextPreTranslateHook ).GetMethod(
               nameof( GenericPlayAsObservable_Prefix ),
               System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic );

            var alreadyPatched = new System.Collections.Generic.HashSet<System.Reflection.MethodInfo>();

            bool patchedAny = false;
            foreach( var implType in implementers )
            {
               if( string.Equals( implType.Name, "AdventureTextBox", StringComparison.Ordinal ) )
               {
                  continue;
               }

               var target = HarmonyLib.AccessTools.Method( implType, "PlayAsObservable", new System.Type[] { typeof( string ) } );
               if( target == null )
               {
                  XuaLogger.AutoTranslator.Warn( "StoryTextPreTranslateHook: " + implType.FullName + ".PlayAsObservable(string) could not be resolved via reflection; skipping." );
                  continue;
               }

               if( !alreadyPatched.Add( target ) )
               {
                  XuaLogger.AutoTranslator.Debug( "StoryTextPreTranslateHook: " + implType.FullName + ".PlayAsObservable(string) resolves to an already-patched method (shared base implementation); skipping duplicate patch." );
                  continue;
               }

               _harmony.Patch( target, prefix: new HarmonyLib.HarmonyMethod( prefix ) );
               XuaLogger.AutoTranslator.Info( "StoryTextPreTranslateHook: patched " + implType.FullName + ".PlayAsObservable(string) (generalized IAdventureTextBox coverage)." );
               patchedAny = true;
            }

            return patchedAny;
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Error( e, "StoryTextPreTranslateHook: failed to apply generalized IAdventureTextBox patch." );
            return false;
         }
      }

      private static System.Collections.Generic.List<System.Type> FindConcreteImplementers( System.Type interfaceType )
      {
         var results = new System.Collections.Generic.List<System.Type>();
         if( interfaceType == null )
         {
            return results;
         }

         foreach( var assembly in System.AppDomain.CurrentDomain.GetAssemblies() )
         {
            System.Type[] types;
            try
            {
               types = assembly.GetTypes();
            }
            catch( System.Reflection.ReflectionTypeLoadException ex )
            {
               var loaded = new System.Collections.Generic.List<System.Type>();
               foreach( var t in ex.Types )
               {
                  if( t != null )
                  {
                     loaded.Add( t );
                  }
               }
               types = loaded.ToArray();
            }
            catch
            {
               continue;
            }

            foreach( var type in types )
            {
               if( type == null || type.IsInterface || type.IsAbstract )
               {
                  continue;
               }

               if( interfaceType.IsAssignableFrom( type ) )
               {
                  results.Add( type );
               }
            }
         }

         return results;
      }

      private static System.Collections.Generic.Dictionary<string, System.Type> ResolveTypesByName(
         params string[] simpleNames )
      {
         var result = new System.Collections.Generic.Dictionary<string, System.Type>();
         var remaining = new System.Collections.Generic.HashSet<string>( simpleNames );

         foreach( var name in simpleNames )
         {
            System.Type direct = System.Type.GetType( name, false );
            if( direct != null )
            {
               result[ name ] = direct;
               remaining.Remove( name );
            }
         }

         if( remaining.Count == 0 )
         {
            return result;
         }

         foreach( var assembly in System.AppDomain.CurrentDomain.GetAssemblies() )
         {
            if( remaining.Count == 0 )
            {
               break;
            }

            System.Type[] types;
            try
            {
               types = assembly.GetTypes();
            }
            catch( System.Reflection.ReflectionTypeLoadException ex )
            {
               var loaded = new System.Collections.Generic.List<System.Type>();
               foreach( var t in ex.Types )
               {
                  if( t != null )
                  {
                     loaded.Add( t );
                  }
               }
               types = loaded.ToArray();
            }
            catch
            {
               continue;
            }

            foreach( var type in types )
            {
               if( type == null )
               {
                  continue;
               }

               if( remaining.Contains( type.FullName ) )
               {
                  result[ type.FullName ] = type;
                  remaining.Remove( type.FullName );
               }
               else if( remaining.Contains( type.Name ) )
               {
                  result[ type.Name ] = type;
                  remaining.Remove( type.Name );
               }
            }
         }

         return result;
      }

      private static void PlayAsObservable_Prefix( object __instance, ref string text )
      {
         try
         {
            XuaLogger.AutoTranslator.Debug( "StoryTextPreTranslateHook: PlayAsObservable_Prefix invoked for: " + text );

            var adventureTextView = _boxViewPropertyInfo?.GetValue( __instance, null );
            SubstituteAndRegister( ref text, adventureTextView );
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Warn( e, "StoryTextPreTranslateHook: PlayAsObservable_Prefix failed; falling back to original (untranslated) line." );
         }
      }

      private static void TextStream_Prefix( object __instance, ref string text )
      {
         try
         {
            XuaLogger.AutoTranslator.Debug( "StoryTextPreTranslateHook: TextStream_Prefix invoked for: " + text );

            SubstituteAndRegister( ref text, __instance );
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Warn( e, "StoryTextPreTranslateHook: TextStream_Prefix failed; falling back to original (untranslated) line." );
         }
      }

      private static void SubstituteAndRegister( ref string text, object adventureTextViewInstance )
      {
         var textComponent = adventureTextViewInstance != null ? _textPropertyInfo?.GetValue( adventureTextViewInstance, null ) : null;

         SubstituteAndRegisterCore( ref text, textComponent );
      }

      private static void GenericPlayAsObservable_Prefix( object __instance, ref string text )
      {
         try
         {
            XuaLogger.AutoTranslator.Debug( "StoryTextPreTranslateHook: GenericPlayAsObservable_Prefix invoked for '" + __instance?.GetType().FullName + "': " + text );

            var textComponent = ResolveTextComponentGeneric( __instance );
            SubstituteAndRegisterCore( ref text, textComponent );
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Warn( e, "StoryTextPreTranslateHook: GenericPlayAsObservable_Prefix failed; falling back to original (untranslated) line." );
         }
      }

      private static object ResolveTextComponentGeneric( object boxInstance )
      {
         var component = boxInstance as Component;
         if( component == null )
         {
            XuaLogger.AutoTranslator.Warn( "StoryTextPreTranslateHook: IAdventureTextBox instance is not a UnityEngine.Component; cannot search its hierarchy for SdwText." );
            return null;
         }

         ResolveWrapPrimitives();
         if( _sdwTextType == null )
         {
            XuaLogger.AutoTranslator.Warn( "StoryTextPreTranslateHook: SdwText type unavailable; cannot search " + component.GetType().FullName + "'s hierarchy for it." );
            return null;
         }

         if( _sdwTextIl2CppType == null )
         {
            XuaLogger.AutoTranslator.Warn( "StoryTextPreTranslateHook: SdwText Il2CppSystem.Type unavailable; cannot search " + component.GetType().FullName + "'s hierarchy for it." );
            return null;
         }

         try
         {
            var found = component.GetComponentInChildren( _sdwTextIl2CppType );
            if( found == null )
            {
               XuaLogger.AutoTranslator.Warn( "StoryTextPreTranslateHook: GetComponentInChildren(SdwText) found nothing under " + component.GetType().FullName + "; this box type's label may live outside its own hierarchy." );
            }
            return found;
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Warn( e, "StoryTextPreTranslateHook: GetComponentInChildren(SdwText) failed for " + component.GetType().FullName + "." );
            return null;
         }
      }

      private static void SubstituteAndRegisterCore( ref string text, object textComponent )
      {
         if( string.IsNullOrEmpty( text ) )
         {
            return;
         }

         if( textComponent != null )
         {
            var existingMarker = textComponent.GetExtensionData<PreTranslatedStoryTextMarker>();
            if( existingMarker != null && existingMarker.InstanceId == GetUnityInstanceId( textComponent )
               && string.Equals( existingMarker.SubstitutedText, text, StringComparison.Ordinal ) )
            {
               XuaLogger.AutoTranslator.Debug( "StoryTextPreTranslateHook: text already matches this component's last processed result; skipping redundant call for: " + text );
               return;
            }
         }

         EnsureDictionaryLoaded();

         bool hasTranslation = _translations.TryGetValue( text, out var translated ) && !string.IsNullOrEmpty( translated );

         string pendingWrapText = null;

         if( hasTranslation )
         {
            if( translated.IndexOf( '\n' ) < 0 )
            {
               pendingWrapText = translated;
               text = translated;
            }
            else
            {
               XuaLogger.AutoTranslator.Debug( "StoryTextPreTranslateHook: translated via exact match (already curated with line breaks): " + text );
               text = translated;
            }
         }
         else if( _translatedValues.Contains( text ) )
         {
            hasTranslation = true;
            XuaLogger.AutoTranslator.Debug( "StoryTextPreTranslateHook: text is already a known translation (substituted upstream); marking without re-substituting: " + text );
         }
         else if( TryGetNormalizedFallback( text, out var fallbackTranslated ) )
         {
            hasTranslation = true;
            pendingWrapText = fallbackTranslated;
            text = fallbackTranslated;
         }
         else
         {
            XuaLogger.AutoTranslator.Debug( "StoryTextPreTranslateHook: no cached translation for story line: " + text );
         }

         if( textComponent != null )
         {
            var info = textComponent.GetOrCreateTextTranslationInfo();
            if( info != null )
            {
               if( hasTranslation )
               {
                  info.SetTranslatedText( text );
               }
               else
               {
                  info.IsTranslated = false;
               }

               info.ChangeFont( textComponent );
            }

            if( pendingWrapText != null )
            {
               var wrapped = TryPixelWrap( pendingWrapText, textComponent, out bool wasWrapped );
               XuaLogger.AutoTranslator.Debug(
                  ( wasWrapped
                     ? "StoryTextPreTranslateHook: translated (pixel-wrapped, post-ChangeFont): "
                     : "StoryTextPreTranslateHook: translated (wrap unavailable; unwrapped): " )
                  + pendingWrapText );
               text = wrapped;

               if( info != null && wasWrapped )
               {
                  info.SetTranslatedText( text );
               }
            }

            textComponent.SetExtensionData( hasTranslation
               ? new PreTranslatedStoryTextMarker { SubstitutedText = text, InstanceId = GetUnityInstanceId( textComponent ) }
               : null );
         }
      }

      private static bool TryGetNormalizedFallback( string originalText, out string result )
      {
         result = null;

         var normalizedKey = NormalizeForFallbackMatch( originalText );
         if( normalizedKey.Length == 0 )
         {
            return false;
         }

         if( !_normalizedTranslations.TryGetValue( normalizedKey, out var translated ) || string.IsNullOrEmpty( translated ) )
         {
            return false;
         }

         result = translated;
         return true;
      }

      private static void ResolveWrapPrimitives()
      {
         if( _wrapPrimitivesResolved )
         {
            return;
         }

         _wrapPrimitivesResolved = true;

         try
         {
            var resolved = ResolveTypesByName( "SdwText" );
            resolved.TryGetValue( "SdwText", out _sdwTextType );

            if( _sdwTextType == null )
            {
               XuaLogger.AutoTranslator.Warn( "StoryTextPreTranslateHook: SdwText type could not be resolved via reflection; story fallback translations will display unwrapped." );
               _wrapPrimitivesAvailable = false;
               return;
            }

            try
            {
               _sdwTextIl2CppType = Il2CppInterop.Runtime.Il2CppType.From( _sdwTextType );
            }
            catch( Exception e )
            {
               XuaLogger.AutoTranslator.Warn( e, "StoryTextPreTranslateHook: Il2CppType.From(SdwText) failed; generalized (non-AdventureTextBox) box types will not be able to locate their SdwText child." );
            }

            _getTextWidthMethod = HarmonyLib.AccessTools.Method( _sdwTextType, "GetTextWidth", new System.Type[] { typeof( string ) } );
            _getSpaceWidthMethod = HarmonyLib.AccessTools.Method( _sdwTextType, "GetSpaceWidth", System.Type.EmptyTypes );
            _dividePatternCharacterListMethod = HarmonyLib.AccessTools.Method( _sdwTextType, "DividePatternCharacterList", new System.Type[] { typeof( string ) } );
            _isNonLineStartingCharacterMethod = HarmonyLib.AccessTools.Method( _sdwTextType, "IsNonLineStartingCharacter", new System.Type[] { typeof( char ) } );

            try
            {
               _textValuePropertyInfo = HarmonyLib.AccessTools.Property( _sdwTextType, "text" );
            }
            catch( Exception e )
            {
               XuaLogger.AutoTranslator.Warn( e, "StoryTextPreTranslateHook: SdwText 'text' property could not be resolved; the tips-screen wrap safety net will be unavailable." );
            }

            _wrapPrimitivesAvailable = _getTextWidthMethod != null
               && _getSpaceWidthMethod != null
               && _dividePatternCharacterListMethod != null
               && _isNonLineStartingCharacterMethod != null;

            if( !_wrapPrimitivesAvailable )
            {
               XuaLogger.AutoTranslator.Warn(
                  "StoryTextPreTranslateHook: could not resolve all SdwText wrap primitives via reflection "
                  + "(GetTextWidth=" + ( _getTextWidthMethod != null )
                  + ", GetSpaceWidth=" + ( _getSpaceWidthMethod != null )
                  + ", DividePatternCharacterList=" + ( _dividePatternCharacterListMethod != null )
                  + ", IsNonLineStartingCharacter=" + ( _isNonLineStartingCharacterMethod != null )
                  + "); story fallback translations will display unwrapped until this is fixed." );
            }
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Warn( e, "StoryTextPreTranslateHook: failed to resolve SdwText wrap primitives; story fallback translations will display unwrapped." );
            _wrapPrimitivesAvailable = false;
         }
      }

      internal static void ClearCachedBoxWidths()
      {
         _cachedBoxWidths.Clear();
      }

      private static void LogLayoutAncestryOnce( RectTransform rectTransform )
      {
         try
         {
            if( rectTransform == null )
            {
               return;
            }

            int id = rectTransform.GetInstanceID();
            if( !_loggedAncestryFor.Add( id ) )
            {
               return;
            }

            var sb = new StringBuilder();
            sb.Append( "StoryTextPreTranslateHook: [ANCESTRY-DIAG] chain for '" )
              .Append( rectTransform.name )
              .Append( "' (looking for anything that could re-derive this box's width from its own text content): " );

            Transform current = rectTransform;
            bool firstAncestor = true;
            int guard = 0;

            while( current != null && guard < 64 )
            {
               guard++;

               if( !firstAncestor )
               {
                  sb.Append( " <- " );
               }
               firstAncestor = false;

               sb.Append( current.name ).Append( " [" );

               try
               {
                  var components = current.GetComponents<Component>();
                  bool firstComp = true;
                  foreach( var comp in components )
                  {
                     if( comp == null )
                     {
                        continue;
                     }

                     if( !firstComp )
                     {
                        sb.Append( ", " );
                     }
                     firstComp = false;

                     sb.Append( comp.GetType().Name );
                  }
               }
               catch( Exception e )
               {
                  sb.Append( "GetComponents failed: " ).Append( e.Message );
               }

               sb.Append( "]" );

               current = current.parent;
            }

            XuaLogger.AutoTranslator.Debug( sb.ToString() );
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Warn( e, "StoryTextPreTranslateHook: ancestry diagnostic logging failed (non-fatal, does not affect wrapping)." );
         }
      }

      private static void TryForceLayoutSettle()
      {
         if( !_canvasForceUpdateResolved )
         {
            _canvasForceUpdateResolved = true;
            try
            {
               var resolved = ResolveTypesByName( "Canvas" );
               if( resolved.TryGetValue( "Canvas", out var canvasType ) && canvasType != null )
               {
                  _canvasForceUpdateCanvasesMethod = HarmonyLib.AccessTools.Method( canvasType, "ForceUpdateCanvases", System.Type.EmptyTypes );
               }

               if( _canvasForceUpdateCanvasesMethod == null )
               {
                  XuaLogger.AutoTranslator.Warn( "StoryTextPreTranslateHook: could not resolve UnityEngine.Canvas.ForceUpdateCanvases(); story box widths may occasionally be read before layout has settled." );
               }
            }
            catch( Exception e )
            {
               XuaLogger.AutoTranslator.Warn( e, "StoryTextPreTranslateHook: failed to resolve UnityEngine.Canvas.ForceUpdateCanvases()." );
            }
         }

         if( _canvasForceUpdateCanvasesMethod == null )
         {
            return;
         }

         try
         {
            _canvasForceUpdateCanvasesMethod.Invoke( null, null );
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Warn( e, "StoryTextPreTranslateHook: Canvas.ForceUpdateCanvases() threw; continuing with whatever rect.width currently reads." );
         }
      }

      internal static int MaxStoryLines = 3;
      internal const double MinStoryFontScale = 0.70;

      private sealed class StoryFontState { public int Original; public int Applied; }
      private static readonly Dictionary<int, StoryFontState> _storyFontStates = new Dictionary<int, StoryFontState>();

      private static int ReadFontSize( object ui )
      {
         try { return (int?)UnityTypes.Text_Properties.FontSize?.Get( ui ) ?? 0; }
         catch { return 0; }
      }

      private static int StoryFontSizeBegin( object ui, int rectId )
      {
         int current = ReadFontSize( ui );
         if( current <= 0 ) return 0;

         if( _storyFontStates.TryGetValue( rectId, out var st ) )
         {
            if( current == st.Applied )
            {
               if( current != st.Original ) UnityTypes.Text_Properties.FontSize.Set( ui, st.Original );
               st.Applied = st.Original;
               return st.Original;
            }
            st.Original = current;
            st.Applied = current;
            return current;
         }

         _storyFontStates[ rectId ] = new StoryFontState { Original = current, Applied = current };
         return current;
      }

      private static void StoryFontSizeSet( object ui, int rectId, int size )
      {
         try
         {
            UnityTypes.Text_Properties.FontSize.Set( ui, size );
            if( _storyFontStates.TryGetValue( rectId, out var st ) ) st.Applied = size;
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Warn( e, "StoryTextPreTranslateHook: could not set story font size." );
         }
      }

      internal static void ReapplyStoryFontSize( object textComponent )
      {
         try
         {
            var component = textComponent as Component;
            if( component == null ) return;
            if( !component.transform.TryCastTo<RectTransform>( out var rt ) || rt == null ) return;
            if( !_storyFontStates.TryGetValue( rt.GetInstanceID(), out var st ) ) return;
            if( st.Applied == st.Original ) return;
            if( ReadFontSize( textComponent ) != st.Applied )
            {
               UnityTypes.Text_Properties.FontSize.Set( textComponent, st.Applied );
            }
         }
         catch { }
      }

      private static void WrapOnce( object typedComponent, List<string> words, float boxWidth, out StringBuilder sb, out int lineCount )
      {
         float spaceWidth = Convert.ToSingle( _getSpaceWidthMethod.Invoke( typedComponent, null ) );
         sb = new StringBuilder();
         float lineWidth = 0f;
         lineCount = 1;
         foreach( var word in words )
         {
            if( string.IsNullOrEmpty( word ) )
            {
               continue;
            }

            float wordWidth = Convert.ToSingle( _getTextWidthMethod.Invoke( typedComponent, new object[] { word } ) );
            lineWidth += wordWidth;

            if( word == "\n" )
            {
               sb.Append( '\n' );
               lineWidth = 0f;
               lineCount++;
               continue;
            }

            if( word == " " )
            {
               lineWidth += spaceWidth;
               sb.Append( word );
               continue;
            }

            if( lineWidth <= boxWidth )
            {
               sb.Append( word );
               continue;
            }

            char lastChar = word[ word.Length - 1 ];
            bool isNonLineStarting = (bool)_isNonLineStartingCharacterMethod.Invoke( null, new object[] { lastChar } );

            if( isNonLineStarting && word.Length == 1 )
            {
               sb.Append( word );
               sb.Append( '\n' );
               lineWidth = 0f;
               lineCount++;
            }
            else
            {
               sb.Append( '\n' );
               sb.Append( word );
               lineWidth = wordWidth;
               lineCount++;
            }
         }
      }

      private static string TryPixelWrap( string flatText, object textComponent, out bool wasWrapped )
      {
         wasWrapped = false;

         if( string.IsNullOrEmpty( flatText ) || textComponent == null )
         {
            return flatText;
         }

         ResolveWrapPrimitives();
         if( !_wrapPrimitivesAvailable )
         {
            return flatText;
         }

         var component = textComponent as Component;
         if( component == null )
         {
            XuaLogger.AutoTranslator.Warn( "StoryTextPreTranslateHook: story text component is not a UnityEngine.Component; cannot read its RectTransform width, displaying unwrapped: " + flatText );
            return flatText;
         }

         if( !component.transform.TryCastTo<RectTransform>( out var rectTransform ) || rectTransform == null )
         {
            XuaLogger.AutoTranslator.Warn( "StoryTextPreTranslateHook: story text component has no RectTransform; displaying unwrapped: " + flatText );
            return flatText;
         }

         LogLayoutAncestryOnce( rectTransform );

         int rectId = rectTransform.GetInstanceID();
         float boxWidth;

         if( _cachedBoxWidths.TryGetValue( rectId, out var cachedWidth ) )
         {
            boxWidth = cachedWidth;
            XuaLogger.AutoTranslator.Debug( "StoryTextPreTranslateHook: using cached box width " + boxWidth.ToString( "0.0" ) + " for '" + rectTransform.name + "' instead of re-reading a value this hook may have already influenced." );
         }
         else
         {
            TryForceLayoutSettle();

            boxWidth = rectTransform.rect.width;
            if( boxWidth <= 0f )
            {
               XuaLogger.AutoTranslator.Warn( "StoryTextPreTranslateHook: story text box RectTransform.rect.width read as " + boxWidth + " (not yet laid out?); displaying unwrapped: " + flatText );
               return flatText;
            }

            _cachedBoxWidths[ rectId ] = boxWidth;
            XuaLogger.AutoTranslator.Debug( "StoryTextPreTranslateHook: caching first-observed box width " + boxWidth.ToString( "0.0" ) + " for '" + rectTransform.name + "'." );
         }

         object typedComponent = textComponent;
         try
         {
            var pointer = ( (Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase)textComponent ).Pointer;
            typedComponent = Il2CppUtilities.CreateProxyComponentWithDerivedType( pointer, _sdwTextType );
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Warn( e, "StoryTextPreTranslateHook: failed to re-type story text component as SdwText; displaying unwrapped: " + flatText );
            return flatText;
         }

         try
         {
            var wordsRaw = _dividePatternCharacterListMethod.Invoke( typedComponent, new object[] { flatText } );
            var words = new List<string>();

            if( wordsRaw == null )
            {
               XuaLogger.AutoTranslator.Warn( "StoryTextPreTranslateHook: DividePatternCharacterList returned null for: " + flatText );
            }
            else
            {
               XuaLogger.AutoTranslator.Debug(
                  "StoryTextPreTranslateHook:   DividePatternCharacterList result type=" + wordsRaw.GetType().FullName
                  + " IEnumerable=" + ( wordsRaw is System.Collections.IEnumerable ) + " for: " + flatText );

               if( wordsRaw is System.Collections.IEnumerable enumerable )
               {
                  int rawSeen = 0;
                  foreach( var w in enumerable )
                  {
                     rawSeen++;
                     var s = w as string ?? w?.ToString();
                     if( rawSeen <= 3 )
                     {
                        XuaLogger.AutoTranslator.Debug(
                           "StoryTextPreTranslateHook:     [enumerable] #" + rawSeen
                           + " raw type=" + ( w?.GetType().FullName ?? "null" ) + " -> \"" + s + "\"" );
                     }
                     if( s != null )
                     {
                        words.Add( s );
                     }
                  }
               }

               if( words.Count == 0 )
               {
                  try
                  {
                     var wordsRawType = wordsRaw.GetType();
                     var countProp = wordsRawType.GetProperty( "Count" );
                     var getItemMethod = wordsRawType.GetMethod( "get_Item", new System.Type[] { typeof( int ) } );

                     if( countProp != null && getItemMethod != null )
                     {
                        int count = Convert.ToInt32( countProp.GetValue( wordsRaw, null ) );
                        XuaLogger.AutoTranslator.Debug( "StoryTextPreTranslateHook:   [indexer fallback] Count=" + count );

                        for( int i = 0; i < count; i++ )
                        {
                           var item = getItemMethod.Invoke( wordsRaw, new object[] { i } );
                           var s = item as string ?? item?.ToString();
                           if( i < 3 )
                           {
                              XuaLogger.AutoTranslator.Debug(
                                 "StoryTextPreTranslateHook:     [indexer fallback] #" + i
                                 + " raw type=" + ( item?.GetType().FullName ?? "null" ) + " -> \"" + s + "\"" );
                           }
                           if( s != null )
                           {
                              words.Add( s );
                           }
                        }
                     }
                     else
                     {
                        XuaLogger.AutoTranslator.Debug(
                           "StoryTextPreTranslateHook:   [indexer fallback] Count/get_Item(int) not found on " + wordsRawType.FullName );
                     }
                  }
                  catch( Exception e )
                  {
                     XuaLogger.AutoTranslator.Warn( e, "StoryTextPreTranslateHook: indexer fallback threw reading DividePatternCharacterList's result." );
                  }
               }
            }

            if( words.Count == 0 )
            {
               XuaLogger.AutoTranslator.Warn( "StoryTextPreTranslateHook: DividePatternCharacterList returned no usable tokens for: " + flatText );
               return flatText;
            }

            var uiText = textComponent;
            int originalSize = StoryFontSizeBegin( uiText, rectId );
            int minSize = originalSize > 0 ? Math.Max( 8, (int)Math.Round( originalSize * MinStoryFontScale ) ) : 0;
            int curSize = originalSize;
            int lineCount;
            StringBuilder sb;

            while( true )
            {
               WrapOnce( typedComponent, words, boxWidth, out sb, out lineCount );

               if( lineCount <= MaxStoryLines || originalSize <= 0 || curSize <= minSize )
               {
                  break;
               }

               int step = Math.Max( 1, (int)Math.Round( originalSize * 0.04 ) );
               curSize = Math.Max( minSize, curSize - step );
               StoryFontSizeSet( uiText, rectId, curSize );
            }

            if( curSize != originalSize )
            {
               XuaLogger.AutoTranslator.Debug( "StoryTextPreTranslateHook: story line shrunk font " + originalSize + " -> " + curSize + " to fit " + lineCount + " line(s) (limit " + MaxStoryLines + ")." );
            }

            wasWrapped = true;
            var result = sb.ToString();
            XuaLogger.AutoTranslator.Debug( "StoryTextPreTranslateHook: pixel-wrapped story line into " + lineCount + " line(s) against box width " + boxWidth.ToString( "0.0" ) + ": " + result.Replace( '\n', '|' ) );
            return result;
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Warn( e, "StoryTextPreTranslateHook: pixel wrap threw for this line; displaying unwrapped instead: " + flatText );
            return flatText;
         }
      }

      internal static void TryApplyWrapSafetyNet( object textComponent )
      {
         if( !Settings.TipsScreenWrapSafetyNet )
         {
            return;
         }

         if( textComponent == null )
         {
            return;
         }

         ResolveWrapPrimitives();
         if( !_wrapPrimitivesAvailable || _sdwTextType == null || _textValuePropertyInfo == null )
         {
            return;
         }

         var component = textComponent as Component;
         if( component == null )
         {
            return;
         }

         var root = component.transform != null ? component.transform.root : null;
         var rootName = root != null ? root.name : null;
         if( string.IsNullOrEmpty( rootName ) || rootName.IndexOf( "SystemTips", StringComparison.Ordinal ) < 0 )
         {
            return;
         }

         if( component.gameObject != null && string.Equals( component.gameObject.name, "Text_Title", StringComparison.Ordinal ) )
         {
            return;
         }

         try
         {
            if( _sdwTextIl2CppType == null )
            {
               return;
            }

            var sdwTextTyped = component.GetComponent( _sdwTextIl2CppType );
            if( sdwTextTyped == null )
            {
               return;
            }

            var currentText = _textValuePropertyInfo.GetValue( sdwTextTyped, null ) as string;
            if( string.IsNullOrEmpty( currentText ) || currentText.IndexOf( '\n' ) >= 0 )
            {
               return;
            }

            var wrapped = TryPixelWrap( currentText, sdwTextTyped, out bool wasWrapped );
            if( wasWrapped && !string.Equals( wrapped, currentText, StringComparison.Ordinal ) )
            {
               XuaLogger.AutoTranslator.Debug( "StoryTextPreTranslateHook: [TIPS-WRAP-SAFETY-NET] wrapped a flat SdwText string that bypassed GetFormatedText: " + wrapped.Replace( '\n', '|' ) );

               var previousSuppress = IsSuppressingSelfWrite;
               IsSuppressingSelfWrite = true;
               try
               {
                  _textValuePropertyInfo.SetValue( sdwTextTyped, wrapped, null );
               }
               finally
               {
                  IsSuppressingSelfWrite = previousSuppress;
               }

               string afterWrite = null;
               try
               {
                  afterWrite = _textValuePropertyInfo.GetValue( sdwTextTyped, null ) as string;
               }
               catch( Exception readEx )
               {
                  XuaLogger.AutoTranslator.Warn( readEx, "StoryTextPreTranslateHook: [TIPS-WRAP-SAFETY-NET] could not read text back after write-back for verification." );
               }

               bool stillWrapped = !string.IsNullOrEmpty( afterWrite ) && afterWrite.IndexOf( '\n' ) >= 0;
               XuaLogger.AutoTranslator.Debug(
                  "StoryTextPreTranslateHook: [TIPS-WRAP-SAFETY-NET] post-write-back verification: stillWrapped=" + stillWrapped
                  + ( afterWrite != null ? ", current=" + afterWrite.Replace( '\n', '|' ) : ", current=<unreadable>" ) );
            }
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Warn( e, "StoryTextPreTranslateHook: [TIPS-WRAP-SAFETY-NET] failed; leaving text unwrapped." );
         }
      }

      private static string NormalizeForFallbackMatch( string s )
      {
         if( string.IsNullOrEmpty( s ) )
         {
            return string.Empty;
         }

         var sb = new StringBuilder( s.Length );
         bool lastWasWhitespace = false;
         for( int i = 0; i < s.Length; i++ )
         {
            var c = s[ i ];
            if( char.IsWhiteSpace( c ) )
            {
               if( !lastWasWhitespace && sb.Length > 0 )
               {
                  sb.Append( ' ' );
               }
               lastWasWhitespace = true;
            }
            else
            {
               sb.Append( c );
               lastWasWhitespace = false;
            }
         }

         if( sb.Length > 0 && sb[ sb.Length - 1 ] == ' ' )
         {
            sb.Length -= 1;
         }

         return sb.ToString();
      }

      private static void EnsureDictionaryLoaded()
      {
         string path = Settings.StoryTranslationFilePath;

         if( _dictionaryLoaded && string.Equals( path, _loadedFromPath, StringComparison.OrdinalIgnoreCase ) )
         {
            return;
         }

         _translations.Clear();
         _translatedValues.Clear();
         _normalizedTranslations.Clear();

         if( string.IsNullOrEmpty( path ) || !File.Exists( path ) )
         {
            XuaLogger.AutoTranslator.Warn( "StoryTextPreTranslateHook: translation file not found at '" + path + "'; story text will pass through untranslated at the source until it exists." );
            _dictionaryLoaded = true;
            _loadedFromPath = path;
            return;
         }

         int loaded = 0, skipped = 0;
         var rawText = File.ReadAllText( path, Encoding.UTF8 );
         var rawLines = rawText.Split( new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries );
         foreach( var rawLine in rawLines )
         {
            var kvp = TextHelper.ReadTranslationLineAndDecode( rawLine );
            if( kvp == null )
            {
               skipped++;
               continue;
            }

            var key = kvp[ 0 ];
            var value = kvp[ 1 ];

            if( string.IsNullOrEmpty( key ) || string.IsNullOrEmpty( value ) )
            {
               skipped++;
               continue;
            }

            if( key.StartsWith( "r:", StringComparison.Ordinal ) )
            {
               skipped++;
               continue;
            }

            if( string.Equals( key, value, StringComparison.Ordinal ) )
            {
               skipped++;
               continue;
            }

            _translations[ key ] = value;
            _translatedValues.Add( value );

            var normalizedKey = NormalizeForFallbackMatch( key );
            if( normalizedKey.Length > 0 )
            {
               _normalizedTranslations[ normalizedKey ] = value;
            }
            loaded++;
         }

         XuaLogger.AutoTranslator.Info( $"StoryTextPreTranslateHook: Loaded {loaded} story translation(s) from '{path}' ({skipped} skipped as untranslated/malformed/regex)." );

         _dictionaryLoaded = true;
         _loadedFromPath = path;
      }
   }
}
