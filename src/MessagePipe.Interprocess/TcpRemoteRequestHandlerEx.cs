namespace MessagePipe.Interprocess;

public sealed class TcpRemoteRequestHandlerEx<TRequest, TResponse> : IRemoteRequestHandler<TRequest, TResponse>
{
    readonly TcpWorkerEx worker;

    [MessagePipe.Interprocess.Internal.PreserveAttribute]
    public TcpRemoteRequestHandlerEx(TcpWorkerEx worker)
    {
        this.worker = worker;
    }

    public async System.Threading.Tasks.ValueTask<TResponse> InvokeAsync(TRequest request, System.Threading.CancellationToken cancellationToken = default)
    {
        return await worker.RequestAsync<TRequest, TResponse>(request, cancellationToken);
    }
}