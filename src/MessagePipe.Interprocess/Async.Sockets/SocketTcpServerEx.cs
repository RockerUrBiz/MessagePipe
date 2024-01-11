using System;
using System.Net.Sockets;
using System.Threading;

using MessagePipe.Interprocess.Async.Sockets; // Assuming this is the namespace for SocketAsyncEventArgsPool

internal sealed class SocketTcpServerEx : IDisposable
{
    const int MaxConnections = 0x7fffffff;
    readonly Socket socket;
    private readonly SemaphoreSlim _maxNumberAcceptedClients; // Semaphore to control the number of connections
    private readonly SocketAsyncEventArgsPool saeaPool;

    public SocketTcpServerEx(AddressFamily addressFamily,
                             ProtocolType protocolType,
                             int? sendBufferSize,
                             int? recvBufferSize,
                             SocketAsyncEventArgsPool pool,
                             string ipAddress,
                             int port,
                             int initialPoolCapacity,
                             int maxPoolSize,
                             int minPoolSize,
                             TimeSpan maxIdleTime,
                             TimeSpan contractionInterval)
    {
        socket = new Socket(addressFamily, SocketType.Stream, protocolType);

        if (sendBufferSize.HasValue) socket.SendBufferSize = sendBufferSize.Value;

        if (recvBufferSize.HasValue) socket.ReceiveBufferSize = recvBufferSize.Value;

        _maxNumberAcceptedClients = new SemaphoreSlim(MaxConnections);

        saeaPool = pool;

        // Bind the socket to the local endpoint
        var localEndPoint = new System.Net.IPEndPoint(System.Net.IPAddress.Parse(ipAddress), port);

        socket.Bind(localEndPoint);

        // Start listening on the socket
        socket.Listen(MaxConnections); // You can adjust the backlog parameter as needed

        // Initialize the pool
        saeaPool = new SocketAsyncEventArgsPool(initialPoolCapacity, maxPoolSize, minPoolSize, maxIdleTime, contractionInterval);
    }

    public SocketTcpServerEx(AddressFamily addressFamily, ProtocolType protocolType, int? sendBufferSize, int? recvBufferSize, SocketAsyncEventArgsPool pool, string ipAddress, int port)
    {
        socket = new Socket(addressFamily, SocketType.Stream, protocolType);
        if (sendBufferSize.HasValue)
            socket.SendBufferSize = sendBufferSize.Value;
        if (recvBufferSize.HasValue)
            socket.ReceiveBufferSize = recvBufferSize.Value;

        _maxNumberAcceptedClients = new SemaphoreSlim(MaxConnections);
        saeaPool = pool;

        // Bind the socket to the local endpoint
        var localEndPoint = new System.Net.IPEndPoint(System.Net.IPAddress.Parse(ipAddress), port);
        socket.Bind(localEndPoint);

        // Start listening on the socket
        socket.Listen(MaxConnections); // You can adjust the backlog parameter as needed
    }

    public void StartAcceptLoopAsync(Action<SocketTcpClientEx> onAccept, CancellationToken cancellationToken)
    {
        void AcceptCallback(object sender, SocketAsyncEventArgs e)
        {
            // Detach the event handler to avoid multiple invocations
            e.Completed -= AcceptCallback;

            if (e.SocketError == SocketError.Success)
            {
                var clientSocket = e.AcceptSocket;
                var client = new SocketTcpClientEx(clientSocket, saeaPool);
                onAccept(client);
            }
            else
            {
                // Handle accept errors here
                // ...
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                // Reset SAEA for reuse
                e.AcceptSocket = null;
                // Start a new accept operation
                StartAccept(e);
            }
            else
            {
                // Return SAEA to the pool
                saeaPool.Return(e);
            }
        }

        void StartAccept(SocketAsyncEventArgs e)
        {
            // Attach the event handler
            e.Completed += AcceptCallback;

            if (!socket.AcceptAsync(e))
            {
                // If the operation completed synchronously, call the callback directly
                AcceptCallback(socket, e);
            }
        }

        // Rent a SocketAsyncEventArgs from the pool and start the accept operation
        var acceptEventArgs = saeaPool.Rent();

        if (acceptEventArgs != null)
        {
            StartAccept(acceptEventArgs);
        }
        else
        {
            // Optionally handle the case when acceptEventArgs is null
            // ...
        }
    }


    public void StartAcceptLoop(Action<SocketTcpClientEx> onAccept)
    {
        var acceptEventArgs = saeaPool.Rent();
        if (acceptEventArgs != null)
        {
            acceptEventArgs.Completed += (sender, e) => ProcessAccept(e, onAccept);
            StartAccept(acceptEventArgs);
        }
        // Optionally handle the case when acceptEventArgs is null
    }

    private void StartAccept(SocketAsyncEventArgs acceptEventArgs)
    {
        acceptEventArgs.AcceptSocket = null; // Reset for reuse

        _maxNumberAcceptedClients.Wait(); // Wait for a slot to be available
        if (!socket.AcceptAsync(acceptEventArgs))
            ProcessAccept(acceptEventArgs, null);
    }

    private void ProcessAccept(SocketAsyncEventArgs e, Action<SocketTcpClientEx> onAccept)
    {
        if (e.SocketError == SocketError.Success)
        {
            var acceptedSocket = e.AcceptSocket;
            onAccept?.Invoke(new SocketTcpClientEx(acceptedSocket, saeaPool));

            _maxNumberAcceptedClients.Release(); // Release the semaphore as this socket is now processed
        }
        else
        {
            // Handle errors here
        }

        // Return the used SocketAsyncEventArgs instance to the pool
        saeaPool.Return(e);

        // Rent a new SocketAsyncEventArgs instance from the pool for the next accept operation
        var newArgs = saeaPool.Rent();

        if (newArgs != null)
        {
            newArgs.AcceptSocket = null; // Reset for reuse
            StartAccept(newArgs);
        }
        // Optionally handle the case when newArgs is null
    }

    public void Dispose()
    {
        socket.Dispose();
        // Do not dispose of the pool here, as it might be shared
    }
}
