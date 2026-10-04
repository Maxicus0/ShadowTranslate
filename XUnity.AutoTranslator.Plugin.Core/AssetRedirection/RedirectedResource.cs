using System;
using System.IO;

namespace XUnity.AutoTranslator.Plugin.Core.AssetRedirection
{
   public class RedirectedResource
   {
      private readonly Func<Stream> _streamFactory;

      internal RedirectedResource( Func<Stream> streamFactory, string containerFile, string fullName )
      {
         _streamFactory = streamFactory;
         IsZipped = containerFile != null;
         ContainerFile = containerFile;
         FullName = fullName;
      }

      internal RedirectedResource( string fullName )
      {
         FullName = fullName;
         _streamFactory = () => File.OpenRead( FullName );
      }

      public bool IsZipped { get; }

      public string ContainerFile { get; }

      public string FullName { get; }

      public Stream OpenStream()
      {
         return _streamFactory();
      }
   }
}
