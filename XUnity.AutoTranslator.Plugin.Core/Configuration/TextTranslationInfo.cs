using Il2CppInterop.Runtime;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using XUnity.AutoTranslator.Plugin.Core.Configuration;
using XUnity.AutoTranslator.Plugin.Core.Extensions;
using XUnity.AutoTranslator.Plugin.Core.Fonts;
using XUnity.AutoTranslator.Plugin.Core.Text;
using XUnity.AutoTranslator.Plugin.Core.UIResize;
using XUnity.AutoTranslator.Plugin.Core.Utilities;
using XUnity.AutoTranslator.Plugin.Utilities;
using XUnity.Common.Constants;
using XUnity.Common.Extensions;
using XUnity.Common.Harmony;
using XUnity.Common.Logging;
using XUnity.Common.Utilities;

namespace XUnity.AutoTranslator.Plugin.Core
{
   internal sealed class TextTranslationInfo
   {
      private Action<object> _unresize;
      private Action<object> _unfont;

      private bool _hasCapturedNativeFont;
      private Font _originalNativeFont;

      private bool _hasCapturedNativeFontTextMeshPro;
      private UnityEngine.Object _originalNativeFontTextMeshPro;

      private bool _hasCheckedTypeWriter;
      private MonoBehaviour _typewriter;
      private float? _alteredLineSpacing;
      private int? _alteredFontSize;

      private Font _autoLineSpacingFont;
      private int _autoLineSpacingFontSize;
      private float? _autoLineSpacingMultiplier;
      private float _autoLineSpacingBaselineValue;
      private float? _autoAppliedLineSpacing;

      private bool _initialized = false;
      private HashSet<string> _redirectedTranslations;

      private HashSet<string> _loggedRussianTextKeys;

      private int _changeFontGeneration;

      public ITextComponentManipulator TextManipulator { get; set; }
      public string OriginalText { get; set; }
      public string TranslatedText { get; set; }
      public bool IsTranslated { get; set; }
      public bool IsCurrentlySettingText { get; set; }

      public bool IsStabilizingText { get; set; }
      public bool IsKnownTextComponent { get; set; }
      public bool SupportsStabilization { get; set; }
      public bool ShouldIgnore { get; set; }

      public HashSet<string> RedirectedTranslations => _redirectedTranslations ?? ( _redirectedTranslations = new HashSet<string>() );

      public IReadOnlyTextTranslationCache TextCache { get; set; }

      public void Initialize( object ui )
      {
         if( !_initialized )
         {
            _initialized = true;

            IsKnownTextComponent = ui.IsKnownTextType();
            SupportsStabilization = ui.SupportsStabilization();
            ShouldIgnore = ui.ShouldIgnoreTextComponent();
            TextManipulator = ui.GetTextManipulator();
         }
      }

      public void Reset( string newText )
      {
         IsTranslated = false;
         TranslatedText = null;
         OriginalText = newText;
      }

      public void SetTranslatedText( string translatedText )
      {
         IsTranslated = true;
         TranslatedText = translatedText;
      }

      public void ResetScrollIn( object ui )
      {
         if( !_hasCheckedTypeWriter )
         {
            _hasCheckedTypeWriter = true;

            if( UnityTypes.Typewriter != null )
            {
               var component = ui as Component;
               if( ui != null && component.GetComponent( UnityTypes.Typewriter.UnityType ).TryCastTo<MonoBehaviour>( out var typeWriter ) )
               {
                  _typewriter = typeWriter;
               }
            }
         }

         if( _typewriter != null )
         {
            AccessToolsShim.Method( UnityTypes.Typewriter.ClrType, "OnEnable" )?.Invoke( _typewriter, null );
         }
      }

      private static readonly WeakDictionary<Material, Material> _FontMaterialCopies = new WeakDictionary<Material, Material>();

