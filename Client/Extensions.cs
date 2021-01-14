using System;

namespace Client
{
    public static class Extensions
    {
        public static void ExtractContent(this string str, string begin, out string content, string end = null)
        {
            content = str.Split(new string[] { begin }, StringSplitOptions.None)[1];

            if (end != null)
                content = content.Split(new string[] { end }, StringSplitOptions.None)[0];
        }
    }
}
