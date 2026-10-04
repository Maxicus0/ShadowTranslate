using UnityEngine;

namespace XUnity.AutoTranslator.Plugin.Core
{
   public static class AutoTranslator
   {
      internal static IInternalTranslator Internal => AutoTranslationPlugin.Current;

      public static ITranslator Default => AutoTranslationPlugin.Current;
   }
}
