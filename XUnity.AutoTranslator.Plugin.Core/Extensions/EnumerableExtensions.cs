using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace XUnity.AutoTranslator.Plugin.Core.Extensions
{
   internal static class EnumerableExtensions
   {

      public static void AddRange<T>( this ICollection<T> collection, IEnumerable<T> values )
      {
         foreach( var value in values )
         {
            collection.Add( value );
         }
      }
   }
}
