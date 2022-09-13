using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Web;

namespace Sockets
{
    public static class RequestProcessor
    {
        private const string HelloFilename = "hello.html";
        private static Encoding Encoding = Encoding.UTF8;
        
        internal static class DefaultHeaders
        {
            public static Header HtmlContentType => new("Content-Type", "text/html; charset=utf-8");

            public static Header SetCookie(string name, string value) => new("Set-Cookie", name + '=' + value);

            public static Header ContentLength(int length) => new("Content-Length", length.ToString());
        }

        public static byte[] Process(Request request)
        {
            var (head, body) = request switch
            {
                {RequestUri: "/hello.html"} => FromHelloFile(request, true),
                {RequestUri: "/"} => FromHelloFile(request, false),
                {RequestUri: "/groot.gif"} => FromFile("groot.gif"),
                {RequestUri: "/time.html"} => FromTimeFile(),
                _ => NotExisingPage()
            };

            return CreateResponseBytes(head, body);
        }

        private static (HeaderBuilder Head, byte[] Body) FromFile(string filename) =>
            FromFileContents(File.ReadAllBytes(filename));

        private static (HeaderBuilder Head, byte[] Body) FromHelloFile(Request request, bool tryToReplaceFromRequest)
        {
            const string name = nameof(name);
            const string greeting = "{{Hello}}";
            const string world = "{{World}}";

            var nameFromRequest = request.RequestParams[name];
            var encodedName = HttpUtility.HtmlEncode(nameFromRequest);
            var file = File.ReadAllText(HelloFilename);
            var replacedName = false;

            if (tryToReplaceFromRequest)
                file = file
                    .TryReplaceEncoded(greeting, request.RequestParams[nameof(greeting)])
                    .TryReplace(world, encodedName, out replacedName);

            if (!replacedName &&
                request.Headers.FirstOrDefault(h => h.Name == "Cookie" && h.Value.StartsWith(name)) is { } header)
                file = file.TryReplaceEncoded(world, header.Value[5..].FromBase64String());

            var result = FromFileContents(Encoding.GetBytes(file));

            if (replacedName)
                result.Head.Append(DefaultHeaders.SetCookie(name, nameFromRequest.ToBase64String()));

            return result;
        }

        private static (HeaderBuilder Head, byte[] Body) FromTimeFile()
        {
            var file = File.ReadAllText("time.template.html")
                .Replace("{{ServerTime}}", DateTime.Now.ToString());
            return FromFileContents(Encoding.GetBytes(file));
        }

        private static (HeaderBuilder Head, byte[] Body) FromFileContents(byte[] fileBytes)
        {
            var head = HeaderBuilder.ForOk()
                .Append(DefaultHeaders.HtmlContentType)
                .Append(DefaultHeaders.ContentLength(fileBytes.Length));
            return (head, fileBytes);
        }

        private static (HeaderBuilder Head, byte[] Body) NotExisingPage() => (HeaderBuilder.ForNotFound(), Array.Empty<byte>());

        // Собирает ответ в виде массива байт из байтов строки head и байтов body.
        private static byte[] CreateResponseBytes(HeaderBuilder head, byte[] body)
        {
            var headBytes = Encoding.ASCII.GetBytes(head.ToString());
            var responseBytes = new byte[headBytes.Length + body.Length];
            Array.Copy(headBytes, responseBytes, headBytes.Length);
            Array.Copy(body, 0, responseBytes, headBytes.Length, body.Length);
            return responseBytes;
        }
    }
}