using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using UnityEngine;
using XUnity.AutoTranslator.Plugin.Core.Configuration;
using XUnity.AutoTranslator.Plugin.Core.Utilities;
using XUnity.Common.Constants;
using XUnity.Common.Logging;
using XUnity.Common.Utilities;

namespace XUnity.AutoTranslator.Plugin.Core.Text
{
   internal class DefaultTextComponentManipulator : ITextComponentManipulator
   {
      private static readonly string TextPropertyName = "text";

      private readonly Type _type;
      private readonly CachedProperty _property;

      public DefaultTextComponentManipulator( Type type )
      {
         _type = type;
         _property = type.CachedProperty( TextPropertyName );
      }

      public string GetText( object ui )
      {
         return (string)_property?.Get( ui );
      }

      public void SetText( object ui, string text )
      {
         var type = _type;

         var property = _property;
         if( property != null )
         {
            property.Set( ui, text );

         }

         var maxVisibleCharactersProperty = type.CachedProperty( "maxVisibleCharacters" );
         if( maxVisibleCharactersProperty != null && maxVisibleCharactersProperty.PropertyType == typeof( int ) )
         {
            var value = (int)maxVisibleCharactersProperty.Get( ui );
            if( 0 < value && value < 99999 )
            {
               maxVisibleCharactersProperty.Set( ui, 99999 );
            }
         }

      }

      private static Dictionary<Type, TypeAndMethod> _textSetters = new Dictionary<Type, TypeAndMethod>();
      private static TypeAndMethod GetTextPropertySetterInParent( Type type )
      {
         var current = type.BaseType;
         while( current != null )
         {
            if( _textSetters.TryGetValue( type, out var typeAndMethod ) )
            {
               return typeAndMethod;
            }
            else
            {
               var property = current.GetProperty( TextPropertyName, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic );

               if( property != null && property.CanWrite )
               {
                  var tam = new TypeAndMethod( current, property.GetSetMethod() );
                  _textSetters[ current ] = tam;
                  return tam;
               }

               current = current.BaseType;
            }
         }

         return null;
      }

      private class TypeAndMethod
      {
         FastReflectionDelegate _setterInvoker;

         public TypeAndMethod( Type type, MethodBase method )
         {
            Type = type;
            SetterMethod = method;
         }

         public Type Type { get; }
         public MethodBase SetterMethod { get; }
         public FastReflectionDelegate SetterInvoker
         {
            get
            {
               return _setterInvoker ?? ( _setterInvoker = CustomFastReflectionHelper.CreateFastDelegate( SetterMethod, true, true ) );
            }
         }
      }
   }

}
