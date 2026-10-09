using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;
using XUnity.AutoTranslator.Plugin.Core.Configuration;
using XUnity.Common.Logging;

namespace XUnity.AutoTranslator.Plugin.Core.Utilities
{
   internal static class HitchProfiler
   {
      private const double HitchThresholdMs = 80.0;
      private const double StallThresholdMs = 100.0;
      private const double GapReportMs = 10.0;
      private const double SummaryIntervalSeconds = 10.0;

      private const int Capacity = 1 << 16;
      private const int MaxKeys = 512;
      private const int MaxDepth = 32;
      private const int MaxDumpLines = 120;
      private const int MaxQueuedMessages = 5000;

      private static readonly double TicksToMs = 1000.0 / Stopwatch.Frequency;

      private struct Evt
      {
         public long Start;
         public long End;
         public int Key;
         public int Depth;
      }

      private static readonly Evt[] _ring = new Evt[ Capacity ];
      private static long _written;

      private static readonly string[] _keyNames = new string[ MaxKeys ];
      private static readonly bool[] _isMark = new bool[ MaxKeys ];
      private static readonly Dictionary<string, int> _keyIds = new Dictionary<string, int>();
      private static int _keyCount;
      private static int _ignoreA = -1;
      private static int _ignoreB = -1;

      private static readonly double[] _aggTotal = new double[ MaxKeys ];
      private static readonly int[] _aggCalls = new int[ MaxKeys ];
      private static readonly double[] _aggMax = new double[ MaxKeys ];
      private static int _framesInWindow;
      private static int _hitchesInWindow;
      private static long _windowStart;

      private static readonly int[] _stackKey = new int[ MaxDepth ];
      private static readonly long[] _stackStart = new long[ MaxDepth ];
      private static int _depth;
      private static volatile int _openDepth;
      private static volatile int _lastKey;
      private static long _lastEnd;

      private static long _heartbeat;
      private static long _lastLoopTs;
      private static long _t0;
      private static int _gc0, _gc1, _gc2;
      private static int _hitchCount;

      private static volatile bool _started;
      private static int _mainThreadId;

      private static readonly Queue<string> _outbox = new Queue<string>();
      private static readonly object _outboxLock = new object();
      private static StreamWriter _writer;

      private static readonly List<Evt> _scratch = new List<Evt>( 4096 );
      private static readonly Comparison<Evt> ByStart = ( a, b ) => a.Start.CompareTo( b.Start );
      private static readonly double[] _sumMs = new double[ MaxKeys ];
      private static readonly int[] _sumCalls = new int[ MaxKeys ];
      private static readonly double[] _sumMax = new double[ MaxKeys ];
      private static readonly bool[] _picked = new bool[ MaxKeys ];

      internal struct Scope : IDisposable
      {
         private readonly bool _valid;
         private readonly int _key;
         private readonly long _start;
         private readonly int _d;

         internal Scope( int key, long start, int d )
         {
            _valid = true;
            _key = key;
            _start = start;
            _d = d;
         }

         public void Dispose()
         {
            if( !_valid ) return;

            long end = Stopwatch.GetTimestamp();
            if( _depth > 0 ) _depth--;
            _openDepth = _depth;
            Record( _key, _start, end, _d, true );
            if( _d == 0 )
            {
               _lastKey = _key;
               Volatile.Write( ref _lastEnd, end );
            }
         }
      }

      public static Scope Measure( string key )
      {
         if( !_started || Environment.CurrentManagedThreadId != _mainThreadId )
         {
            return default( Scope );
         }

         int id = GetKeyId( key, false );
         if( id == _ignoreA || id == _ignoreB )
         {
            return default( Scope );
         }

         long t = Stopwatch.GetTimestamp();
         int d = _depth++;
         if( d < MaxDepth )
         {
            _stackKey[ d ] = id;
            _stackStart[ d ] = t;
         }
         _openDepth = _depth;
         return new Scope( id, t, d );
      }

      public static void Mark( string key )
      {
         if( !_started || Environment.CurrentManagedThreadId != _mainThreadId )
         {
            return;
         }

         int id = GetKeyId( key, true );
         long t = Stopwatch.GetTimestamp();
         Record( id, t, t, 0, false );
         _lastKey = id;
         Volatile.Write( ref _lastEnd, t );
      }

