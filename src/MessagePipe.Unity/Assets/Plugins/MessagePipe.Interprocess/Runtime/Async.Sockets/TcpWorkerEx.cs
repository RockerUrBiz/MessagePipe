using MessagePipe.Interprocess.Internal;

#if !UNITY_2018_3_OR_NEWER
using System.Threading.Channels;
#endif
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

using MessagePack;

using Microsoft.Extensions.DependencyInjection;

using System.Linq;

namespace MessagePipe.Interprocess.Async.Sockets
{

    namespace MessagePipe.Interprocess.Async.Sockets
    {
        [Preserve]
        public sealed class TcpWorkerEx : IDisposable
        {
            readonly IServiceProvider provider;
            readonly CancellationTokenSource cancellationTokenSource;
            readonly IAsyncPublisher<IInterprocessKey, IInterprocessValue> publisher;
            readonly MessagePipeInterprocessOptions _optionsEx;

            int initializedServer = 0;
            Lazy<SocketTcpServerEx> server; // Note the use of SocketTcpServerEx
            Channel<byte[]> channel;

            int initializedClient = 0;
            Lazy<SocketTcpClientEx> client; // Note the use of SocketTcpClientEx

            int messageId = 0;
            ConcurrentDictionary<int, TaskCompletionSource<IInterprocessValue>> responseCompletions;

            private readonly SocketAsyncEventArgsPool _saeaSaeaPool;

            [Preserve]
            public TcpWorkerEx(IServiceProvider provider, MessagePipeInterprocessTcpOptionsEx optionsEx, IAsyncPublisher<IInterprocessKey, IInterprocessValue> publisher, SocketAsyncEventArgsPool saeaPool)
            {
                this.provider = provider;
                this.cancellationTokenSource = new CancellationTokenSource();
                this._optionsEx = optionsEx;
                this.publisher = publisher;
                this._saeaSaeaPool = saeaPool;

                responseCompletions = new ConcurrentDictionary<int, TaskCompletionSource<IInterprocessValue>>();

                this.server = new Lazy<SocketTcpServerEx>(() =>
                                                          {
                                                              // Determine the AddressFamily from the host
                                                              System.Net.IPAddress ipAddress = System.Net.IPAddress.None;

                                                              if (System.Net.IPAddress.TryParse(optionsEx.Host, out ipAddress))
                                                              {
                                                                  // If optionsEx.Host is already an IP address
                                                                  return new SocketTcpServerEx(ipAddress.AddressFamily, System.Net.Sockets.ProtocolType.Tcp, null, null, _saeaSaeaPool);
                                                              }

                                                              // Resolve the hostname to an IPAddress
                                                              var addresses = System.Net.Dns.GetHostAddresses(optionsEx.Host);

                                                              if (addresses.Length == 0)
                                                              {
                                                                  throw new InvalidOperationException("Unable to resolve host: " + optionsEx.Host);
                                                              }

                                                              // Use the first address in the list (you might want to choose differently based on your requirements)
                                                              ipAddress = addresses[0];

                                                              return new SocketTcpServerEx(ipAddress.AddressFamily, System.Net.Sockets.ProtocolType.Tcp, null, null, _saeaSaeaPool);
                                                          });


                this.client = new Lazy<SocketTcpClientEx>(() => SocketTcpClientEx.Connect(optionsEx.Host, optionsEx.Port, _saeaSaeaPool));

                if (optionsEx.HostAsServer != null && optionsEx.HostAsServer.Value)
                {
                    StartReceiver();
                }
            }

            // Implementing StartReceiver method
            // Implementing StartReceiver method
            public void StartReceiver()
            {
                if (Interlocked.Increment(ref initializedServer) == 1) // first increment, channel not yet started
                {
                    var socketTcpServerEx = server.Value; // initialize
                    socketTcpServerEx.StartAcceptLoopAsync(RunReceiveLoop, cancellationTokenSource.Token);
                }
            }

            // Implementing Publish method
            // Correcting method calls in other parts of TcpWorkerEx
            public void Publish<TKey, TMessage>(TKey key, TMessage message)
            {
                if (Interlocked.Increment(ref initializedClient) == 1)
                {
                    _ = client.Value; // Access the client instance
                    RunPublishLoop();
                }

                var buffer = MessageBuilder.BuildPubSubMessage(key, message, _optionsEx.MessagePackSerializerOptions);
                channel.Writer.TryWrite(buffer);
            }

            // Implementing RequestAsync method
            public async ValueTask<TResponse> RequestAsync<TRequest, TResponse>(TRequest request, CancellationToken cancellationToken)
            {
                if (Interlocked.Increment(ref initializedClient) == 1) // first increment, channel not yet started
                {
                    _ = client.Value; // initialize
                    RunPublishLoop();
                }

                var mid = Interlocked.Increment(ref messageId);
                var tcs = new TaskCompletionSource<IInterprocessValue>();
                responseCompletions[mid] = tcs;

                var buffer = MessageBuilder.BuildRemoteRequestMessage(typeof(TRequest), typeof(TResponse), mid, request, _optionsEx.MessagePackSerializerOptions);
                channel.Writer.TryWrite(buffer);

                var memoryValue = await tcs.Task.ConfigureAwait(false);
                return MessagePackSerializer.Deserialize<TResponse>(memoryValue.ValueMemory, _optionsEx.MessagePackSerializerOptions);
            }

