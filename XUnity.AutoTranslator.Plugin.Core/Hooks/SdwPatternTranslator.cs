using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using XUnity.AutoTranslator.Plugin.Core.Configuration;
using XUnity.AutoTranslator.Plugin.Core.Utilities;
using XUnity.AutoTranslator.Plugin.Utilities;
using XUnity.Common.Logging;

namespace XUnity.AutoTranslator.Plugin.Core.Hooks
{
   internal static class SdwPatternTranslator
   {
      private const int MaxCacheEntries = 4096;
      private const int MinLiteralLetters = 3;
      private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds( 50 );

      private static readonly Regex PlaceholderRegex = new Regex( @"\{(\d+)(?:[,:][^{}]*)?\}", RegexOptions.CultureInvariant );

      private sealed class TargetPart
      {
         public string Literal;
         public int GroupIndex = -1;
      }

      private sealed class Pattern
      {
         public Regex Regex;
         public string Anchor;
         public int LiteralLength;
         public TargetPart[] Target;
         public string Source;
      }

      private static Pattern[] _patterns = new Pattern[ 0 ];
      private sealed class Hit
      {
         public string Result;
         public string Source;
      }

      private static readonly Dictionary<string, Hit> _cache = new Dictionary<string, Hit>( StringComparer.Ordinal );

      public static void Preload()
      {
         try
         {
            RefreshIfNeeded();
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Warn( e, "SdwPatternTranslator: preload failed." );
         }
      }

      public static bool HasPatterns
      {
         get
         {
            RefreshIfNeeded();
            return _patterns.Length > 0 || _fuzzy.Count > 0;
         }
      }

      public static bool TryTranslate( string text, out string translated, out string source )
      {
         return TryTranslateInternal( text, 0, out translated, out source );
      }

      private static bool TryTranslateInternal( string text, int depth, out string translated, out string source )
      {
         translated = null;
         source = null;

         if( string.IsNullOrEmpty( text ) )
         {
            return false;
         }

         RefreshIfNeeded();

         var patterns = _patterns;
         if( patterns.Length == 0 )
         {
            return false;
         }

         if( depth == 0 && _cache.TryGetValue( text, out var cached ) )
         {
            translated = cached.Result;
            source = cached.Source;
            return cached.Result != null;
         }

         var hit = new Hit();
         foreach( var candidate in Variants( text ) )
         {
            var order = CollectCandidatePatterns( patterns, candidate );
            for( int k = 0; k < order.Count && hit.Result == null; k++ )
            {
               var p = patterns[ order[ k ] ];

               if( p.Anchor.Length > 0 && candidate.IndexOf( p.Anchor, StringComparison.Ordinal ) < 0 )
               {
                  continue;
               }

               Match m;
               try
               {
                  m = p.Regex.Match( candidate );
               }
               catch( RegexMatchTimeoutException )
               {
                  XuaLogger.AutoTranslator.Warn( "SdwPatternTranslator: pattern timed out, skipped: " + p.Source );
                  continue;
               }

               if( !m.Success )
               {
                  continue;
               }

               hit.Result = Render( p, m, depth );
               hit.Source = p.Source;
            }

            if( hit.Result != null ) break;
         }

         if( depth == 0 && _cache.Count >= MaxCacheEntries )
         {
            _cache.Clear();
         }

         if( depth == 0 ) _cache[ text ] = hit;
         translated = hit.Result;
         source = hit.Source;
         return hit.Result != null;
      }

      private static Dictionary<long, List<int>> _trigramIndex = new Dictionary<long, List<int>>();
      private static List<int> _unindexedPatterns = new List<int>();
      private static Pattern[] _indexOwner;
      private static int[] _indexStamp = new int[ 0 ];
      private static int _indexStampCounter;

      private static long Trigram( string s, int pos )
      {
         return ( (long)s[ pos ] << 32 ) | ( (long)s[ pos + 1 ] << 16 ) | (long)s[ pos + 2 ];
      }

      private static void BuildPatternIndex( Pattern[] patterns )
      {
         var index = new Dictionary<long, List<int>>();
         var unindexed = new List<int>();

         for( int i = 0; i < patterns.Length; i++ )
         {
            var anchor = patterns[ i ].Anchor;
            if( anchor == null || anchor.Length < 3 )
            {
               unindexed.Add( i );
               continue;
            }

            var key = Trigram( anchor, 0 );
            List<int> list;
            if( !index.TryGetValue( key, out list ) )
            {
               list = new List<int>();
               index[ key ] = list;
            }
            list.Add( i );
         }

         _trigramIndex = index;
         _unindexedPatterns = unindexed;
         _indexStamp = new int[ patterns.Length ];
         _indexStampCounter = 0;
         _indexOwner = patterns;
      }

