using System.Text;

namespace XUnity.AutoTranslator.Plugin.Core.AssetRedirection
{
   public class TextAndEncoding
   {
      public TextAndEncoding( string text, Encoding encoding )
      {
         Text = text;
         Encoding = encoding;
      }

      public TextAndEncoding( byte[] bytes, Encoding encoding )
      {
         Bytes = bytes;
         Encoding = encoding;
      }

      public TextAndEncoding( byte[] bytes )
      {
         Bytes = bytes;
      }

      public string Text { get; }

      public byte[] Bytes { get; }

      public Encoding Encoding { get; }
   }
}
