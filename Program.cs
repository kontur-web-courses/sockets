using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Web;

namespace Sockets
{
    class Program
    {
        static void Main(string[] args)
        {
            AsynchronousSocketListener.StartListening();
        }
    }
    public class RequestInfo
    {
        public string Url { get; private set; }
        public NameValueCollection Query { get; private set; }
        public Dictionary<string, string> Cookies { get; private set; }

        public RequestInfo(Request request)
        {
            var uri = request.RequestUri.Split('?', 2);
            Url = uri[0];
            Query = uri.Length > 1 ? HttpUtility.ParseQueryString(uri[1]) : null;
            Cookies = ParseCookies(request.Headers.Find(x => x.Name == "Cookie")?.Value);
        }

        private static Dictionary<string, string> ParseCookies(string cookiesString)
        {
            var cookieDictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(cookiesString))
                return cookieDictionary;

            var values = cookiesString.Split(';', StringSplitOptions.RemoveEmptyEntries);
            foreach (var parts in values.Select(c => c.Split('=', 2)))
            {
                var cookieName = parts[0].Trim();
                string cookieValue;

                if (parts.Length == 1)
                {
                    cookieValue = string.Empty;
                }
                else
                {
                    cookieValue = parts[1];
                }

                cookieDictionary[cookieName] = cookieValue;
            }

