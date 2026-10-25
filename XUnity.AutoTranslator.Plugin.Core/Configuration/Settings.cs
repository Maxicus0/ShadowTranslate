using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using XUnity.AutoTranslator.Plugin.Core.Constants;
using XUnity.AutoTranslator.Plugin.Core.Debugging;
using XUnity.AutoTranslator.Plugin.Core.Extensions;
using XUnity.AutoTranslator.Plugin.Core.Parsing;
using XUnity.AutoTranslator.Plugin.Core.Utilities;
using XUnity.AutoTranslator.Plugin.Utilities;
using XUnity.Common.Constants;
using XUnity.Common.Extensions;
using XUnity.Common.Logging;

namespace XUnity.AutoTranslator.Plugin.Core.Configuration
{
   internal static class Settings
   {
      public static string TextMeshProVersion = null;
      public static readonly int MaxFailuresForSameTextPerEndpoint = 3;
      public static readonly string TranslatorsFolder = "Translators";
      public static readonly int MaxMaxCharactersPerTranslation = 2500;
      public static readonly string DefaultLanguage = "en";
      public static readonly string DefaultFromLanguage = "ja";
      public static readonly string EnglishLanguage = "en";
      public static readonly string Romaji = "romaji";
      public static readonly int MaxErrors = 5;
      public static readonly int MaxTranslationsBeforeShutdown = 8000;
      public static readonly int MaxUnstartedJobs = 4000;
      public static readonly float IncreaseBatchOperationsEvery = 30;
      public static readonly int MaximumStaggers = 6;
      public static readonly int PreviousTextStaggerCount = 3;
      public static readonly int MaximumConsecutiveFramesTranslated = 90;
      public static readonly int MaximumConsecutiveSecondsTranslated = 60;
      public static bool FromLanguageUsesWhitespaceBetweenWords = false;
      public static bool ToLanguageUsesWhitespaceBetweenWords = false;
      public static string ApplicationName;
      public static float Timeout = 150.0f;
      public static string RedirectedResourcesPath;
      public static readonly int MaxImguiKeyCacheCount = 10000;
      public static readonly float DefaultTranslationDelay = 0.9f;
      public static readonly int DefaultMaxRetries = 67;

      public static Dictionary<string, string> Replacements = new Dictionary<string, string>();
      public static Dictionary<string, string> Preprocessors = new Dictionary<string, string>();
      public static Dictionary<string, string> Postprocessors = new Dictionary<string, string>();

      public static bool SimulateError = false;
      public static bool SimulateDelayedError = false;

      public static bool InvokeEvents = true;
      public static Action<object> RemakeTextData = null;
      public static Action<object> SetCurText = null;

      public static bool IsShutdown = false;
      public static int TranslationCount = 0;
      public static int MaxAvailableBatchOperations = 50;

      public static readonly float MaxTranslationsQueuedPerSecond = 5;
      public static readonly int MaxSecondsAboveTranslationThreshold = 30;
      public static readonly int TranslationQueueWatchWindow = 6;

