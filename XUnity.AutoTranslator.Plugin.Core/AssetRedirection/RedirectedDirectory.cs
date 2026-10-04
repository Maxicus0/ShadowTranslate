using System.Collections.Generic;
using System.Text;
using XUnity.AutoTranslator.Plugin.Core.Configuration;

namespace XUnity.AutoTranslator.Plugin.Core.AssetRedirection
{
   public static class RedirectedDirectory
   {
      private static UnzippedDirectory UnzippedDirectory;

      private static void Initialize()
      {
         if( UnzippedDirectory == null )
         {
            UnzippedDirectory = new UnzippedDirectory(
              Settings.RedirectedResourcesPath,
              Settings.CacheMetadataForAllFiles );
         }
      }

      internal static void Uninitialize()
      {
         if( UnzippedDirectory != null )
         {
            UnzippedDirectory.Dispose();
            UnzippedDirectory = null;
         }
      }

      public static IEnumerable<RedirectedResource> GetFilesInDirectory( string path, params string[] extensions )
      {
         Initialize();
         return UnzippedDirectory.GetFiles( path, extensions );
      }

      public static IEnumerable<RedirectedResource> GetFile( string path )
      {
         Initialize();
         return UnzippedDirectory.GetFile( path );
      }

      public static bool DirectoryExists( string path )
      {
         Initialize();
         return UnzippedDirectory.DirectoryExists( path );
      }

      public static bool FileExists( string path )
      {
         Initialize();
         return UnzippedDirectory.FileExists( path );
      }
   }
}
