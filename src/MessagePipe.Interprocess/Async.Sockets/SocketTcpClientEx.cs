using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace MessagePipe.Interprocess.Async.Sockets
{
    internal sealed class SocketTcpClientEx : IDisposable
    {
        readonly Socket socket;
        private readonly SocketAsyncEventArgsPool saeaPool;

        public SocketTcpClientEx(AddressFamily addressFamily, ProtocolType protocolType, SocketAsyncEventArgsPool pool)
        {
            socket = new Socket(addressFamily, SocketType.Stream, protocolType);
            saeaPool = pool;
        }

        internal SocketTcpClientEx(Socket socket, SocketAsyncEventArgsPool pool)
        {
            this.socket = socket;
            saeaPool = pool;
        }

        // ... Connect methods remain the same ...

        public static SocketTcpClientEx Connect(string host, int port, SocketAsyncEventArgsPool pool)
        {
            var ip = new IPEndPoint(IPAddress.Parse(host), port);
            var client = new SocketTcpClientEx(ip.AddressFamily, ProtocolType.Tcp, pool);
            client.socket.Connect(ip);
            return client;
        }

#if NET5_0_OR_GREATER
        public static SocketTcpClientEx ConnectUds(string domainSocketPath, SocketAsyncEventArgsPool pool)
        {
            var client = new SocketTcpClientEx(AddressFamily.Unix, ProtocolType.IP, pool);
            client.socket.Connect(new UnixDomainSocketEndPoint(domainSocketPath));
            return client;
        }
#endif

        public async Task<int> ReceiveAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            var args = saeaPool.Rent();
            if (args == null)
                throw new InvalidOperationException("No available SocketAsyncEventArgs in the pool.");

            args.SetBuffer(buffer, offset, count);

            var tcs = new TaskCompletionSource<int>();
            args.Completed += (sender, e) =>
            {
                if (e.SocketError == SocketError.Success)
                    tcs.TrySetResult(e.BytesTransferred);
                else
                    tcs.TrySetException(new SocketException((int)e.SocketError));

                saeaPool.Return(args);
            };

            if (!socket.ReceiveAsync(args))
                tcs.TrySetResult(args.BytesTransferred);

            await using (cancellationToken.Register(() => tcs.TrySetCanceled()))
            {
                return await tcs.Task.ConfigureAwait(false);
            }
        }

        public async Task<int> SendAsync(byte[] buffer, CancellationToken cancellationToken = default)
        {
            var args = saeaPool.Rent();
            if (args == null)
                throw new InvalidOperationException("No available SocketAsyncEventArgs in the pool.");

            args.SetBuffer(buffer, 0, buffer.Length);

            var tcs = new TaskCompletionSource<int>();
            args.Completed += (sender, e) =>
            {
                if (e.SocketError == SocketError.Success)
                    tcs.TrySetResult(e.BytesTransferred);
                else
                    tcs.TrySetException(new SocketException((int)e.SocketError));

                saeaPool.Return(args);
            };

            if (!socket.SendAsync(args))
                tcs.TrySetResult(args.BytesTransferred);

            await using (cancellationToken.Register(() => tcs.TrySetCanceled()))
            {
                return await tcs.Task.ConfigureAwait(false);
            }
        }

        public void Dispose()
        {
            socket.Dispose();
            // Do not dispose of the pool here, as it's shared and might be used elsewhere
        }
    }
}