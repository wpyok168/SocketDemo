using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace SocketDemo
{
    public class SocketServer
    {
        private TcpListener _listener;
        private readonly ConcurrentDictionary<TcpClient, string> _clients = new ConcurrentDictionary<TcpClient, string>(); // 客户端连接
        private readonly ConcurrentDictionary<string, long> _fileProgress = new ConcurrentDictionary<string, long>(); // 客户端断点记录
        private bool _isRunning = false;

        // 事件
        public event Action<string> OnServerStarted;
        public event Action<string> OnServerStopped;
        public event Action<string> OnClientConnected;
        /// <summary>
        /// 客户端退出事件
        /// </summary>
        public event Action<string> OnClientDisconnected;
        public event Action<string, string> OnTextReceived;
        /// <summary>
        /// 文件上传事件
        /// </summary>
        public event Action<string, string, long, long> OnFileProgress;
        /// <summary>
        /// 文件上传完成事件
        /// </summary>
        public event Action<string, string> OnFileCompleted;

        public event Action<string, string> OnDataSent;

        public async Task StartAsync(int port = 8899)
        {
            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();
            _isRunning = true;
            OnServerStarted?.Invoke($"Server started on port {port}");

            while (_isRunning)
            {
                var client = await _listener.AcceptTcpClientAsync();
                _ = Task.Run(() => HandleClientAsync(client));
            }
        }

        public void Stop()
        {
            _isRunning = false;
            _listener.Stop();
            OnServerStopped?.Invoke("Server stopped.");
        }

        private async Task HandleClientAsync(TcpClient client)
        {
            var clientEndPoint = client.Client.RemoteEndPoint.ToString();
            _clients.TryAdd(client, clientEndPoint);
            OnClientConnected?.Invoke(clientEndPoint);

            try
            {
                using (var networkStream = client.GetStream())
                {
                    var buffer = new byte[8192];

                    while (true)
                    {
                        int bytesRead = await networkStream.ReadAsync(buffer, 0, buffer.Length);
                        if (bytesRead == 0) break; // 客户端断开连接

                        var json = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                        var message = JsonSerializer.Deserialize<Message>(json);

                        switch (message.Type)
                        {
                            case "text":
                                OnTextReceived?.Invoke(clientEndPoint, message.Text);
                                break;

                            case "file":
                                await HandleFileTransferAsync(networkStream, message, clientEndPoint);
                                break;
                            case "request_file":
                                await SendFileAsync(client, message.FilePath);
                                break;
                        }
                    }
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error with client {clientEndPoint}: {ex.Message}");
            }
            finally
            {
                OnClientDisconnected?.Invoke(clientEndPoint);
                _clients.TryRemove(client, out _);
            }
        }

        private async Task HandleFileTransferAsync(NetworkStream stream, Message message, string clientId)
        {
            //var filePath = Path.Combine("ReceivedFiles", message.FileName);
            //Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            var filePath = message.FilePath ?? Path.Combine("ReceivedFiles", message.FileName);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath));

            long existingSize = _fileProgress.GetOrAdd(clientId, 0);

            using (var fileStream = new FileStream(filePath, FileMode.Append, FileAccess.Write))
            {
                while (existingSize < message.FileSize)
                {
                    byte[] fileBuffer = new byte[8192];
                    int bytesRead = await stream.ReadAsync(fileBuffer, 0, fileBuffer.Length);
                    if (bytesRead == 0) break;

                    fileStream.Write(fileBuffer, 0, bytesRead);
                    existingSize += bytesRead;
                    _fileProgress[clientId] = existingSize;

                    OnFileProgress?.Invoke(clientId, message.FileName, existingSize, message.FileSize);
                }

                if (existingSize >= message.FileSize)
                {
                    OnFileCompleted?.Invoke(clientId, message.FileName);
                    _fileProgress.TryRemove(clientId, out _);
                }
            };
        }
        private async Task SendFileAsync(TcpClient client, string filePath)
        {
            //var filePath = Path.Combine("FilesToSend", fileName);
            //if (!File.Exists(filePath))
            //{
            //    Console.WriteLine($"File {fileName} not found!");
            //    return;
            //}
            //var fileSize = new FileInfo(filePath).Length;
            //var metadata = new Message { Type = "file", FileName = fileName, FileSize = fileSize };
            

            var fileName = Path.GetFileName(filePath);
            if (!File.Exists(filePath))
            {
                Console.WriteLine($"File {fileName} not found!");
                return;
            }
            var fileSize = new FileInfo(filePath).Length;
            var metadata = new Message { Type = "file", FileName = fileName, FileSize = fileSize, FilePath = filePath };

            var metadataJson = JsonSerializer.Serialize(metadata);
            var metadataBuffer = Encoding.UTF8.GetBytes(metadataJson);

            await client.GetStream().WriteAsync(metadataBuffer, 0, metadataBuffer.Length);

            using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            {
                var buffer = new byte[8192];
                long bytesSent = 0;
                int bytesRead;

                while ((bytesRead = await fileStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await client.GetStream().WriteAsync(buffer, 0, bytesRead);
                    bytesSent += bytesRead;
                    OnFileProgress?.Invoke(client.Client.RemoteEndPoint.ToString(), fileName, bytesSent, fileSize);
                }

                Console.WriteLine($"File {fileName} sent to {client.Client.RemoteEndPoint.ToString()}.");
            };
        }

        public async Task SendDataAsync(TcpClient client, string data)
        {
            if (!client.Connected) return;
            var buffer = Encoding.UTF8.GetBytes(data);
            await client.GetStream().WriteAsync(buffer, 0, buffer.Length);
            OnDataSent?.Invoke(_clients[client], data);
        }
    }
    public class Message
    {
        public string Type { get; set; }
        public string Text { get; set; }
        public string FileName { get; set; }
        public long FileSize { get; set; }
        public string FilePath { get; set; }
    }

}

