using System;
using System.Collections;
using XUnity.AutoTranslator.Plugin.Core.Web;

namespace XUnity.AutoTranslator.Plugin.Core.Endpoints.Http
{
   public abstract class HttpEndpoint : ITranslateEndpoint
   {
      public abstract string Id { get; }

      public abstract string FriendlyName { get; }

      public virtual int MaxConcurrency => 1;

      public virtual int MaxTranslationsPerRequest => 1;

      public abstract void Initialize( IInitializationContext context );

      public virtual IEnumerator OnBeforeTranslate( IHttpTranslationContext context ) => null;

      public abstract void OnCreateRequest( IHttpRequestCreationContext context );

      public virtual void OnInspectResponse( IHttpResponseInspectionContext context ) { }

      public abstract void OnExtractTranslation( IHttpTranslationExtractionContext context );

      public IEnumerator Translate( ITranslationContext context )
      {
         var httpContext = new HttpTranslationContext( context );

         var setup = OnBeforeTranslate( httpContext );
         if( setup != null )
         {
            while( setup.MoveNext() ) yield return setup.Current;
         }

         OnCreateRequest( httpContext );
         if( httpContext.Request == null ) httpContext.Fail( "No request object was provided by the translator." );

         var client = new XUnityWebClient();
         var response = client.Send( httpContext.Request );

         var iterator = response.GetSupportedEnumerator();
         while( iterator.MoveNext() ) yield return iterator.Current;

         if( response.IsTimedOut ) httpContext.Fail( "Error occurred while retrieving translation. Timeout." );

         httpContext.Response = response;
         OnInspectResponse( httpContext );

         if( response.Error != null ) httpContext.Fail( "Error occurred while retrieving translation.", response.Error );

         if( response.Data == null ) httpContext.Fail( "Error occurred while retrieving translation. Nothing was returned." );

         OnExtractTranslation( httpContext );
      }
   }
}
