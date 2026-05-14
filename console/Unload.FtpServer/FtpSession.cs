using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Unload.FtpServer;

public sealed class FtpSession
{
    private readonly TcpClient _control;
    private readonly string _root;

    private StreamWriter _writer = null!;
    private string _currentDir = "/";
    private bool _loggedIn;
    private string? _pendingUser;
    private TcpListener? _pasvListener;
    private string? _rnfrPath;

    public FtpSession(TcpClient control, string root)
    {
        _control = control;
        _root = root;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        try
        {
            var stream = _control.GetStream();
            _writer = new StreamWriter(stream, Encoding.ASCII, leaveOpen: true)
            {
                AutoFlush = true,
                NewLine = "\r\n"
            };
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);

            var remote = ((IPEndPoint)_control.Client.RemoteEndPoint!).Address;
            Console.WriteLine($"[FTP] Connected from {remote}");

            await Reply("220 Unload FTP Server ready.");

            while (!ct.IsCancellationRequested)
            {
                string? line;
                try { line = await reader.ReadLineAsync(ct); }
                catch (OperationCanceledException) { break; }
                catch { break; }

                if (line == null) break;
                if (string.IsNullOrWhiteSpace(line)) continue;

                if (await DispatchAsync(line, ct))
                    break;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Console.Error.WriteLine($"[FTP] Session error: {ex.Message}");
        }
        finally
        {
            _pasvListener?.Stop();
            _control.Close();
        }
    }