      public void ChangeFont( object ui )
      {
         if( ui == null ) return;

         Hooks.MenuCharacterMessageBoxFontFixHook.EnforceMessageBoxAlignment( ui );

         _changeFontGeneration++;

         XuaLogger.AutoTranslator.Debug(
            "TextTranslationInfo.ChangeFont: called for '" + ui.GetUnityType()?.FullName
            + "', IsTranslated=" + IsTranslated
            + ", TranslatedText.Length=" + ( TranslatedText?.Length ?? -1 ) );

         if( !IsTranslated )
         {
            UnchangeFont( ui );
            return;
         }

         var type = ui.GetUnityType();

         if( UnityTypes.Text != null && UnityTypes.Text.IsAssignableFrom( type ) )
         {
            var Text_fontProperty = UnityTypes.Text_Properties.Font;
            var previousFont = (Font)Text_fontProperty.Get( ui );
            var Font_fontSizeProperty = UnityTypes.Font_Properties.FontSize;

            if( previousFont == null )
            {
               XuaLogger.AutoTranslator.Debug( "TextTranslationInfo.ChangeFont: Text-branch bailing, previousFont (Text.font) is null." );
               return;
            }
            int fontSize = (int)Font_fontSizeProperty?.Get( previousFont );

            if( string.IsNullOrEmpty( Settings.OverrideFont ) )
            {
               if( !_hasCapturedNativeFont )
               {
                  _hasCapturedNativeFont = true;
                  _originalNativeFont = previousFont;
               }
               var nativeFont = _originalNativeFont ?? previousFont;

               if( string.IsNullOrEmpty( TranslatedText ) )
               {
                  XuaLogger.AutoTranslator.Debug(
                     "TextTranslationInfo.ChangeFont: Text-branch keeping native font '" + nativeFont.name
                     + "', skipping proactive RequestCharactersInTexture, TranslatedText is empty." );
                  return;
               }

               Font replacementFont = FontCache.GetOrCreateNativeReplacement( nativeFont.name, fontSize );

               if( replacementFont != null )
               {
                  if( !UnityObjectReferenceComparer.Default.Equals( replacementFont, previousFont ) )
                  {
                     XuaLogger.AutoTranslator.Debug(
                        "TextTranslationInfo.ChangeFont: Text-branch swapping to native replacement font '" + replacementFont.name
                        + "' (id=" + replacementFont.GetInstanceID() + ", bundleReplacement=" + FontCache.IsNativeReplacementBundleFont( replacementFont )
                        + ") at size=" + fontSize + " (native font '" + nativeFont.name + "', id=" + nativeFont.GetInstanceID() + ", previous id=" + previousFont.GetInstanceID() + ")." );

                     Text_fontProperty.Set( ui, replacementFont );
                     _unfont = obj =>
                     {
                        Text_fontProperty.Set( obj, nativeFont );
                     };

                     ui.SetAllDirtyEx();

                     LogAtlasDiagnostics( ui, "swap-to-replacement", replacementFont, nativeFont );
                     ScheduleDelayedFontVerification( ui, replacementFont, "post-swap-to-replacement" );
                     LogAndProbeMeshEffectSiblings( ui, "swap-to-replacement" );
                  }

                  try
                  {
                     var currentFont = (Font)Text_fontProperty.Get( ui );
                     if( !UnityObjectReferenceComparer.Default.Equals( currentFont, replacementFont ) )
                     {
                        XuaLogger.AutoTranslator.Warn(
                           "TextTranslationInfo.ChangeFont: [RU-TEXT-ID] component still holds font id=" + ( currentFont != null ? currentFont.GetInstanceID().ToString() : "null" )
                           + " (name='" + currentFont?.name + "', bundleReplacement=" + FontCache.IsNativeReplacementBundleFont( currentFont )
                           + ") instead of the bundle replacement id=" + replacementFont.GetInstanceID()
                           + " (name='" + replacementFont.name + "') - re-assigning." );

                        Text_fontProperty.Set( ui, replacementFont );
                        _unfont = obj =>
                        {
                           Text_fontProperty.Set( obj, nativeFont );
                        };
                        ui.SetAllDirtyEx();
                     }
                  }
                  catch( Exception e )
                  {
                     XuaLogger.AutoTranslator.Warn( e, "TextTranslationInfo.ChangeFont: failed to read back/verify the replacement font assignment." );
                  }

                  try
                  {
                     int componentFontSize = 0;
                     try
                     {
                        var cs = (int?)UnityTypes.Text_Properties.FontSize?.Get( ui );
                        componentFontSize = cs ?? 0;
                     }
                     catch( Exception )
                     {
                        componentFontSize = 0;
                     }

                     if( componentFontSize > 0 && componentFontSize != fontSize )
                     {
                        Hooks.NativeFontCyrillicSpacingFixHook.PrewarmForSize( replacementFont, componentFontSize, TranslatedText );
                     }
                  }
                  catch( Exception e )
                  {
                     XuaLogger.AutoTranslator.Warn( e, "TextTranslationInfo.ChangeFont: failed to pre-bake glyphs at the component's real font size." );
                  }

                  try
                  {
                     replacementFont.RequestCharactersInTexture( TranslatedText, fontSize );
                  }
                  catch( Exception e )
                  {
                     XuaLogger.AutoTranslator.Warn( e, "TextTranslationInfo.ChangeFont: failed to proactively request/fix characters against the native replacement font." );
                  }

                  TryApplyAutoLineSpacing( ui, replacementFont, fontSize, TranslatedText );

                  LogRussianTextRender( ui, "native-replacement", replacementFont, nativeFont );
               }
               else
               {
                  if( !UnityObjectReferenceComparer.Default.Equals( nativeFont, previousFont ) )
                  {
                     XuaLogger.AutoTranslator.Debug(
                        "TextTranslationInfo.ChangeFont: Text-branch restoring native font '" + nativeFont.name
                        + "', no replacement font shipped for it." );

                     Text_fontProperty.Set( ui, nativeFont );
                     _unfont = null;
                     ui.SetAllDirtyEx();

                     LogAtlasDiagnostics( ui, "restore-native-no-replacement", nativeFont, nativeFont );
                     ScheduleDelayedFontVerification( ui, nativeFont, "post-restore-native" );
                     LogAndProbeMeshEffectSiblings( ui, "restore-native-no-replacement" );
                  }

                  XuaLogger.AutoTranslator.Debug(
                     "TextTranslationInfo.ChangeFont: Text-branch keeping native font '" + nativeFont.name
                     + "' (dynamic=" + nativeFont.dynamic + ") at size=" + fontSize
                     + ", proactively requesting " + TranslatedText.Length + " char(s) against it." );

                  try
                  {
                     nativeFont.RequestCharactersInTexture( TranslatedText, fontSize );
                  }
                  catch( Exception e )
                  {
                     XuaLogger.AutoTranslator.Warn( e, "TextTranslationInfo.ChangeFont: failed to proactively request/fix characters against the native font." );
                  }

                  try
                  {
                     Hooks.NativeFontCyrillicSpacingFixHook.TryCorrect( nativeFont );
                     ui.SetAllDirtyEx();
                  }
                  catch( Exception e )
                  {
                     XuaLogger.AutoTranslator.Warn( e, "TextTranslationInfo.ChangeFont: failed to synchronously apply/refresh the native-font Cyrillic spacing correction." );
                  }

                  TryApplyAutoLineSpacing( ui, nativeFont, fontSize, TranslatedText );

                  LogRussianTextRender( ui, "no-replacement-kept-native", nativeFont, nativeFont );
               }

               return;
            }

            var newFont = FontCache.GetOrCreate( fontSize );
            if( newFont == null )
            {
               XuaLogger.AutoTranslator.Debug( "TextTranslationInfo.ChangeFont: Text-branch bailing, FontCache.GetOrCreate(" + fontSize + ") returned null." );
               return;
            }

            XuaLogger.AutoTranslator.Debug(
               "TextTranslationInfo.ChangeFont: Text-branch resolved override font '" + newFont.name
               + "' (dynamic=" + newFont.dynamic + ") at size=" + fontSize
               + ", previousFont='" + previousFont.name + "'." );

            if( !UnityObjectReferenceComparer.Default.Equals( newFont, previousFont ) )
            {
               Text_fontProperty.Set( ui, newFont );
               _unfont = obj =>
               {
                  Text_fontProperty.Set( obj, previousFont );
               };

               ui.SetAllDirtyEx();

               LogAtlasDiagnostics( ui, "swap-to-override", newFont, previousFont );
               ScheduleDelayedFontVerification( ui, newFont, "post-swap-to-override" );
            }

            if( !string.IsNullOrEmpty( TranslatedText ) )
            {
               XuaLogger.AutoTranslator.Debug(
                  "TextTranslationInfo.ChangeFont: proactively requesting " + TranslatedText.Length
                  + " char(s) against override font '" + newFont.name + "'." );
               try
               {
                  newFont.RequestCharactersInTexture( TranslatedText, fontSize );
               }
               catch( Exception e )
               {
                  XuaLogger.AutoTranslator.Warn( e, "TextTranslationInfo.ChangeFont: failed to proactively request/fix characters against the override font." );
               }

               TryApplyAutoLineSpacing( ui, newFont, fontSize, TranslatedText );
            }
            else
            {
               XuaLogger.AutoTranslator.Debug( "TextTranslationInfo.ChangeFont: skipping proactive RequestCharactersInTexture, TranslatedText is empty." );
            }
         }
         else if( ( UnityTypes.TextMeshPro != null && UnityTypes.TextMeshPro.IsAssignableFrom( type ) )
            || ( UnityTypes.TextMeshProUGUI != null && UnityTypes.TextMeshProUGUI.IsAssignableFrom( type ) ) )
         {
            var clrType = ui.GetType();
            var fontProperty = clrType.CachedProperty( "font" );

            var previousFont = fontProperty.Get( ui );
            if( previousFont == null )
            {
               XuaLogger.AutoTranslator.Debug( "TextTranslationInfo.ChangeFont: TMP-branch bailing, previousFont (font property) is null." );
               return;
            }

            if( string.IsNullOrEmpty( Settings.OverrideFontTextMeshPro ) )
            {
               if( !_hasCapturedNativeFontTextMeshPro )
               {
                  _hasCapturedNativeFontTextMeshPro = true;
                  _originalNativeFontTextMeshPro = previousFont as UnityEngine.Object;
               }
               var nativeFontAsset = _originalNativeFontTextMeshPro ?? ( previousFont as UnityEngine.Object );
               var nativeFontAssetName = nativeFontAsset?.name;

               if( string.IsNullOrEmpty( TranslatedText ) )
               {
                  XuaLogger.AutoTranslator.Debug(
                     "TextTranslationInfo.ChangeFont: TMP-branch keeping native font asset '" + nativeFontAssetName
                     + "', TranslatedText is empty." );
                  return;
               }

               var replacementFont = FontCache.GetOrCreateNativeReplacementTextMeshPro( nativeFontAssetName );
               if( replacementFont != null )
               {
                  if( !UnityObjectReferenceComparer.Default.Equals( replacementFont, previousFont ) )
                  {
                     XuaLogger.AutoTranslator.Debug(
                        "TextTranslationInfo.ChangeFont: TMP-branch swapping native font asset '" + nativeFontAssetName
                        + "' to native replacement '" + ( ( replacementFont as UnityEngine.Object )?.name ?? "<unknown>" ) + "'." );

                     var fontMaterialProperty = clrType.CachedProperty( "fontSharedMaterial" );
                     var oldMaterial = fontMaterialProperty.Get( ui ) as Material;

                     fontProperty.Set( ui, replacementFont );

                     var newMaterial = fontMaterialProperty.Get( ui ) as Material;

                     if( oldMaterial != null && newMaterial != null )
                     {
                        if( !_FontMaterialCopies.TryGetValue( oldMaterial, out var copyMaterial )
                           || copyMaterial.m_CachedPtr == IntPtr.Zero
                        )
                        {
                           copyMaterial = _FontMaterialCopies[ oldMaterial ] = UnityEngine.Object.Instantiate( oldMaterial );

                           var uiCopy = UnityEngine.Object.Instantiate( ui as UnityEngine.Object );
                           fontProperty.Set( uiCopy, previousFont );
                           fontMaterialProperty.Set( uiCopy, oldMaterial );

                           copyMaterial.SetTexture( "_MainTex", newMaterial.GetTexture( "_MainTex" ) );
                           copyMaterial.SetFloat( "_TextureHeight", newMaterial.GetFloat( "_TextureHeight" ) );
                           copyMaterial.SetFloat( "_TextureWidth", newMaterial.GetFloat( "_TextureWidth" ) );
                           copyMaterial.SetFloat( "_GradientScale", newMaterial.GetFloat( "_GradientScale" ) );
                        }
                        fontMaterialProperty.Set( ui, copyMaterial );
                     }

                     _unfont = obj =>
                     {
                        fontProperty.Set( obj, previousFont );
                        fontMaterialProperty.Set( obj, oldMaterial );
                     };
                  }

                  LogRussianTextRender( ui, "native-replacement", replacementFont, nativeFontAsset );
               }
               else
               {
                  XuaLogger.AutoTranslator.Debug(
                     "TextTranslationInfo.ChangeFont: TMP-branch keeping native font asset '" + nativeFontAssetName
                     + "', no replacement shipped for it." );

                  if( _unfont != null )
                  {
                     UnchangeFont( ui );
                  }

                  LogRussianTextRender( ui, "no-replacement-kept-native", nativeFontAsset, nativeFontAsset );
               }

               return;
            }

            var newFont = FontCache.GetOrCreateOverrideFontTextMeshPro();
            if( newFont == null )
            {
               XuaLogger.AutoTranslator.Debug( "TextTranslationInfo.ChangeFont: TMP-branch bailing, GetOrCreateOverrideFontTextMeshPro() returned null for OverrideFontTextMeshPro='" + Settings.OverrideFontTextMeshPro + "'." );
               return;
            }

            XuaLogger.AutoTranslator.Debug(
               "TextTranslationInfo.ChangeFont: TMP-branch resolved override font '" + ( ( newFont as UnityEngine.Object )?.name ?? "<unknown>" ) + "'." );

            if( !UnityObjectReferenceComparer.Default.Equals( newFont, previousFont ) )
            {
               var fontMaterialProperty = clrType.CachedProperty( "fontSharedMaterial" );
               var oldMaterial = fontMaterialProperty.Get( ui ) as Material;

               fontProperty.Set( ui, newFont );

               var newMaterial = fontMaterialProperty.Get( ui ) as Material;

               if( oldMaterial != null && newMaterial != null )
               {
                  if( !_FontMaterialCopies.TryGetValue( oldMaterial, out var copyMaterial )
                     || copyMaterial.m_CachedPtr == IntPtr.Zero
                  )
                  {
                     copyMaterial = _FontMaterialCopies[ oldMaterial ] = UnityEngine.Object.Instantiate( oldMaterial );

                     var uiCopy = UnityEngine.Object.Instantiate( ui as UnityEngine.Object );
                     fontProperty.Set( uiCopy, previousFont );
                     fontMaterialProperty.Set( uiCopy, oldMaterial );

                     copyMaterial.SetTexture( "_MainTex", newMaterial.GetTexture( "_MainTex" ) );
                     copyMaterial.SetFloat( "_TextureHeight", newMaterial.GetFloat( "_TextureHeight" ) );
                     copyMaterial.SetFloat( "_TextureWidth", newMaterial.GetFloat( "_TextureWidth" ) );
                     copyMaterial.SetFloat( "_GradientScale", newMaterial.GetFloat( "_GradientScale" ) );
                  }
                  fontMaterialProperty.Set( ui, copyMaterial );
               }

               _unfont = obj =>
               {
                  fontProperty.Set( obj, previousFont );
                  fontMaterialProperty.Set( obj, oldMaterial );
               };
            }
         }
      }