            return cookieDictionary;
        }
    }


    public class AsynchronousSocketListener
    {
        private const int listeningPort = 11000;
        private static ManualResetEvent connectionEstablished = new ManualResetEvent(false);

        private class ReceivingState
        {
            public Socket ClientSocket;
            public const int BufferSize = 1024;
            public readonly byte[] Buffer = new byte[BufferSize];
            public readonly List<byte> ReceivedData = new List<byte>();
        }

        public static void StartListening()
        {
            // Определяем IP-адрес, по которому будем принимать сообщения.
            // Для этого сначала получаем DNS-имя компьютера,
            // а из всех адресов выбираем первый попавшийся IPv4 адрес.
            string hostName = Dns.GetHostName();
            IPHostEntry ipHostEntry = Dns.GetHostEntry(hostName);
            IPAddress ipV4Address = ipHostEntry.AddressList
                .Where(address => address.AddressFamily == AddressFamily.InterNetwork)
                .OrderBy(address => address.ToString())
                .FirstOrDefault();
            if (ipV4Address == null)
            {
                Console.WriteLine(">>> Can't find IPv4 address for host");
                return;
            }
            // По выбранному IP-адресу будем слушать listeningPort.
            IPEndPoint ipEndPoint = new IPEndPoint(ipV4Address, listeningPort);

            // Создаем TCP/IP сокет для приема соединений.
            Socket connectionSocket = new Socket(ipV4Address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

            try
            {
                // Присоединяем сокет к выбранной конечной точке (IP-адресу и порту).
                connectionSocket.Bind(ipEndPoint);
                // Начинаем слушать, в очереди на установку соединений не более 100 клиентов.
                connectionSocket.Listen(100);

                // Принимаем входящие соединения.
                while (true)
                    Accept(connectionSocket);
            }
            catch (Exception e)
            {
                Console.WriteLine(">>> Got exception:");
                Console.WriteLine(e.ToString());
                Console.WriteLine(">>> ");
            }
        }

        private static void Accept(Socket connectionSocket)
        {
            // Сбрасываем состояние события установки соединения: теперь оно "не произошло".
            // Это событие используется для синхронизации потоков.
            connectionEstablished.Reset();

            // Начинаем слушать асинхронно, ожидая входящих соединений.
            // Вторым параметром передаем объект, который будет передан в callback.
            connectionSocket.BeginAccept(AcceptCallback, connectionSocket);
            Console.WriteLine($">>> Waiting for a connection to http://{connectionSocket.LocalEndPoint}");

            // Поток, в котором начали слушать connectionSocket будет ждать,
            // пока кто-нибудь не установит событие connectionEstablished.
            // Это произойдет в AcceptCallback, когда соединение будет установлено.
            connectionEstablished.WaitOne();
        }

        private static void AcceptCallback(IAsyncResult asyncResult)
        {
            // Соединение установлено, сигнализируем основному потоку,
            // чтобы он продолжил принимать соединения.
            connectionEstablished.Set();

            // Получаем сокет к клиенту, с которым установлено соединение.
            Socket connectionSocket = (Socket)asyncResult.AsyncState;
            Socket clientSocket = connectionSocket.EndAccept(asyncResult);

            // Принимаем данные от клиента.
            Receive(clientSocket);
        }

        private static void Receive(Socket clientSocket)
        {
            // Создаем объект для callback.
            ReceivingState receivingState = new ReceivingState();
            receivingState.ClientSocket = clientSocket;
            // Начинаем асинхронно получать данные от клиента.
            // Передаем буфер, куда будут складываться полученные байты.
            clientSocket.BeginReceive(receivingState.Buffer, 0, ReceivingState.BufferSize, SocketFlags.None,
                ReceiveCallback, receivingState);
        }

        private static void ReceiveCallback(IAsyncResult asyncResult)
        {
            // Достаем клиентский сокет из параметра callback.
            ReceivingState receivingState = (ReceivingState)asyncResult.AsyncState;
            Socket clientSocket = receivingState.ClientSocket;

            // Читаем данные из клиентского сокета.
            int bytesReceived = clientSocket.EndReceive(asyncResult);

            if (bytesReceived > 0)
            {
                // В буфер могли поместиться не все данные.
                // Все данные от клиента складываем в другой буфер - ReceivedData.
                receivingState.ReceivedData.AddRange(receivingState.Buffer.Take(bytesReceived));

                // Пытаемся распарсить Request из полученных данных.
                byte[] receivedBytes = receivingState.ReceivedData.ToArray();
                Request request = Request.StupidParse(receivedBytes);
                if (request == null)
                {
                    // request не распарсился, значит получили не все данные.
                    // Запрашиваем еще.
                    clientSocket.BeginReceive(receivingState.Buffer, 0, ReceivingState.BufferSize, SocketFlags.None,
                        ReceiveCallback, receivingState);
                }
                else
                {
                    // Все данные были получены от клиента.
                    // Для удобства выведем их на консоль.
                    Console.WriteLine($">>> Received {receivedBytes.Length} bytes from {clientSocket.RemoteEndPoint}. Data:\n" +
                        Encoding.ASCII.GetString(receivedBytes));

                    // Сформируем ответ.
                    byte[] responseBytes = ProcessRequest(request);

                    // Отправим ответ клиенту.
                    Send(clientSocket, responseBytes);
                }
            }
        }

        private static byte[] ProcessRequest(Request rawRequest)
        {
            var request = new RequestInfo(rawRequest);
            switch (request.Url)
            {
                case "/":
                case "/hello.html":
                    {
                        var html = new StringBuilder(File.ReadAllText("hello.html"))
                            .Replace("{{Hello}}", HttpUtility.HtmlEncode(request.Query?["greeting"] ?? "Hello"))
                            .Replace("{{World}}", HttpUtility.HtmlEncode(request.Query?["name"] ?? HttpUtility.UrlDecode(request.Cookies.GetValueOrDefault("name")) ?? "World"));
                        var bodyBytes = Encoding.UTF8.GetBytes(html.ToString());
                        return CreateResponseBytes(200,
                            contentType: "text/html; charset=utf-8", body: bodyBytes,
                            cookie: request.Query?["name"] is not null 
                                ? $"name={HttpUtility.UrlEncode(request.Query["name"])}"
                                : null);
                    }

                case "/groot.gif":
                    return CreateResponseBytes(200, "image/gif", File.ReadAllBytes("groot.gif"));
                case "/time.html":
                    {
                        var html = File.ReadAllText("time.template.html").Replace("{{ServerTime}}", DateTime.Now.ToString());
                        return CreateResponseBytes(200, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(html));
                    }

                default:
                    return CreateResponseBytes(404);
            }
        }

        private static byte[] CreateResponseBytes(int statusCode, string contentType = null, byte[] body = null, string cookie = null)
        {
            return statusCode switch
            {
                200 => CreateResponseBytes(
                    $"HTTP/1.1 200 OK\r\n" +
                    $"Content-Type: {contentType}; charset=utf-8\r\n" +
                    $"Content-Length: {body?.Length ?? 0}\r\n" +
                    $"Set-Cookie: {cookie ?? string.Empty}\r\n" +
                    $"\r\n", body),
                404 => CreateResponseBytes("HTTP/1.1 404 Not Found\r\n\r\n", null),
                _ => throw new NotImplementedException()
            };
        }

        // Собирает ответ в виде массива байт из байтов строки head и байтов body.
        private static byte[] CreateResponseBytes(string head, byte[] body = null)
        {
            byte[] headBytes = Encoding.ASCII.GetBytes(head);
            byte[] responseBytes = new byte[headBytes.Length + (body?.Length ?? 0)];
            Array.Copy(headBytes, responseBytes, headBytes.Length);
            if (body is not null)
                Array.Copy(body, 0,
                    responseBytes, headBytes.Length,
                    body.Length);
            return responseBytes;
        }

        private static void Send(Socket clientSocket, byte[] responseBytes)
        {
            Console.WriteLine(">>> Sending {0} bytes to client socket.", responseBytes.Length);
            // Начинаем асинхронно отправлять данные клиенту.
            clientSocket.BeginSend(responseBytes, 0, responseBytes.Length, SocketFlags.None,
                SendCallback, clientSocket);
        }

        private static void SendCallback(IAsyncResult asyncResult)
        {
            // Достаем клиентский сокет из параметра callback.
            Socket clientSocket = (Socket)asyncResult.AsyncState;
            try
            {
                // Завершаем отправку данных клиенту.
                int bytesSent = clientSocket.EndSend(asyncResult);
                Console.WriteLine(">>> Sent {0} bytes to client.", bytesSent);

                // Закрываем соединение.
                clientSocket.Shutdown(SocketShutdown.Both);
                clientSocket.Close();
                Console.WriteLine(">>> ");
            }
            catch (Exception e)
            {
                Console.WriteLine(">>> Got exception:");
                Console.WriteLine(e.ToString());
                Console.WriteLine(">>> ");
            }
        }
    }
}
