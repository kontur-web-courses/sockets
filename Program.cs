using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mime;
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
        private const int ListeningPort = 11000;
        private static ManualResetEvent connectionEstablished = new(false);

        private class ReceivingState
        {
            public Socket ClientSocket;
            public const int BufferSize = 1024;
            public readonly byte[] Buffer = new byte[BufferSize];
            public readonly List<byte> ReceivedData = new();
        }

        public static void StartListening()
        {
            // Определяем IP-адрес, по которому будем принимать сообщения.
            // Для этого сначала получаем DNS-имя компьютера,
            // а из всех адресов выбираем первый попавшийся IPv4 адрес.
            var hostName = Dns.GetHostName();
            var ipHostEntry = Dns.GetHostEntry(hostName);
            var ipV4Address = ipHostEntry.AddressList
                .Where(address => address.AddressFamily == AddressFamily.InterNetwork)
                .OrderBy(address => address.ToString())
                .FirstOrDefault();
            if (ipV4Address == null)
            {
                Console.WriteLine(">>> Can't find IPv4 address for host");
                return;
            }

            // По выбранному IP-адресу будем слушать listeningPort.
            var ipEndPoint = new IPEndPoint(ipV4Address, ListeningPort);

            // Создаем TCP/IP сокет для приема соединений.
            var connectionSocket = new Socket(ipV4Address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

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
            var connectionSocket = (Socket)asyncResult.AsyncState;
            var clientSocket = connectionSocket.EndAccept(asyncResult);

            // Принимаем данные от клиента.
            Receive(clientSocket);
        }

        private static void Receive(Socket clientSocket)
        {
            // Создаем объект для callback.
            var receivingState = new ReceivingState
            {
                ClientSocket = clientSocket
            };

            // Начинаем асинхронно получать данные от клиента.
            // Передаем буфер, куда будут складываться полученные байты.
            clientSocket.BeginReceive(receivingState.Buffer, 0, ReceivingState.BufferSize, SocketFlags.None,
                ReceiveCallback, receivingState);
        }

        private static void ReceiveCallback(IAsyncResult asyncResult)
        {
            // Достаем клиентский сокет из параметра callback.
            var receivingState = (ReceivingState)asyncResult.AsyncState;
            var clientSocket = receivingState.ClientSocket;

            // Читаем данные из клиентского сокета.
            var bytesReceived = clientSocket.EndReceive(asyncResult);

            if (bytesReceived <= 0) return;

            // В буфер могли поместиться не все данные.
            // Все данные от клиента складываем в другой буфер - ReceivedData.
            receivingState.ReceivedData.AddRange(receivingState.Buffer.Take(bytesReceived));

            // Пытаемся распарсить Request из полученных данных.
            var receivedBytes = receivingState.ReceivedData.ToArray();
            var request = Request.StupidParse(receivedBytes);
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
                Console.WriteLine(
                    $">>> Received {receivedBytes.Length} bytes from {clientSocket.RemoteEndPoint}. Data:\n" +
                    Encoding.ASCII.GetString(receivedBytes));

                // Сформируем ответ.
                var responseBytes = ProcessRequest(request);

                // Отправим ответ клиенту.
                Send(clientSocket, responseBytes);
            }
        }

        private static NameValueCollection GetQueryParameters(string uri)
        {
            var parts = uri.Split("?");
            return parts.Length != 2 ? new NameValueCollection() : HttpUtility.ParseQueryString(parts[1]);
        }

        private static byte[] ProcessRequest(Request request)
        {
            var queryParameters = GetQueryParameters(request.RequestUri);
            queryParameters.Add("{{ServerTime}}", DateTime.Now.ToString(CultureInfo.InvariantCulture));
            queryParameters.Add("{{Hello}}", queryParameters.Get("greeting") ?? "Hello");
            queryParameters.Add("{{World}}", queryParameters.Get("name") ?? "World");

            return request.RequestUri.Split("?")[0] switch
            {
                "/hello.html" => CreateResponseBytes(new StringBuilder("HTTP/1.1 200 OK"),
                    Replace(File.ReadAllBytes("hello.html"), queryParameters), "text/html; charset=utf-8"),
                "/" => CreateResponseBytes(new StringBuilder("HTTP/1.1 200 OK"),
                    File.ReadAllBytes("hello.html"), "text/html; charset=utf-8"),
                "/groot.gif" => CreateResponseBytes(new StringBuilder("HTTP/1.1 200 OK"),
                    File.ReadAllBytes("groot.gif"), "image/gif"),
                "/time.html" => CreateResponseBytes(new StringBuilder("HTTP/1.1 200 OK"),
                    Replace(File.ReadAllBytes("time.template.html"), queryParameters),
                    "text/html; charset=utf-8"),
                _ => CreateResponseBytes(new StringBuilder("HTTP/1.1 404 Not Found"),
                    Array.Empty<byte>())
            };
        }

        private static byte[] Replace(byte[] bytes, NameValueCollection nameValueCollection)
        {
            var stringBuilder = new StringBuilder(Encoding.UTF8.GetString(bytes));
            foreach (var name in nameValueCollection.AllKeys)
            {
                if (!string.IsNullOrEmpty(name))
                    stringBuilder.Replace(name, nameValueCollection[name]);
            }

            return Encoding.UTF8.GetBytes(stringBuilder.ToString());
        }

        private static void AddHeaders(StringBuilder head, int bodyLength, string contentType)
        {
            if (contentType != null)
            {
                head.Append('\n');
                head.Append($"Content-Type: {contentType}");
            }

            head.Append('\n');
            head.Append($"Content-Length: {bodyLength}");
            head.Append("\r\n\r\n");
        }

        // Собирает ответ в виде массива байт из байтов строки head и байтов body.
        private static byte[] CreateResponseBytes(StringBuilder head, byte[] body, string contentType = null)
        {
            AddHeaders(head, body.Length, contentType);
            var headBytes = Encoding.ASCII.GetBytes(head.ToString());
            var responseBytes = new byte[headBytes.Length + body.Length];
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