      public void UnchangeFont( object ui )
      {
         if( ui == null ) return;

         _changeFontGeneration++;

         bool hadFontOverride = _unfont != null;

         _unfont?.Invoke( ui );
         _unfont = null;

         if( hadFontOverride )
         {
            ui.SetAllDirtyEx();
         }
      }

      private static void LogAtlasDiagnostics( object ui, string phase, Font activeFont, Font nativeFont )
      {
         try
         {
            var component = ui as Component;
            var go = component != null ? component.gameObject : null;
            string path = GetGameObjectPath( go );

            string siblingComponents = "<no gameObject>";
            if( go != null )
            {
               var comps = go.GetComponents( Il2CppType.Of<Component>() );
               var names = new List<string>( comps.Length );
               foreach( var c in comps )
               {
                  if( c == null )
                  {
                     names.Add( "<destroyed>" );
                     continue;
                  }

                  string resolvedName = null;
                  try
                  {
                     resolvedName = c.GetUnityType()?.FullName;
                  }
                  catch( Exception )
                  {
                  }

                  names.Add( !string.IsNullOrEmpty( resolvedName ) ? resolvedName : c.GetType().Name );
               }
               siblingComponents = string.Join( ", ", names );
            }

            var activeMat = activeFont != null ? activeFont.material : null;
            var activeTex = activeMat != null ? activeMat.mainTexture : null;
            var nativeMat = nativeFont != null ? nativeFont.material : null;
            var nativeTex = nativeMat != null ? nativeMat.mainTexture : null;

            XuaLogger.AutoTranslator.Debug(
               "[ATLAS-DIAG] " + phase + " on '" + path + "'"
               + " | siblingComponents=[" + siblingComponents + "]"
               + " | activeFont='" + ( activeFont != null ? activeFont.name : "null" )
               + "' dynamic=" + ( activeFont != null ? activeFont.dynamic.ToString() : "?" )
               + " activeMat=" + ( activeMat == null ? "null" : activeMat.GetInstanceID().ToString() )
               + " activeTex=" + ( activeTex == null ? "null" : activeTex.GetInstanceID() + " (" + activeTex.width + "x" + activeTex.height + ")" )
               + " | nativeFont='" + ( nativeFont != null ? nativeFont.name : "null" ) + "'"
               + " nativeMat=" + ( nativeMat == null ? "null" : nativeMat.GetInstanceID().ToString() )
               + " nativeTex=" + ( nativeTex == null ? "null" : nativeTex.GetInstanceID() + " (" + nativeTex.width + "x" + nativeTex.height + ")" )
               + " | sameMaterialAsNative=" + UnityObjectReferenceComparer.Default.Equals( activeMat, nativeMat ) );
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Warn( e, "[ATLAS-DIAG] failed to gather diagnostics for phase '" + phase + "'." );
         }
      }

