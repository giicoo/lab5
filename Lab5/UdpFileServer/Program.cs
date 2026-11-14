using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace UdpFileServer
{
    /// <summary>
    /// UDP-сервер файлов. Сервер слушает все сетевые интерфейсы, отдаёт список
    /// двух файлов и отправляет выбранный клиентом файл одной датаграммой.
    /// </summary>
    public class UdpFileServer
    {
        private const int ServerPort = 5003;
        private const int MaxDatagramSize = 8192;
        private static readonly string[] FileNames = { "test.txt", "temp.txt" };

        private sealed class ServerFile
        {
            public string Name { get; set; }
            public string Path { get; set; }
            public long Size { get; set; }
        }

        [STAThread]
        private static void Main(string[] args)
        {
            Console.Title = "UDP файловый сервер";
            List<ServerFile> files = GetFiles();
            if (files.Count != FileNames.Length)
            {
                Console.WriteLine("Для запуска нужны оба файла: test.txt и temp.txt.");
                WaitForExit();
                return;
            }

            using (UdpClient server = new UdpClient(new IPEndPoint(IPAddress.Any, ServerPort)))
            {
                Console.WriteLine("Сервер запущен на 0.0.0.0:{0}.", ServerPort);
                Console.WriteLine("Доступные файлы:");
                foreach (ServerFile file in files)
                    Console.WriteLine("  {0} — {1} байт", file.Name, file.Size);
                Console.WriteLine("Ожидание клиентов из локальной сети...");

                try
                {
                    while (true)
                    {
                        IPEndPoint clientEndPoint = new IPEndPoint(IPAddress.Any, 0);
                        byte[] request = server.Receive(ref clientEndPoint);
                        HandleRequest(server, clientEndPoint, request, files);
                    }
                }
                catch (SocketException ex)
                {
                    Console.WriteLine("Сетевой сокет закрыт: {0}", ex.Message);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Ошибка сервера: {0}", ex.Message);
                }
            }
        }

        private static List<ServerFile> GetFiles()
        {
            List<ServerFile> files = new List<ServerFile>();
            foreach (string name in FileNames)
            {
                string path = Path.Combine(Directory.GetCurrentDirectory(), name);
                if (!File.Exists(path))
                {
                    Console.WriteLine("Файл отсутствует: {0}", path);
                    continue;
                }

                FileInfo info = new FileInfo(path);
                files.Add(new ServerFile { Name = name, Path = path, Size = info.Length });
            }
            return files;
        }

        private static void HandleRequest(UdpClient server, IPEndPoint client,
            byte[] requestBytes, List<ServerFile> files)
        {
            string request = Encoding.UTF8.GetString(requestBytes).Trim();
            if (request.Equals("LIST", StringComparison.OrdinalIgnoreCase))
            {
                SendFileList(server, client, files);
                Console.WriteLine("Список файлов отправлен клиенту {0}.", client);
                return;
            }

            if (!request.StartsWith("GET|", StringComparison.OrdinalIgnoreCase) ||
                !int.TryParse(request.Substring(4), out int fileIndex) ||
                fileIndex < 0 || fileIndex >= files.Count)
            {
                SendText(server, client, "ERROR|Неизвестный запрос");
                return;
            }

            ServerFile file = files[fileIndex];
            if (file.Size > MaxDatagramSize)
            {
                SendText(server, client,
                    "ERROR|Файл больше ограничения UDP в одной датаграмме");
                Console.WriteLine("Клиент {0} запросил слишком большой файл {1}.",
                    client, file.Name);
                return;
            }

            byte[] contents = File.ReadAllBytes(file.Path);
            SendText(server, client, "INFO|" + file.Name + "|" + contents.Length);
            server.Send(contents, contents.Length, client);
            Console.WriteLine("Файл {0} отправлен клиенту {1}.", file.Name, client);
        }

        private static void SendFileList(UdpClient server, IPEndPoint client,
            List<ServerFile> files)
        {
            StringBuilder response = new StringBuilder("FILES");
            for (int i = 0; i < files.Count; i++)
                response.Append('\n').Append(i).Append('|').Append(files[i].Name)
                    .Append('|').Append(files[i].Size);
            SendText(server, client, response.ToString());
        }

        private static void SendText(UdpClient server, IPEndPoint client, string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            server.Send(bytes, bytes.Length, client);
        }

        private static void WaitForExit()
        {
            Console.WriteLine("Нажмите Enter для выхода.");
            Console.ReadLine();
        }
    }
}