      public static string ServiceEndpoint;
      public static string FallbackServiceEndpoint;
      public static string Language;
      public static string FromLanguage;
      public static string OutputFile;
      public static string StoryTranslationFile;
      public static string PatternTranslationFile;
      public static string SubstitutionFile;
      public static string PreprocessorsFile;
      public static string PostprocessorsFile;
      public static string TranslationDirectory;
      public static int MaxCharactersPerTranslation;
      public static bool EnableConsole;
      public static string AutoTranslationsFilePath;
      public static string StoryTranslationFilePath;
      public static string PatternTranslationFilePath;
      public static string SubstitutionFilePath;
      public static string PreprocessorsFilePath;
      public static string PostprocessorsFilePath;
      public static string TranslationsPath;
      public static string TexturesPath;
      public static string TranslatorsPath;
      public static readonly bool EnableUGUI = true;
      public static readonly bool EnableIMGUI = false;
      public static readonly bool EnableUIElements = false;
      public static readonly bool EnableNGUI = false;
      public static readonly bool EnableTextMeshPro = true;
      public static readonly bool EnableTextMesh = false;
      public static readonly bool EnableFairyGUI = false;
      public static bool IgnoreWhitespaceInDialogue;
      public static int MinDialogueChars;
      public static int ForceSplitTextAfterCharacters;
      public static bool EnableMigrations;
      public static string MigrationsTag;
      public static bool EnableBatching;
      public static bool EnableUIResizing;
      public static bool UseStaticTranslations;
      public static string OverrideFont;
      public static string OverrideFontFile;
      public static bool AutoRelaunchOnFontRegistrationFailure;
      public static string StoryScriptMasterTextMember;
      public static string[][] ForceBypassedSdwTextPaths;
      public static bool EnableSdwDiagnostics;
      public static string SingleLinePathParts;
      public static string LabelWidthRules;
      public static bool LogCyrillicTranslations;
      public static string LabelFixedScaleRules;
      public static bool NameLineWrap;
      public static string SettleFramePathParts;
      public static bool EnableFontDiagnostics;
      public static bool EnableHitchProfiler;
      public static bool EnableVSync;
      public static int[] SdwPrebakeFontSizes;
      public static int? OverrideFontSize;
      public static string OverrideFontTextMeshPro;
      public static string FallbackFontTextMeshPro;
      public static string UserAgent;
      public static bool DisableCertificateValidation;
      public static float? ResizeUILineSpacingScale;
      public static bool AutoCalculateLineSpacing;
      public static bool EnableCyrillicSpacingFix;
      public static bool EnableSdwLineSpacingPolicy;
      public static float SdwTargetLineEm;

      public static bool TipsScreenWrapSafetyNet;

      public static bool ForceUIResizing;
      public static string[] IgnoreTextStartingWith;
      public static HashSet<string> GameLogTextPaths;
      public static bool TextGetterCompatibilityMode;
      public static TextPostProcessing RomajiPostProcessing;
      public static TextPostProcessing TranslationPostProcessing;
      public static TextPostProcessing RegexPostProcessing;
      public static bool ForceMonoModHooks;
      public static bool CacheRegexPatternResults;
      public static bool CacheRegexLookups;
      public static bool CacheWhitespaceDifferences;
      public static bool GenerateStaticSubstitutionTranslations;
      public static bool GeneratePartialTranslations;
      public static bool EnableTranslationScoping;
      public static bool EnableSilentMode;
      public static HashSet<string> BlacklistedIMGUIPlugins;
      public static bool EnableTextPathLogging;
      public static bool OutputUntranslatableText;
      public static bool IgnoreVirtualTextSetterCallingRules;
      public static int MaxTextParserRecursion;
      public static bool HtmlEntityPreprocessing;
      public static bool HandleRichText;
      public static PersistRichTextMode PersistRichTextMode;
      public static bool EnableTranslationHelper;
      public static RedirectedResourceDetection RedirectedResourceDetectionStrategy;
      public static bool OutputTooLongText;
      public static bool TemplateAllNumberAway;
      public static bool ReloadTranslationsOnFileChange;
      public static bool DisableTextMeshProScrollInEffects;
      public static bool CacheParsedTranslations;

      public static string TextureDirectory;
      public static bool EnableTextureTranslation;
      public static bool EnableTextureDumping;
      public static bool EnableTextureToggling;
      public static bool EnableTextureScanOnSceneLoad;
      public static bool EnableSpriteRendererHooking;
      public static bool LoadUnmodifiedTextures;
      public static bool DetectDuplicateTextureNames;
      public static bool EnableLegacyTextureLoading;
      public static HashSet<string> DuplicateTextureNames;
      public static TextureHashGenerationStrategy TextureHashGenerationStrategy;
      public static bool CacheTexturesInMemory;
      public static bool EnableSpriteHooking;