      private static List<int> CollectCandidatePatterns( Pattern[] patterns, string candidate )
      {
         var result = new List<int>( 32 );

         if( !ReferenceEquals( _indexOwner, patterns ) || _indexStamp.Length != patterns.Length )
         {
            for( int i = 0; i < patterns.Length; i++ ) result.Add( i );
            return result;
         }

         var stamp = _indexStamp;
         int mark = ++_indexStampCounter;
         if( mark == int.MaxValue )
         {
            Array.Clear( stamp, 0, stamp.Length );
            _indexStampCounter = mark = 1;
         }

         for( int i = 0; i < _unindexedPatterns.Count; i++ )
         {
            int idx = _unindexedPatterns[ i ];
            stamp[ idx ] = mark;
            result.Add( idx );
         }

         for( int pos = 0; pos + 2 < candidate.Length; pos++ )
         {
            List<int> list;
            if( !_trigramIndex.TryGetValue( Trigram( candidate, pos ), out list ) )
            {
               continue;
            }

            for( int j = 0; j < list.Count; j++ )
            {
               int idx = list[ j ];
               if( stamp[ idx ] != mark )
               {
                  stamp[ idx ] = mark;
                  result.Add( idx );
               }
            }
         }

         result.Sort();
         return result;
      }

      private static readonly Regex CodeTagRegex = new Regex( @"<code=[^>]*>", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase );
      private static readonly Regex AnyTagRegex = new Regex( @"<[^<>]{1,200}>", RegexOptions.CultureInvariant );
      private static readonly Regex HyphenBreakRegex = new Regex( @"(?<=\w-)[ \t]*\r?\n[ \t]*", RegexOptions.CultureInvariant );

      internal static string StripCodeTags( string s )
      {
         return string.IsNullOrEmpty( s ) || s.IndexOf( "<code", StringComparison.OrdinalIgnoreCase ) < 0 ? s : CodeTagRegex.Replace( s, string.Empty );
      }

      private static string JoinHyphenBreaks( string s )
      {
         return s.IndexOf( '\n' ) < 0 ? s : HyphenBreakRegex.Replace( s, string.Empty );
      }

      private static List<string> Variants( string text )
      {
         var list = new List<string> { text };
         var noCode = StripCodeTags( text );
         var candidates = new[] { noCode, JoinHyphenBreaks( text ), JoinHyphenBreaks( noCode ) };
         foreach( var v in candidates )
         {
            if( !string.IsNullOrEmpty( v ) && !list.Contains( v ) ) list.Add( v );
         }
         return list;
      }

      internal static string Fuzzy( string s )
      {
         if( string.IsNullOrEmpty( s ) ) return string.Empty;
         if( s.IndexOf( '<' ) >= 0 ) s = AnyTagRegex.Replace( s, string.Empty );
         var sb = new StringBuilder( s.Length );
         foreach( var ch in s )
         {
            if( char.IsLetterOrDigit( ch ) ) sb.Append( char.ToLowerInvariant( ch ) );
         }
         return sb.ToString();
      }

      private const int MinFuzzyLength = 12;
      private const int MinSegmentFuzzyLength = 3;

      internal static bool TryFuzzyTranslation( string text, out string translation, int minLength = MinFuzzyLength )
      {
         translation = null;
         if( string.IsNullOrWhiteSpace( text ) ) return false;
         RefreshIfNeeded();

         var key = Fuzzy( text );
         if( key.Length < minLength ) return false;

         var index = _fuzzy;
         if( index.TryGetValue( key, out var t ) )
         {
            translation = StripCodeTags( t );
            return true;
         }
         return false;
      }

      private static Dictionary<string, string> _fuzzy = new Dictionary<string, string>( StringComparer.Ordinal );

      private static readonly Dictionary<string, string> _composeCache = new Dictionary<string, string>( StringComparer.Ordinal );
      private const int MaxComposeTokens = 40;
      private const int MaxSpanTokens = 24;

      private static bool HasLetter( string s )
      {
         foreach( var ch in s ) if( char.IsLetter( ch ) ) return true;
         return false;
      }

      private static bool TryTranslatePiece( string piece, out string translation )
      {
         if( TryExactDirect( piece, out translation ) ) return true;
         return TryFuzzyTranslation( piece, out translation, MinSegmentFuzzyLength );
      }

      private static readonly Regex TokenRegex = new Regex( @"\S+", RegexOptions.CultureInvariant );

      private sealed class ComposeToken
      {
         public string Text;
         public string LeadingWs;
      }

