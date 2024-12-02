using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SocketDemo
{
    internal class Program
    {
        static void Main(string[] args)
        {
            string datatieme = DateTime.Now.ToString("yyyyMMddHHmmss");
            SocketServer server = new SocketServer();

            server.OnServerStarted += msg => Console.WriteLine(msg);
            server.OnServerStopped += msg => Console.WriteLine(msg);
            server.OnClientConnected += client => Console.WriteLine($"Client connected: {client}");
            server.OnClientDisconnected += client => Console.WriteLine($"Client disconnected: {client}");
            server.OnTextReceived += (client, text) => Console.WriteLine($"Text from {client}: {text}");
            server.OnFileProgress += (client, fileName, current, total) =>
            {
                Console.WriteLine($"File progress from {client}: {fileName} ({current}/{total})");
            };
            server.OnFileCompleted += (client, fileName) =>
            {
                Console.WriteLine($"File received from {client}: {fileName}");
                //server.SendDataAsync(client, "");
            };
            server.OnDataSent += (client, data) => Console.WriteLine($"Data sent to {client}: {data}");

            server.StartAsync().Wait();

            Console.ReadLine();
        }
    }
}
