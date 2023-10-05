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
using Sockets.utils;

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

        private static string GetBodyWithReplacedElements(string body, Dictionary<string, string> replacementPairs)
        {
            foreach (var pair in replacementPairs)
            {
                if (pair.Value is not null)
                {
                    body = body.Replace(
                        pair.Key,
                        HttpUtility.HtmlEncode(pair.Value)
                    );
                }
            }

            return body;
        }
        
        private static byte[] SendHelloHtml(NameValueCollection parameters, Dictionary<string, string> cookie)
        {
            var body = File.ReadAllBytes("hello.html");
                
            var bodyString = Encoding.UTF8.GetString(body);

            var name = parameters["name"];
            if (name is null && cookie.TryGetValue("name", out var value)) 
                name = value;

            var greeting = parameters["greeting"];

            bodyString = GetBodyWithReplacedElements(bodyString, new Dictionary<string, string>
            {
                { "{{World}}", name },
                { "{{Hello}}", greeting }
            });
                
            body = Encoding.UTF8.GetBytes(bodyString);

            var currentCookie = name is null
                ? null
                : new Dictionary<string, string> { { "name", name } };

            var head = CreateSuccessHead(
                body.Length,
                "text/html; charset=utf-8",
                currentCookie
                );

            return CreateResponseBytes(head, body);
        }

        private static StringBuilder CreateSuccessHead(int bodyLength, string contentType = null, 
            Dictionary<string, string> cookie = null)
        {
            return new StringBuilder()
                .Append("HTTP/1.1 200 OK\r\n")
                .Append($"Content-Length: {bodyLength}\r\n")
                .AppendIfNotNull(contentType ,$"Content-Type: {contentType}\r\n")
                .AppendIfNotNull(cookie, ConvertDictionaryToSetCookie(cookie))
                .Append("\r\n");
        }

        private static string ConvertDictionaryToSetCookie(Dictionary<string, string> cookie)
        {
            if (cookie is null) return string.Empty;
            
            var cookieArray = cookie
                .Select(pair => $"{pair.Key}={HttpUtility.UrlEncode(pair.Value)}")
                .ToArray();

            return "Set-Cookie: " + string.Join("; ", cookieArray) + "\r\n";
        }
        
        private static byte[] SendImage(string path)
        {
            var body = File.ReadAllBytes(path);
            
            var head = CreateSuccessHead(body.Length ,"image/gif");

            return CreateResponseBytes(head, body);
        }

        private static byte[] SendTimeHtml()
        {
            var template = Encoding.UTF8.GetString(
                File.ReadAllBytes("time.template.html")
                );

            template = template.Replace(
                "{{ServerTime}}", 
                DateTime.Now.ToString(CultureInfo.InvariantCulture)
                );
            
            var body = Encoding.UTF8.GetBytes(template);

            var head = CreateSuccessHead(
                body.Length,
                "text/html; charset=utf-8"
                );
            
            return CreateResponseBytes(head, body);
        }

        private static byte[] SendNotFoundHtml()
        {
            var head = CreateSuccessHead(0);

            return CreateResponseBytes(head, Array.Empty<byte>());
        }

        private static Dictionary<string, string> GetCookie(IEnumerable<Request.Header> headers)
        {
            var cookieHeader = headers
                .FirstOrDefault(header => header.Name == "Cookie");
            
            return cookieHeader is null 
                ? new Dictionary<string, string>() 
                : cookieHeader.Value.Split("; ")
                    .Select(el => el.Split('='))
                    .ToDictionary(el => el[0], el => HttpUtility.UrlDecode(el[1]));
        }

        private static string GetPath(string uri)
        {
            return uri.Split("?")[0];
        }

        private static NameValueCollection GetParameters(string uri)
        {
            var uriSplit = uri.Split("?");
            
            return uriSplit.Length > 1 
                ? HttpUtility.ParseQueryString(uriSplit[1]) 
                : new NameValueCollection();
        }
        
        private static byte[] ProcessRequest(Request request)
        {
            var uri = request.RequestUri;
            
            var path = GetPath(uri);
            var parameters = GetParameters(uri);
            var cookie = GetCookie(request.Headers);

            return path switch
            {
                "/" or "/hello.html" => SendHelloHtml(parameters, cookie),
                "/groot.gif" => SendImage("groot.gif"),
                "/time.html" => SendTimeHtml(),
                _ => SendNotFoundHtml()
            };
        }

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
