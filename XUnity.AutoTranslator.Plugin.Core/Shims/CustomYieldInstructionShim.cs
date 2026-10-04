using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using XUnity.AutoTranslator.Plugin.Core.Utilities;
using XUnity.Common.Constants;

namespace XUnity.AutoTranslator.Plugin.Core.Shims
{
   public abstract class CustomYieldInstructionShim : IEnumerator
   {
      private float? _startTime;

      protected CustomYieldInstructionShim()
      {
      }

      internal bool DetermineKeepWaiting()
      {
         if( !keepWaiting || IsTimedOut ) return false;

         if( InGameTimeout.HasValue )
         {
            if( !_startTime.HasValue )
            {
               _startTime = TimeSupport.Time.realtimeSinceStartup;
            }

            var startTime = _startTime.Value;
            var time = TimeSupport.Time.realtimeSinceStartup - startTime;

            if( time > InGameTimeout )
            {
               IsTimedOut = true;

               return false;
            }
         }

         return true;
      }

      public bool MoveNext()
      {
         return DetermineKeepWaiting();
      }

      public void Reset()
      {
      }

      public object Current
      {
         get
         {
            return null;
         }
      }

      public abstract bool keepWaiting { get; }

      public float? InGameTimeout { get; set; }

      public bool IsTimedOut { get; set; }

      public IEnumerator GetSupportedEnumerator()
      {
         if( UnityFeatures.SupportsCustomYieldInstruction )
         {
            yield return this;
         }
         else
         {
            while( DetermineKeepWaiting() )
            {
               yield return CoroutineHelper.CreateWaitForSeconds( 0.2f );
            }
         }
      }
   }
}
