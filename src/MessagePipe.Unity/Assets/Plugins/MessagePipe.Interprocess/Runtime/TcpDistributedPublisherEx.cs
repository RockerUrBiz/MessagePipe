namespace MessagePipe.Interprocess;

[MessagePipe.Interprocess.Internal.PreserveAttribute]
[method: MessagePipe.Interprocess.Internal.PreserveAttribute]
public sealed class TcpDistributedPublisherEx<TKey, TMessage>(TcpWorkerEx worker) : IDistributedPublisher<TKey, TMessage>
{
    public System.Threading.Tasks.ValueTask PublishAsync(TKey key, TMessage message, System.Threading.CancellationToken cancellationToken = default)
    {
        worker.Publish(key, message);
        return default;
    }
}