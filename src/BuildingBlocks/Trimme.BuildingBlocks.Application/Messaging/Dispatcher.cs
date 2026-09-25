using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace Trimme.BuildingBlocks.Application.Messaging;

/// <summary>
/// In-house mediator (decision D-037): resolves the single handler for a request and wraps it
/// in the registered <see cref="IPipelineBehavior{TRequest,TResult}"/>s.
/// </summary>
internal sealed class Dispatcher(IServiceProvider serviceProvider) : IDispatcher
{
    private static readonly ConcurrentDictionary<Type, object> Wrappers = new();

    public Task<TResult> Send<TResult>(IRequest<TResult> request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var wrapper = (RequestWrapper<TResult>)Wrappers.GetOrAdd(
            request.GetType(),
            static requestType => Activator.CreateInstance(
                typeof(RequestWrapper<,>).MakeGenericType(requestType, typeof(TResult)))!);

        return wrapper.Handle(request, serviceProvider, cancellationToken);
    }

    private abstract class RequestWrapper<TResult>
    {
        public abstract Task<TResult> Handle(IRequest<TResult> request, IServiceProvider services, CancellationToken cancellationToken);
    }

    private sealed class RequestWrapper<TRequest, TResult> : RequestWrapper<TResult>
        where TRequest : IRequest<TResult>
    {
        public override Task<TResult> Handle(IRequest<TResult> request, IServiceProvider services, CancellationToken cancellationToken)
        {
            var typedRequest = (TRequest)request;
            var handler = services.GetService<IRequestHandler<TRequest, TResult>>()
                ?? throw new InvalidOperationException($"No handler is registered for {typeof(TRequest).FullName}.");

            RequestHandlerDelegate<TResult> pipeline = () => handler.Handle(typedRequest, cancellationToken);

            // Reverse so the first registered behaviour becomes the outermost.
            foreach (var behavior in services.GetServices<IPipelineBehavior<TRequest, TResult>>().Reverse())
            {
                var next = pipeline;
                pipeline = () => behavior.Handle(typedRequest, next, cancellationToken);
            }

            return pipeline();
        }
    }
}
