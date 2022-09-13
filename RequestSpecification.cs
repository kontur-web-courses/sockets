using System.Collections.Generic;
using System.Text;
using FluentAssertions;
using NUnit.Framework;

namespace Sockets
{
    [TestFixture]
    public class RequestSpecification
    {
        private const string RequestLine = "GET /users/1 HTTP/1.1\r\n";
        private const string Header1 = "UnknownHeader1: Value1\r\n";
        private const string Header2 = "UnknownHeader2: Value2\r\n";
        private const string Separator = "\r\n";

        private Request.Header requestHeader1;
        private Request.Header requestHeader2;

        private Request CreateRequest()
        {
            return new Request
            {
                Method = "GET",
                HttpVersion = "HTTP/1.1",
                RequestUri = "/users/1",
                Headers = new List<Request.Header>(),
                MessageBody = new byte[0]
            };
        }

        [SetUp]
        public void SetUp()
        {
            requestHeader1 = new Request.Header("UnknownHeader1", "Value1");
            requestHeader2 = new Request.Header("UnknownHeader2", "Value2");
        }

        [Test]
        public void Parse_ShouldReturnNull_WhenEmptyInput()
        {
            var actual = Request.StupidParse(Encoding.ASCII.GetBytes(""));
            actual.Should().BeNull();
        }

        [Test]
        public void Parse_ShouldSucceed_WhenOnlyRequestLine()
        {
            var expected = CreateRequest();
            var actual = Request.StupidParse(Encoding.ASCII.GetBytes(RequestLine + Separator));
            actual.Should().BeEquivalentTo(expected);
        }

        [Test]
        public void Parse_ShouldReturnNull_WhenNotFinishedRequestLine()
        {
            var actual = Request.StupidParse(Encoding.ASCII.GetBytes("GET http://host/users/1 HTTP/1.1"));
            actual.Should().BeNull();
        }

        [Test]
        public void Parse_ShouldReturnNull_WhenRequestLineWithoutSeparator()
        {
            var actual = Request.StupidParse(Encoding.ASCII.GetBytes(RequestLine));
            actual.Should().BeNull();
        }

        [Test]
        public void Parse_ShouldSucceed_WhenRequestLineAndSingleHeader()
        {
            var input = RequestLine + Header1 + Separator;
            var expected = CreateRequest();
            expected.Headers.Add(requestHeader1);

            var actual = Request.StupidParse(Encoding.ASCII.GetBytes(input));
            actual.Should().BeEquivalentTo(expected);
        }

        [Test]
        public void Parse_ShouldSucceed_WhenRequestLineAndHeaders()
        {
            var input = RequestLine + Header1 + Header2 + Separator;
            var expected = CreateRequest();
            expected.Headers.Add(requestHeader1);
            expected.Headers.Add(requestHeader2);

            var actual = Request.StupidParse(Encoding.ASCII.GetBytes(input));
            actual.Should().BeEquivalentTo(expected);
        }

        [Test]
        public void Parse_ShouldReturnNull_WhenNotFinishedHeader()
        {
            var input = RequestLine + "UnknownHeader1: Value1";

            var actual = Request.StupidParse(Encoding.ASCII.GetBytes(input));
            actual.Should().BeNull();
        }

        [Test]
        public void Parse_ShouldReturnNull_WhenHeadersWithoutSeparator()
        {
            var input = RequestLine + Header1 + Header2;

            var actual = Request.StupidParse(Encoding.ASCII.GetBytes(input));
            actual.Should().BeNull();
        }

        [Test]
        public void Parse_ShouldSucceed_WhenBodyWithoutContentLength()
        {
            var input = RequestLine + Header1 + Header2 + Separator + "just text body";
            var expected = CreateRequest();
            expected.Headers.Add(requestHeader1);
            expected.Headers.Add(requestHeader2);
            expected.MessageBody = Encoding.ASCII.GetBytes("just text body");

            var actual = Request.StupidParse(Encoding.ASCII.GetBytes(input));
            actual.Should().BeEquivalentTo(expected);
        }

        [Test]
        public void Parse_ShouldSucceed_WhenBodyWithContentLength()
        {
            var input = RequestLine +
                        Header1 +
                        Header2 +
                        "Content-Length: 14\r\n" +
                        Separator +
                        "just text body";
            var expected = CreateRequest();
            expected.Headers.Add(requestHeader1);
            expected.Headers.Add(requestHeader2);
            expected.Headers.Add(new Request.Header("Content-Length", "14"));
            expected.MessageBody = Encoding.ASCII.GetBytes("just text body");

            var actual = Request.StupidParse(Encoding.ASCII.GetBytes(input));
            actual.Should().BeEquivalentTo(expected);
        }

        [Test]
        public void Parse_ShouldTrimBody_WhenContentLengthLessThanBody()
        {
            var input = RequestLine +
                        Header1 +
                        Header2 +
                        "Content-Length: 10\r\n" +
                        Separator +
                        "just text body";
            var expected = CreateRequest();
            expected.Headers.Add(requestHeader1);
            expected.Headers.Add(requestHeader2);
            expected.Headers.Add(new Request.Header("Content-Length", "10"));
            expected.MessageBody = Encoding.ASCII.GetBytes("just text ");

            var actual = Request.StupidParse(Encoding.ASCII.GetBytes(input));
            actual.Should().BeEquivalentTo(expected);
        }

        [Test]
        public void Parse_ShouldReturnNull_WhenContentLengthMoreThanBody()
        {
            var input = RequestLine +
                        Header1 +
                        Header2 +
                        "Content-Length: 15\r\n" +
                        Separator +
                        "just text body";

            var actual = Request.StupidParse(Encoding.ASCII.GetBytes(input));
            actual.Should().BeNull();
        }
    }
}