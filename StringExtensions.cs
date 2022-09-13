using System.Web;

namespace Sockets
{
    internal static class StringExtensions
    {
        public static string TryReplaceEncoded(this string s, string oldPart, string newPart) =>
            s.TryReplaceEncoded(oldPart, newPart, out _);

        public static string TryReplaceEncoded(this string s, string oldPart, string newPart, out bool replaced) =>
            s.TryReplace(oldPart, HttpUtility.HtmlEncode(newPart), out replaced);
        
        public static string TryReplace(this string s, string oldPart, string newPart) =>
            s.TryReplace(oldPart, newPart, out _);

        public static string TryReplace(this string s, string oldPart, string newPart, out bool replaced)
        {
            replaced = !(newPart is null);
            return replaced
                ? s.Replace(oldPart, newPart)
                : s;
        }
    }
}