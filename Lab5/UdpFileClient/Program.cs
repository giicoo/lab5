using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace UdpFileClient
{
    /// <summary>
    /// UDP-клиент файлов. Запрашивает у сервера список, даёт выбрать файл
    /// и сохраняет выбранный файл с префиксом downloaded_.
    /// </summary>
    public class UdpFileClient
    {
        private const int ServerPort = 5003;
        private const int MaxDatagramSize = 8192;
        private static RemoteFile[] lastFileList = new RemoteFile[0];

        private sealed class RemoteFile
        {
            public int Index { get; set; }
            public string Name { get; set; }
            public long Size { get; set; }
        }

        [STAThread]
        private static void Main(string[] args)
        {
            Console.Title = "UDP файловый клиент";
            try
            {
                Console.Write("IP-адрес сервера (Enter = 127.0.0.1): ");
                string ipText = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(ipText)) ipText = "127.0.0.1";

                IPEndPoint serverEndPoint = new IPEndPoint(
                    IPAddress.Parse(ipText.Trim()), ServerPort);
                using (UdpClient client = new UdpClient())
                {
                    client.Connect(serverEndPoint);
                    client.Client.ReceiveTimeout = 5000;
                    ListFiles(client);
                    RemoteFile selected = ChooseFile(client);
                    DownloadFile(client, selected);
                }
            }
            catch (SocketException ex)
            {
                Console.WriteLine("Не удалось связаться с сервером: {0}", ex.Message);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Ошибка клиента: {0}", ex.Message);
            }
            finally
            {
                Console.WriteLine("Нажмите Enter для выхода.");
                Console.ReadLine();
            }
        }

        private static void ListFiles(UdpClient client)
        {
            SendText(client, "LIST");
            IPEndPoint ignored = new IPEndPoint(IPAddress.Any, 0);
            string response = Encoding.UTF8.GetString(client.Receive(ref ignored));
            string[] lines = response.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0 || !lines[0].Equals("FILES", StringComparison.Ordinal))
                throw new InvalidDataException("Сервер вернул некорректный список файлов");

            Console.WriteLine("\nФайлы на сервере:");
            lastFileList = new RemoteFile[lines.Length - 1];
            for (int i = 1; i < lines.Length; i++)
            {
                string[] fields = lines[i].Split('|');
                if (fields.Length != 3 || !int.TryParse(fields[0], out int index) ||
                    !long.TryParse(fields[2], out long size))
                    throw new InvalidDataException("Сервер вернул некорректные данные файла");

                lastFileList[i - 1] = new RemoteFile
                {
                    Index = index,
                    Name = fields[1],
                    Size = size
                };
                Console.WriteLine("  {0}. {1} ({2} байт)", i, fields[1], size);
            }
        }

        private static RemoteFile ChooseFile(UdpClient client)
        {
            while (true)
            {
                Console.Write("Введите номер файла для скачивания: ");
                string choiceText = Console.ReadLine();
                if (int.TryParse(choiceText, out int choice) &&
                    choice >= 1 && choice <= lastFileList.Length)
                {
                    RemoteFile selected = lastFileList[choice - 1];
                    SendText(client, "GET|" + selected.Index);
                    return selected;
                }
                Console.WriteLine("Введите номер из списка.");
            }
        }

        private static void DownloadFile(UdpClient client, RemoteFile selected)
        {
            IPEndPoint ignored = new IPEndPoint(IPAddress.Any, 0);
            byte[] infoBytes = client.Receive(ref ignored);
            string[] info = Encoding.UTF8.GetString(infoBytes).Split('|');
            if (info.Length == 2 && info[0] == "ERROR")
                throw new InvalidDataException(info[1]);
            if (info.Length != 3 || info[0] != "INFO" ||
                !long.TryParse(info[2], out long announcedSize))
                throw new InvalidDataException("Сервер вернул некорректные сведения о файле");

            byte[] contents = client.Receive(ref ignored);
            if (contents.Length > MaxDatagramSize)
                throw new InvalidDataException("Получена слишком большая UDP-датаграмма");

            string safeName = Path.GetFileName(selected.Name);
            if (safeName != selected.Name ||
                !Regex.IsMatch(safeName, "^[a-zA-Z0-9_.-]+$"))
                throw new InvalidDataException("Сервер прислал недопустимое имя файла");

            string localName = "downloaded_" + safeName;
            File.WriteAllBytes(localName, contents);
            Console.WriteLine("\nСкачан файл: {0} ({1} байт).",
                Path.GetFullPath(localName), contents.Length);
            if (contents.Length != announcedSize || contents.Length != selected.Size)
                Console.WriteLine("Внимание: размер файла не совпал с заявленным сервером.");
            else
                Console.WriteLine("Размер файла проверен.");
        }

        private static void SendText(UdpClient client, string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            client.Send(bytes, bytes.Length);
        }
    }
}