      private sealed class ComposePart
      {
         public string Text;
         public string Source;
         public string Sep;
         public bool Translated;
      }

      internal static bool TryCompose( string text, out string translated )
      {
         translated = null;
         if( string.IsNullOrWhiteSpace( text ) || text.Length > 400 ) return false;
         RefreshIfNeeded();

         text = StripCodeTags( text );
         if( _composeCache.TryGetValue( text, out var cached ) )
         {
            translated = cached;
            return cached != null;
         }

         var tokens = new List<ComposeToken>();
         int prevEnd = 0;
         foreach( Match m in TokenRegex.Matches( text ) )
         {
            tokens.Add( new ComposeToken { Text = m.Value, LeadingWs = text.Substring( prevEnd, m.Index - prevEnd ) } );
            prevEnd = m.Index + m.Length;
         }

         string result = null;
         if( tokens.Count >= 2 && tokens.Count <= MaxComposeTokens )
         {
            result = ComposeTokens( tokens );
         }

         if( _composeCache.Count >= MaxCacheEntries ) _composeCache.Clear();
         _composeCache[ text ] = result;
         translated = result;
         return result != null;
      }

      private static readonly Regex BracketTagRegex = new Regex( @"^\[[A-Za-z0-9]{1,4}\]$", RegexOptions.CultureInvariant );
      private static readonly Regex WholeMarkupTagRegex = new Regex( @"^<[^<>]{1,200}>$", RegexOptions.CultureInvariant );

      private static bool IsPassthroughToken( string token )
      {
         return !HasLetter( token ) || BracketTagRegex.IsMatch( token ) || WholeMarkupTagRegex.IsMatch( token );
      }

      private static string SeparatorFor( string ws )
      {
         int breaks = 0;
         foreach( var ch in ws ) if( ch == '\n' ) breaks++;
         if( breaks == 0 ) return " ";
         return breaks == 1 ? "\n" : "\n\n";
      }

      private static string ComposeTokens( List<ComposeToken> tokens )
      {
         int n = tokens.Count;
         const int Inf = int.MaxValue / 2;
         var cost = new int[ n + 1 ];
         var prev = new int[ n + 1 ];
         var piece = new string[ n + 1 ];
         for( int i = 1; i <= n; i++ ) cost[ i ] = Inf;

         for( int i = 0; i < n; i++ )
         {
            if( cost[ i ] >= Inf ) continue;

            if( IsPassthroughToken( tokens[ i ].Text ) && cost[ i ] + 1 < cost[ i + 1 ] )
            {
               cost[ i + 1 ] = cost[ i ] + 1;
               prev[ i + 1 ] = i;
               piece[ i + 1 ] = null;
            }

            var sb = new StringBuilder();
            bool spanHasLetter = false;
            for( int j = i; j < n && j - i < MaxSpanTokens; j++ )
            {
               if( j > i ) sb.Append( ' ' );
               sb.Append( tokens[ j ].Text );
               if( !spanHasLetter ) spanHasLetter = HasLetter( tokens[ j ].Text );
               if( !spanHasLetter ) continue;
               if( TryTranslatePiece( sb.ToString(), out var tr ) && cost[ i ] + 2 < cost[ j + 1 ] )
               {
                  cost[ j + 1 ] = cost[ i ] + 2;
                  prev[ j + 1 ] = i;
                  piece[ j + 1 ] = tr;
               }
            }
         }

         if( cost[ n ] >= Inf ) return null;

         var parts = new List<ComposePart>();
         bool anyTranslated = false;
         for( int i = n; i > 0; i = prev[ i ] )
         {
            int start = prev[ i ];
            var words = new StringBuilder();
            for( int k = start; k < i; k++ )
            {
               if( k > start ) words.Append( ' ' );
               words.Append( tokens[ k ].Text );
            }

            var part = new ComposePart { Source = words.ToString(), Sep = start == 0 ? string.Empty : SeparatorFor( tokens[ start ].LeadingWs ) };
            if( piece[ i ] != null )
            {
               anyTranslated = true;
               part.Translated = true;
               part.Text = StripCodeTags( piece[ i ] );
            }
            else
            {
               part.Text = part.Source;
            }
            parts.Add( part );
         }
         if( !anyTranslated ) return null;
         parts.Reverse();

         for( int k = 0; k < parts.Count - 1; k++ )
         {
            var cur = parts[ k ];
            if( cur.Translated && parts[ k + 1 ].Sep == " "
               && cur.Text.EndsWith( ".", StringComparison.Ordinal ) && !cur.Text.EndsWith( "..", StringComparison.Ordinal )
               && !cur.Source.EndsWith( ".", StringComparison.Ordinal ) )
            {
               cur.Text = cur.Text.Substring( 0, cur.Text.Length - 1 );
            }
         }

         var result = new StringBuilder();
         foreach( var part in parts )
         {
            result.Append( part.Sep ).Append( part.Text );
         }
         return result.ToString();
      }

