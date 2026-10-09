using System;
using System.Collections.Generic;
using System.Reflection;

namespace XUnity.AutoTranslator.Plugin.Core.Utilities
{
   internal static class TypeLookup
   {
      private static readonly object Sync = new object();

      private static readonly HashSet<Assembly> Scanned = new HashSet<Assembly>();
      private static readonly Dictionary<string, Type> ByFullName = new Dictionary<string, Type>( StringComparer.Ordinal );
      private static readonly Dictionary<string, Type> BySimpleName = new Dictionary<string, Type>( StringComparer.Ordinal );
      private static readonly List<Type> AllTypes = new List<Type>();

      private static readonly string[] SkippedAssemblyPrefixes =
      {
         "System",
         "mscorlib",
         "netstandard",
         "Mono.",
         "Microsoft.",
         "Il2CppInterop",
         "BepInEx",
         "0Harmony",
         "HarmonyX",
         "MonoMod",
         "Newtonsoft",
      };

      private static bool ShouldSkip( Assembly assembly )
      {
         try
         {
            if( assembly.IsDynamic ) return true;

            var name = assembly.GetName().Name;
            if( string.IsNullOrEmpty( name ) ) return true;

            for( int i = 0; i < SkippedAssemblyPrefixes.Length; i++ )
            {
               if( name.StartsWith( SkippedAssemblyPrefixes[ i ], StringComparison.Ordinal ) )
               {
                  return true;
               }
            }
            return false;
         }
         catch
         {
            return true;
         }
      }

      private static Type[] SafeGetTypes( Assembly assembly )
      {
         try
         {
            return assembly.GetTypes();
         }
         catch( ReflectionTypeLoadException ex )
         {
            var loaded = new List<Type>();
            foreach( var t in ex.Types )
            {
               if( t != null ) loaded.Add( t );
            }
            return loaded.ToArray();
         }
         catch
         {
            return null;
         }
      }

      private static void RefreshIndex()
      {
         Assembly[] assemblies;
         try
         {
            assemblies = AppDomain.CurrentDomain.GetAssemblies();
         }
         catch
         {
            return;
         }

         for( int i = 0; i < assemblies.Length; i++ )
         {
            var assembly = assemblies[ i ];
            if( !Scanned.Add( assembly ) ) continue;
            if( ShouldSkip( assembly ) ) continue;

            var types = SafeGetTypes( assembly );
            if( types == null ) continue;

            for( int j = 0; j < types.Length; j++ )
            {
               var type = types[ j ];
               if( type == null ) continue;

               AllTypes.Add( type );

               var fullName = type.FullName;
               if( fullName != null && !ByFullName.ContainsKey( fullName ) )
               {
                  ByFullName[ fullName ] = type;
               }

               var simpleName = type.Name;
               if( simpleName != null && !BySimpleName.ContainsKey( simpleName ) )
               {
                  BySimpleName[ simpleName ] = type;
               }
            }
         }
      }

      public static Dictionary<string, Type> ResolveTypesByName( params string[] names )
      {
         var result = new Dictionary<string, Type>();
         if( names == null || names.Length == 0 ) return result;

         List<string> remaining = null;
         foreach( var name in names )
         {
            if( string.IsNullOrEmpty( name ) || result.ContainsKey( name ) ) continue;

            Type direct = null;
            try
            {
               direct = Type.GetType( name, false );
            }
            catch
            {
            }

            if( direct != null )
            {
               result[ name ] = direct;
            }
            else
            {
               if( remaining == null ) remaining = new List<string>();
               remaining.Add( name );
            }
         }

         if( remaining == null ) return result;

         lock( Sync )
         {
            RefreshIndex();

            foreach( var name in remaining )
            {
               if( ByFullName.TryGetValue( name, out var type ) || BySimpleName.TryGetValue( name, out type ) )
               {
                  result[ name ] = type;
               }
            }
         }

         return result;
      }

      public static Type ResolveType( string fullName )
      {
         if( string.IsNullOrEmpty( fullName ) ) return null;
         return ResolveTypesByName( fullName ).TryGetValue( fullName, out var type ) ? type : null;
      }

      public static List<Type> FindConcreteImplementers( Type baseOrInterface )
      {
         var results = new List<Type>();
         if( baseOrInterface == null ) return results;

         lock( Sync )
         {
            RefreshIndex();

            for( int i = 0; i < AllTypes.Count; i++ )
            {
               var type = AllTypes[ i ];
               if( type.IsInterface || type.IsAbstract ) continue;

               try
               {
                  if( baseOrInterface.IsAssignableFrom( type ) )
                  {
                     results.Add( type );
                  }
               }
               catch
               {
               }
            }
         }

         return results;
      }
   }
}
