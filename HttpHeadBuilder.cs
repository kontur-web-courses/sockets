using System.Text;

namespace Sockets
{
    public class HttpHeadBuilder
    {
        private readonly StringBuilder headersBuilder;
        private const string HttpHeader = "HTTP/1.1 ";

        private HttpHeadBuilder(string responseCode)
        {
            headersBuilder = new StringBuilder(HttpHeader);
            headersBuilder.Append(responseCode);
        }

        public static HttpHeadBuilder Ok() => new("200 OK\r\n");
        public static HttpHeadBuilder NotFound() => new("404 Not Found\r\n");

        public HttpHeadBuilder AddHeader(string header)
        {
            headersBuilder.Append(header);
            headersBuilder.Append("\r\n");
            return this;
        }
        
        public HttpHeadBuilder AddHeaders(params string[] headers)
        {
            foreach (var header in headers)
            {
                AddHeader(header);
            }

            return this;
        }

        public StringBuilder GetBuilder()
        {
            headersBuilder.Append("\r\n");
            return headersBuilder;
        }
    }
}