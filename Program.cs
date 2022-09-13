using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Web;
using static Sockets.Request;

namespace Sockets
{
    internal class Program
    {
        static void Main(string[] args) => AsynchronousSocketListener.StartListening();
    }

    internal class AsynchronousSocketListener
    {
        private const int listeningPort = 11000;
        private const string HelloFilename = "hello.html";
        private static ManualResetEvent connectionEstablished = new(false);

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
            var ipEndPoint = new IPEndPoint(ipV4Address, listeningPort);

            // Создаем TCP/IP сокет для приема соединений.
            var connectionSocket = new Socket(ipV4Address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

            try
            {
                // Присоединяем сокет к выбранной конечной точке (IP-адресу и порту).
                connectionSocket.Bind(ipEndPoint);
                // Начинаем слушать, в очереди на установку соединений не более 100 клиентов.
                connectionSocket.Listen(100);

                // Принимаем входящие соединения.
                while (true) Accept(connectionSocket);
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
            var receivingState = new ReceivingState() {ClientSocket = clientSocket};

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

            if (bytesReceived > 0)
            {
                // В буфер могли поместиться не все данные.
                // Все данные от клиента складываем в другой буфер - ReceivedData.
                receivingState.ReceivedData.AddRange(receivingState.Buffer.Take(bytesReceived));

                // Пытаемся распарсить Request из полученных данных.
                var receivedBytes = receivingState.ReceivedData.ToArray();

                if (Request.StupidParse(receivedBytes) is { } request)
                {
                    // Все данные были получены от клиента.
                    // Для удобства выведем их на консоль.
                    Console.WriteLine($">>> Received {receivedBytes.Length} bytes from {clientSocket.RemoteEndPoint}. Data:\n" +
                        Encoding.ASCII.GetString(receivedBytes));

                    // Сформируем ответ.
                    var responseBytes = ProcessRequest(request);

                    // Отправим ответ клиенту.
                    Send(clientSocket, responseBytes);
                }
                else
                {
                    // request не распарсился, значит получили не все данные.
                    // Запрашиваем еще.
                    clientSocket.BeginReceive(receivingState.Buffer, 0, ReceivingState.BufferSize, SocketFlags.None,
                        ReceiveCallback, receivingState);
                }
            }
        }

        private static byte[] ProcessRequest(Request request)
        {
            var (head, body) = request switch
            {
                {RequestUri: "/hello.html"} => FromHelloFile(request, true),
                {RequestUri: "/"} => FromHelloFile(request, false),
                {RequestUri: "/groot.gif"} => FromFile("groot.gif"),
                {RequestUri: "/time.html"} => FromTimeFile(),
                _ => NotExisingPage()
            };

            return CreateResponseBytes(head, body);
        }

        private static (Builder Head, byte[] Body) FromFile(string filename) =>
            FromFileContents(File.ReadAllBytes(filename));

        private static (Builder Head, byte[] Body) FromHelloFile(Request request, bool tryToReplaceFromRequest)
        {
            const string name = nameof(name);
            const string greeting = "{{Hello}}";
            const string world = "{{World}}";

            var nameFromRequest = request.RequestParams[name];
            var encodedName = HttpUtility.HtmlEncode(nameFromRequest);
            var file = File.ReadAllText(HelloFilename);
            var replacedName = false;

            if (tryToReplaceFromRequest)
                file = file
                    .TryReplaceEncoded(greeting, request.RequestParams[nameof(greeting)])
                    .TryReplace(world, encodedName, out replacedName);

            if (!replacedName &&
                request.Headers.FirstOrDefault(h => h.Name == "Cookie" && h.Value.StartsWith(name)) is { } header)
            {
                var value = header.Value[5..];
                file = file.TryReplaceEncoded(world, Encoding.UTF8.GetString(Convert.FromBase64String(value)));
            }
                       
            var result = FromFileContents(Encoding.UTF8.GetBytes(file));

            if (replacedName)
            {
                var a = Convert.ToBase64String(Encoding.UTF8.GetBytes(nameFromRequest));
                result.Head.Append(DefaultHeaders.SetCookie(name, a));
            }

            return result;
        }

        private static (Builder Head, byte[] Body) FromTimeFile()
        {
            var file = File.ReadAllText("time.template.html")
                .Replace("{{ServerTime}}", DateTime.Now.ToString());
            return FromFileContents(Encoding.UTF8.GetBytes(file));
        }

        private static (Builder Head, byte[] Body) FromFileContents(byte[] fileBytes)
        {
            var head = Builder.ForOk()
                .Append(DefaultHeaders.HtmlContentType)
                .Append(DefaultHeaders.ContentLength(fileBytes.Length));
            return (head, fileBytes);
        }

        private static (Builder Head, byte[] Body) NotExisingPage() => (Builder.ForNotFound(), Array.Empty<byte>());

        // Собирает ответ в виде массива байт из байтов строки head и байтов body.
        private static byte[] CreateResponseBytes(Builder head, byte[] body)
        {
            var headBytes = Encoding.ASCII.GetBytes(head.ToString());
            var responseBytes = new byte[headBytes.Length + body.Length];
            Array.Copy(headBytes, responseBytes, headBytes.Length);
            Array.Copy(body, 0, responseBytes, headBytes.Length, body.Length);
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
            var clientSocket = (Socket)asyncResult.AsyncState;
            try
            {
                // Завершаем отправку данных клиенту.
                var bytesSent = clientSocket.EndSend(asyncResult);
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

        private class ReceivingState
        {
            public Socket ClientSocket;
            public const int BufferSize = 1024;
            public readonly byte[] Buffer = new byte[BufferSize];
            public readonly List<byte> ReceivedData = new List<byte>();
        }
    }
}