      private const bool AttemptMeshEffectRefreshKick = true;

      private static void LogAndProbeMeshEffectSiblings( object ui, string phase )
      {
         try
         {
            var component = ui as Component;
            var go = component != null ? component.gameObject : null;
            if( go == null )
            {
               return;
            }

            string path = GetGameObjectPath( go );
            var comps = go.GetComponents( Il2CppType.Of<Component>() );
            var found = new List<string>();

            foreach( var c in comps )
            {
               if( c == null || ReferenceEquals( c, component ) )
               {
                  continue;
               }

               string resolvedName;
               try
               {
                  resolvedName = c.GetUnityType()?.FullName ?? c.GetType().Name;
               }
               catch( Exception )
               {
                  continue;
               }

               if( resolvedName == null )
               {
                  continue;
               }

               var lower = resolvedName.ToLowerInvariant();
               bool looksLikeMeshEffect =
                  lower.Contains( "outline" )
                  || lower.Contains( "shadow" )
                  || lower.Contains( "gradient" )
                  || lower.Contains( "softmask" )
                  || lower.Contains( "uieffect" );

               if( !looksLikeMeshEffect )
               {
                  continue;
               }

               bool? wasEnabled = null;
               bool refreshed = false;
               string refreshFailureReason = null;

               try
               {
                  var enabledProperty = c.GetType().CachedProperty( "enabled" );
                  if( enabledProperty != null )
                  {
                     wasEnabled = (bool)enabledProperty.Get( c );

                     if( AttemptMeshEffectRefreshKick && lower.Contains( "outline" ) && wasEnabled == true )
                     {
                        enabledProperty.Set( c, false );
                        enabledProperty.Set( c, true );
                        refreshed = true;
                     }
                  }
                  else
                  {
                     refreshFailureReason = "no 'enabled' property resolved";
                  }
               }
               catch( Exception e )
               {
                  refreshFailureReason = e.GetType().Name + ": " + e.Message;
               }

               found.Add(
                  resolvedName
                  + "(enabled=" + ( wasEnabled.HasValue ? wasEnabled.Value.ToString() : "?" )
                  + ", refreshKicked=" + refreshed
                  + ( refreshFailureReason != null ? ", refreshFailed='" + refreshFailureReason + "'" : "" )
                  + ")" );
            }

            if( found.Count > 0 )
            {
               XuaLogger.AutoTranslator.Debug(
                  "[MESH-FX-DIAG] " + phase + " on '" + path + "' | meshEffectSiblings=[" + string.Join( ", ", found ) + "]" );
            }
            else
            {
               XuaLogger.AutoTranslator.Debug(
                  "[MESH-FX-DIAG] " + phase + " on '" + path + "' | no outline/shadow/gradient/softmask/effect-named sibling found." );
            }
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Warn( e, "[MESH-FX-DIAG] failed to probe mesh-effect siblings (" + phase + ")." );
         }
      }

