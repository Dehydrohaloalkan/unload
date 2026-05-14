# Unload.FtpServer

Minimal FTP server for local testing. Accepts files from the main Linux app via FTP.

## Run

```bash
dotnet run --project console/Unload.FtpServer
```

With custom port and root directory:

```bash
dotnet run --project console/Unload.FtpServer -- --port 2121 --root ./my-ftp-root
```

## Defaults

| Setting    | Value        |
|------------|--------------|
| Port       | `21`         |
| Directory  | `./ftp-root` |
| Username   | `unload`     |
| Password   | `unload`     |

Anonymous access is also accepted.

## Console output

| Message | When |
|---|---|
| `[FTP] Connected from {ip}` | New client connected |
| `[FTP] LIST {dir}` | Directory listing requested |
| `[FTP] STOR {filename}` | File upload started |
| `[FTP] RNFR {filename}` | Rename-from received |
| `[FTP] RNTO {newname}` | Rename-to completed |
| `[FTP] File visible: {filename}` | File renamed from `.name` → `name` (hidden→visible) |

## Notes

- Passive mode (PASV) only — required for cross-OS connections.
- Multiple simultaneous connections are supported.
- Port 21 requires elevated privileges on Linux/macOS. Use `--port 2121` if needed.
