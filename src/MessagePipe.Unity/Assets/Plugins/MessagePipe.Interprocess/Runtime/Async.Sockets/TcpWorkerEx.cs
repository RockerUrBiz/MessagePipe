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
using System.IO;

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

                // Initialize the channel
                channel = Channel.CreateUnbounded<byte[]>(); // or any other configuration that suits your needs


                this.server = new Lazy<SocketTcpServerEx>(() =>
                                                          {
                                                              // Determine the AddressFamily from the host
                                                              System.Net.IPAddress ipAddress = System.Net.IPAddress.None;

                                                              if (System.Net.IPAddress.TryParse(optionsEx.Host, out ipAddress))
                                                              {
                                                                  // If optionsEx.Host is already an IP address

                                                                  return new SocketTcpServerEx(ipAddress.AddressFamily, System.Net.Sockets.ProtocolType.Tcp,
                                                                                               null, null, _saeaSaeaPool, optionsEx.Host, optionsEx.Port,
                                                                                               optionsEx.InitialPoolCapacity, optionsEx.MaxPoolSize,
                                                                                               optionsEx.MinPoolSize, optionsEx.MaxIdleTime, optionsEx.ContractionInterval);

                                                                  //return new SocketTcpServerEx(ipAddress.AddressFamily, System.Net.Sockets.ProtocolType.Tcp, null, null, _saeaSaeaPool, optionsEx.Host, optionsEx.Port);


                                                              }

                                                              // Resolve the hostname to an IPAddress
                                                              var addresses = System.Net.Dns.GetHostAddresses(optionsEx.Host);

                                                              if (addresses.Length == 0)
                                                              {
                                                                  throw new InvalidOperationException("Unable to resolve host: " + optionsEx.Host);
                                                              }

                                                              // Use the first address in the list
                                                              ipAddress = addresses[0];

                                                              //return new SocketTcpServerEx(ipAddress.AddressFamily, System.Net.Sockets.ProtocolType.Tcp, null, null, _saeaSaeaPool, optionsEx.Host, optionsEx.Port);

                                                              return new SocketTcpServerEx(ipAddress.AddressFamily, System.Net.Sockets.ProtocolType.Tcp,
                                                                                           null, null, _saeaSaeaPool, optionsEx.Host, optionsEx.Port,
                                                                                           optionsEx.InitialPoolCapacity, optionsEx.MaxPoolSize,
                                                                                           optionsEx.MinPoolSize, optionsEx.MaxIdleTime, optionsEx.ContractionInterval);
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

            //async void RunReceiveLoop(SocketTcpClientEx client)
            //{
            //    var token = cancellationTokenSource.Token;
            //    var buffer = new byte[65536];

            //    while (!token.IsCancellationRequested)
            //    {
            //        try
            //        {
            //            int readLen = await client.ReceiveAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false);
            //            if (readLen == 0)
            //                return; // End of stream (disconnect)

            //            // Process the received data
            //            await ProcessReceivedMessage(buffer.AsMemory(0, readLen));
            //        }
            //        catch (Exception ex)
            //        {
            //            if (ex is OperationCanceledException || token.IsCancellationRequested)
            //                return;

            //            _optionsEx.UnhandledErrorHandler?.Invoke("network error, receive loop will terminate.", ex);
            //        }
            //    }
            //}

            /// <summary>
            /// Runs the receive loop.
            /// </summary>
            /// <param name="client">The client.</param>
            /// <autogeneratedoc />
            /// TODO Edit XML Comment Template for RunReceiveLoop
            private async void RunReceiveLoop(SocketTcpClientEx client)
            {
                var token = cancellationTokenSource.Token;
                var buffer = new byte[65536];

                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        int readLen = await client.ReceiveAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false);
                        if (readLen == 0)
                            return; // End of stream (disconnect)

                        // Process the received data
                        await ProcessReceivedMessage(client, buffer.AsMemory(0, readLen));
                    }
                    catch (Exception ex)
                    {
                        if (ex is OperationCanceledException || token.IsCancellationRequested)
                            return;

                        _optionsEx.UnhandledErrorHandler?.Invoke("network error, receive loop will terminate.", ex);
                    }
                }
            }

            /// <summary>
            /// Processes the received message.
            /// </summary>
            /// <param name="client">The client.</param>
            /// <param name="initialBuffer">The initial buffer.</param>
            /// <autogeneratedoc />
            /// TODO Edit XML Comment Template for ProcessReceivedMessage
            private async Task ProcessReceivedMessage(SocketTcpClientEx client, ReadOnlyMemory<byte> initialBuffer)
            {
                var token = cancellationTokenSource.Token;
                var buffer = new byte[65536];
                var readBuffer = initialBuffer; // Start with the initial buffer

                try
                {
                    while (true)
                    {
                        if (readBuffer.Length < 4)
                        {
                            // If less than 4 bytes are available, it's impossible to determine message length
                            readBuffer = await ReadMoreAsync(client, buffer, readBuffer, token);
                            continue;
                        }

                        int messageLen = MessageBuilder.FetchMessageLength(readBuffer.Span);
                        int totalMessageSize = messageLen + 4;

                        if (readBuffer.Length < totalMessageSize)
                        {
                            // Partial message received, need to read more
                            readBuffer = await ReadMoreAsync(client, buffer, readBuffer, token);
                            continue;
                        }

                        // Extract and process the message
                        var message = readBuffer.Slice(4, messageLen);
                        await ParseMessage(message, token);

                        // Check if there's more data in the buffer
                        if (readBuffer.Length > totalMessageSize)
                        {
                            readBuffer = readBuffer.Slice(totalMessageSize);
                        }
                        else
                        {
                            break; // No more data to process
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (ex is OperationCanceledException) return;
                    if (token.IsCancellationRequested) return;

                    // Network error, terminate.
                    _optionsEx.UnhandledErrorHandler?.Invoke("Network error in ProcessReceivedMessage.", ex);
                }
            }


            /// <summary>
            /// Processes the received message.
            /// </summary>
            /// <param name="message">The message.</param>
            /// <autogeneratedoc />
            /// TODO Edit XML Comment Template for ProcessReceivedMessage
            //private async Task ProcessReceivedMessage(ReadOnlyMemory<byte> message)
            //{
            //    var token = cancellationTokenSource.Token;
            //    var buffer = new byte[65536];
            //    ReadOnlyMemory<byte> readBuffer = message; // Use the message parameter directly

            //    try
            //    {
            //        ReadOnlyMemory<byte> value = Array.Empty<byte>();

            //        switch (readBuffer.Length)
            //        {
            //            case 0:
            //                {
            //                    // This condition should not be hit as 'message' is expected to have content
            //                    var readLen = await client.Value.ReceiveAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false);
            //                    if (readLen == 0) return; // End of stream (disconnect)
            //                    readBuffer = buffer.AsMemory(0, readLen);
            //                    break;
            //                }
            //            // Rare case
            //            case < 4:
            //                {
            //                    // This condition should also not be hit as 'message' is expected to be complete
            //                    var readLen = await client.Value.ReceiveAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false);
            //                    if (readLen == 0) return;
            //                    var newBuffer = new byte[readBuffer.Length + readLen];
            //                    readBuffer.CopyTo(newBuffer);
            //                    buffer.AsSpan(readLen).CopyTo(newBuffer.AsSpan(readBuffer.Length));
            //                    readBuffer = newBuffer;
            //                    break;
            //                }
            //        }

            //        // Read the message length

            //        var messageLen = MessageBuilder.FetchMessageLength(readBuffer.Span);

            //        if (readBuffer.Length == (messageLen + 4)) // Just size
            //        {
            //            value = readBuffer.Slice(4, messageLen); // Skip length header

            //            await ParseMessage(value, token);

            //            readBuffer = Array.Empty<byte>();
            //        }
            //        else if (readBuffer.Length > (messageLen + 4)) // Over size
            //        {
            //            value = readBuffer.Slice(4, messageLen);

            //            await ParseMessage(value, token);

            //            readBuffer = readBuffer.Slice(messageLen + 4);
            //        }
            //        else // Needs to read more
            //        {
            //            // This condition should not be hit as 'message' is expected to be complete
            //            var readLen = readBuffer.Length;

            //            if (readLen < (messageLen + 4))
            //            {
            //                if (readBuffer.Length != buffer.Length)
            //                {
            //                    var newBuffer = new byte[buffer.Length];
            //                    readBuffer.CopyTo(newBuffer);
            //                    buffer = newBuffer;
            //                }

            //                if (buffer.Length < messageLen + 4)
            //                {
            //                    Array.Resize(ref buffer, messageLen + 4);
            //                }
            //            }

            //            var remain = messageLen - (readLen - 4);

            //            await ReadFullyAsync(buffer, client, readLen, remain, token).ConfigureAwait(false);

            //            value = buffer.AsMemory(4, messageLen);

            //            await ParseMessage(value, token);

            //            readBuffer = Array.Empty<byte>();
            //        }
            //    }
            //    catch (Exception ex)
            //    {
            //        if (ex is OperationCanceledException) return;
            //        if (token.IsCancellationRequested) return;

            //        // network error, terminate.
            //        _optionsEx.UnhandledErrorHandler("network error, receive loop will terminate." + Environment.NewLine, ex);
            //        return;
            //    }

            //    return;

            //    // Local function to parse the message
            //}

            // Function to read more data from the socket
            private async Task<ReadOnlyMemory<byte>> ReadMoreAsync(SocketTcpClientEx client, byte[] buffer, ReadOnlyMemory<byte> currentBuffer, CancellationToken token)
            {
                int readLen = await client.ReceiveAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false);
                if (readLen == 0) throw new EndOfStreamException("Socket closed while reading.");

                var newBuffer = new byte[currentBuffer.Length + readLen];
                currentBuffer.CopyTo(newBuffer);
                buffer.AsMemory(0, readLen).CopyTo(newBuffer.AsMemory(currentBuffer.Length));

                return newBuffer;
            }

            /// <summary>
            /// Parses the message.
            /// </summary>
            /// <param name="readOnlyMemory">The read only memory.</param>
            /// <param name="cancellationToken">The cancellation token.</param>
            /// <exception cref="System.ArgumentOutOfRangeException"></exception>
            /// <autogeneratedoc />
            /// TODO Edit XML Comment Template for ParseMessage
            private async Task ParseMessage(ReadOnlyMemory<byte> readOnlyMemory, CancellationToken cancellationToken)
            {
                try
                {
                    var interprocessMessage = MessageBuilder.ReadPubSubMessage(readOnlyMemory.ToArray());

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
                                    var genericArgs = interfaceType.GetGenericArguments();        // [TRequest, TResponse]
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

                                await client.Value.SendAsync(resultBytes, cancellationToken).ConfigureAwait(false);
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
                            throw new ArgumentOutOfRangeException();
                    }
                }
                catch (Exception ex)
                {
                    _optionsEx.UnhandledErrorHandler("", ex);
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

            private bool _disposed = false; // Flag to indicate disposal status

            public void Dispose()
            {
                Dispose(true);
                GC.SuppressFinalize(this); // Suppress finalization
            }


            // Dispose and other methods...

            private void Dispose(bool disposing)
            {
                if (!_disposed)
                {
                    if (disposing)
                    {
                        // Dispose managed resources
                        cancellationTokenSource?.Cancel();
                        cancellationTokenSource?.Dispose();
                        channel?.Writer.Complete();
                        responseCompletions?.Clear();

                        // Dispose of other IDisposable members if any
                        // ...

                        // Dispose the server and client if they have been created
                        if (server.IsValueCreated)
                        {
                            server.Value.Dispose();
                        }
                        if (client.IsValueCreated)
                        {
                            client.Value.Dispose();
                        }

                        // Optionally dispose the SocketAsyncEventArgsPool
                        _saeaSaeaPool?.Dispose();
                    }

                    // Free unmanaged resources if there are any
                    // ...

                    _disposed = true;
                }
            }

            ~TcpWorkerEx()
            {
                Dispose(false);
            }
        }
    }
}
