namespace MessagePipe.Interprocess;

public sealed class MessagePipeInterprocessTcpOptionsEx(string host, int port) : MessagePipeInterprocessOptionsEx
{
    // Hostname or IP address for TCP communication
    public string Host { get; } = host;

    // Port number for TCP communication
    public int Port { get; } = port;

    // Flag to determine if the host should act as a server
    public bool? HostAsServer { get; set; } = null;

    // Additional TCP-specific methods or properties can be added here
    // ...
}