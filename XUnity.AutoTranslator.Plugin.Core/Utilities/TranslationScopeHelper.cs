using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using XUnity.AutoTranslator.Plugin.Core;
using XUnity.AutoTranslator.Plugin.Core.Configuration;
using XUnity.AutoTranslator.Plugin.Core.Extensions;
using XUnity.AutoTranslator.Plugin.Core.Text;
using XUnity.AutoTranslator.Plugin.Utilities;
using XUnity.Common.Constants;
using XUnity.Common.Logging;
using XUnity.Common.Utilities;

namespace XUnity.AutoTranslator.Plugin.Utilities
{
   internal static class TranslationScopeHelper
   {
      public static int GetScope( object ui )
      {
         if( Settings.EnableTranslationScoping )
         {
            try
            {
               var GetScopeReturn = -1;
               if( ui is Component component && component )
               {
                  GetScopeReturn = GetScopeFromComponent( component );
               }
               if( GetScopeReturn != -1 )
			      {
				      return GetScopeReturn;
			      }
               else if( ui is GUIContent guic )
               {
                  return TranslationScopes.None;
               }
               else
               {
                  return GetActiveSceneId();
               }
            }
            catch( MissingMemberException e )
            {
               XuaLogger.AutoTranslator.Error( e, "A 'missing member' error occurred while retriving translation scope. Disabling translation scopes." );
               Settings.EnableTranslationScoping = false;
            }
         }
         return TranslationScopes.None;
      }

      private static int GetScopeFromComponent( Component component )
      {
         return component.gameObject.scene.buildIndex;
      }

      public static int GetActiveSceneId()
      {
         if( UnityFeatures.SupportsSceneManager )
         {
            return GetActiveSceneIdBySceneManager();
         }
         return GetActiveSceneIdByApplication();
      }

      private static int GetActiveSceneIdBySceneManager()
      {
         return SceneManager.GetActiveScene().buildIndex;
      }

      public static void RegisterSceneLoadCallback( Action<int> sceneLoaded )
      {
         UnityTypes.SceneManager_Methods.add_sceneLoaded( new Action<Scene, LoadSceneMode>( ( scene, mode ) => sceneLoaded( scene.buildIndex ) ) );
      }

      private static int GetActiveSceneIdByApplication()
      {
         return Application.loadedLevel;
      }
   }

}
