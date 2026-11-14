using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
 
namespace ChatUdp
{
    /// <summary>
    /// Чат на базе UDP (аналог ChatSample из методички).
    /// Один экземпляр = один участник. На одном компьютере запускаются два окна
    /// с «зеркальными» настройками портов.
    /// </summary>
    class Program
    {
        private static IPAddress remoteIPAddress;
        private static int remotePort;
        private static int localPort;
 
        static void Main(string[] args)
        {
            Console.Title = "UDP чат";
            try
            {
                Console.Write("Укажите локальный порт (например, 5001): ");
                localPort = Convert.ToInt32(Console.ReadLine());
 
                Console.Write("Укажите удалённый порт (например, 5002): ");
                remotePort = Convert.ToInt32(Console.ReadLine());
 
                Console.Write("Укажите удалённый IP-адрес (например, 127.0.0.1): ");
                remoteIPAddress = IPAddress.Parse(Console.ReadLine());
 
                // Поток приёма: Receive() блокирует поток, поэтому его нельзя
                // держать в главном потоке, иначе не получится вводить текст.
                Thread tRec = new Thread(Receiver);
                tRec.IsBackground = true;
                tRec.Start();
 
                Console.WriteLine();
                Console.WriteLine("Чат запущен. Локальный порт {0}, удалённый {1}:{2}.",
                    localPort, remoteIPAddress, remotePort);
                Console.WriteLine("Введите сообщение и нажмите Enter. " +
                    "Для выхода наберите /exit.");
                Console.WriteLine(new string('-', 60));
 
                Console.Write("> ");
                while (true)
                {
                    string line = Console.ReadLine();
                    if (line == null) break;
                    if (line.Trim() == "/exit") break;
                    Send(line);
                    Console.Write("> ");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Возникло исключение: " + ex.Message);
            }
        }
 
        /// <summary>Отправка одной дейтаграммы удалённому узлу.</summary>
        private static void Send(string datagram)
        {
            // Отдельный UdpClient на каждое сообщение — как в методичке:
            // так ОС сама выбирает свободный порт отправителя.
            UdpClient sender = new UdpClient();
            IPEndPoint endPoint = new IPEndPoint(remoteIPAddress, remotePort);
            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(datagram);
                sender.Send(bytes, bytes.Length, endPoint);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Ошибка отправки: " + ex.Message);
            }
            finally
            {
                sender.Close();
            }
        }
 
        /// <summary>Потоковая функция приёма дейтаграмм.</summary>
        public static void Receiver()
        {
            UdpClient receivingUdpClient = null;
            try
            {
                receivingUdpClient = new UdpClient(localPort);
                IPEndPoint remoteIpEndPoint = null;
 
                Console.WriteLine();
                Console.WriteLine("******* Общий чат, порт " + localPort + " *******");
                Console.WriteLine();
 
                while (true)
                {
                    byte[] receiveBytes = receivingUdpClient.Receive(ref remoteIpEndPoint);
                    string text = Encoding.UTF8.GetString(receiveBytes);
 
                    Console.WriteLine();
                    Console.WriteLine("[{0:HH:mm:ss}] {1} --> {2}",
                        DateTime.Now, remoteIpEndPoint, text);
                }
            }
            catch (SocketException ex)
            {
                Console.WriteLine("Сокет недоступен (порт {0} занят?): {1}",
                    localPort, ex.Message);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Ошибка приёма: " + ex.Message);
            }
            finally
            {
                if (receivingUdpClient != null) receivingUdpClient.Close();
            }
        }
    }
}
