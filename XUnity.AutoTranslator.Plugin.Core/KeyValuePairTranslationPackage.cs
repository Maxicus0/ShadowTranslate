using System.Collections.Generic;
using System.Linq;

namespace XUnity.AutoTranslator.Plugin.Core
{
   public class KeyValuePairTranslationPackage
   {
      private List<KeyValuePair<string, string>> _cachedEntries;

      public KeyValuePairTranslationPackage( string name, IEnumerable<KeyValuePair<string, string>> entries, bool allowMultipleIterations )
      {
         Name = name;
         Entries = entries;
         AllowMultipleIterations = allowMultipleIterations;
      }

      public string Name { get; }
      private IEnumerable<KeyValuePair<string, string>> Entries { get; }
      private bool AllowMultipleIterations { get; }

      internal IEnumerable<KeyValuePair<string, string>> GetIterableEntries()
      {
         if( !AllowMultipleIterations )
         {
            if( _cachedEntries == null )
            {
               _cachedEntries = Entries.ToList();
            }
            return _cachedEntries;
         }
         return Entries;
      }
   }
}