      public static string PreferredStoragePath;
      public static bool EnableDumping;
      public static bool EnableTextAssetRedirector;
      public static bool LogAllLoadedResources;
      public static bool CacheMetadataForAllFiles;

      public static float Height;
      public static float Width;
      public static HashSet<string> EnabledTranslators;

      public static bool CopyToClipboard;
      public static int MaxClipboardCopyCharacters;
      public static float ClipboardDebounceTime;

      public static void Configure()
      {
         try
         {
            var fi = new FileInfo( typeof( Settings ).Assembly.Location );
            var di = fi.Directory;
            TranslatorsPath = Path.Combine( di.FullName, TranslatorsFolder );

            try
            {
               try
               {
                  ApplicationName = Path.GetFileNameWithoutExtension( ApplicationInformation.StartupPath );
               }
               catch( Exception )
               {
                  ApplicationName = Path.GetFileNameWithoutExtension( Process.GetCurrentProcess().MainModule.FileName );
               }
            }
            catch( Exception e )
            {
               XuaLogger.AutoTranslator.Warn( e, "An error occurred while obtaining the path to the executable. {GameExeName} variable in configuration files will not work correctly." );

               ApplicationName = string.Empty;
            }

            try
            {
               XuaLogger.AutoTranslator.Debug( "Screen resolution determine to be: " + Screen.width + "x" + Screen.height );
            }
            catch( Exception e )
            {
               XuaLogger.AutoTranslator.Warn( e, "An error occurred while trying to determine the game resolution." );
            }

            try
            {
               var versionProperty = UnityTypes.TMP_Settings_Properties.Version;
               if( versionProperty != null )
               {
                  TextMeshProVersion = (string)versionProperty.Get( null );
                  XuaLogger.AutoTranslator.Info( $"Version of TextMesh Pro: {TextMeshProVersion}." );
               }
            }
            catch( Exception e )
            {
               XuaLogger.AutoTranslator.Warn( e, "An error occurred while trying to determine TextMesh Pro version." );
            }

            ServiceEndpoint = PluginEnvironment.Current.Preferences.GetOrDefault( "Service", "Endpoint", string.Empty );
            FallbackServiceEndpoint = string.Empty;

            Language = string.Intern( PluginEnvironment.Current.Preferences.GetOrDefault( "General", "Language", "ru" ) );
            FromLanguage = string.Intern( PluginEnvironment.Current.Preferences.GetOrDefault( "General", "FromLanguage", "en" ) );

            TranslationDirectory = PluginEnvironment.Current.Preferences.GetOrDefault( "Files", "Directory", Path.Combine( "Translation", Path.Combine( "{Lang}", "Text" ) ) );
            OutputFile = PluginEnvironment.Current.Preferences.GetOrDefault( "Files", "OutputFile", Path.Combine( "Translation", Path.Combine( "{Lang}", Path.Combine( "Text", "_AutoGeneratedTranslations.txt" ) ) ) );
            SubstitutionFile = PluginEnvironment.Current.Preferences.GetOrDefault( "Files", "SubstitutionFile", Path.Combine( "Translation", Path.Combine( "{Lang}", Path.Combine( "Text", "_Substitutions.txt" ) ) ) );
            PreprocessorsFile = PluginEnvironment.Current.Preferences.GetOrDefault( "Files", "PreprocessorsFile", Path.Combine( "Translation", Path.Combine( "{Lang}", Path.Combine( "Text", "_Preprocessors.txt" ) ) ) );
            PostprocessorsFile = PluginEnvironment.Current.Preferences.GetOrDefault( "Files", "PostprocessorsFile", Path.Combine( "Translation", Path.Combine( "{Lang}", Path.Combine( "Text", "_Postprocessors.txt" ) ) ) );
            StoryTranslationFile = PluginEnvironment.Current.Preferences.GetOrDefault( "Files", "StoryTranslationFile", Path.Combine( "Translation", Path.Combine( "{Lang}", Path.Combine( "Text", "_TranslationsStory.txt" ) ) ) );
            PatternTranslationFile = PluginEnvironment.Current.Preferences.GetOrDefault( "Files", "PatternTranslationFile", Path.Combine( "Translation", Path.Combine( "{Lang}", Path.Combine( "Text", "_TranslationsPatterns.txt" ) ) ) );

            MaxCharactersPerTranslation = PluginEnvironment.Current.Preferences.GetOrDefault( "Behaviour", "MaxCharactersPerTranslation", 2500 );
            IgnoreWhitespaceInDialogue = PluginEnvironment.Current.Preferences.GetOrDefault( "Behaviour", "IgnoreWhitespaceInDialogue", true );
            MinDialogueChars = PluginEnvironment.Current.Preferences.GetOrDefault( "Behaviour", "MinDialogueChars", 1 );
            ForceSplitTextAfterCharacters = PluginEnvironment.Current.Preferences.GetOrDefault( "Behaviour", "ForceSplitTextAfterCharacters", 0 );
            CopyToClipboard = false;
            MaxClipboardCopyCharacters = 2500;
            ClipboardDebounceTime = 1.25f;
            EnableUIResizing = true;
            EnableBatching = true;
            UseStaticTranslations = PluginEnvironment.Current.Preferences.GetOrDefault( "Behaviour", "UseStaticTranslations", true );
            OverrideFont = PluginEnvironment.Current.Preferences.GetOrDefault( "Behaviour", "OverrideFont", string.Empty );
            OverrideFontFile = string.Empty;
            AutoRelaunchOnFontRegistrationFailure = PluginEnvironment.Current.Preferences.GetOrDefault( "Behaviour", "AutoRelaunchOnFontRegistrationFailure", true );
            StoryScriptMasterTextMember = PluginEnvironment.Current.Preferences.GetOrDefault( "Behaviour", "StoryScriptMasterTextMember", string.Empty );
            ForceBypassedSdwTextPaths = ParseForceBypassedSdwTextPaths(
               PluginEnvironment.Current.Preferences.GetOrDefault( "Behaviour", "ForceBypassedSdwTextPaths", "Download_SystemTips,TipsObject" ) );
            EnableSdwDiagnostics = PluginEnvironment.Current.Preferences.GetOrDefault( "Behaviour", "EnableSdwDiagnostics", true );
            SingleLinePathParts = PluginEnvironment.Current.Preferences.GetOrDefault( "Behaviour", "SingleLinePathParts", "/BalloonRoot/;/CommonPopBalloon;/BoostIcon/;/Text_Limit;/DesiredItemView/Text_Name;/ItemName/ItemName;/EquipmentDetail/LayoutArea/UpperArea/TextArea/NameLabel;/DetailTable01/Text_Template" );
            LabelWidthRules = PluginEnvironment.Current.Preferences.GetOrDefault( "Behaviour", "LabelWidthRules4", "/DesiredItemView/Text_Name=0.74;/ItemName/ItemName=0.7;/EquipmentDetail/LayoutArea/UpperArea/TextArea/NameLabel=0.5;/DetailTable01/Text_Template=0.66" );
            LogCyrillicTranslations = PluginEnvironment.Current.Preferences.GetOrDefault( "Behaviour", "LogCyrillicTranslations", true );
            NameLineWrap = PluginEnvironment.Current.Preferences.GetOrDefault( "Behaviour", "NameLineWrap", false );
            LabelFixedScaleRules = PluginEnvironment.Current.Preferences.GetOrDefault( "Behaviour", "FixedScaleRules", "/CommonPopBalloon=1.0" );
            SettleFramePathParts = PluginEnvironment.Current.Preferences.GetOrDefault( "Behaviour", "SettleFramePathParts", "/ScreenNameText" );
            EnableFontDiagnostics = PluginEnvironment.Current.Preferences.GetOrDefault( "Behaviour", "EnableFontDiagnostics", false );
            EnableHitchProfiler = PluginEnvironment.Current.Preferences.GetOrDefault( "Behaviour", "EnableHitchProfiler", false );
            EnableVSync = PluginEnvironment.Current.Preferences.GetOrDefault( "Behaviour", "EnableVSync", true );
            SdwPrebakeFontSizes = ParsePrebakeSizes(
               PluginEnvironment.Current.Preferences.GetOrDefault( "Behaviour", "SdwPrebakeFontSizes", "16,18,20,22,24,26,28,30,32,34,36,38,40,46,60" ) );
            OverrideFontSize = PluginEnvironment.Current.Preferences.GetOrDefault( "Behaviour", "OverrideFontSize", (int?)null );
            OverrideFontTextMeshPro = string.Empty;
            FallbackFontTextMeshPro = string.Empty;
            ResizeUILineSpacingScale = null;
            AutoCalculateLineSpacing = false;
            EnableCyrillicSpacingFix = PluginEnvironment.Current.Preferences.GetOrDefault( "Behaviour", "EnableCyrillicSpacingFix", false );
            EnableSdwLineSpacingPolicy = PluginEnvironment.Current.Preferences.GetOrDefault( "Behaviour", "EnableSdwLineSpacingPolicy", true );
            SdwTargetLineEm = PluginEnvironment.Current.Preferences.GetOrDefault( "Behaviour", "SdwTargetLineEm", 1.2f );
            TipsScreenWrapSafetyNet = PluginEnvironment.Current.Preferences.GetOrDefault( "Behaviour", "TipsScreenWrapSafetyNet", true );
            ForceUIResizing = false;
            IgnoreTextStartingWith = PluginEnvironment.Current.Preferences.GetOrDefault( "Behaviour", "IgnoreTextStartingWith", "\\u180e;" )
               ?.Split( new[] { ';' }, StringSplitOptions.RemoveEmptyEntries ).Select( x => JsonHelper.Unescape( x ) ).ToArray() ?? new string[ 0 ];
            TextGetterCompatibilityMode = false;
            GameLogTextPaths = new HashSet<string>();
            RomajiPostProcessing = TextPostProcessing.ReplaceMacronWithCircumflex | TextPostProcessing.RemoveApostrophes | TextPostProcessing.ReplaceHtmlEntities;
            TranslationPostProcessing = PluginEnvironment.Current.Preferences.GetOrDefault( "Behaviour", "TranslationPostProcessing", TextPostProcessing.ReplaceMacronWithCircumflex | TextPostProcessing.ReplaceHtmlEntities );
            RegexPostProcessing = PluginEnvironment.Current.Preferences.GetOrDefault( "Behaviour", "RegexPostProcessing", TextPostProcessing.None );
            CacheRegexPatternResults = PluginEnvironment.Current.Preferences.GetOrDefault( "Behaviour", "CacheRegexPatternResults", true );
            PersistRichTextMode = PluginEnvironment.Current.Preferences.GetOrDefault( "Behaviour", "PersistRichTextMode", PersistRichTextMode.Final );
            CacheRegexLookups = PluginEnvironment.Current.Preferences.GetOrDefault( "Behaviour", "CacheRegexLookups", true );
            CacheWhitespaceDifferences = PluginEnvironment.Current.Preferences.GetOrDefault( "Behaviour", "CacheWhitespaceDifferences", false );
            GenerateStaticSubstitutionTranslations = PluginEnvironment.Current.Preferences.GetOrDefault( "Behaviour", "GenerateStaticSubstitutionTranslations", false );
            GeneratePartialTranslations = PluginEnvironment.Current.Preferences.GetOrDefault( "Behaviour", "GeneratePartialTranslations", false );
            EnableTranslationScoping = PluginEnvironment.Current.Preferences.GetOrDefault( "Behaviour", "EnableTranslationScoping", true );
            EnableSilentMode = PluginEnvironment.Current.Preferences.GetOrDefault( "Behaviour", "EnableSilentMode", true );
            BlacklistedIMGUIPlugins = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
            EnableTextPathLogging = false;
            OutputUntranslatableText = false;
            IgnoreVirtualTextSetterCallingRules = false;
            MaxTextParserRecursion = PluginEnvironment.Current.Preferences.GetOrDefault( "Behaviour", "MaxTextParserRecursion", 1 );
            HtmlEntityPreprocessing = PluginEnvironment.Current.Preferences.GetOrDefault( "Behaviour", "HtmlEntityPreprocessing", true );
            HandleRichText = PluginEnvironment.Current.Preferences.GetOrDefault( "Behaviour", "HandleRichText", true );
            EnableTranslationHelper = PluginEnvironment.Current.Preferences.GetOrDefault( "Behaviour", "EnableTranslationHelper", false );
            ForceMonoModHooks = false;
            RedirectedResourceDetectionStrategy = PluginEnvironment.Current.Preferences.GetOrDefault( "Behaviour", "RedirectedResourceDetectionStrategy", RedirectedResourceDetection.AppendMongolianVowelSeparatorAndRemoveAll );
            OutputTooLongText = false;
            TemplateAllNumberAway = PluginEnvironment.Current.Preferences.GetOrDefault( "Behaviour", "TemplateAllNumberAway", false );
            ReloadTranslationsOnFileChange = PluginEnvironment.Current.Preferences.GetOrDefault( "Behaviour", "ReloadTranslationsOnFileChange", true );
            DisableTextMeshProScrollInEffects = ApplicationName.Equals( "SamuraiVandalism", StringComparison.OrdinalIgnoreCase ) || UnityTypes.UguiNovelText != null;
            CacheParsedTranslations = PluginEnvironment.Current.Preferences.GetOrDefault( "Behaviour", "CacheParsedTranslations", true );

            TextureDirectory = PluginEnvironment.Current.Preferences.GetOrDefault( "Files", "TextureDirectory", Path.Combine( "Translation", Path.Combine( "{Lang}", "Texture" ) ) );
            TexturesPath = Path.Combine( PluginEnvironment.Current.TranslationPath, Settings.TextureDirectory ).Parameterize();

            EnableTextureTranslation = PluginEnvironment.Current.Preferences.GetOrDefault( "Texture", "EnableTextureTranslation", true );
            EnableTextureDumping = PluginEnvironment.Current.Preferences.GetOrDefault( "Texture", "EnableTextureDumping", false );
            EnableTextureToggling = PluginEnvironment.Current.Preferences.GetOrDefault( "Texture", "EnableTextureToggling", true );
            EnableTextureScanOnSceneLoad = PluginEnvironment.Current.Preferences.GetOrDefault( "Texture", "EnableTextureScanOnSceneLoad", true );
            EnableSpriteRendererHooking = PluginEnvironment.Current.Preferences.GetOrDefault( "Texture", "EnableSpriteRendererHooking", true );
            LoadUnmodifiedTextures = PluginEnvironment.Current.Preferences.GetOrDefault( "Texture", "LoadUnmodifiedTextures", false );
            DetectDuplicateTextureNames = PluginEnvironment.Current.Preferences.GetOrDefault( "Texture", "DetectDuplicateTextureNames", false );
            DuplicateTextureNames = new HashSet<string>(
               ( PluginEnvironment.Current.Preferences.GetOrDefault( "Texture", "DuplicateTextureNames", string.Empty ) ?? string.Empty )
                  .Split( new[] { ';' }, StringSplitOptions.RemoveEmptyEntries ) );
            EnableLegacyTextureLoading = PluginEnvironment.Current.Preferences.GetOrDefault( "Texture", "EnableLegacyTextureLoading", false );
            TextureHashGenerationStrategy = PluginEnvironment.Current.Preferences.GetOrDefault( "Texture", "TextureHashGenerationStrategy", TextureHashGenerationStrategy.FromImageName );
            CacheTexturesInMemory = PluginEnvironment.Current.Preferences.GetOrDefault( "Texture", "CacheTexturesInMemory", true );
            EnableSpriteHooking = PluginEnvironment.Current.Preferences.GetOrDefault( "Texture", "EnableSpriteHooking", true );

            if( EnableTextureTranslation || EnableTextureDumping )
            {
               try
               {
                  Directory.CreateDirectory( TexturesPath );
               }
               catch( Exception e )
               {
                  XuaLogger.AutoTranslator.Error( e, "Could not create texture directory: " + TexturesPath );
               }
            }

            PreferredStoragePath = Path.Combine( "Translation", Path.Combine( "{Lang}", "RedirectedResources" ) );
            EnableTextAssetRedirector = false;
            LogAllLoadedResources = false;
            EnableDumping = false;
            CacheMetadataForAllFiles = true;

            if( ClipboardDebounceTime < 0.1f )
            {
               XuaLogger.AutoTranslator.Warn( "'ClipboardDebounceTime' must not be lower than 0.1. Setting it to that..." );

               ClipboardDebounceTime = 0.1f;

            }

            if( CacheMetadataForAllFiles && EnableDumping )
            {
               XuaLogger.AutoTranslator.Warn( "'EnableDumping' and 'CacheMetadataForAllFiles' cannot be enabled at the same time. Disabling 'CacheMetadataForAllFiles'..." );

               CacheMetadataForAllFiles = false;
            }

            RedirectedResourcesPath = Path.Combine( PluginEnvironment.Current.TranslationPath, PreferredStoragePath ).UseCorrectDirectorySeparators().Parameterize();

            if( MaxCharactersPerTranslation > MaxMaxCharactersPerTranslation )
            {
               PluginEnvironment.Current.Preferences[ "Behaviour" ][ "MaxCharactersPerTranslation" ].Value = MaxMaxCharactersPerTranslation.ToString( CultureInfo.InvariantCulture );
               MaxCharactersPerTranslation = MaxMaxCharactersPerTranslation;
            }

            UserAgent = string.Empty;
            DisableCertificateValidation = true;

            Width = 400.0f;
            Height = 100.0f;
            EnabledTranslators = new HashSet<string>();

            EnableConsole = PluginEnvironment.Current.Preferences.GetOrDefault( "Debug", "EnableConsole", false );

            EnableMigrations = PluginEnvironment.Current.Preferences.GetOrDefault( "Migrations", "Enable", true );
            MigrationsTag = PluginEnvironment.Current.Preferences.GetOrDefault( "Migrations", "Tag", string.Empty );

            AutoTranslationsFilePath = Path.Combine( PluginEnvironment.Current.TranslationPath, OutputFile.UseCorrectDirectorySeparators() ).Parameterize();
            StoryTranslationFilePath = Path.Combine( PluginEnvironment.Current.TranslationPath, StoryTranslationFile.UseCorrectDirectorySeparators() ).Parameterize();
            PatternTranslationFilePath = Path.Combine( PluginEnvironment.Current.TranslationPath, PatternTranslationFile.UseCorrectDirectorySeparators() ).Parameterize();
            SubstitutionFilePath = Path.Combine( PluginEnvironment.Current.TranslationPath, SubstitutionFile.UseCorrectDirectorySeparators() ).Parameterize();
            PreprocessorsFilePath = Path.Combine( PluginEnvironment.Current.TranslationPath, PreprocessorsFile.UseCorrectDirectorySeparators() ).Parameterize();
            PostprocessorsFilePath = Path.Combine( PluginEnvironment.Current.TranslationPath, PostprocessorsFile.UseCorrectDirectorySeparators() ).Parameterize();

            TranslationsPath = Path.Combine( PluginEnvironment.Current.TranslationPath, Settings.TranslationDirectory.UseCorrectDirectorySeparators() ).Parameterize();

            FromLanguageUsesWhitespaceBetweenWords = LanguageHelper.RequiresWhitespaceUponLineMerging( FromLanguage );
            ToLanguageUsesWhitespaceBetweenWords = LanguageHelper.RequiresWhitespaceUponLineMerging( Language );

            if( EnableTranslationScoping && !UnityFeatures.SupportsSceneManager )
            {
               EnableTranslationScoping = false;

               XuaLogger.AutoTranslator.Warn( "Disabling translation scoping because the SceneManager API is not supported in this version of Unity." );
            }

            if( EnableMigrations )
            {
               Migrate();
            }

            MigrationsTag = PluginEnvironment.Current.Preferences[ "Migrations" ][ "Tag" ].Value = PluginData.Version;

            Save();
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Error( e, "An error occurred during configuration. Shutting plugin down." );

            IsShutdown = true;
         }
      }

