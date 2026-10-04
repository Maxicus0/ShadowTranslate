using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace XUnity.AutoTranslator.Plugin.Core
{
   public class UnityEngineTime : ITime
   {
      public float realtimeSinceStartup => Time.realtimeSinceStartup;

      public int frameCount => Time.frameCount;
   }

   public class TimeSupport
   {
      public static ITime Time { get; set; } = new UnityEngineTime();
   }

   public interface ITime
   {
      float realtimeSinceStartup { get; }

      int frameCount { get; }
   }
}
