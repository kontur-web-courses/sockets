using System;
using System.Text;

namespace Sockets.utils
{
    public static class StringBuilderExtension
    {
        public static StringBuilder AppendIfNotNull(this StringBuilder str, Object value, string newString)
        {
            return value is null ? str : str.Append(newString);
        } 
    }
}