      internal static bool TryExactDirect( string text, out string translation )
      {
         translation = null;
         try
         {
            var cache = AutoTranslationPlugin.Current?.TextCache;
            if( cache == null || string.IsNullOrWhiteSpace( text ) )
            {
               return false;
            }

            var key = new UntranslatedText( text, false, true, Settings.FromLanguageUsesWhitespaceBetweenWords, false, Settings.TemplateAllNumberAway );
            if( cache.TryGetTranslation( key, false, false, -1 , out var t )
               && !string.IsNullOrEmpty( t )
               && !string.Equals( t, text, StringComparison.Ordinal ) )
            {
               translation = t;
               return true;
            }
         }
         catch
         {
         }
         return false;
      }

      internal static bool TryExactTranslation( string text, out string translation )
      {
         foreach( var v in Variants( text ) )
         {
            if( TryExactDirect( v, out translation ) )
            {
               return true;
            }
         }
         translation = null;
         return false;
      }

      internal static bool TryResolve( string text, out string translated, out string how )
      {
         how = null;
         if( TryExactTranslation( text, out translated ) )
         {
            how = "exact-variant";
            return true;
         }
         if( TryFuzzyTranslation( text, out translated ) )
         {
            how = "fuzzy";
            return true;
         }
         if( TryTranslate( text, out translated, out var src ) )
         {
            how = "pattern '" + src + "'";
            return true;
         }
         if( TryCompose( text, out translated ) )
         {
            how = "composed-from-parts";
            return true;
         }
         return false;
      }

      private static string TranslateCapture( string value, int depth )
      {
         if( string.IsNullOrEmpty( value ) )
         {
            return value;
         }

         bool hasLetter = false;
         foreach( var ch in value )
         {
            if( char.IsLetter( ch ) ) { hasLetter = true; break; }
         }
         if( !hasLetter )
         {
            return value;
         }

         var trimmed = value.Trim();
         if( TryExactTranslation( trimmed, out var exact ) || TryFuzzyTranslation( trimmed, out exact ) )
         {
            return value.Replace( trimmed, exact );
         }

         if( depth < 1 && TryTranslateInternal( trimmed, depth + 1, out var viaPattern, out _ ) )
         {
            return value.Replace( trimmed, viaPattern );
         }

         return value;
      }

      private static string Render( Pattern p, Match m, int depth )
      {
         var sb = new StringBuilder();
         foreach( var part in p.Target )
         {
            if( part.GroupIndex < 0 )
            {
               sb.Append( part.Literal );
               continue;
            }

            var g = m.Groups[ "g" + part.GroupIndex ];
            if( g.Success )
            {
               sb.Append( TranslateCapture( g.Value, depth ) );
            }
            else
            {
               sb.Append( '{' ).Append( part.GroupIndex ).Append( '}' );
            }
         }
         return sb.ToString();
      }

      private sealed class Source
      {
         public string Path;
         public bool Appendable;
         public int MinIntervalMs;
         public int LastCheckTick;
         public bool Checked;
         public long Offset;
         public DateTime WriteTimeUtc = DateTime.MinValue;
         public Dictionary<string, Pattern> Patterns = new Dictionary<string, Pattern>( StringComparer.Ordinal );
         public Dictionary<string, string> Fuzzy = new Dictionary<string, string>( StringComparer.Ordinal );
      }

      private static readonly Dictionary<string, int> _rejectedReported = new Dictionary<string, int>( StringComparer.OrdinalIgnoreCase );

      private static Source[] _sources = new Source[ 0 ];

      private static bool _fullRebuildNeeded = true;
      private static Dictionary<string, Pattern> _mergedPatterns = new Dictionary<string, Pattern>( StringComparer.Ordinal );
      private static readonly List<PendingPattern> _pendingPatterns = new List<PendingPattern>();
      private static readonly List<PendingFuzzy> _pendingFuzzy = new List<PendingFuzzy>();

      private struct PendingPattern
      {
         public Source Owner;
         public string Key;
         public Pattern Pattern;
      }

      private struct PendingFuzzy
      {
         public Source Owner;
         public string Key;
         public string Value;
      }

      private static string _sourcesAuto;
      private static string _sourcesPattern;
      private static int _lastGlobalCheckTick;
      private static bool _everChecked;

