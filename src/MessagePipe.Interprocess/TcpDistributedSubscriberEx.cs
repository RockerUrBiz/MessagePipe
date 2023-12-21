namespace MessagePipe.Interprocess;

[MessagePipe.Interprocess.Internal.PreserveAttribute]
public sealed class TcpDistributedSubscriberEx<TKey, TMessage> : IDistributedSubscriber<TKey, TMessage>
{
    // Pubsished from UdpWorker.
    readonly MessagePipeInterprocessOptions options;
    readonly IAsyncSubscriber<IInterprocessKey, IInterprocessValue> subscriberCore;
    readonly FilterAttachedMessageHandlerFactory syncHandlerFactory;
    readonly FilterAttachedAsyncMessageHandlerFactory asyncHandlerFactory;

    [MessagePipe.Interprocess.Internal.PreserveAttribute]
    public TcpDistributedSubscriberEx(TcpWorkerEx worker,
                                      MessagePipeInterprocessTcpOptions options,
                                      IAsyncSubscriber<IInterprocessKey, IInterprocessValue> subscriberCore,
                                      FilterAttachedMessageHandlerFactory syncHandlerFactory,
                                      FilterAttachedAsyncMessageHandlerFactory asyncHandlerFactory)
    {
        this.options = options;
        this.subscriberCore = subscriberCore;
        this.syncHandlerFactory = syncHandlerFactory;
        this.asyncHandlerFactory = asyncHandlerFactory;

        worker.StartReceiver();
    }
#if NET5_0_OR_GREATER
    [MessagePipe.Interprocess.Internal.PreserveAttribute]
    public TcpDistributedSubscriberEx(TcpWorkerEx worker,
                                      MessagePipeInterprocessTcpUdsOptions options,
                                      IAsyncSubscriber<IInterprocessKey, IInterprocessValue> subscriberCore,
                                      FilterAttachedMessageHandlerFactory syncHandlerFactory,
                                      FilterAttachedAsyncMessageHandlerFactory asyncHandlerFactory)
    {
        this.options = options;
        this.subscriberCore = subscriberCore;
        this.syncHandlerFactory = syncHandlerFactory;
        this.asyncHandlerFactory = asyncHandlerFactory;

        worker.StartReceiver();
    }
#endif
    public System.Threading.Tasks.ValueTask<System.IAsyncDisposable> SubscribeAsync(TKey key, IMessageHandler<TMessage> handler, System.Threading.CancellationToken cancellationToken = default)
    {
        return SubscribeAsync(key, handler, System.Array.Empty<MessageHandlerFilter<TMessage>>(), cancellationToken);
    }

    public System.Threading.Tasks.ValueTask<System.IAsyncDisposable> SubscribeAsync(TKey key, IMessageHandler<TMessage> handler, MessageHandlerFilter<TMessage>[] filters, System.Threading.CancellationToken cancellationToken = default)
    {
        handler = syncHandlerFactory.CreateMessageHandler(handler, filters);
        var transform = new MessagePipe.Interprocess.Internal.TransformSyncMessageHandler<TMessage>(handler, options.MessagePackSerializerOptions);
        return SubscribeCore(key, transform);
    }

    public System.Threading.Tasks.ValueTask<System.IAsyncDisposable> SubscribeAsync(TKey key, IAsyncMessageHandler<TMessage> handler, System.Threading.CancellationToken cancellationToken = default)
    {
        return SubscribeAsync(key, handler, System.Array.Empty<AsyncMessageHandlerFilter<TMessage>>(), cancellationToken);
    }

    public System.Threading.Tasks.ValueTask<System.IAsyncDisposable> SubscribeAsync(TKey key, IAsyncMessageHandler<TMessage> handler, AsyncMessageHandlerFilter<TMessage>[] filters, System.Threading.CancellationToken cancellationToken = default)
    {
        handler = asyncHandlerFactory.CreateAsyncMessageHandler(handler, filters);
        var transform = new MessagePipe.Interprocess.Internal.TransformAsyncMessageHandler<TMessage>(handler, options.MessagePackSerializerOptions);
        return SubscribeCore(key, transform);
    }

    System.Threading.Tasks.ValueTask<System.IAsyncDisposable> SubscribeCore(TKey key, IAsyncMessageHandler<IInterprocessValue> handler)
    {
        var byteKey = MessageBuilder.CreateKey(key, options.MessagePackSerializerOptions);
        var d = subscriberCore.Subscribe(byteKey, handler);
        return new System.Threading.Tasks.ValueTask<System.IAsyncDisposable>(new MessagePipe.Interprocess.Internal.AsyncDisposableBridge(d));
    }
}