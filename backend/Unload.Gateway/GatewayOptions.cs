namespace Unload.Gateway;

public class GatewayOptions
{
    public const string SectionName = "Gateway";

    public FtpGatewayOptions Ftp { get; set; } = new();
}

public class FtpGatewayOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 21;
    public string Username { get; set; } = "unload";
    public string Password { get; set; } = "unload";

    /// <summary>
    /// Целевая директория — обработчик смотрит именно сюда.
    /// </summary>
    public string RemoteDirectory { get; set; } = "/target";

    /// <summary>
    /// Staging-директория на том же томе, что RemoteDirectory.
    /// Если не задана — авто: sibling-директория "/staging" рядом с RemoteDirectory.
    /// </summary>
    public string StagingDirectory { get; set; } = "";

    public int ConnectTimeoutMs { get; set; } = 30_000;
}