      private static int[] ParsePrebakeSizes( string raw )
      {
         var result = new List<int>();
         if( !string.IsNullOrEmpty( raw ) )
         {
            foreach( var part in raw.Split( new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries ) )
            {
               if( int.TryParse( part.Trim(), out var size ) && size > 0 && size < 400 && !result.Contains( size ) )
               {
                  result.Add( size );
               }
            }
         }
         return result.ToArray();
      }

      private static string[][] ParseForceBypassedSdwTextPaths( string raw )
      {
         if( string.IsNullOrEmpty( raw ) )
         {
            return new string[ 0 ][];
         }

         return raw
            .Split( new[] { ';' }, StringSplitOptions.RemoveEmptyEntries )
            .Select( group => group
               .Split( new[] { ',' }, StringSplitOptions.RemoveEmptyEntries )
               .Select( fragment => fragment.Trim() )
               .Where( fragment => fragment.Length > 0 )
               .ToArray() )
            .Where( fragments => fragments.Length > 0 )
            .ToArray();
      }

      public static void AddDuplicateName( string name )
      {
         DuplicateTextureNames.Add( name );
         PluginEnvironment.Current.Preferences[ "Texture" ][ "DuplicateTextureNames" ].Value = string.Join( ";", DuplicateTextureNames.ToArray() );
         Save();
      }

