using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using System.Text;
using UnityEngine;
using XUnity.AutoTranslator.Plugin.Core.Configuration;
using XUnity.AutoTranslator.Plugin.Core.Constants;
using XUnity.AutoTranslator.Plugin.Core.Web;
using XUnity.Common.Constants;
using XUnity.Common.Harmony;

namespace XUnity.AutoTranslator.Plugin.Core.Endpoints.Www
{
   public abstract class WwwEndpoint : ITranslateEndpoint
   {
      public abstract string Id { get; }

      public abstract string FriendlyName { get; }

      public virtual int MaxConcurrency => 1;

      public virtual int MaxTranslationsPerRequest => 1;

      public virtual IEnumerator OnBeforeTranslate( IWwwTranslationContext context ) => null;

      public abstract void Initialize( IInitializationContext context );

      public abstract void OnCreateRequest( IWwwRequestCreationContext context );

      public abstract void OnExtractTranslation( IWwwTranslationExtractionContext context );

      protected WWW CreateWww( string address, byte[] data, Dictionary<string, string> headers )
      {
         throw new NotSupportedException( "WWW API generally not supported in unstripped unity proxy API." );
      }

      public IEnumerator Translate( ITranslationContext context )
      {
         var wwwContext = new WwwTranslationContext( context );

         var setup = OnBeforeTranslate( wwwContext );
         if( setup != null )
         {
            while( setup.MoveNext() ) yield return setup.Current;
         }

         OnCreateRequest( wwwContext );
         if( wwwContext.RequestInfo == null ) wwwContext.Fail( "No request object was provided by the translator." );

         var request = wwwContext.RequestInfo;
         var url = request.Address;
         var data = request.Data;
         var headers = request.Headers;

         var www = CreateWww( request.Address, data != null ? Encoding.UTF8.GetBytes( data ) : null, headers );

         yield return www;

         string error = www.error;
         if( error != null ) wwwContext.Fail( "Error occurred while retrieving translation. " + error );

         var text = www.text;
         if( text == null ) wwwContext.Fail( "Error occurred while extracting text from response." );

         wwwContext.ResponseData = text;

         OnExtractTranslation( wwwContext );
      }
   }
}
