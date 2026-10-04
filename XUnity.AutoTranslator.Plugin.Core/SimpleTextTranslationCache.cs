using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using UnityEngine;
using XUnity.AutoTranslator.Plugin.Core.Configuration;
using XUnity.AutoTranslator.Plugin.Core.Utilities;
using XUnity.Common.Logging;

namespace XUnity.AutoTranslator.Plugin.Core
{
   public class SimpleTextTranslationCache
   {
      protected readonly Dictionary<string, string> _translations = new Dictionary<string, string>();
      private List<RegexTranslation> _defaultRegexes = new List<RegexTranslation>();
      private HashSet<string> _registeredRegexes = new HashSet<string>();

      private static object _writeToFileSync = new object();
      private Dictionary<string, string> _newTranslations = new Dictionary<string, string>();
      private Coroutine _currentScheduledTask;
      private bool _shouldOverrideEntireFile;

      public SimpleTextTranslationCache( string file, bool loadTranslationsInFile )
      {
         LoadedFile = file;
         IsDirectory = false;

         if( loadTranslationsInFile )
         {
            LoadTranslationFiles( true );
         }
      }

      public SimpleTextTranslationCache(
         string fileOrDirectory,
         bool loadTranslationsInFile,
         bool isDirectory,
         bool allowTranslationOverride )
      {
         LoadedFile = fileOrDirectory;
         IsDirectory = isDirectory;

         if( loadTranslationsInFile )
         {
            LoadTranslationFiles( allowTranslationOverride );
         }
      }

      public SimpleTextTranslationCache(
         string outputFile,
         IEnumerable<Stream> inputStreams,
         bool allowTranslationOverride,
         bool closeStreams )
      {
         LoadedFile = outputFile;
         IsDirectory = false;

         if( inputStreams != null )
         {
            LoadTranslationStreams( inputStreams, allowTranslationOverride, closeStreams );
         }
      }

      public string LoadedFile { get; }

      public bool IsDirectory { get; }

      public virtual bool IsEmpty => ( _translations.Count + _defaultRegexes.Count ) == 0;

      internal void LoadTranslationFiles( bool overrideLaterWithEarlier )
      {
         try
         {
            if( IsDirectory )
            {
               var files = Directory.GetFiles( LoadedFile, "*.txt" ).OrderBy( x => x );
               foreach( var file in files )
               {
                  LoadTranslationsInFile( file, overrideLaterWithEarlier );
               }
            }
            else
            {
               LoadTranslationsInFile( LoadedFile, overrideLaterWithEarlier );
            }
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Error( e, "An error occurred while loading translations." );
         }
      }

      internal void LoadTranslationStreams( IEnumerable<Stream> streams, bool overrideLaterWithEarlier, bool closeStreams )
      {
         foreach( var stream in streams )
         {
            try
            {
               LoadTranslationsInStream( stream, overrideLaterWithEarlier, closeStreams );
            }
            catch( Exception e )
            {
               XuaLogger.AutoTranslator.Error( e, "An error occurred while loading translations." );
            }
         }
      }

      private void LoadTranslationsInFile( string fullFileName, bool allowOverride )
      {
         var fileExists = File.Exists( fullFileName );
         if( fileExists )
         {
            LoadTranslationsInStream( File.OpenRead( fullFileName ), allowOverride, true );
         }
      }

      private void LoadTranslationsInStream( Stream stream, bool allowOverride, bool closeStream )
      {
         try
         {
            var reader = new StreamReader( stream, Encoding.UTF8 );
            {
               string[] translations = reader.ReadToEnd().Split( new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries );
               foreach( string translatioOrDirective in translations )
               {
                  try
                  {
                     var kvp = TextHelper.ReadTranslationLineAndDecode( translatioOrDirective );
                     if( kvp != null )
                     {
                        string key = kvp[ 0 ];
                        string value = kvp[ 1 ];

                        if( !string.IsNullOrEmpty( key ) && !string.IsNullOrEmpty( value ) && key != value )
                        {
                           if( key.StartsWith( "r:" ) )
                           {
                              try
                              {
                                 var regex = new RegexTranslation( key, value );

                                 AddTranslationRegex( regex );
                              }
                              catch( Exception e )
                              {
                                 XuaLogger.AutoTranslator.Warn( e, $"An error occurred while constructing regex translation: '{translatioOrDirective}'." );
                              }
                           }
                           else
                           {
                              AddTranslationInternal( key, value.MakeRedirected(), allowOverride );
                           }
                        }
                     }
                  }
                  catch( Exception e )
                  {
                     XuaLogger.AutoTranslator.Warn( e, $"An error occurred while reading translation: '{translatioOrDirective}'." );
                  }
               }
            }
         }
         finally
         {
            if( closeStream )
            {
               stream.Dispose();
            }
         }
      }

