namespace Trimme.BuildingBlocks.Application.Messaging;

/// <summary>A request dispatched through <see cref="IDispatcher"/>. Prefer <see cref="ICommand{TResult}"/> or <see cref="IQuery{TResult}"/>.</summary>
public interface IRequest<TResult>;

/// <summary>A state-changing request. Commands run inside the transaction behaviour (added with persistence features).</summary>
public interface ICommand<TResult> : IRequest<TResult>;

/// <summary>A read-only request.</summary>
public interface IQuery<TResult> : IRequest<TResult>;

public interface IRequestHandler<in TRequest, TResult>
    where TRequest : IRequest<TResult>
{
    Task<TResult> Handle(TRequest request, CancellationToken cancellationToken);
}

public interface ICommandHandler<in TCommand, TResult> : IRequestHandler<TCommand, TResult>
    where TCommand : ICommand<TResult>;

public interface IQueryHandler<in TQuery, TResult> : IRequestHandler<TQuery, TResult>
    where TQuery : IQuery<TResult>;

public delegate Task<TResult> RequestHandlerDelegate<TResult>();

/// <summary>
/// Cross-cutting step around every handler (validation, logging, transactions).
/// Behaviours run in registration order: the first registered is the outermost.
/// </summary>
public interface IPipelineBehavior<in TRequest, TResult>
    where TRequest : IRequest<TResult>
{
    Task<TResult> Handle(TRequest request, RequestHandlerDelegate<TResult> next, CancellationToken cancellationToken);
}

public interface IDispatcher
{
    Task<TResult> Send<TResult>(IRequest<TResult> request, CancellationToken cancellationToken);
}