      private static readonly List<FileSystemWatcher> _watchers = new List<FileSystemWatcher>();
      private static volatile bool _watcherDirty;
      private static int _watcherDirtyTick;
      private static bool _watchingActive;
      private const int WatcherSettleMs = 100;

      private static void OnWatchedFileChanged( object sender, FileSystemEventArgs e )
      {
         _watcherDirtyTick = Environment.TickCount;
         _watcherDirty = true;
      }

      private static void OnWatchedFileRenamed( object sender, RenamedEventArgs e )
      {
         _watcherDirtyTick = Environment.TickCount;
         _watcherDirty = true;
      }

      private static void RestartWatchers( string autoPath, string patPath )
      {
         lock( _watchers )
         {
            foreach( var w in _watchers )
            {
               try { w.EnableRaisingEvents = false; w.Dispose(); } catch { }
            }
            _watchers.Clear();
            _watchingActive = false;

            try
            {
               bool any = false;
               foreach( var path in new[] { autoPath, patPath } )
               {
                  if( string.IsNullOrEmpty( path ) ) continue;
                  var full = System.IO.Path.GetFullPath( path );
                  var dir = System.IO.Path.GetDirectoryName( full );
                  if( string.IsNullOrEmpty( dir ) || !Directory.Exists( dir ) ) continue;

                  var w = new FileSystemWatcher( dir, System.IO.Path.GetFileName( full ) )
                  {
                     NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime,
                  };
                  w.Changed += OnWatchedFileChanged;
                  w.Created += OnWatchedFileChanged;
                  w.Deleted += OnWatchedFileChanged;
                  w.Renamed += OnWatchedFileRenamed;
                  w.EnableRaisingEvents = true;
                  _watchers.Add( w );
                  any = true;
               }
               _watchingActive = any;
            }
            catch( Exception e )
            {
               _watchingActive = false;
               XuaLogger.AutoTranslator.Warn( e, "SdwPatternTranslator: could not start file watching; falling back to fast polling." );
            }
         }
      }

      private static void RefreshIfNeeded()
      {
         var autoPath = Settings.AutoTranslationsFilePath;
         var patPath = Settings.PatternTranslationFilePath;

         var now = Environment.TickCount;
         if( _everChecked
            && string.Equals( autoPath, _sourcesAuto, StringComparison.OrdinalIgnoreCase )
            && string.Equals( patPath, _sourcesPattern, StringComparison.OrdinalIgnoreCase )
            && unchecked( now - _lastGlobalCheckTick ) < 250
            && !( _watcherDirty && unchecked( now - _watcherDirtyTick ) >= WatcherSettleMs ) )
         {
            return;
         }
         _lastGlobalCheckTick = now;
         _everChecked = true;

         bool forceCheck = false;
         if( _watcherDirty && unchecked( now - _watcherDirtyTick ) >= WatcherSettleMs )
         {
            _watcherDirty = false;
            forceCheck = true;
         }

         if( !string.Equals( autoPath, _sourcesAuto, StringComparison.OrdinalIgnoreCase )
            || !string.Equals( patPath, _sourcesPattern, StringComparison.OrdinalIgnoreCase ) )
         {
            _sourcesAuto = autoPath;
            _sourcesPattern = patPath;

            RestartWatchers( autoPath, patPath );
            var list = new List<Source>();
            if( !string.IsNullOrEmpty( autoPath ) )
            {
               list.Add( new Source { Path = autoPath, Appendable = true, MinIntervalMs = _watchingActive ? 10000 : 2000 } );
            }
            if( !string.IsNullOrEmpty( patPath ) && !string.Equals( patPath, autoPath, StringComparison.OrdinalIgnoreCase ) )
            {
               list.Add( new Source { Path = patPath, Appendable = false, MinIntervalMs = _watchingActive ? 5000 : 500 } );
            }
            _sources = list.ToArray();
            _fullRebuildNeeded = true;
         }

         bool changed = false;
         foreach( var src in _sources )
         {
            if( !forceCheck && src.Checked && unchecked( now - src.LastCheckTick ) < src.MinIntervalMs )
            {
               continue;
            }
            src.LastCheckTick = now;
            src.Checked = true;

            try
            {
               changed |= CheckSource( src );
            }
            catch( Exception e )
            {
               XuaLogger.AutoTranslator.Warn( e, "SdwPatternTranslator: failed to read '" + src.Path + "'." );
            }
         }

         if( changed )
         {
            Rebuild();
         }
      }

