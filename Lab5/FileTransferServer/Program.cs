using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace FileTransferServer
{
    /// <summary>
    /// Демонстрационный сервер: принимает загрузки по TCP и UDP.
    /// TCP сообщает о закрытии потока, а UDP узнаёт об остановке передачи
    /// только после тайм-аута и не может получить событие «соединение закрыто».
    /// </summary>
    internal class Program
    {
        private const int TcpPort = 5004;
        private const int UdpPort = 5005;
        private const int UdpTimeoutMilliseconds = 5000;
        private const string ReceivedDirectory = "ReceivedFiles";

        private static void Main(string[] args)
        {
            Console.Title = "Сравнение TCP и UDP: сервер";
            Directory.CreateDirectory(ReceivedDirectory);

            TcpListener tcpListener = new TcpListener(IPAddress.Any, TcpPort);
            UdpClient udpServer = new UdpClient(new IPEndPoint(IPAddress.Any, UdpPort));

            try
            {
                tcpListener.Start();
                Console.WriteLine("TCP-сервер слушает 0.0.0.0:{0}.", TcpPort);
                Console.WriteLine("UDP-сервер слушает 0.0.0.0:{0}.", UdpPort);
                Console.WriteLine("Для остановки нажмите Enter.");

                Thread tcpThread = new Thread(() => AcceptTcpClients(tcpListener));
                tcpThread.IsBackground = true;
                tcpThread.Start();

                Thread udpThread = new Thread(() => ReceiveUdpFiles(udpServer));
                udpThread.IsBackground = true;
                udpThread.Start();

                Console.ReadLine();
            }
            finally
            {
                tcpListener.Stop();
                udpServer.Close();
            }
        }

        private static void AcceptTcpClients(TcpListener listener)
        {
            try
            {
                while (true)
                {
                    TcpClient client = listener.AcceptTcpClient();
                    Thread clientThread = new Thread(() => ReceiveTcpFile(client));
                    clientThread.IsBackground = true;
                    clientThread.Start();
                }
            }
            catch (SocketException)
            {
                // Слушатель закрывается при завершении сервера.
            }
        }

        private static void ReceiveTcpFile(TcpClient client)
        {
            string partialPath = null;
            try
            {
                using (client)
                using (NetworkStream stream = client.GetStream())
                {
                    string[] header = ReadLine(stream).Split('|');
                    if (header.Length != 3 || header[0] != "FILE" ||
                        !long.TryParse(header[2], out long expectedSize))
                        throw new InvalidDataException("Некорректный TCP-заголовок");

                    string fileName = SafeFileName(header[1]);
                    partialPath = Path.Combine(ReceivedDirectory, "tcp_" + fileName + ".part");
                    long receivedSize = CopyTcpData(stream, partialPath, expectedSize);

                    if (receivedSize == expectedSize)
                    {
                        string completedPath = Path.Combine(ReceivedDirectory, "tcp_" + fileName);
                        File.Move(partialPath, completedPath, true);
                        Console.WriteLine(
                            "[TCP] Передача завершена: получено {0} из {1} байт. Файл: {2}",
                            receivedSize, expectedSize, Path.GetFullPath(completedPath));
                    }
                    else
                    {
                        Console.WriteLine(
                            "[TCP] ОБРЫВ: поток закрыт, получено {0} из {1} байт. " +
                            "Сервер обнаружил обрыв сразу; неполный файл: {2}",
                            receivedSize, expectedSize, Path.GetFullPath(partialPath));
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[TCP] Ошибка передачи: {0}", ex.Message);
            }
        }

        private static long CopyTcpData(NetworkStream stream, string path, long expectedSize)
        {
            byte[] buffer = new byte[8192];
            long received = 0;
            using (FileStream output = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                try
                {
                    while (received < expectedSize)
                    {
                        int requested = (int)Math.Min((long)buffer.Length, expectedSize - received);
                        int count = stream.Read(buffer, 0, requested);
                        if (count == 0) break;
                        output.Write(buffer, 0, count);
                        received += count;
                    }
                }
                catch (IOException)
                {
                    // Сброс TCP-соединения также рассматривается как обрыв.
                }
            }
            return received;
        }

        private static void ReceiveUdpFiles(UdpClient server)
        {
            try
            {
                while (true)
                {
                    IPEndPoint clientEndPoint = new IPEndPoint(IPAddress.Any, 0);
                    string start = Encoding.UTF8.GetString(server.Receive(ref clientEndPoint));
                    string[] header = start.Split('|');
                    if (header.Length != 5 || header[0] != "START" ||
                        !long.TryParse(header[3], out long expectedSize) ||
                        !int.TryParse(header[4], out int totalPackets) || totalPackets < 1)
                    {
                        Console.WriteLine("[UDP] Получен некорректный заголовок.");
                        continue;
                    }

                    string transferId = header[1];
                    string fileName = SafeFileName(header[2]);
                    Dictionary<int, byte[]> packets = new Dictionary<int, byte[]>();
                    server.Client.ReceiveTimeout = UdpTimeoutMilliseconds;
                    Console.WriteLine("[UDP] Начата передача {0}: ожидается {1} байт, " +
                        "пакетов: {2}.", fileName, expectedSize, totalPackets);

                    try
                    {
                        while (packets.Count < totalPackets)
                        {
                            IPEndPoint packetEndPoint = new IPEndPoint(IPAddress.Any, 0);
                            byte[] packet = server.Receive(ref packetEndPoint);
                            if (!packetEndPoint.Equals(clientEndPoint)) continue;

                            if (!TryParseUdpPacket(packet, transferId,
                                out int sequence, out byte[] contents)) continue;
                            if (sequence >= 0 && sequence < totalPackets &&
                                !packets.ContainsKey(sequence))
                                packets.Add(sequence, contents);
                        }
                    }
                    catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut)
                    {
                        Console.WriteLine("[UDP] Тайм-аут ожидания следующего пакета.");
                    }
                    finally
                    {
                        server.Client.ReceiveTimeout = 0;
                    }

                    long receivedSize = GetReceivedSize(packets);
                    bool complete = packets.Count == totalPackets && receivedSize == expectedSize;
                    string partialPath = SaveUdpFile(fileName, packets, complete);
                    if (complete)
                    {
                        Console.WriteLine("[UDP] Передача завершена: получено {0} из {1} байт. " +
                            "Файл: {2}", receivedSize, expectedSize,
                            Path.GetFullPath(partialPath));
                    }
                    else
                    {
                        Console.WriteLine("[UDP] ОБРЫВ/ПОТЕРЯ: получено {0} из {1} байт, " +
                            "пакетов {2} из {3}. UDP не сообщает серверу о закрытии клиента; " +
                            "обрыв не сообщается закрытием сокета; проблема " +
                            "обнаружена по тайм-ауту или несовпадению размера. Файл: {4}",
                            receivedSize, expectedSize, packets.Count, totalPackets,
                            Path.GetFullPath(partialPath));
                    }
                }
            }
            catch (SocketException)
            {
                // Сокет закрывается при завершении сервера.
            }
        }

        private static bool TryParseUdpPacket(byte[] packet, string transferId,
            out int sequence, out byte[] contents)
        {
            sequence = -1;
            contents = new byte[0];
            int first = FindByte(packet, 0, (byte)'|');
            int second = FindByte(packet, first + 1, (byte)'|');
            int third = FindByte(packet, second + 1, (byte)'|');
            if (first < 0 || second < 0 || third < 0) return false;

            string type = Encoding.UTF8.GetString(packet, 0, first);
            string id = Encoding.UTF8.GetString(packet, first + 1, second - first - 1);
            string sequenceText = Encoding.UTF8.GetString(packet, second + 1, third - second - 1);
            if (type != "DATA" || id != transferId || !int.TryParse(sequenceText, out sequence))
                return false;

            int contentLength = packet.Length - third - 1;
            contents = new byte[contentLength];
            Buffer.BlockCopy(packet, third + 1, contents, 0, contentLength);
            return true;
        }

        private static int FindByte(byte[] data, int start, byte value)
        {
            for (int i = start; i < data.Length; i++)
                if (data[i] == value) return i;
            return -1;
        }

        private static long GetReceivedSize(Dictionary<int, byte[]> packets)
        {
            long result = 0;
            foreach (byte[] packet in packets.Values) result += packet.Length;
            return result;
        }

        private static string SaveUdpFile(string fileName, Dictionary<int, byte[]> packets,
            bool complete)
        {
            string prefix = complete ? "udp_" : "udp_";
            string suffix = complete ? "" : ".part";
            string path = Path.Combine(ReceivedDirectory, prefix + fileName + suffix);
            using (FileStream output = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                for (int sequence = 0; ; sequence++)
                {
                    if (!packets.TryGetValue(sequence, out byte[] contents)) break;
                    output.Write(contents, 0, contents.Length);
                }
            }
            return path;
        }

        private static string ReadLine(NetworkStream stream)
        {
            List<byte> bytes = new List<byte>();
            int value;
            while ((value = stream.ReadByte()) != -1)
            {
                if (value == '\n') return Encoding.UTF8.GetString(bytes.ToArray());
                if (bytes.Count >= 4096) throw new InvalidDataException("Слишком длинный заголовок");
                bytes.Add((byte)value);
            }
            throw new EndOfStreamException("TCP-клиент закрыл соединение до заголовка");
        }

        private static string SafeFileName(string fileName)
        {
            string safeName = Path.GetFileName(fileName);
            if (safeName != fileName || safeName.Length == 0 || safeName.Contains('|'))
                throw new InvalidDataException("Недопустимое имя файла");
            return safeName;
        }
    }
}
