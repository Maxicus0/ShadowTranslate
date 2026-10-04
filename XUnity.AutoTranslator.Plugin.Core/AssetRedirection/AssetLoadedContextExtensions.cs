using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using XUnity.AutoTranslator.Plugin.Core.Configuration;
using XUnity.ResourceRedirector;

namespace XUnity.AutoTranslator.Plugin.Core.AssetRedirection
{
   public static class AssetLoadedContextExtensions
   {
      public static string GetPreferredFilePath( this IAssetOrResourceLoadedContext context, UnityEngine.Object asset, string extension )
      {
         string parentDirectory;
         if( context is AssetLoadedContext )
         {
            parentDirectory = "assets";
         }
         else if( context is ResourceLoadedContext )
         {
            parentDirectory = "resources";
         }
         else
         {
            throw new ArgumentException( "context" );
         }

         return Path.Combine( Path.Combine( Settings.RedirectedResourcesPath, parentDirectory ), context.GetUniqueFileSystemAssetPath( asset ) ) + extension;
      }

      public static string GetPreferredFilePath( this IAssetOrResourceLoadedContext context, string parentDirectory, UnityEngine.Object asset, string extension )
      {
         return Path.Combine( parentDirectory, context.GetUniqueFileSystemAssetPath( asset ) ) + extension;
      }

      public static string GetPreferredFilePathWithCustomFileName( this IAssetOrResourceLoadedContext context, UnityEngine.Object asset, string fileName )
      {
         string parentDirectory;
         if( context is AssetLoadedContext )
         {
            parentDirectory = "assets";
         }
         else if( context is ResourceLoadedContext )
         {
            parentDirectory = "resources";
         }
         else
         {
            throw new ArgumentException( "context" );
         }

         var path = Path.Combine( Path.Combine( Settings.RedirectedResourcesPath, parentDirectory ), context.GetUniqueFileSystemAssetPath( asset ) );
         if( fileName != null )
         {
            path = Path.Combine( path, fileName );
         }
         return path;
      }

      public static string GetPreferredFilePathWithCustomFileName( this IAssetOrResourceLoadedContext context, string parentDirectory, UnityEngine.Object asset, string fileName )
      {
         var path = Path.Combine( parentDirectory, context.GetUniqueFileSystemAssetPath( asset ) );
         if( fileName != null )
         {
            path = Path.Combine( path, fileName );
         }
         return path;
      }
   }
}