    private async Task<bool> DispatchAsync(string line, CancellationToken ct)
    {
        int sp = line.IndexOf(' ');
        string cmd = (sp >= 0 ? line[..sp] : line).ToUpperInvariant().Trim();
        string arg = sp >= 0 ? line[(sp + 1)..].Trim() : string.Empty;

        try
        {
            switch (cmd)
            {
                case "USER":
                    _pendingUser = arg;
                    await Reply("331 Password required.");
                    break;

                case "PASS":
                    if (_pendingUser is "anonymous" ||
                        (_pendingUser == "unload" && arg == "unload"))
                    {
                        _loggedIn = true;
                        await Reply("230 User logged in.");
                    }
                    else
                    {
                        await Reply("530 Login incorrect.");
                    }
                    break;

                case "QUIT":
                    await Reply("221 Goodbye.");
                    return true;

                case "NOOP":
                    await Reply("200 OK");
                    break;

                case "SYST":
                    await Reply("215 UNIX Type: L8");
                    break;

                case "FEAT":
                    await ReplyRaw("211-Features:\r\n PASV\r\n SIZE\r\n211 End\r\n");
                    break;

                case "TYPE":
                    await Reply($"200 Type set to {arg}.");
                    break;

                case "MODE":
                    await Reply("200 Mode set.");
                    break;

                case "STRU":
                    await Reply("200 Structure set.");
                    break;

                case "ABOR":
                    await Reply("225 No transfer in progress.");
                    break;

                case "PWD":
                case "XPWD":
                    await Reply($"257 \"{_currentDir}\" is current directory.");
                    break;

                case "CWD":
                case "XCWD":
                    if (!CheckLogin()) break;
                    await CwdAsync(arg);
                    break;

                case "CDUP":
                    if (!CheckLogin()) break;
                    await CwdAsync("..");
                    break;

                case "PASV":
                    if (!CheckLogin()) break;
                    await PasvAsync();
                    break;

                case "LIST":
                case "NLST":
                    if (!CheckLogin()) break;
                    await ListAsync(StripListFlags(arg), ct);
                    break;

                case "RETR":
                    if (!CheckLogin()) break;
                    await RetrAsync(arg, ct);
                    break;

                case "STOR":
                    if (!CheckLogin()) break;
                    await StorAsync(arg, ct);
                    break;

                case "RNFR":
                    if (!CheckLogin()) break;
                    await RnfrAsync(arg);
                    break;

                case "RNTO":
                    if (!CheckLogin()) break;
                    await RntoAsync(arg);
                    break;

                case "MKD":
                case "XMKD":
                    if (!CheckLogin()) break;
                    await MkdAsync(arg);
                    break;

                case "RMD":
                case "XRMD":
                    if (!CheckLogin()) break;
                    await RmdAsync(arg);
                    break;

                case "DELE":
                    if (!CheckLogin()) break;
                    await DeleAsync(arg);
                    break;

                case "SIZE":
                    if (!CheckLogin()) break;
                    await SizeAsync(arg);
                    break;

                default:
                    await Reply($"502 Command not implemented: {cmd}");
                    break;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Console.Error.WriteLine($"[FTP] '{cmd}' error: {ex.Message}");
            try { await Reply("550 Internal error."); } catch { }
        }

        return false;
    }

    // -------------------------------------------------------------------------
    // Command handlers
    // -------------------------------------------------------------------------

    private async Task CwdAsync(string path)
    {
        string target = Resolve(path);
        if (!Directory.Exists(target))
        {
            await Reply("550 No such directory.");
            return;
        }

        string rel = Path.GetRelativePath(_root, target).Replace('\\', '/');
        _currentDir = rel == "." ? "/" : "/" + rel.TrimStart('/');
        await Reply($"250 Directory changed to \"{_currentDir}\".");
    }

    private async Task PasvAsync()
    {
        _pasvListener?.Stop();
        _pasvListener = new TcpListener(IPAddress.Any, 0);
        _pasvListener.Start(1);

        var localAddr = ((IPEndPoint)_control.Client.LocalEndPoint!).Address;
        if (localAddr.AddressFamily == AddressFamily.InterNetworkV6)
            localAddr = localAddr.IsIPv4MappedToIPv6 ? localAddr.MapToIPv4() : IPAddress.Loopback;

        int port = ((IPEndPoint)_pasvListener.LocalEndpoint).Port;
        string ip = localAddr.ToString().Replace('.', ',');
        await Reply($"227 Entering Passive Mode ({ip},{port >> 8},{port & 0xFF}).");
    }

    private async Task ListAsync(string path, CancellationToken ct)
    {
        string target = string.IsNullOrEmpty(path) ? Resolve(_currentDir) : Resolve(path);

        Console.WriteLine($"[FTP] LIST {_currentDir}");

        await Reply("150 Opening ASCII mode data connection for directory list.");

        using var data = await AcceptDataAsync(ct);
        if (data is null) return;

        try
        {
            var sb = new StringBuilder();

            if (Directory.Exists(target))
            {
                var dir = new DirectoryInfo(target);
                foreach (var entry in dir.EnumerateFileSystemInfos().OrderBy(e => e.Name))
                {
                    bool isDir = entry is DirectoryInfo;
                    long size = isDir ? 0 : ((FileInfo)entry).Length;
                    string perm = isDir ? "drwxr-xr-x" : "-rw-r--r--";
                    string date = FormatDate(entry.LastWriteTime);
                    sb.Append($"{perm} 1 ftp ftp {size,12} {date} {entry.Name}\r\n");
                }
            }

            var bytes = Encoding.ASCII.GetBytes(sb.ToString());
            await data.GetStream().WriteAsync(bytes, ct);
            await Reply("226 Transfer complete.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[FTP] LIST error: {ex.Message}");
            try { await Reply("426 Transfer aborted."); } catch { }
        }
    }

    private async Task RetrAsync(string path, CancellationToken ct)
    {
        string full = Resolve(path);
        if (!File.Exists(full))
        {
            await Reply("550 File not found.");
            return;
        }

        await Reply($"150 Opening BINARY mode data connection for {Path.GetFileName(full)}.");

        using var data = await AcceptDataAsync(ct);
        if (data is null) return;

        try
        {
            await using var fs = File.OpenRead(full);
            await fs.CopyToAsync(data.GetStream(), ct);
            await Reply("226 Transfer complete.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[FTP] RETR error: {ex.Message}");
            try { await Reply("426 Transfer aborted."); } catch { }
        }
    }

    private async Task StorAsync(string path, CancellationToken ct)
    {
        string full = Resolve(path);
        string? dir = Path.GetDirectoryName(full);
        if (dir is not null) Directory.CreateDirectory(dir);

        await Reply($"150 Opening BINARY mode data connection for {Path.GetFileName(full)}.");

        using var data = await AcceptDataAsync(ct);
        if (data is null) return;

        Console.WriteLine($"[FTP] STOR {path}");

        try
        {
            await using var fs = File.Create(full);
            await data.GetStream().CopyToAsync(fs, ct);
            await Reply("226 Transfer complete.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[FTP] STOR error: {ex.Message}");
            try { await Reply("426 Transfer aborted."); } catch { }
        }
    }

    private async Task RnfrAsync(string path)
    {
        string full = Resolve(path);
        if (!File.Exists(full) && !Directory.Exists(full))
        {
            await Reply("550 File or directory not found.");
            return;
        }

        _rnfrPath = full;
        Console.WriteLine($"[FTP] RNFR {path}");
        await Reply("350 Ready for RNTO.");
    }

    private async Task RntoAsync(string path)
    {
        if (_rnfrPath is null)
        {
            await Reply("503 RNFR required first.");
            return;
        }

        string dest = Resolve(path);

        Console.WriteLine($"[FTP] RNTO {path}");

        try
        {
            if (File.Exists(_rnfrPath))
                File.Move(_rnfrPath, dest, overwrite: true);
            else if (Directory.Exists(_rnfrPath))
                Directory.Move(_rnfrPath, dest);
            else
            {
                await Reply("550 Source no longer exists.");
                return;
            }

            await Reply("250 Rename successful.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[FTP] RNTO error: {ex.Message}");
            await Reply("550 Rename failed.");
        }
        finally
        {
            _rnfrPath = null;
        }
    }

    private async Task MkdAsync(string path)
    {
        string full = Resolve(path);
        try
        {
            Directory.CreateDirectory(full);
            string rel = "/" + Path.GetRelativePath(_root, full).Replace('\\', '/').TrimStart('/');
            await Reply($"257 \"{rel}\" directory created.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[FTP] MKD error: {ex.Message}");
            await Reply("550 Failed to create directory.");
        }
    }

    private async Task RmdAsync(string path)
    {
        string full = Resolve(path);
        try
        {
            Directory.Delete(full, recursive: true);
            await Reply("250 Directory removed.");
        }
        catch
        {
            await Reply("550 Remove failed.");
        }
    }

    private async Task DeleAsync(string path)
    {
        string full = Resolve(path);
        try
        {
            File.Delete(full);
            await Reply("250 File deleted.");
        }
        catch
        {
            await Reply("550 Delete failed.");
        }
    }

    private async Task SizeAsync(string path)
    {
        string full = Resolve(path);
        if (File.Exists(full))
            await Reply($"213 {new FileInfo(full).Length}");
        else
            await Reply("550 File not found.");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<TcpClient?> AcceptDataAsync(CancellationToken ct)
    {
        if (_pasvListener is null)
        {
            await Reply("425 Use PASV first.");
            return null;
        }

        var listener = _pasvListener;
        _pasvListener = null;

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(30));
            var client = await listener.AcceptTcpClientAsync(cts.Token);
            client.NoDelay = true;
            return client;
        }
        catch (OperationCanceledException)
        {
            await Reply("425 Data connection timed out.");
            return null;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[FTP] Data accept error: {ex.Message}");
            await Reply("425 Can't open data connection.");
            return null;
        }
        finally
        {
            listener.Stop();
        }
    }

