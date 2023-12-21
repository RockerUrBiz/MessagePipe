using System.Threading;
using System.Threading.Tasks;

using MessagePipe.Interprocess.Internal;
using MessagePipe.Interprocess.Workers;

namespace MessagePipe.Interprocess
{
    [Preserve]
    public sealed class TcpDistributedPublisher<TKey, TMessage> : IDistributedPublisher<TKey, TMessage>
    {
        readonly TcpWorker worker;

        [Preserve]
        public TcpDistributedPublisher(TcpWorker worker)
        {
            this.worker = worker;
        }

        public ValueTask PublishAsync(TKey key, TMessage message, CancellationToken cancellationToken = default)
        {
            worker.Publish(key, message);
            return default;
        }
    }
}