      private void LogRussianTextRender( object ui, string outcome, string appliedFontName, string nativeFontName, bool? liveHasCyrillicGlyph = null, bool? faceVerified = null, string actualResolvedFace = null, bool? osEnumerable = null, bool? fingerprintMatches = null, float? expectedAdvancePx = null, float? measuredAdvancePx = null )
      {
         try
         {
            var text = TranslatedText;
            if( string.IsNullOrEmpty( text ) || !LanguageHelper.ContainsRussianSymbols( text ) )
            {
               return;
            }

            var key = text + "\u0001" + outcome + "\u0001" + appliedFontName + "\u0001" + ( liveHasCyrillicGlyph?.ToString() ?? "null" ) + "\u0001" + ( faceVerified?.ToString() ?? "null" ) + "\u0001" + ( fingerprintMatches?.ToString() ?? "null" );
            if( _loggedRussianTextKeys == null )
            {
               _loggedRussianTextKeys = new HashSet<string>();
            }
            if( !_loggedRussianTextKeys.Add( key ) )
            {
               return;
            }

            var component = ui as Component;
            var path = component != null ? GetGameObjectPath( component.gameObject ) : "<no gameObject>";

            var safeText = text.Length > 300 ? text.Substring( 0, 300 ) + "…" : text;
            safeText = safeText.Replace( "\r", "\\r" ).Replace( "\n", "\\n" );

            var liveGlyphText = liveHasCyrillicGlyph.HasValue ? liveHasCyrillicGlyph.Value.ToString() : "<not-checked>";
            var faceVerifiedText = faceVerified.HasValue ? faceVerified.Value.ToString() : "<not-checked>";
            var osEnumerableText = osEnumerable.HasValue ? osEnumerable.Value.ToString() : "<not-checked>";
            var fingerprintText = fingerprintMatches.HasValue ? fingerprintMatches.Value.ToString() : "<not-checked>";

            XuaLogger.AutoTranslator.Debug(
               "[RU-TEXT] outcome=" + outcome
               + " | path='" + path + "'"
               + " | nativeFont='" + ( nativeFontName ?? "null" ) + "'"
               + " | appliedFont='" + ( appliedFontName ?? "null" ) + "'"
               + " | osEnumerable=" + osEnumerableText
               + " | faceVerified=" + faceVerifiedText
               + ( faceVerified == false ? " | actualResolvedFace='" + ( actualResolvedFace ?? "?" ) + "'" : "" )
               + " | liveHasCyrillicGlyph=" + liveGlyphText
               + " | fingerprintMatches=" + fingerprintText
               + ( fingerprintMatches.HasValue ? " | expectedAdvancePx=" + ( expectedAdvancePx?.ToString( "0.00" ) ?? "?" ) + " | measuredAdvancePx=" + ( measuredAdvancePx?.ToString( "0.00" ) ?? "?" ) : "" )
               + " | text=\"" + safeText + "\"" );
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Warn( e, "[RU-TEXT] failed to log Russian text render for outcome '" + outcome + "'." );
         }
      }

      private void LogRussianTextRender( object ui, string outcome, Font appliedFont, Font nativeFont )
      {
         bool? liveHasCyrillicGlyph = null;
         bool? faceVerified = null;
         string actualResolvedFace = null;
         bool? osEnumerable = null;
         bool? fingerprintMatches = null;
         float? expectedAdvancePx = null;
         float? measuredAdvancePx = null;
         try
         {
            if( appliedFont != null )
            {
               liveHasCyrillicGlyph = appliedFont.HasCharacter( 'А' );
               osEnumerable = NativeFontReplacements.TryVerifyAliasEnumerable( appliedFont.name );

               if( NativeFontReplacements.TryGetReplacementWeight( appliedFont.name, out var weight ) )
               {
                  faceVerified = NativeFontReplacements.TryVerifyFontFaceResolves( appliedFont.name, weight, out actualResolvedFace );
               }

               fingerprintMatches = NativeFontReplacements.TryVerifyAdvanceWidthFingerprint( appliedFont.name, appliedFont, appliedFont.fontSize, out float expectedPx, out float measuredPx );
               expectedAdvancePx = expectedPx;
               measuredAdvancePx = measuredPx;
            }
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Warn( e, "[RU-TEXT] live font verification threw for outcome '" + outcome + "'." );
         }

         LogRussianTextRender( ui, outcome, appliedFont?.name, nativeFont?.name, liveHasCyrillicGlyph, faceVerified, actualResolvedFace, osEnumerable, fingerprintMatches, expectedAdvancePx, measuredAdvancePx );
      }

      private void LogRussianTextRender( object ui, string outcome, UnityEngine.Object appliedFontAsset, UnityEngine.Object nativeFontAsset )
      {
         LogRussianTextRender( ui, outcome, appliedFontAsset?.name, nativeFontAsset?.name, liveHasCyrillicGlyph: null, faceVerified: null, actualResolvedFace: null, osEnumerable: null );
      }

      private void TryApplyAutoLineSpacing( object ui, Font font, int fontSize, string translatedText )
      {
         if( !Settings.AutoCalculateLineSpacing )
         {
            return;
         }

         if( ui == null || font == null || fontSize <= 0 )
         {
            return;
         }

         var LineSpacingProperty = UnityTypes.Text_Properties.LineSpacing;
         if( LineSpacingProperty == null )
         {
            return;
         }

         bool fontOrSizeChanged = _autoLineSpacingFont == null
            || !UnityObjectReferenceComparer.Default.Equals( _autoLineSpacingFont, font )
            || _autoLineSpacingFontSize != fontSize;

         float? multiplier = _autoLineSpacingMultiplier;
         if( fontOrSizeChanged || !multiplier.HasValue )
         {
            multiplier = AutoLineSpacingHelper.ComputeAutoLineSpacingMultiplier( font, fontSize, translatedText );
            if( !multiplier.HasValue )
            {
               return;
            }

            _autoLineSpacingFont = font;
            _autoLineSpacingFontSize = fontSize;
            _autoLineSpacingMultiplier = multiplier;

            _autoAppliedLineSpacing = null;
         }

         float? currentLineSpacing = (float?)LineSpacingProperty.Get( ui );
         if( !currentLineSpacing.HasValue )
         {
            return;
         }

         if( _autoAppliedLineSpacing.HasValue && Equals( _autoAppliedLineSpacing.Value, currentLineSpacing.Value ) )
         {
            return;
         }

         float baseline = currentLineSpacing.Value;
         float newLineSpacing = baseline * multiplier.Value;

         if( Mathf.Approximately( newLineSpacing, currentLineSpacing.Value ) )
         {
            _autoLineSpacingBaselineValue = baseline;
            _autoAppliedLineSpacing = currentLineSpacing.Value;
            return;
         }

         LineSpacingProperty.Set( ui, newLineSpacing );
         _autoLineSpacingBaselineValue = baseline;
         _autoAppliedLineSpacing = newLineSpacing;

         XuaLogger.AutoTranslator.Debug(
            "TextTranslationInfo.ChangeFont: [AUTO-LINE-SPACING] font='" + font.name + "' size=" + fontSize
            + " ratio=" + multiplier.Value.ToString( "0.000" )
            + ", lineSpacing " + currentLineSpacing.Value.ToString( "0.000" ) + " -> " + newLineSpacing.ToString( "0.000" ) + "." );
      }

      private static string GetGameObjectPath( GameObject go )
      {
         if( go == null ) return "<null>";

         var sb = new StringBuilder( go.name );
         var t = go.transform != null ? go.transform.parent : null;
         int guard = 0;
         while( t != null && guard++ < 20 )
         {
            sb.Insert( 0, t.name + "/" );
            t = t.parent;
         }
         return sb.ToString();
      }