      private static bool CheckSource( Source src )
      {
         var fi = new FileInfo( src.Path );
         if( !fi.Exists )
         {
            if( src.Patterns.Count > 0 || src.Fuzzy.Count > 0 || src.Offset != 0 )
            {
               _fullRebuildNeeded = true;
               src.Patterns.Clear(); src.Fuzzy.Clear();
               src.Offset = 0;
               src.WriteTimeUtc = DateTime.MinValue;
               return true;
            }
            return false;
         }

         var length = fi.Length;
         var writeTime = fi.LastWriteTimeUtc;

         if( src.Appendable )
         {
            if( length == src.Offset && writeTime == src.WriteTimeUtc )
            {
               return false;
            }

            bool full = length < src.Offset;
            if( full )
            {
               _fullRebuildNeeded = true;
               src.Patterns.Clear(); src.Fuzzy.Clear();
               src.Offset = 0;
            }

            int before = src.Patterns.Count;
            var consumed = ReadRange( src, src.Offset, completeLinesOnly: true, out var parsed );
            src.Offset += consumed;
            src.WriteTimeUtc = writeTime;

            if( parsed > 0 || full )
            {
               XuaLogger.AutoTranslator.Info( $"SdwPatternTranslator: '{Path.GetFileName( src.Path )}' +{src.Patterns.Count - before} pattern(s) (now {src.Patterns.Count})." );
            }
            return full || parsed > 0;
         }
         else
         {
            if( writeTime == src.WriteTimeUtc && length == src.Offset )
            {
               return false;
            }

            _fullRebuildNeeded = true;
            src.Patterns.Clear(); src.Fuzzy.Clear();
            ReadRange( src, 0, completeLinesOnly: false, out _ );
            src.Offset = length;
            src.WriteTimeUtc = writeTime;
            XuaLogger.AutoTranslator.Info( $"SdwPatternTranslator: loaded {src.Patterns.Count} pattern(s) from '{src.Path}'." );
            return true;
         }
      }

      private static long ReadRange( Source src, long offset, bool completeLinesOnly, out int parsedCount )
      {
         parsedCount = 0;
         int fuzzyBefore = src.Fuzzy.Count;

         byte[] buffer;
         using( var fs = new FileStream( src.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete ) )
         {
            if( offset > fs.Length )
            {
               return 0;
            }
            fs.Seek( offset, SeekOrigin.Begin );
            buffer = new byte[ fs.Length - offset ];
            int read = 0;
            while( read < buffer.Length )
            {
               int n = fs.Read( buffer, read, buffer.Length - read );
               if( n <= 0 ) break;
               read += n;
            }
            if( read < buffer.Length )
            {
               Array.Resize( ref buffer, read );
            }
         }

         int usable = buffer.Length;
         if( completeLinesOnly )
         {
            usable = Array.LastIndexOf( buffer, (byte)'\n' ) + 1;
         }
         if( usable <= 0 )
         {
            return 0;
         }

         int skipBom = ( offset == 0 && usable >= 3 && buffer[ 0 ] == 0xEF && buffer[ 1 ] == 0xBB && buffer[ 2 ] == 0xBF ) ? 3 : 0;
         var text = Encoding.UTF8.GetString( buffer, skipBom, usable - skipBom );

         int rejectedLines = 0;
         var rejectedExamples = new List<string>();

         foreach( var rawLine in text.Split( new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries ) )
         {
            if( rawLine.Length < 3 || rawLine.StartsWith( "//", StringComparison.Ordinal ) )
            {
               continue;
            }

            string[] kvp;
            try
            {
               kvp = TextHelper.ReadTranslationLineAndDecode( rawLine );
            }
            catch
            {
               continue;
            }

            if( kvp == null || string.IsNullOrEmpty( kvp[ 0 ] ) || string.IsNullOrEmpty( kvp[ 1 ] ) )
            {
               continue;
            }

            var key = StripCodeTags( kvp[ 0 ] );
            var value = StripCodeTags( kvp[ 1 ] );

            if( key.StartsWith( "r:", StringComparison.Ordinal )
               || string.Equals( key, value, StringComparison.Ordinal ) )
            {
               continue;
            }

            if( !PlaceholderRegex.IsMatch( key ) )
            {
               var fk = Fuzzy( key );
               if( fk.Length >= MinSegmentFuzzyLength && !src.Fuzzy.ContainsKey( fk ) )
               {
                  src.Fuzzy[ fk ] = value;
                  _pendingFuzzy.Add( new PendingFuzzy { Owner = src, Key = fk, Value = value } );
               }
               continue;
            }

            var reject = ValidateLine( key, value );
            if( reject != null )
            {
               rejectedLines++;
               if( rejectedExamples.Count < 3 )
               {
                  rejectedExamples.Add( key + "  [" + reject + "]" );
               }
               if( Settings.EnableSdwDiagnostics )
               {
                  XuaLogger.AutoTranslator.Warn( "SdwPatternTranslator: ignored line (" + reject + "): " + key + "=" + value );
               }
               continue;
            }

            try
            {
               var compiled = Compile( key, value );
               src.Patterns[ key ] = compiled;
               _pendingPatterns.Add( new PendingPattern { Owner = src, Key = key, Pattern = compiled } );
               parsedCount++;
            }
            catch( Exception e )
            {
               XuaLogger.AutoTranslator.Warn( e, "SdwPatternTranslator: could not compile pattern: " + key );
            }
         }

         int lastReported;
         bool reportRejected = rejectedLines > 0
            && ( !_rejectedReported.TryGetValue( src.Path, out lastReported ) || lastReported != rejectedLines );
         _rejectedReported[ src.Path ] = rejectedLines;
         if( reportRejected )
         {
            XuaLogger.AutoTranslator.Info( "SdwPatternTranslator: ignored " + rejectedLines + " line(s) in '" + System.IO.Path.GetFileName( src.Path )
               + "' (too little fixed text / invalid placeholders). Examples: " + string.Join( "; ", rejectedExamples.ToArray() )
               + ". Set [Behaviour] EnableSdwDiagnostics=true to list every line." );
         }

         parsedCount += src.Fuzzy.Count - fuzzyBefore;
         return usable;
      }

