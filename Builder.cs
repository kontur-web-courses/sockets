using System.Text;
using static Sockets.Request;

namespace Sockets
{
    class Builder
    {
        private const string Separator = "\r\n";

        private StringBuilder builder;

        private Builder(StringBuilder builder) => this.builder = builder.Append(Separator);

        public static Builder ForOk() => new(new("HTTP/1.1 200 OK"));

        public static Builder ForNotFound() => new(new("HTTP/1.1 404 Not Found"));

        public Builder Append(Header header)
        {
            builder.Append(header.Name).Append(':').Append(' ').Append(header.Value).Append(Separator);
            return this;
        }

        public override string ToString() => builder.Append(Separator).ToString();
    }
}