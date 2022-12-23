using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web;

namespace Sockets
{
    public static class Response
    {
        private static class ContentType
        {
            public const string TextUtf8 = "text/html; charset=utf-8";
            public const string Gif = "image/gif";
        }

        private static class StatusLine
        {
            public const string Ok = "HTTP/1.1 200 OK\r\n";
            public const string NotFound = "HTTP/1.1 404 Not Found\r\n";
        }

        public static byte[] HomePage(NameValueCollection queryString)
        {
            var greeting = queryString["greeting"] ?? "Hello";
            var name = queryString["name"] ?? "World";
            greeting = HttpUtility.HtmlEncode(greeting);
            name = HttpUtility.HtmlEncode(name);
            return HomePage(greeting, name);
        }
        
        private static byte[] HomePage(string greeting, string name)
            => FromFile("hello.html", ContentType.TextUtf8, 
                new Dictionary<string, Func<string>>
                {
                    {"Hello", () => greeting},
                    {"World", () => name}
                });

        public static byte[] Gif(string filePath) 
            => FromFile(filePath, ContentType.Gif);

        public static byte[] TimePage()
            => FromFile("time.template.html", ContentType.TextUtf8, 
                new Dictionary<string, Func<string>>
                {
                    {"ServerTime", () => DateTime.Now.ToString(CultureInfo.InvariantCulture)}
                });

        private static byte[] FromFile(string filePath, string contentType)
            => GetResponse(StatusLine.Ok,contentType, File.ReadAllBytes(filePath));

        private static byte[] FromFile(
            string filePath,
            string contentType,
            Dictionary<string, Func<string>> toReplace)
        {
            var fileBytes = File.ReadAllBytes(filePath);
            var file = Encoding.UTF8.GetString(fileBytes);
            foreach (var (key, value) in toReplace)
            {
                file = file.Replace("{{" + key + "}}", value());
            }
            fileBytes = Encoding.UTF8.GetBytes(file);
            return GetResponse(StatusLine.Ok, contentType, fileBytes);
        }

        private static byte[] GetResponse(string statusLine, string contentType, byte[] body)
        {
            var head = new StringBuilder(statusLine);
            head.Append($"Content-Type: {contentType}\r\n");
            head.Append($"Content-Length: {body.Length}\r\n\r\n");
            return CreateResponseBytes(head, body);
        }

        public static byte[] NotFound()
        {
            var head = new StringBuilder(StatusLine.NotFound);
            return CreateResponseBytes(head, Array.Empty<byte>());
        }

        private static byte[] CreateResponseBytes(StringBuilder head, byte[] body)
        {
            byte[] headBytes = Encoding.ASCII.GetBytes(head.ToString());
            byte[] responseBytes = new byte[headBytes.Length + body.Length];
            Array.Copy(headBytes, responseBytes, headBytes.Length);
            Array.Copy(body, 0,
                responseBytes, headBytes.Length,
                body.Length);
            return responseBytes;
        }
    }
}