      private void ScheduleDelayedFontVerification( object ui, Font expectedFont, string tag )
      {
         try
         {
            CoroutineHelper.Start( DelayedFontVerification( ui, expectedFont, tag, _changeFontGeneration, this ) );
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Warn( e, "[ATLAS-DIAG] failed to schedule delayed font verification (" + tag + ")." );
         }
      }

      private static IEnumerator DelayedFontVerification( object ui, Font expectedFont, string tag, int expectedGeneration, TextTranslationInfo info )
      {
         var wait = CoroutineHelper.CreateWaitForSeconds( 1f );
         if( wait != null )
         {
            yield return wait;
         }

         if( ui == null )
         {
            yield break;
         }

         Component component = ui as Component;
         if( component == null )
         {
            yield break;
         }

         var type = ui.GetUnityType();
         if( UnityTypes.Text == null || !UnityTypes.Text.IsAssignableFrom( type ) )
         {
            yield break;
         }

         Font currentFont;
         try
         {
            currentFont = (Font)UnityTypes.Text_Properties.Font.Get( ui );
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Warn( e, "[ATLAS-DIAG] delayed verification (" + tag + ") failed to read Text.font." );
            yield break;
         }

         bool stillMatches = UnityObjectReferenceComparer.Default.Equals( currentFont, expectedFont );
         string goName = component.gameObject != null ? component.gameObject.name : "<no gameObject>";

         bool generationChanged = info != null && info._changeFontGeneration != expectedGeneration;

         XuaLogger.AutoTranslator.Debug(
            "[ATLAS-DIAG] delayed verification (" + tag + ", +1s) for '" + goName
            + "': expected='" + ( expectedFont != null ? expectedFont.name : "null" )
            + "', actual='" + ( currentFont != null ? currentFont.name : "null" )
            + "', stillMatches=" + stillMatches
            + ", generationChanged=" + generationChanged + "." );

         if( !stillMatches && !generationChanged )
         {
            XuaLogger.AutoTranslator.Warn(
               "[ATLAS-DIAG] FONT REVERTED after our own swap on '" + goName
               + "' -- something other than TextTranslationInfo.ChangeFont wrote Text.font again within the last second. "
               + "That call site (not the registration/HasCharacter/ChangeFont pipeline) is the actual bug -- look for another "
               + "SetFontSetting invocation, a repeated DiscoverComponent pass with stale IsTranslated state, or component pooling "
               + "reassigning this same instance to different content in between." );
         }
         else if( !stillMatches )
         {
            XuaLogger.AutoTranslator.Debug(
               "[ATLAS-DIAG] font differs from the post-swap snapshot on '" + goName
               + "', but ChangeFont/UnchangeFont already ran again for this component since scheduling (generation "
               + expectedGeneration + " -> " + ( info?._changeFontGeneration.ToString() ?? "?" )
               + ") - expected, not a revert." );
         }
      }

      private static float GetComponentWidth( Component component )
      {
         if( component.transform.TryCastTo<RectTransform>( out var rectTransform ) )
         {
            return rectTransform.rect.width;
         }
         return 0f;
      }

