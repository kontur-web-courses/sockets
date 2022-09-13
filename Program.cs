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

        private static byte[] ProcessRequest(Request request)
        {
            var separatedRelativeUri = request.RequestUri.Split('?');
            var pagePath = separatedRelativeUri.First();
            var queryParams = HttpUtility.ParseQueryString(separatedRelativeUri.Last());
            var cookie = ParseCookie(request.Headers.FirstOrDefault(h => h.Name == "Cookie")?.Value);
            var name = queryParams["name"] ?? cookie["name"] ??  "{{World}}";
            var greeting = queryParams["greeting"] ?? cookie["greeting"] ?? "{{Hello}}";
            
            switch (pagePath)
            {
                case "/":
                case "/hello.html":
                    return CreateResponseBytes(
                        GetOkResponseHeader(ContentTypes.Utf8Html, queryParams),
                        GetHelloPage(name, greeting));
                case "/groot.gif":
                    return CreateResponseBytes(
                        GetOkResponseHeader(ContentTypes.Gif), File.ReadAllBytes("groot.gif"));
                case "/time.html":
                    return CreateResponseBytes(
                        GetOkResponseHeader(ContentTypes.Utf8Html), GetDynamicServerTimePage());
                default:
                    return Encoding.ASCII.GetBytes("HTTP/1.1 404 Not Found"); 
            }
        }

        private static NameValueCollection ParseCookie(string cookieValue)
        {
            var cookieCollection = new NameValueCollection();
            foreach (var keyValuePair in cookieValue.Split(";"))
            {
                cookieCollection.Add(
                    keyValuePair.Split('=').First().Trim(),
                    keyValuePair.Split('=').Last().Trim());
            }

            return cookieCollection;
        }

        private static byte[] GetHelloPage(string name, string greeting)
        {
            var helloPage = Encoding.UTF8.GetString(File.ReadAllBytes("hello.html"));
            var replacedGreetingPage = helloPage.Replace("{{Hello}}", HttpUtility.HtmlEncode(greeting));
            var replacedNamePage = replacedGreetingPage.Replace("{{World}}", HttpUtility.HtmlEncode(name));
            return Encoding.UTF8.GetBytes(replacedNamePage);
        }
        
        private static byte[] GetDynamicServerTimePage()
        {
            var timePage = Encoding.UTF8.GetString(File.ReadAllBytes("time.template.html"));
            var currentTimePage = timePage.Replace("{{ServerTime}}", $"{DateTime.Now}");
            return Encoding.UTF8.GetBytes(currentTimePage);
        }

        private static void SetCookieHeader(StringBuilder header, NameValueCollection queryParams)
        {
            var nameParam = queryParams["name"];
            var greetingParam = queryParams["greeting"];
            if (nameParam != null)
                SetHeader(header, $"Set-Cookie: name={nameParam}");
            if (greetingParam != null)
                SetHeader(header, $"Set-Cookie: greeting={greetingParam}");
        }
        
        private static StringBuilder GetOkResponseHeader(string contentType, NameValueCollection queryParams=null)
        {
            var header = new StringBuilder();
            SetHeader(header, "HTTP/1.1 200 OK");
            SetHeader(header, $"Content-Type: {contentType}");
            SetHeader(header, "Content-Length: 100500");
            SetCookieHeader(header, queryParams ?? new NameValueCollection());
            header.Append("\r\n");
            return header;
        }

        private static void SetHeader(StringBuilder header, string value) => header.Append($"{value}\r\n"); 

        // Собирает ответ в виде массива байт из байтов строки head и байтов body.
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