      private static int GetKeyId( string key, bool mark )
      {
         int id;
         if( _keyIds.TryGetValue( key, out id ) )
         {
            return id;
         }

         if( _keyCount >= MaxKeys - 1 )
         {
            return MaxKeys - 1;
         }

         id = _keyCount++;
         _keyIds[ key ] = id;
         _keyNames[ id ] = key;
         _isMark[ id ] = mark;
         if( key == "Co:Loop" ) _ignoreA = id;
         else if( key == "Co:EndOfFrameLoop" ) _ignoreB = id;
         return id;
      }

      private static void Record( int key, long start, long end, int depth, bool aggregate )
      {
         int i = (int)( _written & ( Capacity - 1 ) );
         _ring[ i ].Start = start;
         _ring[ i ].End = end;
         _ring[ i ].Key = key;
         _ring[ i ].Depth = depth;
         _written++;

         if( aggregate )
         {
            double ms = ( end - start ) * TicksToMs;
            _aggTotal[ key ] += ms;
            _aggCalls[ key ]++;
            if( ms > _aggMax[ key ] ) _aggMax[ key ] = ms;
         }
      }

      private static string NameOf( int key )
      {
         if( key < 0 || key >= MaxKeys ) return "?";
         return _keyNames[ key ] ?? "?";
      }

      private static double RelSeconds( long ts )
      {
         return ( ts - _t0 ) / (double)Stopwatch.Frequency;
      }

      public static void Start()
      {
         if( _started || !Settings.EnableHitchProfiler )
         {
            return;
         }

         try
         {
            _mainThreadId = Environment.CurrentManagedThreadId;
            _keyNames[ MaxKeys - 1 ] = "(other)";
            _t0 = Stopwatch.GetTimestamp();
            _windowStart = _t0;
            _gc0 = GC.CollectionCount( 0 ); _gc1 = GC.CollectionCount( 1 ); _gc2 = GC.CollectionCount( 2 );

            OpenWriter();
            EmitHeader();

            _started = true;

            CoroutineHelper.Start( Loop() );
            CoroutineHelper.Start( EndOfFrameLoop() );

            var thread = new Thread( WatchdogLoop );
            thread.IsBackground = true;
            thread.Name = "XUA-HitchWatchdog";
            thread.Start();
         }
         catch( Exception e )
         {
            _started = false;
            XuaLogger.AutoTranslator.Warn( e, "HitchProfiler: could not start." );
         }
      }

      private static void OpenWriter()
      {
         try
         {
            var path = Path.Combine( Path.Combine( XUnity.Common.Utilities.Paths.GameRoot, "BepInEx" ), "hitch_trace.log" );
            _writer = new StreamWriter( new FileStream( path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite ), new UTF8Encoding( false ) );
            _writer.AutoFlush = false;
            XuaLogger.AutoTranslator.Warn( "HitchProfiler: writing frame trace to '" + path + "' (console is not used for the trace)." );
         }
         catch( Exception e )
         {
            _writer = null;
            XuaLogger.AutoTranslator.Warn( e, "HitchProfiler: cannot open hitch_trace.log; trace output disabled." );
         }
      }