      public static void SetEndpoint( string id )
      {
         id = id ?? string.Empty;

         ServiceEndpoint = id;
         PluginEnvironment.Current.Preferences[ "Service" ][ "Endpoint" ].Value = id;
         Save();
      }

      public static void SetFallback( string id )
      {
         id = id ?? string.Empty;

         FallbackServiceEndpoint = id;
         PluginEnvironment.Current.Preferences[ "Service" ][ "FallbackEndpoint" ].Value = id;
         Save();
      }

      public static void SetSlientMode( bool enabled )
      {
         EnableSilentMode = enabled;
         PluginEnvironment.Current.Preferences[ "Behaviour" ][ "EnableSilentMode" ].Value = enabled.ToString( CultureInfo.InvariantCulture );
         Save();
      }

      public static void SetTranslationAggregatorBounds( float width, float height )
      {
         Width = width;
         Height = height;
         PluginEnvironment.Current.Preferences[ "TranslationAggregator" ][ "Width" ].Value = Width.ToString( CultureInfo.InvariantCulture );
         PluginEnvironment.Current.Preferences[ "TranslationAggregator" ][ "Height" ].Value = Height.ToString( CultureInfo.InvariantCulture );
         Save();
      }

      public static void AddTranslator( string id )
      {
         EnabledTranslators.Add( id );
         PluginEnvironment.Current.Preferences[ "TranslationAggregator" ][ "EnabledTranslators" ].Value = string.Join( ";", EnabledTranslators.ToArray() );
         Save();
      }

      public static void RemoveTranslator( string id )
      {
         EnabledTranslators.Remove( id );
         PluginEnvironment.Current.Preferences[ "TranslationAggregator" ][ "EnabledTranslators" ].Value = string.Join( ";", EnabledTranslators.ToArray() );
         Save();
      }

      internal static void Save()
      {
         try
         {
            PluginEnvironment.Current.SaveConfig();
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Error( e, "An error occurred during while saving configuration." );
         }
      }

      private static void Migrate()
      {
      }
   }
}
