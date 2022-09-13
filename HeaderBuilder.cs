using System.Text;
using static Sockets.Request;

namespace Sockets
{
    internal class HeaderBuilder
    {
        private const string Separator = "\r\n";

        private StringBuilder builder;

        private HeaderBuilder(StringBuilder builder) => this.builder = builder.Append(Separator);

        public static HeaderBuilder ForOk() => new(new("HTTP/1.1 200 OK"));

        public static HeaderBuilder ForNotFound() => new(new("HTTP/1.1 404 Not Found"));

        public HeaderBuilder Append(Header header)
        {
            builder.Append(header.Name).Append(':').Append(' ').Append(header.Value).Append(Separator);
            return this;
        }

        public override string ToString() => builder.Append(Separator).ToString();
    }
}