            // Implementing RunPublishLoop method
            async void RunPublishLoop()
            {
                var reader = channel.Reader;
                var token = cancellationTokenSource.Token;
                var tcpClient = client.Value; // Access the client instance

                while (await reader.WaitToReadAsync(token).ConfigureAwait(false))
                {
                    while (reader.TryRead(out var item))
                    {
                        try
                        {
                            await tcpClient.SendAsync(item, token).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            if (ex is OperationCanceledException || token.IsCancellationRequested) return;
                            _optionsEx.UnhandledErrorHandler?.Invoke("network error, publish loop will terminate." + Environment.NewLine, ex);
                            return;
                        }
                    }
                }
            }

            // Implementing RunReceiveLoop method
            async void RunReceiveLoop(SocketTcpClientEx client)
            {
                var token = cancellationTokenSource.Token;
                var buffer = new byte[65536]; // Buffer size may need to be adjusted

                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        var readLen = await client.ReceiveAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false);
                        if (readLen == 0) return; // end of stream (disconnect)

                        await ProcessReceivedMessage(buffer.AsMemory(0, readLen));
                    }
                    catch (Exception ex)
                    {
                        if (ex is OperationCanceledException || token.IsCancellationRequested) return;
                        _optionsEx.UnhandledErrorHandler?.Invoke("network error, receive loop will terminate." + Environment.NewLine, ex);
                        return;
                    }
                }
            }

            // Implementing ProcessReceivedMessage method
            private async Task ProcessReceivedMessage(ReadOnlyMemory<byte> message)
            {
                var token = cancellationTokenSource.Token;
                var buffer = new byte[65536];
                ReadOnlyMemory<byte> readBuffer = Array.Empty<byte>();
                while (!token.IsCancellationRequested)
                {
                    ReadOnlyMemory<byte> value = Array.Empty<byte>();
                    try
                    {
                        if (readBuffer.Length == 0)
                        {
                            var readLen = await client.Value.ReceiveAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false);
                            if (readLen == 0) return; // end of stream(disconnect)
                            readBuffer = buffer.AsMemory(0, readLen);
                        }
                        else if (readBuffer.Length < 4) // rare case
                        {
                            var readLen = await client.Value.ReceiveAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false);
                            if (readLen == 0) return;
                            var newBuffer = new byte[readBuffer.Length + readLen];
                            readBuffer.CopyTo(newBuffer);
                            buffer.AsSpan(readLen).CopyTo(newBuffer.AsSpan(readBuffer.Length));
                            readBuffer = newBuffer;
                        }

                        var messageLen = MessageBuilder.FetchMessageLength(readBuffer.Span);
                        if (readBuffer.Length == (messageLen + 4)) // just size
                        {
                            value = readBuffer.Slice(4, messageLen); // skip length header
                            readBuffer = Array.Empty<byte>();
                            goto PARSE_MESSAGE;
                        }
                        else if (readBuffer.Length > (messageLen + 4)) // over size
                        {
                            value = readBuffer.Slice(4, messageLen);
                            readBuffer = readBuffer.Slice(messageLen + 4);
                            goto PARSE_MESSAGE;
                        }
                        else // needs to read more
                        {
                            var readLen = readBuffer.Length;
                            if (readLen < (messageLen + 4))
                            {
                                if (readBuffer.Length != buffer.Length)
                                {
                                    var newBuffer = new byte[buffer.Length];
                                    readBuffer.CopyTo(newBuffer);
                                    buffer = newBuffer;
                                }

                                if (buffer.Length < messageLen + 4)
                                {
                                    Array.Resize(ref buffer, messageLen + 4);
                                }
                            }
                            var remain = messageLen - (readLen - 4);
                            await ReadFullyAsync(buffer, client, readLen, remain, token).ConfigureAwait(false);
                            value = buffer.AsMemory(4, messageLen);
                            readBuffer = Array.Empty<byte>();
                            goto PARSE_MESSAGE;
                        }
                    }
                    catch (Exception ex)
                    {
                        if (ex is OperationCanceledException) return;
                        if (token.IsCancellationRequested) return;

                        // network error, terminate.
                        _optionsEx.UnhandledErrorHandler("network error, receive loop will terminate." + Environment.NewLine, ex);
                        return;
                    }
                PARSE_MESSAGE:
                    try
                    {
                        var interprocessMessage = MessageBuilder.ReadPubSubMessage(value.ToArray()); // can avoid copy?

                        switch (interprocessMessage.MessageType)
                        {
                            case MessageType.PubSub:
                                publisher.Publish(interprocessMessage, interprocessMessage, CancellationToken.None);
                                break;
                            case MessageType.RemoteRequest:
                                {
                                    // NOTE: should use without reflection(Expression.Compile)
                                    var header = Deserialize<RequestHeader>(interprocessMessage.KeyMemory, _optionsEx.MessagePackSerializerOptions);
                                    var (mid, reqTypeName, resTypeName) = (header.MessageId, header.RequestType, header.ResponseType);
                                    byte[] resultBytes;
                                    try
                                    {
                                        var t = AsyncRequestHandlerRegistory.Get(reqTypeName, resTypeName);
                                        var interfaceType = t.GetInterfaces().Where(x => x.IsGenericType && x.Name.StartsWith("IAsyncRequestHandler"))
                                            .First(x => x.GetGenericArguments().Any(y => y.FullName == header.RequestType));
                                        var coreInterfaceType = t.GetInterfaces().Where(x => x.IsGenericType && x.Name.StartsWith("IAsyncRequestHandlerCore"))
                                            .First(x => x.GetGenericArguments().Any(y => y.FullName == header.RequestType));
                                        var service = provider.GetRequiredService(interfaceType); // IAsyncRequestHandler<TRequest,TResponse>
                                        var genericArgs = interfaceType.GetGenericArguments(); // [TRequest, TResponse]
                                                                                               // Unity IL2CPP does not work(can not invoke nongenerics MessagePackSerializer)
                                        var request = MessagePackSerializer.Deserialize(genericArgs[0], interprocessMessage.ValueMemory, _optionsEx.MessagePackSerializerOptions);
                                        var responseTask = coreInterfaceType.GetMethod("InvokeAsync")!.Invoke(service, new[] { request, CancellationToken.None });
#if !UNITY_2018_3_OR_NEWER
                                        var task = typeof(ValueTask<>).MakeGenericType(genericArgs[1]).GetMethod("AsTask")!.Invoke(responseTask, null);
#else
                                    var asTask = typeof(UniTaskExtensions).GetMethods().First(x => x.IsGenericMethod && x.Name == "AsTask")
                                        .MakeGenericMethod(genericArgs[1]);
                                    var task = asTask.Invoke(null, new[] { responseTask });
#endif
                                        await ((System.Threading.Tasks.Task)task!); // Task<T> -> Task
                                        var result = task.GetType().GetProperty("Result")!.GetValue(task);
                                        resultBytes = MessageBuilder.BuildRemoteResponseMessage(mid, genericArgs[1], result!, _optionsEx.MessagePackSerializerOptions);
                                    }
                                    catch (Exception ex)
                                    {
                                        // NOTE: ok to send stacktrace?
                                        resultBytes = MessageBuilder.BuildRemoteResponseError(mid, ex.ToString(), _optionsEx.MessagePackSerializerOptions);
                                    }

                                    await client.Value.SendAsync(resultBytes, token).ConfigureAwait(false);
                                }
                                break;
                            case MessageType.RemoteResponse:
                            case MessageType.RemoteError:
                                {
                                    var mid = Deserialize<int>(interprocessMessage.KeyMemory, _optionsEx.MessagePackSerializerOptions);
                                    if (responseCompletions.TryRemove(mid, out var tcs))
                                    {
                                        if (interprocessMessage.MessageType == MessageType.RemoteResponse)
                                        {
                                            tcs.TrySetResult(interprocessMessage); // synchronous completion, use memory buffer immediately.
                                        }
                                        else
                                        {
                                            var errorMsg = MessagePackSerializer.Deserialize<string>(interprocessMessage.ValueMemory, _optionsEx.MessagePackSerializerOptions);
                                            tcs.TrySetException(new RemoteRequestException(errorMsg));
                                        }
                                    }
                                }
                                break;
                            default:
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        if (ex is OperationCanceledException) continue;
                        _optionsEx.UnhandledErrorHandler("", ex);
                    }
                }
            }

            [System.Runtime.CompilerServices.MethodImplAttribute(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
            static T Deserialize<T>(ReadOnlyMemory<byte> buffer, MessagePackSerializerOptions options)
            {
                if (buffer.IsEmpty && System.Runtime.InteropServices.MemoryMarshal.TryGetArray(buffer, out var segment))
                {
                    buffer = segment;
                }
                return MessagePackSerializer.Deserialize<T>(buffer, options);
            }

            static async ValueTask ReadFullyAsync(byte[] buffer, Lazy<SocketTcpClientEx> client, int index, int remain, CancellationToken token)
            {
                while (remain > 0)
                {
                    var len = await client.Value.ReceiveAsync(buffer, index, remain, token).ConfigureAwait(false);
                    index += len;
                    remain -= len;
                }
            }


            // Dispose and other methods...

            #region Implementation of IDisposable

            public void Dispose()
            {
                throw new NotImplementedException();
            }

            #endregion
        }
    }
}
