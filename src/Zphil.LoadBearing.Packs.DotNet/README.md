# Zphil.LoadBearing.Packs.DotNet

`Zphil.LoadBearing.Packs.DotNet` is a rule pack: nine canonical .NET rules as an ordinary class
library, so a spec that wants them does not write them again. There is no plugin host, no
manifest, and no discovery. You reference the project and call the methods you want.

This project is not published. It ships inside this repository as a working example and as the
pack three of the repository's own specs consume.

## Taking a rule

```csharp
using Zphil.LoadBearing;
using Zphil.LoadBearing.Packs.DotNet;

public sealed class MyArchSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        Selection host = arch.Namespace("MyApp.Host.*");

        DotNetGuidance.ReuseHttpClient(arch, arch.Types, host, PackPosture.Enforce);

        DotNetGuidance.AsyncSuffix(arch, arch.Types.InNamespace("MyApp.*"),
            PackPosture.Migrate("Repository methods return Task without the Async suffix."));
    }
}
```

Add a `ProjectReference` to this project alongside the one to `Zphil.LoadBearing`. Also set
`<CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>`: the checker loads a spec through
a resolver rooted at the spec's own DLL, and staging the pack's package dependencies beside it makes
that resolution independent of what the checking machine has restored. Note where the failure would
land if it is missing — at check time, not at build time.

A `typeof()` anchor on a **.NET shared framework** type is the one case no build setting reaches: an
ASP.NET Core or WPF type arriving through `<FrameworkReference>` is never staged into a class
library's output, so anchor those by string (`.DerivedFrom("Microsoft.AspNetCore.Mvc.ControllerBase")`,
which renders identically) rather than by `typeof()`.

## The nine rules

| Method | Rule ID | What it says |
|---|---|---|
| `ReuseHttpClient` | `http/reuse-httpclient` | nothing outside the composition root constructs an `HttpClient` |
| `NoServiceLocator` | `di/no-service-locator` | nothing outside the resolve seam resolves from an `IServiceProvider` |
| `NoBuildServiceProvider` | `di/no-buildserviceprovider` | nothing builds a second container while configuring services |
| `NoSyncOverAsync` | `async/no-sync-over-async` | nothing blocks on a `Task` |
| `NoCaptiveDependencies` | `di/no-captive-dependencies` | singletons inject nothing scoped or transient |
| `AsyncSuffix` | `naming/async-suffix` | `Task`-returning methods carry the `Async` suffix |
| `NoGeneralCatch` | `exceptions/no-general-catch` | only the top-level handler catches base `Exception` |
| `AcceptCancellation` | `async/accept-cancellation` | `Task`-returning methods accept a `CancellationToken` |
| `NoMappingAttributes` | `persistence/no-mapping-attributes` | persisted types carry no ORM mapping attribute |

The method name is the rule-name half of the ID, PascalCased with the area dropped, so the
mapping needs no table to use. Each method takes `(Arch arch, Selection subject, …, PackPosture
posture, string? fix = null)`; three of them take one extra `Selection` naming a seam
(`compositionRoot`, `resolveSeam`, `topLevelHandler`).

## What the pack owns, and what you own

The pack owns `Because`. The reason to reuse an `HttpClient` is the same in every codebase, so
it ships with the rule, citation URL and all, and you cannot override it. If a rule's rationale is
genuinely different for you, that is a signal the rule is yours to write rather than take.

You own the posture. `PackPosture.Enforce` for law, `PackPosture.Migrate("<what the code does
today>")` for a ratchet against a baseline. The same rule is `Enforce` in a codebase that already
keeps it and `Migrate` in one working it off; the pack has never seen your code and does not
guess. Baseline paths stay conventional, so the pack never names a path.

You own the selections: which types the rule governs, and which seam is exempt.

You may override `Fix`. Remediation names local types, and the pack's generic hint cannot.
Pass it as the last argument, not as a `.Fix(...)` trailer: pack methods return `void`, so exactly
one `Because` and one `Fix` reach the rule and a second trailer will not compile.

## Opting out, and colliding

Opting out is not calling the method. There is no suppression mechanism and no severity dial,
because a rule you did not ask for cannot arrive.

Taking a rule from the pack **and** writing it yourself is a duplicate ID, which fails the spec
build with both sites reported. That is deliberate: rule IDs are the baseline key and the
`explain` handle, so one loud error beats two quiet reports of the same law under different names.
It is also why the pack uses no ID prefix.

## `ApplyAll`

`ApplyAll` declares all nine at one posture, with no `Fix` overrides. Its only consumer is the
test that proves the pack's full surface in one call. No real spec wants all nine at the same
posture over the same subject; name the ones you mean.

## Anchor doctrine

Every member anchor in this pack is written `arch.Member(typeof(X), nameof(X.M))`, never the
expression form. The pack ships inside a codebase its own rules govern, and an expression anchor
is real syntax: `arch.Member<IServiceProvider>(sp => sp.GetService(...))` would mint a use edge
attributed to `DotNetGuidance` and make the pack a violator of `di/no-service-locator`. A `nameof`
operand mints nothing, and both forms reify to the same anchor, so the doctrine costs nothing.

A test pins it by reading this project's source, so the rule survives a reformat.
