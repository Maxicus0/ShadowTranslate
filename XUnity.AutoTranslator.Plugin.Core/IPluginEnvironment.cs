using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ExIni;

namespace XUnity.AutoTranslator.Plugin.Core
{
   public interface IPluginEnvironment
   {
      string TranslationPath { get; }

      string ConfigPath { get; }

      IniFile Preferences { get; }

      void SaveConfig();

      bool AllowDefaultInitializeHarmonyDetourBridge { get; }
   }
}
