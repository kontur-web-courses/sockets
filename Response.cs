using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Web;

namespace Sockets
{
    public class Response
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

        private readonly string address;
        private readonly NameValueCollection queryString;
        private string name;
        private string cookieName;
        private string cookie;
        
        public Response(Request request)
        {
            (address, queryString) = ParseUri(request.RequestUri);
            name = queryString["name"];
            if (name is not null)
            {
                var nameBytes = Encoding.UTF8.GetBytes(name);
                cookie = $"name={Convert.ToBase64String(nameBytes)}";
                return;
            }
            
            var cookieStr = request.Headers.FirstOrDefault(h => h.Name == "Cookie")?.Value;
            if (cookieStr is not null && ParseCookie(cookieStr).TryGetValue("name", out cookieName))
            {
                var cookieName64 = Convert.FromBase64String(cookieName);
                cookieName = Encoding.UTF8.GetString(cookieName64);
            }
        }
        
        private static (string, NameValueCollection) ParseUri(string uri)
        {
            if (!uri.Contains('?'))
                return (uri, new NameValueCollection());

            var fields = uri.Split('?');
            return (fields[0], HttpUtility.ParseQueryString(fields[1]));
        }

        private Dictionary<string, string> ParseCookie(string cookie)
        {
            return cookie
                .Split(';')
                .Select(s => s.Trim())
                .Select(s => s.Split('='))
                .ToDictionary(s => s[0], s => s[1]);
        }

        public byte[] ToBytes()
        {
            return address switch
            {
                "/" or "/hello.html" => HomePage(),
                "/groot.gif" => Gif("groot.gif"),
                "/time.html" => TimePage(),
                _ => NotFound(),
            };
        }

        private byte[] HomePage()
        {
            var greeting = queryString["greeting"] ?? "Hello";
            var name = this.name ?? cookieName;
            name = name is null ? "World" : HttpUtility.HtmlEncode(name);
            greeting = HttpUtility.HtmlEncode(greeting);
            return HomePage(greeting, name);
        }
        
        private byte[] HomePage(string greeting, string name)
            => FromFile("hello.html", ContentType.TextUtf8, 
                new Dictionary<string, Func<string>>
                {
                    {"Hello", () => greeting},
                    {"World", () => name}
                });

        private byte[] Gif(string filePath) 
            => FromFile(filePath, ContentType.Gif);

        private byte[] TimePage()
            => FromFile("time.template.html", ContentType.TextUtf8, 
                new Dictionary<string, Func<string>>
                {
                    {"ServerTime", () => DateTime.Now.ToString(CultureInfo.InvariantCulture)}
                });

        private byte[] FromFile(string filePath, string contentType)
            => GetResponse(StatusLine.Ok,contentType, File.ReadAllBytes(filePath));

        private byte[] FromFile(
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

        private byte[] GetResponse(string statusLine, string contentType, byte[] body)
        {
            var head = new StringBuilder(statusLine);
            if (cookie is not null)
            {
                head.Append($"Set-cookie: {cookie}\r\n");
            }
            head.Append($"Content-Type: {contentType}\r\n");
            head.Append($"Content-Length: {body.Length}\r\n\r\n");
            return CreateResponseBytes(head, body);
        }

        private byte[] NotFound()
        {
            var head = new StringBuilder(StatusLine.NotFound);
            return CreateResponseBytes(head, Array.Empty<byte>());
        }

        private byte[] CreateResponseBytes(StringBuilder head, byte[] body)
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
