#if NET5_0_OR_GREATER
// Extended Unix Domain Socket (UDS) options for TCP communication
namespace MessagePipe.Interprocess;

public sealed class MessagePipeInterprocessTcpUdsOptionsEx(string socketPath,
                                                           int? sendBufferSize = null,
                                                           int? recvBufferSize = null) : MessagePipeInterprocessOptionsEx
{
    // Path to the Unix Domain Socket
    public string SocketPath { get; set; } = socketPath;

    // Optional buffer size settings for send and receive operations
    public int? SendBufferSize { get; set; } = sendBufferSize;
    public int? ReceiveBufferSize { get; set; } = recvBufferSize;
    public bool? HostAsServer { get; set; } = null;

    // Additional Unix Domain Socket-specific methods or properties can be added here
    // ...
}
#endif