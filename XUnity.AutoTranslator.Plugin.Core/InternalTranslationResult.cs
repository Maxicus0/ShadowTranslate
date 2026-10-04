using System;
using System.Collections;
using XUnity.Common.Logging;

namespace XUnity.AutoTranslator.Plugin.Core
{
   class InternalTranslationResult : IEnumerator
   {
      private readonly Action<TranslationResult> _onCompleted;

      internal InternalTranslationResult( bool isGlobal, Action<TranslationResult> onCompleted )
      {
         IsGlobal = isGlobal;
         _onCompleted = onCompleted;
      }

      internal bool IsGlobal { get; private set; }

      public bool IsCompleted { get; private set; }

      public string TranslatedText { get; private set; }

      public string ErrorMessage { get; private set; }

      public bool HasError => ErrorMessage != null;

      internal void SetCompleted( string translatedText )
      {
         if( !IsCompleted )
         {
            IsCompleted = true;
            SetCompletedInternal( translatedText );
         }
      }

      internal void SetEmptyResponse()
      {
         SetError( "Received empty response." );
      }

      internal void SetErrorWithMessage( string errorMessage )
      {
         SetError( errorMessage );
      }

      private void SetError( string errorMessage )
      {
         if( !IsCompleted )
         {
            IsCompleted = true;
            SetErrorInternal( errorMessage );
         }
      }

      private void SetErrorInternal( string errorMessage )
      {
         ErrorMessage = errorMessage ?? "Unknown error";

         try
         {
            _onCompleted?.Invoke( new TranslationResult( null, ErrorMessage ) );
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Error( e, "An error occurred while notifying of translation failure." );
         }
      }

      private void SetCompletedInternal( string translatedText )
      {
         TranslatedText = translatedText;

         try
         {
            _onCompleted?.Invoke( new TranslationResult( TranslatedText, null ) );
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Error( e, "An error occurred while notifying of translation completion." );
         }
      }

      public object Current => null;

      public bool MoveNext()
      {
         return !IsCompleted;
      }

      public void Reset()
      {
      }
   }
}
