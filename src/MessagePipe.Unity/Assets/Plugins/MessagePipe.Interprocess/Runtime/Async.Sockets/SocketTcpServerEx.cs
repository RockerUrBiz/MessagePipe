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

    public SocketTcpServerEx(AddressFamily addressFamily, ProtocolType protocolType, int? sendBufferSize, int? recvBufferSize, SocketAsyncEventArgsPool pool)
    {
        socket = new Socket(addressFamily, SocketType.Stream, protocolType);
        if (sendBufferSize.HasValue)
            socket.SendBufferSize = sendBufferSize.Value;
        if (recvBufferSize.HasValue)
            socket.ReceiveBufferSize = recvBufferSize.Value;

        _maxNumberAcceptedClients = new SemaphoreSlim(MaxConnections); // Initialize the semaphore
        saeaPool = pool;
    }

    public void StartAcceptLoopAsync(Action<SocketTcpClientEx> onAccept, CancellationToken cancellationToken)
    {
        void AcceptCallback(object sender, SocketAsyncEventArgs e)
        {
            if (e.SocketError == SocketError.Success)
            {
                var clientSocket = e.AcceptSocket;
                var client = new SocketTcpClientEx(clientSocket, saeaPool);
                onAccept(client);
            }
            else
            {
                // Handle accept errors here, if necessary
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                e.AcceptSocket = null;
                StartAccept(e);
            }
            else
            {
                saeaPool.Return(e);
            }
        }

        void StartAccept(SocketAsyncEventArgs e)
        {
            e.Completed += new EventHandler<SocketAsyncEventArgs>(AcceptCallback);
            if (!socket.AcceptAsync(e))
            {
                AcceptCallback(this, e);
            }
        }

        var acceptEventArgs = saeaPool.Rent();
        StartAccept(acceptEventArgs);
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