      private static void EmitHeader()
      {
         var sb = new StringBuilder( 512 );
         sb.Append( "# XUnity hitch trace  " ).Append( DateTime.Now.ToString( "yyyy-MM-dd HH:mm:ss" ) ).Append( '\n' );
         sb.Append( "# hitch threshold " ).Append( HitchThresholdMs.ToString( "0" ) ).Append( " ms, stall threshold " ).Append( StallThresholdMs.ToString( "0" ) ).Append( " ms, gap report " ).Append( GapReportMs.ToString( "0" ) ).Append( " ms\n" );
         sb.Append( "# EnableSdwDiagnostics=" ).Append( Settings.EnableSdwDiagnostics )
           .Append( " EnableCyrillicSpacingFix=" ).Append( Settings.EnableCyrillicSpacingFix )
           .Append( " EnableSdwLineSpacingPolicy=" ).Append( Settings.EnableSdwLineSpacingPolicy )
           .Append( " SdwPrebakeFontSizes=" );
         var sizes = Settings.SdwPrebakeFontSizes;
         if( sizes != null )
         {
            for( int i = 0; i < sizes.Length; i++ )
            {
               if( i > 0 ) sb.Append( ',' );
               sb.Append( sizes[ i ] );
            }
         }
         sb.Append( '\n' );
         sb.Append( "# Reading guide: [STALL] lines come from a watchdog thread WHILE the main thread is stuck.\n" );
         sb.Append( "#   'NO plugin scope open' = the stall is outside measured plugin code (Unity/game).\n" );
         sb.Append( "#   '===== HITCH' blocks list the frame timeline; GAP lines = stretches with no plugin code running.\n" );
         sb.Append( "#   '[SUMMARY]' lines = steady-state plugin cost per key (inclusive, nested scopes counted in both).\n" );
         Emit( sb.ToString() );
      }

      private static void Emit( string text )
      {
         lock( _outboxLock )
         {
            if( _outbox.Count < MaxQueuedMessages )
            {
               _outbox.Enqueue( text );
            }
         }
      }

      private static void DrainOutbox()
      {
         if( _writer == null )
         {
            lock( _outboxLock )
            {
               _outbox.Clear();
            }
            return;
         }

         bool any = false;
         while( true )
         {
            string s = null;
            lock( _outboxLock )
            {
               if( _outbox.Count > 0 )
               {
                  s = _outbox.Dequeue();
               }
            }

            if( s == null )
            {
               break;
            }

            _writer.Write( s );
            _writer.Write( '\n' );
            any = true;
         }

         if( any )
         {
            _writer.Flush();
         }
      }

      private static void WatchdogLoop()
      {
         long reportedFor = 0;
         long lastReportTs = 0;

         while( true )
         {
            try
            {
               Thread.Sleep( 10 );

               long hb = Volatile.Read( ref _heartbeat );
               if( hb != 0 )
               {
                  long now = Stopwatch.GetTimestamp();
                  double stalledMs = ( now - hb ) * TicksToMs;
                  if( stalledMs >= StallThresholdMs )
                  {
                     if( reportedFor != hb || ( now - lastReportTs ) * TicksToMs >= 250.0 )
                     {
                        reportedFor = hb;
                        lastReportTs = now;
                        EmitStall( stalledMs, now );
                     }
                  }
               }

               DrainOutbox();
            }
            catch( Exception )
            {
            }
         }
      }

      private static void EmitStall( double stalledMs, long now )
      {
         var sb = new StringBuilder( 256 );
         sb.Append( "[STALL] +" ).Append( RelSeconds( now ).ToString( "0.00" ) ).Append( "s  main thread has not finished a frame for " )
           .Append( stalledMs.ToString( "0" ) ).Append( " ms  | " );

         int depth = _openDepth;
         if( depth <= 0 )
         {
            sb.Append( "NO plugin scope open => time is spent in Unity/game code (or the thread is blocked), not in measured plugin code" );
         }
         else
         {
            sb.Append( "open plugin scopes: " );
            int n = depth < MaxDepth ? depth : MaxDepth;
            for( int d = 0; d < n; d++ )
            {
               if( d > 0 ) sb.Append( " > " );
               sb.Append( NameOf( _stackKey[ d ] ) ).Append( ' ' ).Append( ( ( now - _stackStart[ d ] ) * TicksToMs ).ToString( "0" ) ).Append( " ms" );
            }
         }

         long le = Volatile.Read( ref _lastEnd );
         if( le != 0 )
         {
            sb.Append( "  | last plugin activity: '" ).Append( NameOf( _lastKey ) ).Append( "' ended " ).Append( ( ( now - le ) * TicksToMs ).ToString( "0" ) ).Append( " ms ago" );
         }

         Emit( sb.ToString() );
      }

      private static IEnumerator EndOfFrameLoop()
      {
         var wait = new WaitForEndOfFrame();
         while( true )
         {
            yield return wait;

            try
            {
               Mark( "EndOfFrame" );
               Volatile.Write( ref _heartbeat, Stopwatch.GetTimestamp() );
            }
            catch( Exception )
            {
            }
         }
      }