      private void AddTranslationRegex( RegexTranslation regex )
      {
         if( !_registeredRegexes.Contains( regex.Original ) )
         {
            _registeredRegexes.Add( regex.Original );
            _defaultRegexes.Add( regex );
         }
      }

      private void AddTranslationInternal( string key, string value, bool allowOverride )
      {
         if( key != null && value != null )
         {
            if( allowOverride || !HasTranslated( key ) )
            {
               AddTranslation( key, value );
            }
         }
      }

      protected virtual bool HasTranslated( string key )
      {
         return _translations.ContainsKey( key );
      }

      protected virtual void AddTranslation( string key, string value )
      {
         _translations[ key ] = value;
      }

      [Obsolete("Do not use. Function only exists in case someone calls it through reflection.")]
      private void AddTranslation( string key, string value, bool allowOverride )
      {
         if( key != null && value != null )
         {
            if( allowOverride || !HasTranslated( key ) )
            {
               AddTranslation( key, value );
            }
         }
      }

      public virtual void AddTranslationToCache( string key, string value )
      {
         var hadTranslated = HasTranslated( key );

         if( !hadTranslated )
         {
            AddTranslationInternal( key, value.MakeRedirected(), false );

            QueueNewTranslationForDisk( key, value, hadTranslated );
         }
      }

      private void QueueNewTranslationForDisk( string key, string value, bool hadTranslated )
      {
         lock( _newTranslations )
         {
            _newTranslations[ key ] = value;

            if( hadTranslated )
            {
               _shouldOverrideEntireFile = true;
            }

            if( _currentScheduledTask != null )
            {
               CoroutineHelper.Stop( _currentScheduledTask );
            }
            _currentScheduledTask = CoroutineHelper.Start( ScheduleFileWriting() );
         }
      }

      private IEnumerator ScheduleFileWriting()
      {
         yield return CoroutineHelper.CreateWaitForSeconds( 1 );

         _currentScheduledTask = null;
         ThreadPool.QueueUserWorkItem( SaveNewTranslationsToDisk );
      }

      internal void SaveNewTranslationsToDisk( object state )
      {
         bool overwriteFile = false;
         if( _newTranslations.Count > 0 )
         {
            Dictionary<string, string> newTranslations;
            lock( _newTranslations )
            {
               newTranslations = _newTranslations.ToDictionary( x => x.Key, x => x.Value );
               _newTranslations.Clear();

               if( _shouldOverrideEntireFile )
               {
                  overwriteFile = true;
                  _shouldOverrideEntireFile = false;

                  lock( _translations )
                  {
                     foreach( var kvp in _translations )
                     {
                        if( !newTranslations.ContainsKey( kvp.Key ) )
                        {
                           newTranslations[ kvp.Key ] = kvp.Value;
                        }
                     }

                     foreach( var regex in _defaultRegexes )
                     {
                        if( !newTranslations.ContainsKey( regex.Key ) )
                        {
                           newTranslations[ regex.Key ] = regex.Value;
                        }
                     }
                  }
               }
            }

            lock( _writeToFileSync )
            {
               Directory.CreateDirectory( new FileInfo( LoadedFile ).Directory.FullName );

               using( var stream = File.Open( LoadedFile, overwriteFile ? FileMode.Create : FileMode.Append, FileAccess.Write ) )
               using( var writer = new StreamWriter( stream, Encoding.UTF8 ) )
               {
                  foreach( var kvp in newTranslations )
                  {
                     writer.WriteLine( TextHelper.Encode( kvp.Key ) + '=' + TextHelper.Encode( kvp.Value ) );
                  }
                  writer.Flush();
               }
            }
         }
      }

