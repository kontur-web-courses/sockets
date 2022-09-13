using System.Web;

namespace Sockets
{
    internal static class StringExtensions
    {
        public static string TryReplaceEncoded(this string s, string oldPart, string newPart) =>
            newPart is { }
                ? s.Replace(oldPart, HttpUtility.HtmlEncode(newPart))
                : s;
    }
}