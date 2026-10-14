using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace XUnity.AutoTranslator.Plugin.Core.Updating
{
   internal static class ShadowUpdateGate
   {
      private const string ResourceName = "ShadowTranslate.Updater.exe";

      public static void Run( string pluginVersion, Action<string> log = null, string gameExeOverride = null )
      {
         Action<string> L = msg => { try { if( log != null ) log( "[ShadowUpdater] " + msg ); } catch { } };
         Process updater = null;
         try
         {
            if( Environment.GetEnvironmentVariable( "SHADOWTRANSLATE_NO_UPDATE" ) == "1" ) { L( "автообновление отключено переменной SHADOWTRANSLATE_NO_UPDATE." ); return; }
            if( Environment.GetEnvironmentVariable( "SHADOWTRANSLATE_JUST_UPDATED" ) == "1" )
            {
               Environment.SetEnvironmentVariable( "SHADOWTRANSLATE_JUST_UPDATED", null );
               L( "игра только что перезапущена после обновления, проверка пропущена." );
               return;
            }

            var self = Process.GetCurrentProcess();
            var gameExe = gameExeOverride ?? self.MainModule.FileName;
            var gameDir = Path.GetDirectoryName( gameExe );
            var sdir = Path.Combine( gameDir, "ShadowTranslate" );
            Directory.CreateDirectory( sdir );

            var ini = ReadIni( Path.Combine( sdir, "updater.ini" ) );
            if( ini.TryGetValue( "enabled", out var en ) && en.Equals( "false", StringComparison.OrdinalIgnoreCase ) ) { L( "автообновление выключено в updater.ini." ); return; }
            int checkSec = 12;
            if( ini.TryGetValue( "checktimeoutseconds", out var cs ) && int.TryParse( cs, out var csv ) ) checkSec = Math.Max( 3, Math.Min( 120, csv ) );

            var exe = EnsureUpdaterExe( sdir, L );
            if( exe == null ) { L( "ShadowTranslate.Updater.exe не найден, обновление пропущено." ); return; }

            var statusPath = Path.Combine( sdir, "status.txt" );
            try { File.Delete( statusPath ); } catch { }

            var args = new StringBuilder();
            args.Append( "--pid " ).Append( self.Id );
            args.Append( " --pid-start " ).Append( self.StartTime.ToUniversalTime().Ticks );
            args.Append( " --game-dir " ).Append( Quote( gameDir ) );
            args.Append( " --game-exe " ).Append( Quote( gameExe ) );
            args.Append( " --status " ).Append( Quote( statusPath ) );
            args.Append( " --local-version " ).Append( Quote( pluginVersion ?? "" ) );
            var cmd = Environment.GetCommandLineArgs();
            if( cmd.Length > 1 )
            {
               var joined = string.Join( "\n", cmd, 1, cmd.Length - 1 );
               args.Append( " --game-args " ).Append( Convert.ToBase64String( Encoding.UTF8.GetBytes( joined ) ) );
            }

            var psi = new ProcessStartInfo( exe, args.ToString() )
            {
               WorkingDirectory = sdir,
               UseShellExecute = false,
               CreateNoWindow = true
            };
            updater = Process.Start( psi );
            L( "проверка обновлений запущена (подробный журнал: ShadowTranslate\\updater.log)." );

            var start = DateTime.UtcNow;
            var lastChange = DateTime.UtcNow;
            int lastSeq = -1;
            string lastState = null;
            DateTime lastLog = DateTime.MinValue;

            while( true )
            {
               Thread.Sleep( 100 );
               var st = ReadStatus( statusPath );
               if( st != null && st.Seq != lastSeq )
               {
                  lastSeq = st.Seq;
                  lastChange = DateTime.UtcNow;
                  bool stateChanged = st.State != lastState;
                  if( stateChanged || ( DateTime.UtcNow - lastLog ).TotalSeconds >= 15 )
                  {
                     L( st.State + ( string.IsNullOrEmpty( st.Msg ) ? "" : ": " + st.Msg ) );
                     lastLog = DateTime.UtcNow;
                  }
                  lastState = st.State;
                  if( st.State == "continue" ) return;
               }

               if( updater.HasExited )
               {
                  var fin = ReadStatus( statusPath );
                  L( "обновлятор завершился (код " + updater.ExitCode + ")" + ( fin != null ? ", статус: " + fin.State : "" ) + ", продолжаю запуск." );
                  return;
               }

               var elapsed = ( DateTime.UtcNow - start ).TotalSeconds;
               var quiet = ( DateTime.UtcNow - lastChange ).TotalSeconds;
               bool early = lastState == null || lastState == "starting" || lastState == "checking";
               if( early && elapsed > checkSec + 15 )
               {
                  L( "обновлятор не ответил вовремя, продолжаю запуск без обновления." );
                  AbandonUpdater( updater );
                  return;
               }
               if( !early && lastState != "restart" && quiet > 90 )
               {
                  L( "обновлятор завис (нет прогресса 90 с), продолжаю запуск без обновления." );
                  AbandonUpdater( updater );
                  return;
               }
               if( lastState == "restart" && quiet > 180 )
               {
                  L( "обновлятор не закрыл игру за 180 с, продолжаю запуск." );
                  AbandonUpdater( updater );
                  return;
               }
            }
         }
         catch( Exception e )
         {
            L( "сбой модуля обновления (игра запускается как обычно): " + e.GetType().Name + ": " + e.Message );
            AbandonUpdater( updater );
         }
      }

      private static void AbandonUpdater( Process p )
      {
         try { if( p != null && !p.HasExited ) p.Kill(); } catch { }
      }

      private class StatusData { public int Seq; public string State, Msg; }

      private static StatusData ReadStatus( string path )
      {
         for( int i = 0; i < 3; i++ )
         {
            try
            {
               if( !File.Exists( path ) ) return null;
               var d = new StatusData();
               bool ok = false;
               using( var fs = new FileStream( path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete ) )
               using( var sr = new StreamReader( fs, Encoding.UTF8 ) )
               {
                  string line;
                  while( ( line = sr.ReadLine() ) != null )
                  {
                     int k = line.IndexOf( '=' ); if( k <= 0 ) continue;
                     var key = line.Substring( 0, k ); var val = line.Substring( k + 1 );
                     if( key == "seq" ) ok = int.TryParse( val, out d.Seq );
                     else if( key == "state" ) d.State = val;
                     else if( key == "msg" ) d.Msg = val;
                  }
               }
               return ok && d.State != null ? d : null;
            }
            catch( IOException ) { Thread.Sleep( 20 ); }
            catch( UnauthorizedAccessException ) { Thread.Sleep( 20 ); }
         }
         return null;
      }

      private static Dictionary<string, string> ReadIni( string path )
      {
         var d = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
         try
         {
            if( File.Exists( path ) )
               foreach( var raw in File.ReadAllLines( path ) )
               {
                  var line = raw.Trim();
                  if( line.Length == 0 || line[ 0 ] == ';' || line[ 0 ] == '#' || line[ 0 ] == '[' ) continue;
                  int i = line.IndexOf( '=' ); if( i <= 0 ) continue;
                  d[ line.Substring( 0, i ).Trim() ] = line.Substring( i + 1 ).Trim();
               }
         }
         catch { }
         return d;
      }

      private static string EnsureUpdaterExe( string sdir, Action<string> L )
      {
         var target = Path.Combine( sdir, "ShadowTranslate.Updater.exe" );
         try
         {
            var asm = typeof( ShadowUpdateGate ).Assembly;
            string res = null;
            foreach( var n in asm.GetManifestResourceNames() )
               if( n.EndsWith( ResourceName, StringComparison.OrdinalIgnoreCase ) ) { res = n; break; }
            if( res == null ) return File.Exists( target ) ? target : null;

            byte[] data;
            using( var s = asm.GetManifestResourceStream( res ) )
            using( var ms = new MemoryStream() ) { s.CopyTo( ms ); data = ms.ToArray(); }

            if( File.Exists( target ) )
            {
               using( var sha = SHA256.Create() )
               {
                  var h1 = sha.ComputeHash( data );
                  byte[] cur = null;
                  try { cur = sha.ComputeHash( File.ReadAllBytes( target ) ); } catch { }
                  if( cur != null && Convert.ToBase64String( cur ) == Convert.ToBase64String( h1 ) ) return target;
               }
            }
            try
            {
               var tmp = target + ".tmp";
               File.WriteAllBytes( tmp, data );
               if( File.Exists( target ) ) File.Delete( target );
               File.Move( tmp, target );
               L( "обновлятор распакован: " + target );
            }
            catch( Exception e ) { L( "не удалось записать обновлятор (" + e.Message + "), использую имеющийся." ); }
         }
         catch( Exception e ) { L( "ошибка распаковки обновлятора: " + e.Message ); }
         return File.Exists( target ) ? target : null;
      }

      private static string Quote( string a )
      {
         if( a.Length > 0 && a.IndexOfAny( new[] { ' ', '\t', '"' } ) < 0 ) return a;
         var sb = new StringBuilder( "\"" );
         int bs = 0;
         foreach( var ch in a )
         {
            if( ch == '\\' ) { bs++; continue; }
            if( ch == '"' ) { sb.Append( '\\', bs * 2 + 1 ); sb.Append( '"' ); }
            else { sb.Append( '\\', bs ); sb.Append( ch ); }
            bs = 0;
         }
         sb.Append( '\\', bs * 2 );
         sb.Append( '"' );
         return sb.ToString();
      }
   }
}
