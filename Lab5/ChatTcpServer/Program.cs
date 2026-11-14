using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
 
namespace ChatTcpServer
{
    /// <summary>
    /// TCP-сервер чата (аналог Server_Chat из методички).
    /// Принимает подключения, каждому клиенту — свой поток чтения,
    /// каждое сообщение рассылается всем остальным участникам.
    /// Протокол: одна строка = одно сообщение (StreamReader/StreamWriter).
    /// </summary>
    class Program
    {
        private const int Port = 8888;
 
        private static TcpListener tcpListener;
        private static Thread listenThread;
        private static readonly List<TcpClient> clients = new List<TcpClient>();
        private static readonly List<StreamWriter> writers = new List<StreamWriter>();
        private static readonly object sync = new object();
 
        static void Main(string[] args)
        {
            Console.Title = "TCP сервер чата";
            try
            {
                tcpListener = new TcpListener(IPAddress.Any, Port);
                tcpListener.Start();
                Console.WriteLine("Server Started! Порт {0}. Ожидание клиентов...", Port);
 
                listenThread = new Thread(ListenThread);
                listenThread.IsBackground = true;
                listenThread.Start();
 
                Console.WriteLine("Сервер работает. Для остановки закройте окно.");
                Console.ReadLine();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Ошибка запуска сервера: " + ex.Message);
            }
            finally
            {
                Disconnect();
            }
        }
 
        /// <summary>Принимает подключения, создаёт поток чтения для клиента.</summary>
        static void ListenThread()
        {
            int id = 0;
            while (true)
            {
                TcpClient client = tcpListener.AcceptTcpClient();
                id++;
                NetworkStream stream = client.GetStream();
                StreamReader reader = new StreamReader(stream, new UTF8Encoding(false));
                StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false));
                writer.AutoFlush = true;
 
                lock (sync)
                {
                    clients.Add(client);
                    writers.Add(writer);
                }
 
                Console.WriteLine("Клиент #{0} подключён: {1}. Всего клиентов: {2}.",
                    id, client.Client.RemoteEndPoint, clients.Count);
 
                Thread clientThread = new Thread(ClientReceive);
                clientThread.IsBackground = true;
                clientThread.Start(new object[] { id, client, reader, writer });
            }
        }
 
        /// <summary>Читает строки от одного клиента и пересылает их остальным.</summary>
        static void ClientReceive(object state)
        {
            object[] args = (object[])state;
            int id = (int)args[0];
            TcpClient client = (TcpClient)args[1];
            StreamReader reader = (StreamReader)args[2];
            StreamWriter writer = (StreamWriter)args[3];
 
            try
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    Console.WriteLine("{0}: {1}", "клиент #" + id, line);
                    SendMessageToClients(line, writer);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Клиент #{0}: связь потеряна ({1}).", id, ex.Message);
            }
            finally
            {
                Console.WriteLine("Клиент #{0} отключился.", id);
                lock (sync)
                {
                    clients.Remove(client);
                    writers.Remove(writer);
                }
                try { client.Close(); } catch { }
            }
        }
 
        /// <summary>Рассылка строки всем клиентам, кроме отправителя.</summary>
        static void SendMessageToClients(string message, StreamWriter from)
        {
            lock (sync)
            {
                foreach (StreamWriter w in writers)
                {
                    if (ReferenceEquals(w, from)) continue;
                    try { w.WriteLine(message); }
                    catch { /* клиент уже отключился — пропускаем */ }
                }
            }
        }
 
        static void Disconnect()
        {
            try { if (tcpListener != null) tcpListener.Stop(); } catch { }
            lock (sync)
            {
                foreach (TcpClient c in clients)
                {
                    try { c.Close(); } catch { }
                }
                clients.Clear();
                writers.Clear();
            }
        }
    }
}
