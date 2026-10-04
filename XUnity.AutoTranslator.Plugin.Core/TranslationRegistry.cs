using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;

namespace XUnity.AutoTranslator.Plugin.Core
{
   public static class TranslationRegistry
   {
      public static ITranslationRegistry Default => AutoTranslationPlugin.Current;
   }

   public interface ITranslationRegistry
   {
      void RegisterPluginSpecificTranslations( Assembly assembly, StreamTranslationPackage package );

      void RegisterPluginSpecificTranslations( Assembly assembly, KeyValuePairTranslationPackage package );

      void EnablePluginTranslationFallback( Assembly assembly );
   }

   public static class TranslationRegistryExtensions
   {
      private static Assembly GetCallingPlugin()
      {
         var frame = new StackFrame( 2 );
         var method = frame.GetMethod();
         if( method != null )
         {
            var ass = method.DeclaringType.Assembly;
            return ass;
         }
         throw new ArgumentException( "Could not automatically determine the calling plugin. Consider calling the overload of this method taking an assembly name." );
      }

      public static void RegisterPluginSpecificTranslations( this ITranslationRegistry registry, StreamTranslationPackage package )
      {
         var assembly = GetCallingPlugin();
         registry.RegisterPluginSpecificTranslations( assembly, package );
      }

      public static void RegisterPluginSpecificTranslations( this ITranslationRegistry registry, KeyValuePairTranslationPackage package )
      {
         var assembly = GetCallingPlugin();
         registry.RegisterPluginSpecificTranslations( assembly, package );
      }

      public static void EnablePluginTranslationFallback( this ITranslationRegistry registry )
      {
         var assembly = GetCallingPlugin();
         registry.EnablePluginTranslationFallback( assembly );
      }
   }
}
