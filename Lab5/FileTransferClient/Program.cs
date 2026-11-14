using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace FileTransferClient
{
    /// <summary>
    /// Клиент демонстрации TCP/UDP. Можно специально закрыть передачу после
    /// указанного количества байт и сравнить реакцию двух протоколов на сервере.
    /// </summary>
    internal class Program
    {
        private const int TcpPort = 5004;
        private const int UdpPort = 5005;
        private const int UdpPayloadSize = 6000;
        private const int SendBufferSize = 4096;

        private static void Main(string[] args)
        {
            Console.Title = "Сравнение TCP и UDP: клиент";
            try
            {
                Console.Write("IP-адрес сервера (Enter = 127.0.0.1): ");
                string ipText = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(ipText)) ipText = "127.0.0.1";
                IPAddress serverAddress = IPAddress.Parse(ipText.Trim());

                Console.Write("Протокол (1 = TCP, 2 = UDP): ");
                string protocol = Console.ReadLine();
                Console.Write("Путь к файлу (Enter = test.txt): ");
                string path = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(path)) path = "test.txt";
                if (!File.Exists(path)) throw new FileNotFoundException("Файл не найден", path);

                byte[] file = File.ReadAllBytes(path);
                Console.Write("Отправить байт перед обрывом (0 = отправить всё): ");
                long interruption = ReadNonNegativeLong();

                if (protocol == "1")
                    SendTcp(serverAddress, Path.GetFileName(path), file, interruption);
                else if (protocol == "2")
                    SendUdp(serverAddress, Path.GetFileName(path), file, interruption);
                else
                    throw new InvalidDataException("Выберите протокол 1 или 2");
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

        private static void SendTcp(IPAddress serverAddress, string fileName, byte[] file,
            long interruption)
        {
            using (TcpClient client = new TcpClient())
            {
                client.Connect(new IPEndPoint(serverAddress, TcpPort));
                using (NetworkStream stream = client.GetStream())
                {
                    string header = "FILE|" + fileName + "|" + file.Length + "\n";
                    byte[] headerBytes = Encoding.UTF8.GetBytes(header);
                    stream.Write(headerBytes, 0, headerBytes.Length);

                    long limit = interruption == 0
                        ? file.Length : Math.Min(interruption, (long)file.Length);
                    long sent = 0;
                    while (sent < limit)
                    {
                        int count = (int)Math.Min((long)SendBufferSize, limit - sent);
                        stream.Write(file, (int)sent, count);
                        sent += count;
                    }
                    stream.Flush();
                    Console.WriteLine("[TCP] Отправлено {0} из {1} байт.", sent, file.Length);
                }
            }
            if (interruption > 0 && interruption < file.Length)
                Console.WriteLine("[TCP] Соединение закрыто специально для демонстрации обрыва.");
            else
                Console.WriteLine("[TCP] Соединение закрыто после полной передачи.");
        }

        private static void SendUdp(IPAddress serverAddress, string fileName, byte[] file,
            long interruption)
        {
            string transferId = Guid.NewGuid().ToString("N");
            int totalPackets = file.Length == 0
                ? 1 : (int)((file.Length + UdpPayloadSize - 1) / UdpPayloadSize);
            IPEndPoint server = new IPEndPoint(serverAddress, UdpPort);
            using (UdpClient client = new UdpClient())
            {
                client.Connect(server);
                SendText(client, "START|" + transferId + "|" + fileName + "|" +
                    file.Length + "|" + totalPackets);

                long limit = interruption == 0
                    ? file.Length : Math.Min(interruption, (long)file.Length);
                long sent = 0;
                int sequence = 0;
                while (sent < limit || (file.Length == 0 && sequence == 0))
                {
                    int count = file.Length == 0
                        ? 0 : (int)Math.Min((long)UdpPayloadSize,
                            Math.Min(limit - sent, file.Length - sent));
                    byte[] prefix = Encoding.UTF8.GetBytes(
                        "DATA|" + transferId + "|" + sequence + "|");
                    byte[] packet = new byte[prefix.Length + count];
                    Buffer.BlockCopy(prefix, 0, packet, 0, prefix.Length);
                    if (count > 0)
                        Buffer.BlockCopy(file, (int)sent, packet, prefix.Length, count);
                    client.Send(packet, packet.Length);
                    sent += count;
                    sequence++;
                    if (file.Length == 0) break;
                }
                Console.WriteLine("[UDP] Отправлено {0} из {1} байт, пакетов: {2}.",
                    sent, file.Length, sequence);
            }
            if (interruption > 0 && interruption < file.Length)
                Console.WriteLine("[UDP] Сокет закрыт, но сервер не получает события обрыва; " +
                    "он обнаружит его по тайм-ауту.");
            else
                Console.WriteLine("[UDP] Датаграммы отправлены, подтверждения не используются.");
        }

        private static void SendText(UdpClient client, string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            client.Send(bytes, bytes.Length);
        }

        private static long ReadNonNegativeLong()
        {
            string value = Console.ReadLine();
            if (!long.TryParse(value, out long result) || result < 0)
                throw new InvalidDataException("Введите неотрицательное число байт");
            return result;
        }
    }
}
