using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
 
namespace ChatTcpClient
{
    /// <summary>
    /// TCP-клиент чата (аналог Chat_Client из методички).
    /// Подключается к серверу, отдельный поток читает сообщения,
    /// главный поток отправляет введённые строки.
    /// </summary>
    class Program
    {
        private static string usr;
        private static TcpClient client;
        private static NetworkStream netStream;
        private static StreamWriter writer;
 
        static void Main(string[] args)
        {
            Console.Title = "TCP клиент чата";
            Console.Write("User name: ");
            usr = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(usr)) usr = "Гость";
 
            Console.Write("IP (Enter = 127.0.0.1): ");
            string ip = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(ip)) ip = "127.0.0.1";
 
            Console.Write("Port (Enter = 8888): ");
            string portText = Console.ReadLine();
            int port = string.IsNullOrWhiteSpace(portText) ? 8888 : Convert.ToInt32(portText);
 
            Connect(ip, port);
        }
 
        static void Connect(string ip, int port)
        {
            IPEndPoint ipe = new IPEndPoint(IPAddress.Parse(ip), port);
            client = new TcpClient();
            try
            {
                client.Connect(ipe);
                netStream = client.GetStream();
                writer = new StreamWriter(netStream, new UTF8Encoding(false));
                writer.AutoFlush = true;
 
                Thread receiveThread = new Thread(ReceiveData);
                receiveThread.IsBackground = true;
                receiveThread.Start();
 
                Console.WriteLine(
                    "Connected! Сервер {0}. Введите сообщение и нажмите Enter " +
                    "(/exit — выход).", ipe);
                Console.Write("> ");
                SendMessage();
            }
            catch (SocketException ex)
            {
                Console.WriteLine("Не удалось подключиться к {0}: {1}", ipe, ex.Message);
            }
        }
 
        static void SendMessage()
        {
            while (true)
            {
                string text = Console.ReadLine();
                if (text == null) break;
                if (text.Trim() == "/exit") break;
 
                try
                {
                    writer.WriteLine(usr + ": " + text);
                    Console.Write("> ");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Ошибка отправки: " + ex.Message);
                    break;
                }
            }
 
            try { client.Close(); } catch { }
        }
 
        /// <summary>Потоковая функция: постоянное чтение сообщений сервера.</summary>
        static void ReceiveData()
        {
            StreamReader reader = new StreamReader(netStream, new UTF8Encoding(false));
            try
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    Console.WriteLine(line);
                }
                Console.WriteLine("Сервер закрыл соединение.");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Соединение прервано: " + ex.Message);
            }
        }
    }
}
