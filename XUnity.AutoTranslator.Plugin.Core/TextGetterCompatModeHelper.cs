using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using XUnity.AutoTranslator.Plugin.Core.Configuration;
using XUnity.AutoTranslator.Plugin.Core.Extensions;

namespace XUnity.AutoTranslator.Plugin.Core.Utilities
{
   internal static class TextGetterCompatModeHelper
   {
      public static bool IsGettingText = false;

      [MethodImpl( MethodImplOptions.NoInlining )]
      public static void ReplaceTextWithOriginal( object instance, ref string __result )
      {
         if( !Settings.TextGetterCompatibilityMode || IsGettingText ) return;

         var tti = instance.GetTextTranslationInfo();
         if( tti?.IsTranslated == true )
         {
            var originalAssembly = instance.GetType().Assembly;
            var withinGameCode = originalAssembly.IsAssemblyCsharp();
            if( withinGameCode )
            {
               __result = tti.OriginalText;
            }
            else
            {
               var callingMethod = new StackFrame( 4 ).GetMethod();

               var callingAssembly = callingMethod.DeclaringType?.Assembly;

               if( !originalAssembly.Equals( callingAssembly ) )
               {
                  __result = tti.OriginalText;
               }
            }
         }
      }
   }
}
