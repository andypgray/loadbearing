using Meridian.Quoting.Application.Abstractions;
using Zphil.LoadBearing;

namespace Meridian.Quoting.ArchSpec;

/// <summary>
///     The quoting subsystem's architecture spec — the greenfield strangler step after the
///     Meridian monolith migrated its quoting controllers. Enforce-only, born conforming, with
///     no baselines.
/// </summary>
public sealed class QuotingArchSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        Layer domain = arch.Layer("Domain", "Meridian.Quoting.Domain.*");
        Layer application = arch.Layer("Application", "Meridian.Quoting.Application.*");
        Layer infrastructure = arch.Layer("Infrastructure", "Meridian.Quoting.Infrastructure.*");
        Layer api = arch.Layer("Api", "Meridian.Quoting.Api.*");

        arch.Rule("layering/domain-independent")
            .Enforce(domain.MustNotReference(application, infrastructure, api))
            .Because("The Domain holds the quote and rate model the rest of the subsystem is built on; it stays free of the layers that depend on it so it can be reasoned about and tested on its own.")
            .Fix("Move the dependency out of Domain: define an interface here and implement it in the outer layer that needs it.");

        arch.Rule("layering/application-boundaries")
            .Enforce(application.MustOnlyReference(application, domain))
            .Because("Use cases depend on the Domain and on abstractions they own, never on a concrete adapter; keeping Infrastructure and Api out of Application is what lets persistence and transport be swapped or faked in a test.")
            .Fix("Depend on a port (an interface in Domain or Application) instead of the concrete type; wire the implementation in the Api composition root.");

        arch.Rule("naming/interfaces")
            .Enforce(arch.Types.OfKind(TypeKind.Interface).InNamespace("Meridian.Quoting.*").MustHavePrefix("I"))
            .Because("The `I` prefix is how a reader and an agent tell a port from its implementation at a glance; the convention stays total so the distinction is reliable.");

        arch.Rule("naming/controllers")
            .Enforce(arch.Types.InNamespace("Meridian.Quoting.Api.Controllers.*").MustHaveSuffix("Controller"))
            .Because("Request handlers are found by their `*Controller` name, by routing and by agents reading the code; keep the convention total.");

        arch.Rule("handlers/naming")
            .Enforce(arch.Types.Implementing<IHandler>().MustHaveSuffix("Handler"))
            .Because("Handlers are registered and resolved by the `IHandler` marker; the `*Handler` suffix keeps them greppable and the registration total.");

        arch.Rule("handlers/transactional")
            .Enforce(arch.Types.Implementing(typeof(ICommandHandler<>)).MustBeAttributedWith<TransactionalAttribute>())
            .Because("Every command here mutates the store, and the command bus opens a unit of work only around a handler marked `[Transactional]`; an unmarked command handler would commit each write on its own and leave a half-written quote if it failed midway.")
            .Fix("Add `[Transactional]` to the command handler (see RequestQuoteHandler); if the work is read-only, make it a query handler instead.");

        arch.Rule("persistence/repository-ports")
            .Enforce(arch.Types.OfKind(TypeKind.Interface).WithSuffix("Repository").MustResideInNamespace("Meridian.Quoting.Domain.*"))
            .Because("A `*Repository` is the Domain's own persistence contract; the interface lives with the model it serves and only its implementation lives in Infrastructure. This is the boundary that keeps data access out of the request path.")
            .Fix("Move the `*Repository` interface into Meridian.Quoting.Domain; leave the implementation in Meridian.Quoting.Infrastructure.");

        arch.Rule("messaging/immutable-messages")
            .Enforce(arch.Types.InNamespace("Meridian.Quoting.Application.Messages.*")
                         .Must(t => t.IsRecord, description: "be declared as records"))
            .Because("Commands, queries, and the views they return are values that cross the dispatch boundary; record equality and init-only members keep a message from being mutated after it is sent.")
            .Fix("Declare the type as a `record` (or `record struct`).");

        arch.Rule("time/injected-clock")
            .Enforce(arch.Types.InNamespace("Meridian.Quoting.*")
                         .Except(arch.Types.WithNameMatching("SystemClock"))
                         .MustNotUse(
                             () => DateTime.Now,
                             () => DateTime.UtcNow))
            .Because("A quote's validity window is computed from the current instant; read straight from the wall clock it cannot be tested at a fixed moment, so time enters through IClock and SystemClock is the one adapter that reads the machine clock.")
            .Fix("Take IClock in the constructor and read clock.UtcNow; see SystemClock for the single sanctioned wall-clock read.");
    }
}
