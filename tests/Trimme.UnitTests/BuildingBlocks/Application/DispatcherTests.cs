using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Trimme.BuildingBlocks.Application;
using Trimme.BuildingBlocks.Application.Messaging;
using Trimme.BuildingBlocks.Application.Validation;

namespace Trimme.UnitTests.BuildingBlocks.Application;

public sealed class DispatcherTests
{
    [Fact]
    public async Task Send_invokes_the_registered_handler()
    {
        await using var scope = BuildScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        var result = await dispatcher.Send(new Greet("Sara"), TestContext.Current.CancellationToken);

        result.ShouldBe("Hello Sara");
    }

    [Fact]
    public async Task Behaviours_wrap_the_handler_in_registration_order()
    {
        var journal = new List<string>();
        await using var scope = BuildScope(services =>
        {
            services.AddSingleton(journal);
            services.AddScoped(typeof(IPipelineBehavior<,>), typeof(OuterBehavior<,>));
            services.AddScoped(typeof(IPipelineBehavior<,>), typeof(InnerBehavior<,>));
        });

        await scope.ServiceProvider.GetRequiredService<IDispatcher>().Send(new Greet("Omar"), TestContext.Current.CancellationToken);

        journal.ShouldBe(["outer:before", "inner:before", "inner:after", "outer:after"]);
    }

    [Fact]
    public async Task Missing_handler_throws_a_clear_error()
    {
        await using var scope = BuildScope();

        var exception = await Should.ThrowAsync<InvalidOperationException>(() =>
            scope.ServiceProvider.GetRequiredService<IDispatcher>().Send(new Unhandled(), TestContext.Current.CancellationToken));

        exception.Message.ShouldContain(nameof(Unhandled));
    }

    [Fact]
    public async Task Validation_failure_throws_with_camel_case_fields_and_codes()
    {
        await using var scope = BuildScope();

        var exception = await Should.ThrowAsync<RequestValidationException>(() =>
            scope.ServiceProvider.GetRequiredService<IDispatcher>().Send(new Greet(string.Empty), TestContext.Current.CancellationToken));

        exception.Errors.ShouldContainKey("name");
        exception.Errors["name"].ShouldBe(["greet.name_required"]);
    }

    [Fact]
    public void AddTrimmeApplication_is_idempotent()
    {
        var services = new ServiceCollection();
        services.AddTrimmeApplication();
        services.AddTrimmeApplication();

        services.Count(d => d.ServiceType == typeof(IDispatcher)).ShouldBe(1);
        services.Count(d => d.ServiceType == typeof(IPipelineBehavior<,>)).ShouldBe(1);
    }

    private static AsyncServiceScope BuildScope(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddTrimmeApplication();
        services.AddScoped<IRequestHandler<Greet, string>, GreetHandler>();
        services.AddScoped<IValidator<Greet>, GreetValidator>();
        configure?.Invoke(services);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }).CreateAsyncScope();
    }

    internal sealed record Greet(string Name) : IQuery<string>;

    internal sealed record Unhandled : ICommand<int>;

    internal sealed class GreetHandler : IQueryHandler<Greet, string>
    {
        public Task<string> Handle(Greet request, CancellationToken cancellationToken) => Task.FromResult($"Hello {request.Name}");
    }

    internal sealed class GreetValidator : AbstractValidator<Greet>
    {
        public GreetValidator() => RuleFor(g => g.Name).NotEmpty().WithErrorCode("greet.name_required");
    }

    internal sealed class OuterBehavior<TRequest, TResult>(List<string> journal) : IPipelineBehavior<TRequest, TResult>
        where TRequest : IRequest<TResult>
    {
        public async Task<TResult> Handle(TRequest request, RequestHandlerDelegate<TResult> next, CancellationToken cancellationToken)
        {
            journal.Add("outer:before");
            var result = await next();
            journal.Add("outer:after");
            return result;
        }
    }

    internal sealed class InnerBehavior<TRequest, TResult>(List<string> journal) : IPipelineBehavior<TRequest, TResult>
        where TRequest : IRequest<TResult>
    {
        public async Task<TResult> Handle(TRequest request, RequestHandlerDelegate<TResult> next, CancellationToken cancellationToken)
        {
            journal.Add("inner:before");
            var result = await next();
            journal.Add("inner:after");
            return result;
        }
    }
}
