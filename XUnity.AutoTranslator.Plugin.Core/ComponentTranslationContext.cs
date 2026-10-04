namespace XUnity.AutoTranslator.Plugin.Core
{
   public class ComponentTranslationContext
   {
      internal ComponentTranslationContext( object component, string originalText )
      {
         Component = component;
         OriginalText = originalText;
      }

      public object Component { get; }

      public string OriginalText { get; }

      public string OverriddenTranslatedText { get; private set; }

      internal ComponentTranslationBehaviour Behaviour { get; private set; }

      public void ResetBehaviour()
      {
         Behaviour = ComponentTranslationBehaviour.Default;
         OverriddenTranslatedText = null;
      }

      public void OverrideTranslatedText( string translation )
      {
         Behaviour = ComponentTranslationBehaviour.OverrideTranslatedText;
         OverriddenTranslatedText = translation;
      }

      public void IgnoreComponent()
      {
         Behaviour = ComponentTranslationBehaviour.IgnoreComponent;
         OverriddenTranslatedText = null;
      }
   }
}
