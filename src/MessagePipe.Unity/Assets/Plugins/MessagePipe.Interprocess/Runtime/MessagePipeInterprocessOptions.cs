using System;

using MessagePack;
using MessagePack.Resolvers;

namespace MessagePipe.Interprocess
{
    public abstract class MessagePipeInterprocessOptions
    {
        public MessagePackSerializerOptions MessagePackSerializerOptions { get; set; }
        public InstanceLifetime InstanceLifetime { get; set; }
        public Action<string, Exception> UnhandledErrorHandler { get; set; }

        protected MessagePipeInterprocessOptions()
        {
            this.MessagePackSerializerOptions = ContractlessStandardResolver.Options;
            this.InstanceLifetime = InstanceLifetime.Scoped;
#if !UNITY_2018_3_OR_NEWER
            this.UnhandledErrorHandler = (msg, x) => Console.WriteLine(msg + x);
#else
            this.UnhandledErrorHandler = (msg, x) => UnityEngine.Debug.Log(msg + x);
#endif
        }
    }

    public sealed class MessagePipeInterprocessUdpOptions(string host, int port) : MessagePipeInterprocessOptions
    {
        public string Host { get; } = host;
        public int Port { get; } = port;
    }

    public sealed class MessagePipeInterprocessNamedPipeOptions(string pipeName) : MessagePipeInterprocessOptions
    {
        public string PipeName { get; } = pipeName;
        public string ServerName { get; set; } = ".";
        public bool? HostAsServer { get; set; } = null;
    }

    public sealed class MessagePipeInterprocessTcpOptions(string host, int port) : MessagePipeInterprocessOptions
    {
        public string Host { get; } = host;
        public int Port { get; } = port;
        public bool? HostAsServer { get; set; } = null;
    }
#if NET5_0_OR_GREATER
    public sealed class MessagePipeInterprocessUdpUdsOptions(string socketPath) : MessagePipeInterprocessOptions
    {
        public string SocketPath { get; set; } = socketPath;
    }
    public sealed class MessagePipeInterprocessTcpUdsOptions(string socketPath,
                                                             int? sendBufferSize = null,
                                                             int? recvBufferSize = null) : MessagePipeInterprocessOptions
    {
        public string SocketPath { get; set; } = socketPath;
        public int? SendBufferSize { get; set; } = sendBufferSize;
        public int? ReceiveBufferSize { get; set; } = recvBufferSize;
        public bool? HostAsServer { get; set; } = null;
    }
#endif
}