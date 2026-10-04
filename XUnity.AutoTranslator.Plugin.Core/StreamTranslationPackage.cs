using System;
using System.IO;
using XUnity.Common.Extensions;

namespace XUnity.AutoTranslator.Plugin.Core
{
   public sealed class StreamTranslationPackage : IDisposable
   {
      private Stream _cachedStream;

      public StreamTranslationPackage( string name, Stream stream, bool allowMultipleIterations )
      {
         if( allowMultipleIterations && !stream.CanSeek )
         {
            throw new ArgumentException( "Cannot iterate a non-seekable stream multiple times.", nameof( allowMultipleIterations ) );
         }
         Name = name;
         Stream = stream;
         AllowMultipleIterations = allowMultipleIterations;
      }

      public string Name { get; }
      private Stream Stream { get; set; }
      private bool AllowMultipleIterations { get; }

      internal Stream GetReadableStream()
      {
         if( !AllowMultipleIterations )
         {
            if( _cachedStream == null )
            {
               _cachedStream = new MemoryStream( Stream.ReadFully( 0 ) );
               Stream.Dispose();
               Stream = null;
            }
            return _cachedStream;
         }
         return Stream;
      }

      private bool disposedValue = false;

      private void Dispose( bool disposing )
      {
         if( !disposedValue )
         {
            if( disposing )
            {
               Stream?.Dispose();
            }

            Stream = null;

            disposedValue = true;
         }
      }

      public void Dispose()
      {
         Dispose( true );
      }
   }
}
