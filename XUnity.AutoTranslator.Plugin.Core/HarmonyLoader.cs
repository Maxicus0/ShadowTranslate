using System;
using System.Reflection;
using HarmonyLib;

namespace XUnity.AutoTranslator.Plugin.Core
{
   internal static class HarmonyLoader
   {
      public static void Load()
      {
         try
         {
            Harmony12Loader.Load();
         }
         catch
         {
         }
      }
   }

   internal static class Harmony12Loader
   {
      public static void Load()
      {
         _ = AccessTools.all;
      }
   }
}
