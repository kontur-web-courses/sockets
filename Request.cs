using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using System.Web;

namespace Sockets
{
    public class Request
    {
        public const string HttpLineSeparator = "\r\n";

        public string Method;
        public string RequestUri;
        public NameValueCollection RequestParams;
        public string HttpVersion;
        public List<Header> Headers;
        public byte[] MessageBody;

        public record Header(string Name, string Value);

        public static class DefaultHeaders
        {
            public static Header HtmlContentType => new("Content-Type", "text/html; charset=utf-8");

            public static Header SetCookie(string name, string value) => new("Set-Cookie", name + '=' + value);

            public static Header ContentLength(int length) => new("Content-Length", length.ToString());
        }

        // Структура http-запроса: https://www.w3.org/Protocols/rfc2616/rfc2616-sec5.html
        public static Request StupidParse(byte[] requestBytes)
        {
            var requestString = Encoding.ASCII.GetString(requestBytes);
            var requestLine = ParseRequestLine(requestString, out int readCharsCount);
            var headers = ParseHeaders(requestString, ref readCharsCount);

            if (headers == null) return null;

            var readBytesCount = Encoding.ASCII.GetBytes(requestString.Substring(0, readCharsCount)).Length;
            var messageBody = ParseMessageBody(requestBytes, headers, readBytesCount);

            if (messageBody == null) return null;

            return new Request
            {
                Method = requestLine.Method,
                RequestUri = requestLine.RequestUri.Uri,
                RequestParams = requestLine.RequestUri.QueryParams,
                HttpVersion = requestLine.HttpVersion,
                Headers = headers,
                MessageBody = messageBody
            };
        }

        private static RequestLine ParseRequestLine(string requestString, out int readCharsCount)
        {
            readCharsCount = 0;
            var lineEnd = requestString.IndexOf(HttpLineSeparator, StringComparison.InvariantCulture);

            if (lineEnd < 0) return null;

            readCharsCount = lineEnd + 2;
            var requestLineString = requestString.Substring(0, lineEnd);
            var requestLineParts = requestLineString.Split(' ');
            var requestUriParts = requestLineParts[1].Split('?');
            var @params = HttpUtility.ParseQueryString(requestUriParts.Length == 2 ? requestUriParts[1] : string.Empty);
            return new RequestLine(requestLineParts[0], new RequestUriParts(requestUriParts[0], @params), requestLineParts[2]);
        }

        private static List<Header> ParseHeaders(string requestString, ref int readCharsCount)
        {
            var lineStart = readCharsCount;
            var headers = new List<Header>();

            while (true)
            {
                if (lineStart >= requestString.Length) return null;
                
                var lineEnd = requestString.IndexOf(HttpLineSeparator, lineStart, StringComparison.InvariantCulture);
                
                if (lineEnd < 0) return null;
                
                if (lineStart == lineEnd) break;

                var headerString = requestString.Substring(lineStart, lineEnd - lineStart);
                var headerParts = headerString.Split(':');
                headers.Add(new Header(headerParts[0].Trim(), headerParts[1].Trim()));

                lineStart = lineEnd + HttpLineSeparator.Length;
            }

            readCharsCount = lineStart + HttpLineSeparator.Length;
            return headers;
        }

        private static byte[] ParseMessageBody(byte[] requestBytes, List<Header> headers, int readBytesCount)
        {
            var contentLength = FindContentLength(headers);
            var messageBodyLength = contentLength.HasValue ? contentLength.Value : requestBytes.Length - readBytesCount;
            
            if (messageBodyLength > requestBytes.Length - readBytesCount) return null;

            var messageBody = new byte[messageBodyLength];
            Array.Copy(requestBytes, readBytesCount, messageBody, 0, messageBody.Length);
            return messageBody;
        }

        private static int? FindContentLength(List<Header> headers)
        {
            var contentLengthHeader = headers.FirstOrDefault(
                h => string.Equals(h.Name, "Content-Length", StringComparison.InvariantCultureIgnoreCase));
            return contentLengthHeader != null ? (int?)int.Parse(contentLengthHeader.Value) : null;
        }

        private record RequestLine(string Method, RequestUriParts RequestUri, string HttpVersion);

        private record RequestUriParts(string Uri, NameValueCollection QueryParams);
    }
}