      private static string ValidateLine( string key, string value )
      {
         int literal = 0;
         var keyWithoutVars = PlaceholderRegex.Replace( key, string.Empty );
         foreach( var ch in keyWithoutVars )
         {
            if( char.IsLetterOrDigit( ch ) ) literal++;
         }
         if( literal < MinLiteralLetters )
         {
            return "too little fixed text in the original, would match almost anything";
         }

         var keyIdx = new HashSet<int>();
         foreach( Match m in PlaceholderRegex.Matches( key ) ) keyIdx.Add( int.Parse( m.Groups[ 1 ].Value ) );

         var valueMatches = PlaceholderRegex.Matches( value );
         if( valueMatches.Count == 0 )
         {
            return "translation contains no {N} variable";
         }
         foreach( Match m in valueMatches )
         {
            if( !keyIdx.Contains( int.Parse( m.Groups[ 1 ].Value ) ) )
            {
               return "translation uses {" + m.Groups[ 1 ].Value + "} which the original does not define";
            }
         }

         if( value.IndexOf( "(.+)", StringComparison.Ordinal ) >= 0
            || value.IndexOf( "(.*)", StringComparison.Ordinal ) >= 0
            || value.IndexOf( "(\\d+)", StringComparison.Ordinal ) >= 0
            || value.IndexOf( "ZX", StringComparison.Ordinal ) >= 0 && key.IndexOf( "ZX", StringComparison.Ordinal ) < 0
            || Regex.IsMatch( value, @"\$\d" ) && !Regex.IsMatch( key, @"\$\d" ) )
         {
            return "translation contains regex/template syntax";
         }

         return null;
      }

      private static bool IsOverriddenByLaterSource( Source owner, string key, bool fuzzy )
      {
         bool afterOwner = false;
         foreach( var src in _sources )
         {
            if( ReferenceEquals( src, owner ) )
            {
               afterOwner = true;
               continue;
            }
            if( afterOwner && ( fuzzy ? src.Fuzzy.ContainsKey( key ) : src.Patterns.ContainsKey( key ) ) )
            {
               return true;
            }
         }
         return false;
      }

      private static void RemoveNegativeHits( Dictionary<string, Hit> cache )
      {
         List<string> toRemove = null;
         foreach( var kv in cache )
         {
            if( kv.Value == null || kv.Value.Result == null )
            {
               if( toRemove == null ) toRemove = new List<string>();
               toRemove.Add( kv.Key );
            }
         }
         if( toRemove != null )
         {
            foreach( var k in toRemove ) cache.Remove( k );
         }
      }

      private static void RemoveNegativeCompositions( Dictionary<string, string> cache )
      {
         List<string> toRemove = null;
         foreach( var kv in cache )
         {
            if( kv.Value == null )
            {
               if( toRemove == null ) toRemove = new List<string>();
               toRemove.Add( kv.Key );
            }
         }
         if( toRemove != null )
         {
            foreach( var k in toRemove ) cache.Remove( k );
         }
      }

