using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Hosting;

namespace Unload.FtpServer;

public sealed class FtpServerOptions
{
    public int Port { get; set; } = 21;
    public string RootDirectory { get; set; } = "./ftp-root";
}

public sealed class FtpServerService : BackgroundService
{
    private readonly FtpServerOptions _options;

    public FtpServerService(FtpServerOptions options) => _options = options;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string root = Path.GetFullPath(_options.RootDirectory);
        Directory.CreateDirectory(root);

        var listener = new TcpListener(IPAddress.Any, _options.Port);
        listener.Start();

        Console.WriteLine($"[FTP] Server listening on port {_options.Port}");
        Console.WriteLine($"[FTP] Root directory: {root}");

        stoppingToken.Register(() => listener.Stop());

        var sessions = new List<Task>();

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync(stoppingToken);
                }
                catch (OperationCanceledException) { break; }
                catch (SocketException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[FTP] Accept error: {ex.Message}");
                    if (stoppingToken.IsCancellationRequested) break;
                    continue;
                }

                client.NoDelay = true;
                var session = new FtpSession(client, root);
                sessions.Add(Task.Run(() => session.RunAsync(stoppingToken), CancellationToken.None));
                sessions.RemoveAll(t => t.IsCompleted);
            }
        }
        finally
        {
            listener.Stop();
        }

        if (sessions.Count > 0)
            await Task.WhenAll(sessions);

        Console.WriteLine("[FTP] Server stopped.");
    }
}