      private static IEnumerator Loop()
      {
         while( true )
         {
            yield return null;

            try
            {
               _mainThreadId = Environment.CurrentManagedThreadId;
               Mark( "LoopResume" );
               long now = Stopwatch.GetTimestamp();
               Volatile.Write( ref _heartbeat, now );

               int g0 = GC.CollectionCount( 0 ), g1 = GC.CollectionCount( 1 ), g2 = GC.CollectionCount( 2 );
               _framesInWindow++;

               if( _lastLoopTs != 0 )
               {
                  double intervalMs = ( now - _lastLoopTs ) * TicksToMs;
                  double unityMs = Time.unscaledDeltaTime * 1000.0;
                  if( intervalMs >= HitchThresholdMs || unityMs >= HitchThresholdMs )
                  {
                     _hitchesInWindow++;
                     DumpHitch( _lastLoopTs, now, intervalMs, unityMs, g0 - _gc0, g1 - _gc1, g2 - _gc2 );
                  }
               }

               _gc0 = g0; _gc1 = g1; _gc2 = g2;
               _lastLoopTs = now;

               MaybeEmitSummary( now );
            }
            catch( Exception e )
            {
               Emit( "[PROFILER-ERROR] " + e );
            }
         }
      }

      private static void DumpHitch( long from, long to, double intervalMs, double unityMs, int g0, int g1, int g2 )
      {
         _hitchCount++;
         _scratch.Clear();

         long w = _written;
         long lim = w - Capacity;
         if( lim < 0 ) lim = 0;
         for( long i = w - 1; i >= lim; i-- )
         {
            var e = _ring[ (int)( i & ( Capacity - 1 ) ) ];
            if( e.End < from ) break;
            _scratch.Add( e );
         }
         _scratch.Sort( ByStart );

         Array.Clear( _sumMs, 0, MaxKeys );
         Array.Clear( _sumCalls, 0, MaxKeys );
         Array.Clear( _sumMax, 0, MaxKeys );

         var gaps = new StringBuilder( 256 );
         var body = new StringBuilder( 2048 );
         int gapLines = 0, bodyLines = 0, omitted = 0;
         double topLevelMs = 0, gapsTotal = 0;
         long cursor = from;
         string prevName = "(start of interval)";

         for( int n = 0; n < _scratch.Count; n++ )
         {
            var e = _scratch[ n ];
            string name = NameOf( e.Key );
            bool mark = _isMark[ e.Key ];
            double off = ( e.Start - from ) * TicksToMs;
            double dur = ( e.End - e.Start ) * TicksToMs;

            if( !mark )
            {
               _sumMs[ e.Key ] += dur;
               _sumCalls[ e.Key ]++;
               if( dur > _sumMax[ e.Key ] ) _sumMax[ e.Key ] = dur;
            }

            if( e.Depth == 0 )
            {
               double gap = ( e.Start - cursor ) * TicksToMs;
               if( gap >= GapReportMs )
               {
                  gapsTotal += gap;
                  if( gapLines < 25 )
                  {
                     gaps.Append( "  GAP " ).Append( gap.ToString( "0" ) ).Append( " ms with no plugin code, between '" ).Append( prevName )
                         .Append( "' and '" ).Append( name ).Append( "' (+" ).Append( ( ( cursor - from ) * TicksToMs ).ToString( "0" ) ).Append( " .. +" ).Append( off.ToString( "0" ) ).Append( " ms)\n" );
                     gapLines++;
                  }
               }

               if( e.End > cursor ) cursor = e.End;
               prevName = mark ? name : name + " (end)";
               if( !mark ) topLevelMs += dur;
            }

            bool show = mark || dur >= ( e.Depth == 0 ? 1.0 : 3.0 );
            if( show )
            {
               if( bodyLines < MaxDumpLines )
               {
                  body.Append( "  +" ).Append( off.ToString( "0.0" ) ).Append( ' ' );
                  int indent = e.Depth < 6 ? e.Depth : 6;
                  for( int k = 0; k < indent; k++ ) body.Append( "  " );
                  if( mark ) body.Append( "---- " ).Append( name ).Append( '\n' );
                  else body.Append( name ).Append( ' ' ).Append( dur.ToString( "0.0" ) ).Append( " ms\n" );
                  bodyLines++;
               }
               else
               {
                  omitted++;
               }
            }
         }

         double tail = ( to - cursor ) * TicksToMs;
         if( tail >= GapReportMs )
         {
            gapsTotal += tail;
            gaps.Append( "  GAP " ).Append( tail.ToString( "0" ) ).Append( " ms with no plugin code, between '" ).Append( prevName ).Append( "' and the next LoopResume\n" );
         }

         var sb = new StringBuilder( 4096 );
         sb.Append( "===== HITCH #" ).Append( _hitchCount ).Append( "  +" ).Append( RelSeconds( to ).ToString( "0.00" ) ).Append( "s  frame " ).Append( Time.frameCount )
           .Append( "  interval " ).Append( intervalMs.ToString( "0" ) ).Append( " ms (Unity delta " ).Append( unityMs.ToString( "0" ) )
           .Append( " ms) | plugin top-level " ).Append( topLevelMs.ToString( "0" ) ).Append( " ms | no-plugin gaps " ).Append( gapsTotal.ToString( "0" ) )
           .Append( " ms | GC gen0/1/2 +" ).Append( g0 ).Append( '/' ).Append( g1 ).Append( '/' ).Append( g2 ).Append( '\n' );
         sb.Append( gaps );

         Array.Clear( _picked, 0, MaxKeys );
         sb.Append( "  top keys (inclusive):" );
         for( int r = 0; r < 8; r++ )
         {
            int best = -1;
            double bestMs = 0.99;
            for( int k = 0; k < MaxKeys; k++ )
            {
               if( !_picked[ k ] && _sumMs[ k ] > bestMs )
               {
                  best = k;
                  bestMs = _sumMs[ k ];
               }
            }
            if( best < 0 ) break;
            _picked[ best ] = true;
            sb.Append( "  " ).Append( NameOf( best ) ).Append( '=' ).Append( _sumMs[ best ].ToString( "0" ) ).Append( "ms/" ).Append( _sumCalls[ best ] ).Append( "(max " ).Append( _sumMax[ best ].ToString( "0" ) ).Append( ')' );
         }
         sb.Append( '\n' );

         sb.Append( body );
         if( omitted > 0 )
         {
            sb.Append( "  ... " ).Append( omitted ).Append( " more events omitted\n" );
         }

         Emit( sb.ToString() );
      }

