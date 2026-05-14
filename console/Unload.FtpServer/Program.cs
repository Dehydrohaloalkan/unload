using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Unload.FtpServer;

int port = 21;
string rootDir = "./ftp-root";

for (int i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--port" && int.TryParse(args[i + 1], out int p))
        port = p;
    else if (args[i] == "--root")
        rootDir = args[i + 1];
}

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton(new FtpServerOptions { Port = port, RootDirectory = rootDir });
builder.Services.AddHostedService<FtpServerService>();

await builder.Build().RunAsync();