    private string Resolve(string path)
    {
        string combined = string.IsNullOrEmpty(path)
            ? Path.Combine(_root, _currentDir.TrimStart('/'))
            : path.StartsWith('/')
                ? Path.Combine(_root, path.TrimStart('/'))
                : Path.Combine(_root, _currentDir.TrimStart('/'), path);

        string full = Path.GetFullPath(combined);

        if (!full.StartsWith(_root, StringComparison.OrdinalIgnoreCase))
            return _root;

        return full;
    }

    private bool CheckLogin()
    {
        if (_loggedIn) return true;
        _ = Reply("530 Not logged in.");
        return false;
    }

    private Task Reply(string response) => _writer.WriteLineAsync(response);

    private Task ReplyRaw(string raw) => _writer.WriteAsync(raw);

    private static string FormatDate(DateTime dt)
    {
        bool sameYear = dt.Year == DateTime.Now.Year;
        return sameYear
            ? dt.ToString("MMM dd HH:mm")
            : dt.ToString("MMM dd  yyyy");
    }

    private static string StripListFlags(string arg)
    {
        if (!arg.StartsWith('-')) return arg;
        int sp = arg.IndexOf(' ');
        return sp >= 0 ? arg[(sp + 1)..].Trim() : string.Empty;
    }
}