      public virtual bool TryGetTranslation( string untranslatedText, bool allowRegex, out string value )
      {
         var key = new UntranslatedText( untranslatedText, false, true, Settings.FromLanguageUsesWhitespaceBetweenWords, false, Settings.TemplateAllNumberAway );

         bool result;
         string untemplated;
         string unmodifiedValue;
         string unmodifiedKey;

         string untemplated_TemplatedOriginal_Text = null;
         string untemplated_TemplatedOriginal_Text_InternallyTrimmed = null;
         string untemplated_TemplatedOriginal_Text_ExternallyTrimmed = null;
         string untemplated_TemplatedOriginal_Text_FullyTrimmed = null;

         lock( _translations )
         {
            if( key.IsTemplated && !key.IsFromSpammingComponent )
            {

               untemplated = untemplated_TemplatedOriginal_Text ?? ( untemplated_TemplatedOriginal_Text = key.Untemplate( key.TemplatedOriginal_Text ) );
               result = _translations.TryGetValue( untemplated, out value );
               if( result )
               {
                  return result;
               }

               if( !ReferenceEquals( key.TemplatedOriginal_Text, key.TemplatedOriginal_Text_ExternallyTrimmed ) )
               {

                  untemplated = untemplated_TemplatedOriginal_Text_ExternallyTrimmed ?? ( untemplated_TemplatedOriginal_Text_ExternallyTrimmed = key.Untemplate( key.TemplatedOriginal_Text_ExternallyTrimmed ) );
                  result = _translations.TryGetValue( untemplated, out value );
                  if( result )
                  {
                     unmodifiedValue = key.LeadingWhitespace + value + key.TrailingWhitespace;
                     unmodifiedKey = untemplated_TemplatedOriginal_Text ?? ( untemplated_TemplatedOriginal_Text = key.Untemplate( key.TemplatedOriginal_Text ) );

                     value = unmodifiedValue;
                     return result;
                  }
               }

               if( !ReferenceEquals( key.TemplatedOriginal_Text, key.TemplatedOriginal_Text_InternallyTrimmed ) )
               {

                  untemplated = untemplated_TemplatedOriginal_Text_InternallyTrimmed ?? ( untemplated_TemplatedOriginal_Text_InternallyTrimmed = key.Untemplate( key.TemplatedOriginal_Text_InternallyTrimmed ) );
                  result = _translations.TryGetValue( untemplated, out value );
                  if( result )
                  {
                     unmodifiedValue = value;
                     unmodifiedKey = untemplated_TemplatedOriginal_Text ?? ( untemplated_TemplatedOriginal_Text = key.Untemplate( key.TemplatedOriginal_Text ) );

                     value = unmodifiedValue;
                     return result;
                  }
               }

               if( !ReferenceEquals( key.TemplatedOriginal_Text_InternallyTrimmed, key.TemplatedOriginal_Text_FullyTrimmed ) )
               {

                  untemplated = untemplated_TemplatedOriginal_Text_FullyTrimmed ?? ( untemplated_TemplatedOriginal_Text_FullyTrimmed = key.Untemplate( key.TemplatedOriginal_Text_FullyTrimmed ) );
                  result = _translations.TryGetValue( untemplated, out value );
                  if( result )
                  {
                     unmodifiedValue = key.LeadingWhitespace + value + key.TrailingWhitespace;
                     unmodifiedKey = untemplated_TemplatedOriginal_Text ?? ( untemplated_TemplatedOriginal_Text = key.Untemplate( key.TemplatedOriginal_Text ) );

                     value = unmodifiedValue;
                     return result;
                  }
               }
            }

            result = _translations.TryGetValue( key.TemplatedOriginal_Text, out value );
            if( result )
            {
               return result;
            }

            if( !ReferenceEquals( key.TemplatedOriginal_Text, key.TemplatedOriginal_Text_ExternallyTrimmed ) )
            {

               result = _translations.TryGetValue( key.TemplatedOriginal_Text_ExternallyTrimmed, out value );
               if( result )
               {
                  unmodifiedValue = key.LeadingWhitespace + value + key.TrailingWhitespace;

                  value = unmodifiedValue;
                  return result;
               }
            }

            if( !ReferenceEquals( key.TemplatedOriginal_Text, key.TemplatedOriginal_Text_InternallyTrimmed ) )
            {

               result = _translations.TryGetValue( key.TemplatedOriginal_Text_InternallyTrimmed, out value );
               if( result )
               {
                  return result;
               }
            }

            if( !ReferenceEquals( key.TemplatedOriginal_Text_InternallyTrimmed, key.TemplatedOriginal_Text_FullyTrimmed ) )
            {

               result = _translations.TryGetValue( key.TemplatedOriginal_Text_FullyTrimmed, out value );
               if( result )
               {
                  unmodifiedValue = key.LeadingWhitespace + value + key.TrailingWhitespace;

                  value = unmodifiedValue;
                  return result;
               }
            }

            if( allowRegex )
            {
               for( int i = _defaultRegexes.Count - 1; i > -1; i-- )
               {
                  var regex = _defaultRegexes[ i ];
                  try
                  {
                     var match = regex.CompiledRegex.Match( key.TemplatedOriginal_Text );
                     if( !match.Success ) continue;

                     value = regex.CompiledRegex.Replace( key.TemplatedOriginal_Text, regex.Translation ).MakeRedirected();

                     return true;
                  }
                  catch( Exception e )
                  {
                     _defaultRegexes.RemoveAt( i );

                     XuaLogger.AutoTranslator.Error( e, $"Failed while attempting to replace or match text of regex '{regex.Original}'. Removing that regex from the cache." );
                  }
               }
            }
         }

         return result;
      }
   }
}