      private static void MaybeEmitSummary( long now )
      {
         double windowSec = ( now - _windowStart ) / (double)Stopwatch.Frequency;
         if( windowSec < SummaryIntervalSeconds )
         {
            return;
         }

         var sb = new StringBuilder( 512 );
         sb.Append( "[SUMMARY] +" ).Append( RelSeconds( now ).ToString( "0" ) ).Append( "s  last " ).Append( windowSec.ToString( "0.0" ) ).Append( "s: " )
           .Append( _framesInWindow ).Append( " frames, " ).Append( _hitchesInWindow ).Append( " hitches |" );

         Array.Clear( _picked, 0, MaxKeys );
         for( int r = 0; r < 12; r++ )
         {
            int best = -1;
            double bestMs = 0.99;
            for( int k = 0; k < MaxKeys; k++ )
            {
               if( !_picked[ k ] && _aggTotal[ k ] > bestMs )
               {
                  best = k;
                  bestMs = _aggTotal[ k ];
               }
            }
            if( best < 0 ) break;
            _picked[ best ] = true;
            sb.Append( "  " ).Append( NameOf( best ) ).Append( '=' ).Append( _aggTotal[ best ].ToString( "0" ) ).Append( "ms/" ).Append( _aggCalls[ best ] ).Append( "(max " ).Append( _aggMax[ best ].ToString( "0" ) ).Append( ')' );
         }
         Emit( sb.ToString() );

         Array.Clear( _aggTotal, 0, MaxKeys );
         Array.Clear( _aggCalls, 0, MaxKeys );
         Array.Clear( _aggMax, 0, MaxKeys );
         _framesInWindow = 0;
         _hitchesInWindow = 0;
         _windowStart = now;
      }
   }
}