      private static void Rebuild()
      {
         if( !_fullRebuildNeeded )
         {
            if( _pendingPatterns.Count == 0 && _pendingFuzzy.Count == 0 )
            {
               return;
            }

            foreach( var p in _pendingPatterns )
            {
               if( !IsOverriddenByLaterSource( p.Owner, p.Key, false ) )
               {
                  _mergedPatterns[ p.Key ] = p.Pattern;
               }
            }
            foreach( var f in _pendingFuzzy )
            {
               if( !IsOverriddenByLaterSource( f.Owner, f.Key, true ) )
               {
                  _fuzzy[ f.Key ] = f.Value;
               }
            }
            bool patternsAdded = _pendingPatterns.Count > 0;
            _pendingPatterns.Clear();
            _pendingFuzzy.Clear();

            if( patternsAdded )
            {
               var incrementalList = new List<Pattern>( _mergedPatterns.Values );
               incrementalList.Sort( ( a, b ) => b.LiteralLength.CompareTo( a.LiteralLength ) );
               _patterns = incrementalList.ToArray();
               BuildPatternIndex( _patterns );
            }

            RemoveNegativeHits( _cache );
            RemoveNegativeCompositions( _composeCache );
            return;
         }

         _fullRebuildNeeded = false;
         _pendingPatterns.Clear();
         _pendingFuzzy.Clear();

         var merged = new Dictionary<string, Pattern>( StringComparer.Ordinal );
         foreach( var src in _sources )
         {
            foreach( var kv in src.Patterns )
            {
               merged[ kv.Key ] = kv.Value;
            }
         }
         _mergedPatterns = merged;

         var fuzzy = new Dictionary<string, string>( StringComparer.Ordinal );
         foreach( var src in _sources )
         {
            foreach( var kv in src.Fuzzy )
            {
               fuzzy[ kv.Key ] = kv.Value;
            }
         }
         _fuzzy = fuzzy;
         _composeCache.Clear();

         var list = new List<Pattern>( merged.Values );
         list.Sort( ( a, b ) => b.LiteralLength.CompareTo( a.LiteralLength ) );
         _patterns = list.ToArray();
         BuildPatternIndex( _patterns );
         _cache.Clear();
      }

      private static Pattern Compile( string key, string value )
      {
         var rx = new StringBuilder( key.Length * 2 );
         rx.Append( @"^\s*" );

         var seen = new HashSet<int>();
         string anchor = string.Empty;
         int literalLength = 0;

         int pos = 0;
         foreach( Match m in PlaceholderRegex.Matches( key ) )
         {
            AppendLiteral( rx, key.Substring( pos, m.Index - pos ), ref anchor, ref literalLength );
            pos = m.Index + m.Length;

            int idx = int.Parse( m.Groups[ 1 ].Value );
            if( seen.Add( idx ) )
            {
               rx.Append( "(?<g" ).Append( idx ).Append( ">.+?)" );
            }
            else
            {
               rx.Append( @"\k<g" ).Append( idx ).Append( '>' );
            }
         }
         AppendLiteral( rx, key.Substring( pos ), ref anchor, ref literalLength );

         rx.Append( @"\s*$" );

         var regex = new Regex(
            rx.ToString(),
            RegexOptions.Singleline | RegexOptions.CultureInvariant,
            RegexTimeout );

         var parts = new List<TargetPart>();
         pos = 0;
         foreach( Match m in PlaceholderRegex.Matches( value ) )
         {
            if( m.Index > pos )
            {
               parts.Add( new TargetPart { Literal = value.Substring( pos, m.Index - pos ) } );
            }
            parts.Add( new TargetPart { GroupIndex = int.Parse( m.Groups[ 1 ].Value ) } );
            pos = m.Index + m.Length;
         }
         if( pos < value.Length )
         {
            parts.Add( new TargetPart { Literal = value.Substring( pos ) } );
         }

         return new Pattern
         {
            Regex = regex,
            Anchor = anchor,
            LiteralLength = literalLength,
            Target = parts.ToArray(),
            Source = key
         };
      }

      private static void AppendLiteral( StringBuilder rx, string literal, ref string anchor, ref int literalLength )
      {
         int i = 0;
         while( i < literal.Length )
         {
            int start = i;
            bool ws = char.IsWhiteSpace( literal[ i ] );
            while( i < literal.Length && char.IsWhiteSpace( literal[ i ] ) == ws )
            {
               i++;
            }

            if( ws )
            {
               rx.Append( @"\s+" );
            }
            else
            {
               var token = literal.Substring( start, i - start );
               rx.Append( Regex.Escape( token ) );
               literalLength += token.Length;
               if( token.Length > anchor.Length )
               {
                  anchor = token;
               }
            }
         }
      }
   }
}