      public void ResizeUI( object ui, UIResizeCache cache )
      {
         if( ui == null ) return;

         var type = ui.GetUnityType();

         if( UnityTypes.Text != null && UnityTypes.Text.IsAssignableFrom( type ) )
         {
            var text = (Component)ui;
            if( !text || !text.gameObject ) return;

            var componentWidth = GetComponentWidth( text );
            var quarterScreenSize = Screen.width / 4;
            var isComponentWide = componentWidth > quarterScreenSize;

            bool isLineSpacingSet = false;
            bool isHorizontalOverflowSet = false;
            bool isVerticalOverflowSet = false;
            bool isUntouched = _unresize == null;
            if( cache.HasAnyResizeCommands )
            {
               var segments = text.gameObject.GetPathSegments();
               var scope = TranslationScopeHelper.GetScope( ui );
               if( cache.TryGetUIResize( segments, scope, out var result ) )
               {
                  if( result.AutoResizeCommand != null )
                  {
                     var resizeTextForBestFitValue = UnityTypes.Text_Properties.ResizeTextForBestFit.Get( ui );

                     if( resizeTextForBestFitValue != null )
                     {
                        var resizeTextMinSizeValue = UnityTypes.Text_Properties.ResizeTextMinSize?.Get( ui );
                        var resizeTextMaxSizeValue = UnityTypes.Text_Properties.ResizeTextMaxSize?.Get( ui );

                        var minSize = result.AutoResizeCommand.GetMinSize() ?? 1d;
                        if( UnityTypes.Text_Properties.ResizeTextMinSize != null )
                        {
                           int minSizeCorrected = double.IsNaN( minSize ) ? 1 : (int)minSize;
                           UnityTypes.Text_Properties.ResizeTextMinSize.Set( ui, minSizeCorrected );
                        }

                        var maxSize = result.AutoResizeCommand.GetMaxSize();
                        if( maxSize.HasValue && UnityTypes.Text_Properties.ResizeTextMaxSize != null )
                        {
                           int maxSizeCorrected = double.IsNaN( maxSize.Value ) ? 1 : (int)maxSize.Value;
                           UnityTypes.Text_Properties.ResizeTextMaxSize?.Set( ui, maxSizeCorrected );
                        }

                        var shouldAutoResize = result.AutoResizeCommand.ShouldAutoResize();
                        UnityTypes.Text_Properties.ResizeTextForBestFit.Set( ui, shouldAutoResize );

                        if( isUntouched )
                        {
                           _unresize += g =>
                           {
                              UnityTypes.Text_Properties.ResizeTextForBestFit.Set( g, resizeTextForBestFitValue );
                           };

                           if( UnityTypes.Text_Properties.ResizeTextMinSize != null )
                           {
                              _unresize += g =>
                              {
                                 UnityTypes.Text_Properties.ResizeTextMinSize.Set( g, resizeTextMinSizeValue );
                              };
                           }

                           if( maxSize.HasValue && UnityTypes.Text_Properties.ResizeTextMaxSize != null )
                           {
                              _unresize += g =>
                              {
                                 UnityTypes.Text_Properties.ResizeTextMaxSize.Set( g, resizeTextMaxSizeValue );
                              };
                           }
                        }
                     }
                  }

                  if( result.ResizeCommand != null )
                  {
                     var currentFontSize = (int?)UnityTypes.Text_Properties.FontSize.Get( ui );

                     if( currentFontSize.HasValue && !Equals( _alteredFontSize, currentFontSize ) )
                     {
                        var newFontSize = result.ResizeCommand.GetSize( currentFontSize.Value );
                        if( newFontSize.HasValue )
                        {
                           UnityTypes.Text_Properties.FontSize.Set( ui, newFontSize.Value );
                           _alteredFontSize = newFontSize.Value;

                           if( isUntouched )
                           {
                              _unresize += g =>
                              {
                                 UnityTypes.Text_Properties.FontSize.Set( g, currentFontSize );
                              };
                           }
                        }
                     }
                  }

                  if( result.LineSpacingCommand != null )
                  {
                     var lineSpacingValue = (float?)UnityTypes.Text_Properties.LineSpacing.Get( ui );

                     if( lineSpacingValue.HasValue && !Equals( _alteredLineSpacing, lineSpacingValue ) )
                     {
                        var newLineSpacingValue = result.LineSpacingCommand.GetLineSpacing( lineSpacingValue.Value );
                        if( newLineSpacingValue.HasValue )
                        {
                           isLineSpacingSet = true;
                           UnityTypes.Text_Properties.LineSpacing.Set( ui, newLineSpacingValue.Value );
                           _alteredLineSpacing = newLineSpacingValue;

                           if( isUntouched )
                           {
                              _unresize += g =>
                              {
                                 UnityTypes.Text_Properties.LineSpacing.Set( g, lineSpacingValue );
                              };
                           }
                        }
                     }
                  }

                  if( result.HorizontalOverflowCommand != null )
                  {
                     var horizontalOverflowValue = UnityTypes.Text_Properties.HorizontalOverflow.Get( ui );

                     if( horizontalOverflowValue != null )
                     {
                        var newHorizontalOverflowValue = result.HorizontalOverflowCommand.GetMode();
                        if( newHorizontalOverflowValue.HasValue )
                        {
                           isHorizontalOverflowSet = true;
                           UnityTypes.Text_Properties.HorizontalOverflow.Set( ui, newHorizontalOverflowValue.Value );

                           if( isUntouched )
                           {
                              _unresize += g =>
                              {
                                 UnityTypes.Text_Properties.HorizontalOverflow.Set( g, horizontalOverflowValue );
                              };
                           }
                        }
                     }
                  }

                  if( result.VerticalOverflowCommand != null )
                  {
                     var verticalOverflowValue = UnityTypes.Text_Properties.VerticalOverflow.Get( ui );

                     if( verticalOverflowValue != null )
                     {
                        var newVerticalOverflowValue = result.VerticalOverflowCommand.GetMode();
                        if( newVerticalOverflowValue.HasValue )
                        {
                           isVerticalOverflowSet = true;
                           UnityTypes.Text_Properties.VerticalOverflow.Set( ui, newVerticalOverflowValue.Value );

                           if( isUntouched )
                           {
                              _unresize += g =>
                              {
                                 UnityTypes.Text_Properties.VerticalOverflow.Set( g, verticalOverflowValue );
                              };
                           }
                        }
                     }
                  }
               }
            }

            if( isComponentWide && ( UnityTypes.Text_Properties.ResizeTextForBestFit == null || !(bool)UnityTypes.Text_Properties.ResizeTextForBestFit.Get( text ) ) )
            {
               if( !isLineSpacingSet && Settings.ResizeUILineSpacingScale.HasValue && UnityTypes.Text_Properties.LineSpacing != null )
               {
                  var originalLineSpacing = UnityTypes.Text_Properties.LineSpacing.Get( text );

                  if( !Equals( _alteredLineSpacing, originalLineSpacing ) )
                  {
                     var newLineSpacing = (float)originalLineSpacing * Settings.ResizeUILineSpacingScale.Value;
                     UnityTypes.Text_Properties.LineSpacing.Set( text, newLineSpacing );
                     _alteredLineSpacing = newLineSpacing;

                     if( isUntouched )
                     {
                        _unresize += g =>
                        {
                           UnityTypes.Text_Properties.LineSpacing.Set( g, originalLineSpacing );
                        };
                     }
                  }
               }

               if( !isVerticalOverflowSet && UnityTypes.Text_Properties.VerticalOverflow != null )
               {
                  var originalVerticalOverflow = UnityTypes.Text_Properties.VerticalOverflow.Get( text );
                  UnityTypes.Text_Properties.VerticalOverflow.Set( text, 1  );

                  if( isUntouched )
                  {
                     _unresize += g =>
                     {
                        UnityTypes.Text_Properties.VerticalOverflow.Set( g, originalVerticalOverflow );
                     };
                  }
               }

               if( !isHorizontalOverflowSet && UnityTypes.Text_Properties.HorizontalOverflow != null )
               {
                  var originalHorizontalOverflow = UnityTypes.Text_Properties.HorizontalOverflow.Get( text );
                  UnityTypes.Text_Properties.HorizontalOverflow.Set( text, 0  );

                  if( isUntouched )
                  {
                     _unresize += g =>
                     {
                        UnityTypes.Text_Properties.HorizontalOverflow.Set( g, originalHorizontalOverflow );
                     };
                  }
               }
            }
         }
         else if( type == UnityTypes.UILabel?.UnityType )
         {

            var useFloatSpacingPropertyValue = UnityTypes.UILabel_Properties.UseFloatSpacing?.Get( ui );
            var spacingXPropertyValue = UnityTypes.UILabel_Properties.SpacingX?.Get( ui );
            var multiLinePropertyValue = UnityTypes.UILabel_Properties.MultiLine?.Get( ui );
            var overflowMethodPropertyValue = UnityTypes.UILabel_Properties.OverflowMethod?.Get( ui );

            UnityTypes.UILabel_Properties.UseFloatSpacing?.Set( ui, false );
            UnityTypes.UILabel_Properties.SpacingX?.Set( ui, -1 );
            UnityTypes.UILabel_Properties.MultiLine?.Set( ui, true );
            UnityTypes.UILabel_Properties.OverflowMethod?.Set( ui, 0 );

            if( _unresize == null )
            {
               _unresize = g =>
               {
                  UnityTypes.UILabel_Properties.UseFloatSpacing?.Set( g, useFloatSpacingPropertyValue );
                  UnityTypes.UILabel_Properties.SpacingX?.Set( g, spacingXPropertyValue );
                  UnityTypes.UILabel_Properties.MultiLine?.Set( g, multiLinePropertyValue );
                  UnityTypes.UILabel_Properties.OverflowMethod?.Set( g, overflowMethodPropertyValue );
               };
            }

            if( cache.HasAnyResizeCommands )
            {
               var text = (Component)ui;
               if( !text || !text.gameObject ) return;
               var clrType = ui.GetType();

               var segments = text.gameObject.GetPathSegments();
               var scope = TranslationScopeHelper.GetScope( ui );
               if( cache.TryGetUIResize( segments, scope, out var result ) )
               {
                  if( result.ResizeCommand != null )
                  {
                     var fontSizeProperty = clrType.CachedProperty( "fontSize" );
                     var currentFontSize = (int)fontSizeProperty.Get( ui );

                     if( !Equals( _alteredFontSize, currentFontSize ) )
                     {
                        var newFontSize = result.ResizeCommand.GetSize( currentFontSize );
                        if( newFontSize.HasValue )
                        {
                           fontSizeProperty.Set( ui, newFontSize.Value );
                           _alteredFontSize = newFontSize.Value;

                           _unresize += g =>
                           {
                              fontSizeProperty.Set( g, currentFontSize );
                           };
                        }
                     }
                  }
               }
            }
         }
         else if( type == UnityTypes.TextMeshPro?.UnityType || type == UnityTypes.TextMeshProUGUI?.UnityType )
         {
            var clrType = ui.GetType();
            var overflowModeProperty = clrType.CachedProperty( "overflowMode" );
            var originalOverflowMode = overflowModeProperty?.Get( ui );
            bool changedOverflow = false;
            bool isUntouched = _unresize == null;

            if( cache.HasAnyResizeCommands )
            {
               var text = (Component)ui;
               if( !text || !text.gameObject ) return;

               var segments = text.gameObject.GetPathSegments();
               var scope = TranslationScopeHelper.GetScope( ui );
               if( cache.TryGetUIResize( segments, scope, out var result ) )
               {
                  if( result.OverflowCommand != null )
                  {
                     changedOverflow = true;
                     if( overflowModeProperty != null )
                     {
                        var newOverflowMode = result.OverflowCommand.GetMode();
                        if( newOverflowMode.HasValue )
                        {
                           overflowModeProperty.Set( ui, newOverflowMode );

                           if( isUntouched )
                           {
                              _unresize = g =>
                              {
                                 overflowModeProperty.Set( g, originalOverflowMode );
                              };
                           }
                        }
                     }
                  }

                  if( result.AlignmentCommand != null )
                  {
                     var alignmentProperty = clrType.CachedProperty( "alignment" );
                     if( alignmentProperty != null )
                     {
                        var alignmentValue = alignmentProperty.Get( ui );
                        var newAlignmentValue = result.AlignmentCommand.GetMode();

                        if( newAlignmentValue.HasValue )
                        {
                           alignmentProperty.Set( ui, newAlignmentValue.Value );

                           if( isUntouched )
                           {
                              _unresize += g =>
                              {
                                 alignmentProperty.Set( g, alignmentValue );
                              };
                           }
                        }
                     }
                  }

                  if( result.AutoResizeCommand != null )
                  {
                     var enableAutoSizingProperty = clrType.CachedProperty( "enableAutoSizing" );
                     var fontSizeMinProperty = clrType.CachedProperty( "fontSizeMin" );
                     var fontSizeMaxProperty = clrType.CachedProperty( "fontSizeMax" );
                     var fontSizeProperty = clrType.CachedProperty( "fontSize" );
                     var currentFontSize = (float?)fontSizeProperty.Get( ui );

                     if( enableAutoSizingProperty != null )
                     {
                        var enableAutoSizingValue = enableAutoSizingProperty.Get( ui );
                        var fontSizeMinValue = fontSizeMinProperty?.Get( ui );
                        var fontSizeMaxValue = fontSizeMaxProperty?.Get( ui );

                        var minSize = result.AutoResizeCommand.GetMinSize();
                        if( minSize.HasValue && fontSizeMinProperty != null )
                        {
                           float minSizeCorrected = double.IsNaN( minSize.Value ) ? 0f : (float)minSize.Value;
                           fontSizeMinProperty?.Set( ui, minSizeCorrected );
                        }

                        var maxSize = result.AutoResizeCommand.GetMaxSize();
                        if( maxSize.HasValue && fontSizeMaxProperty != null )
                        {
                           float maxSizeCorrected = double.IsNaN( maxSize.Value ) ? float.MaxValue : (float)maxSize.Value;
                           fontSizeMaxProperty?.Set( ui, maxSizeCorrected );
                        }

                        var shouldAutoResize = result.AutoResizeCommand.ShouldAutoResize();
                        enableAutoSizingProperty.Set( ui, shouldAutoResize );

                        if( isUntouched )
                        {
                           _unresize += g =>
                           {
                              enableAutoSizingProperty.Set( g, enableAutoSizingValue );
                           };

                           if( minSize.HasValue && fontSizeMinProperty != null )
                           {
                              _unresize += g =>
                              {
                                 fontSizeMinProperty.Set( g, fontSizeMinValue );
                              };
                           }

                           if( maxSize.HasValue && fontSizeMaxProperty != null )
                           {
                              _unresize += g =>
                              {
                                 fontSizeMaxProperty.Set( g, fontSizeMaxValue );
                              };
                           }

                           _unresize += g =>
                           {
                              fontSizeProperty.Set( g, currentFontSize );
                           };
                        }
                     }
                  }

                  if( result.ResizeCommand != null )
                  {
                     var fontSizeProperty = clrType.CachedProperty( "fontSize" );
                     var currentFontSize = (float?)fontSizeProperty.Get( ui );

                     if( currentFontSize.HasValue )
                     {
                        var currentFontSizeInt = (int)currentFontSize.Value;
                        if( !Equals( _alteredFontSize, currentFontSizeInt ) )
                        {
                           var newFontSize = result.ResizeCommand.GetSize( (int)currentFontSize.Value );
                           if( newFontSize.HasValue )
                           {
                              fontSizeProperty.Set( ui, (float)newFontSize.Value );
                              _alteredFontSize = newFontSize.Value;

                              if( isUntouched )
                              {
                                 _unresize += g =>
                                 {
                                    fontSizeProperty.Set( g, currentFontSize );
                                 };
                              }
                           }
                        }
                     }
                  }
               }
            }

            if( !changedOverflow )
            {
               if( originalOverflowMode != null && (int)originalOverflowMode == 2 )
               {
                  overflowModeProperty.Set( ui, 3 );

                  if( isUntouched )
                  {
                     _unresize = g =>
                     {
                        overflowModeProperty.Set( g, 2 );
                     };
                  }
               }
            }
         }
      }

      public void UnresizeUI( object ui )
      {
         if( ui == null ) return;

         _unresize?.Invoke( ui );
         _unresize = null;

         _alteredFontSize = null;
      }
   }
}
