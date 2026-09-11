using System.Linq.Expressions;
using Zphil.LoadBearing.Internal;
using Zphil.LoadBearing.Model;
using static Zphil.LoadBearing.Internal.Guard;

namespace Zphil.LoadBearing;

/// <summary>
///     The verbs that finish a rule about types: every <c>Must</c> call on a <see cref="Selection" />,
///     each returning the <see cref="Constraint" /> that <c>Enforce</c> or <c>Migrate</c> takes.
///     Negation belongs to the verb's name (<c>MustNotReference</c> beside <c>MustOnlyReference</c>);
///     nothing negates a constraint afterwards. The verbs that name what a rule points at mostly come
///     in two shapes, a list of selections or a list of types, each type standing for the selection
///     <c>arch.Type</c> makes of it; one call takes one shape, and naming both in a single rule means
///     wrapping the type with <c>arch.Type</c>. Every list needs at least one operand, and a rule with
///     nothing to list has a verb of its own instead: <see cref="MustOnlyReferenceItself" />,
///     <see cref="MustOnlyBeReferencedByItself" />, <see cref="MustNotReferenceEachOther" /> and
///     <see cref="MustNotHaveCircularReferences" />. A subject that matches no type fails the rule.
///     Where a forbidden-set dependency verb (<c>MustNotReference</c>, <c>MustNotBeReferencedBy</c>,
///     <c>MustNotConstruct</c>, <c>MustNotCatch</c>, <c>MustNotCatchUnfiltered</c>,
///     <c>MustNotSwallow</c>, <c>MustNotThrow</c> or <c>MustNotExpose</c>) has operands that together
///     match nothing and at least one of them is a pattern (a layer, a namespace, a project, a narrowed
///     <c>arch.Types</c>), the check warns that the rule is inert; a bare <c>typeof</c> operand the
///     codebase never mentions stays silent, as do the <c>MustOnly</c> verbs, whose empty allow-list is
///     loud on its own, and <c>MustNotUse</c> and <c>MustNotInject</c>, where absence is the win
///     condition.
/// </summary>
// The (first, params more) shape on every list verb is what keeps a zero-operand call a compile error
// instead of a GRAMMAR §8 item; the shapes that legitimately name no operand carry nullary verbs of
// their own (GRAMMAR §3.3, §10). Polarity stays lexical (GRAMMAR §2 principle 3): no verb here may
// grow a Not() combinator or a polarity flag.
public static class SelectionConstraints
{
    /// <summary>
    ///     States that no type in the subject may reference any type the operands name, such as
    ///     <c>domain.MustNotReference(web, infrastructure)</c>. A reference is a source-level type
    ///     reference: anywhere the subject's own source names that type. Operands may be layers, projects,
    ///     namespaces, narrowed selections or unions, and they reach types from referenced packages and the
    ///     framework too (<c>arch.Namespace("System.Web.*")</c>); at least one is required. Every such
    ///     reference fails the check, reported at the file and line that makes it, and a type never counts
    ///     as referencing itself.
    /// </summary>
    public static Constraint MustNotReference(this Selection subject, Selection first, params Selection[] more)
    {
        return new MustNotReferenceConstraint(subject, Selections(subject, first, more));
    }

    /// <summary>
    ///     States that no type in the subject may reference any of the listed types, such as
    ///     <c>domain.MustNotReference(typeof(SqlConnection), typeof(DataTable))</c>. Each type stands for
    ///     the selection <c>arch.Type</c> makes of it, so types from referenced packages and the framework
    ///     are accepted; a generic type is given as its open definition, <c>typeof(Task&lt;&gt;)</c>, and a
    ///     constructed one such as <c>typeof(Task&lt;int&gt;)</c> is refused at check time with the
    ///     definition to name instead. A reference is a source-level type reference: anywhere the subject's
    ///     own source names that type. At least one type is required, and a type never counts as
    ///     referencing itself. To ban selections and bare types in one rule, wrap each type with
    ///     <c>arch.Type</c> and use the selection overload.
    /// </summary>
    public static Constraint MustNotReference(this Selection subject, Type first, params Type[] more)
    {
        return new MustNotReferenceConstraint(subject, WrappedTypes(subject, first, more));
    }

    /// <summary>
    ///     States that the subject may reference only the types the operands name, such as
    ///     <c>arch.Types.WithSuffix("Controller").MustOnlyReference(domain)</c>: every other reference out
    ///     of the subject fails the check. Two tolerances the rule leans on: references to types from
    ///     referenced packages and the framework are never constrained, and the subject is always an
    ///     allowed target, so one Controller may reference another. "The subject" is the narrowed selection
    ///     the rule ranges over, after any <c>Except</c>; over a family subject (<c>arch.Each</c>) it is
    ///     each cell as declared instead, the whole layer or project a type sits in even where
    ///     <c>Except</c> removed part of it. Operands may be layers, projects, namespaces, narrowed
    ///     selections or unions, at least one is required, and listing the subject among them is legal.
    /// </summary>
    public static Constraint MustOnlyReference(this Selection subject, Selection first, params Selection[] more)
    {
        return new MustOnlyReferenceConstraint(subject, Selections(subject, first, more));
    }

    /// <summary>
    ///     States that the subject may reference only the listed types, such as
    ///     <c>adapters.MustOnlyReference(typeof(SqlConnection), typeof(SqlCommand))</c>: every other
    ///     reference out of the subject fails the check. Each type stands for the selection
    ///     <c>arch.Type</c> makes of it, so types from referenced packages and the framework are accepted
    ///     and a generic type is given as its open definition, <c>typeof(Task&lt;&gt;)</c>. Two tolerances
    ///     the rule leans on: references to package and framework types are never constrained whether or
    ///     not they are listed, and the subject is always an allowed target, so its types may reference one
    ///     another. At least one type is required. To allow selections and bare types in one rule, wrap
    ///     each type with <c>arch.Type</c> and use the selection overload.
    /// </summary>
    public static Constraint MustOnlyReference(this Selection subject, Type first, params Type[] more)
    {
        return new MustOnlyReferenceConstraint(subject, WrappedTypes(subject, first, more));
    }

    /// <summary>
    ///     States that the subject may reference only itself: every reference from a subject type to a
    ///     solution type outside the subject fails the check, so nothing the subject reaches lies beyond
    ///     it. References to types from referenced packages and the framework are not constrained.
    ///     "Itself" is the narrowed selection the rule ranges over, after any
    ///     <c>Except</c>; over a family subject (<c>arch.Each</c>) it is each cell as declared, the whole
    ///     layer or project a type sits in, so the rule reads "no module reaches outside its own walls".
    ///     Takes no operands; to allow named outsiders as well, use <c>MustOnlyReference</c>.
    /// </summary>
    public static Constraint MustOnlyReferenceItself(this Selection subject)
    {
        return new MustOnlyReferenceItselfConstraint(Subject(subject));
    }

    /// <summary>
    ///     States that no type the operands name may reference the subject, such as
    ///     <c>domain.MustNotBeReferencedBy(web)</c> — the inbound direction, where <c>MustNotReference</c>
    ///     is outbound. Only types the solution declares can be observed referencing, so an operand naming
    ///     package or framework types matches nothing on this side. Operands may be layers, projects,
    ///     namespaces, narrowed selections or unions, and at least one is required. Every such reference
    ///     fails the check, reported at the file and line inside the referencing type, and a type never
    ///     counts as referencing itself.
    /// </summary>
    public static Constraint MustNotBeReferencedBy(this Selection subject, Selection first, params Selection[] more)
    {
        return new MustNotBeReferencedByConstraint(subject, Selections(subject, first, more));
    }

    /// <summary>
    ///     States that none of the listed types may reference the subject, such as
    ///     <c>domain.MustNotBeReferencedBy(typeof(ReportController))</c> — the inbound direction, where
    ///     <c>MustNotReference</c> is outbound. Each type stands for the selection <c>arch.Type</c> makes
    ///     of it, and a generic type is given as its open definition, <c>typeof(Task&lt;&gt;)</c>; only
    ///     types the solution declares can be observed referencing, so a package or framework type listed
    ///     here matches nothing. At least one type is required, and a type never counts as referencing
    ///     itself. To name selections and bare types in one rule, wrap each type with <c>arch.Type</c> and
    ///     use the selection overload.
    /// </summary>
    public static Constraint MustNotBeReferencedBy(this Selection subject, Type first, params Type[] more)
    {
        return new MustNotBeReferencedByConstraint(subject, WrappedTypes(subject, first, more));
    }

    /// <summary>
    ///     States that only the types the operands name may reference the subject: every other reference
    ///     into it fails the check, such as
    ///     <c>persistence.MustOnlyBeReferencedBy(repositories, composition)</c>. The subject is always
    ///     allowed, so its own types may reference one another; "the subject" is the narrowed selection the
    ///     rule ranges over, after any <c>Except</c>, and over a family subject (<c>arch.Each</c>) it is
    ///     each cell as declared. No package exemption applies here and none is needed, because only types
    ///     the solution declares can be observed referencing. Operands may be layers, projects, namespaces,
    ///     narrowed selections or unions, and at least one is required.
    /// </summary>
    public static Constraint MustOnlyBeReferencedBy(this Selection subject, Selection first, params Selection[] more)
    {
        return new MustOnlyBeReferencedByConstraint(subject, Selections(subject, first, more));
    }

    /// <summary>
    ///     States that only the listed types may reference the subject: every other reference into it fails
    ///     the check, such as <c>persistence.MustOnlyBeReferencedBy(typeof(OrderRepository))</c>. Each type
    ///     stands for the selection <c>arch.Type</c> makes of it, and a generic type is given as its open
    ///     definition, <c>typeof(Task&lt;&gt;)</c>; only types the solution declares can be observed
    ///     referencing, so listing a package or framework type allows nothing. The subject is always
    ///     allowed, so its own types may reference one another. At least one type is required. To allow
    ///     selections and bare types in one rule, wrap each type with <c>arch.Type</c> and use the
    ///     selection overload.
    /// </summary>
    public static Constraint MustOnlyBeReferencedBy(this Selection subject, Type first, params Type[] more)
    {
        return new MustOnlyBeReferencedByConstraint(subject, WrappedTypes(subject, first, more));
    }

    /// <summary>
    ///     States that nothing outside the subject may reference it, making it a hermetic set reachable
    ///     only from within: every reference into it from a solution type outside it fails the check.
    ///     "Itself" is the narrowed selection the rule ranges over, after any <c>Except</c>; over a family
    ///     subject (<c>arch.Each</c>) it is each cell as declared, the whole layer or project a type sits
    ///     in, which states "each module's internals are reached only through its own surface". Takes no
    ///     operands; to admit named outside callers, use <c>MustOnlyBeReferencedBy</c>.
    /// </summary>
    public static Constraint MustOnlyBeReferencedByItself(this Selection subject)
    {
        return new MustOnlyBeReferencedByItselfConstraint(Subject(subject));
    }

    /// <summary>
    ///     States that no cell of a family subject may reference any other cell, as in
    ///     <c>arch.Each(dispatch, tracking, invoicing).MustNotReferenceEachOther()</c>: the modules may
    ///     each reference shared code, but never one another. A cell is a family member as declared, the
    ///     whole layer or project, so a type <c>Except</c> removed from the subject still counts as part of
    ///     its own cell. The verb is symmetric and has no inbound counterpart, and it takes no operands
    ///     because the targets are the subject's own cells. It needs a family subject (<c>arch.Each</c>);
    ///     on a plain selection it is reported when the spec is loaded, naming <c>MustNotReference</c> as
    ///     the verb to write instead. A cell matching no type, and a type belonging to two cells, each fail
    ///     the rule naming the cell.
    /// </summary>
    public static Constraint MustNotReferenceEachOther(this Selection subject)
    {
        return new MustNotReferenceEachOtherConstraint(Subject(subject));
    }

    /// <summary>
    ///     States that the cells of a family subject may reference one another but never in a circle, as in
    ///     <c>arch.Each(api, application, domain).MustNotHaveCircularReferences()</c>. Draw an arrow from
    ///     cell to cell for every reference crossing between them: any group of two or more cells that can
    ///     all reach each other around those arrows fails the check, and every type pair on an arrow inside
    ///     that group is reported. A one-cell family passes, and the verb takes no operands because the
    ///     targets are the subject's own cells. It needs a family of layers; both a plain selection and a
    ///     family of projects are reported when the spec is loaded, projects being unable to reference each
    ///     other in a circle at all. A cell matching no type, and a type belonging to two cells, each fail
    ///     the rule naming the cell.
    /// </summary>
    public static Constraint MustNotHaveCircularReferences(this Selection subject)
    {
        return new MustNotHaveCircularReferencesConstraint(Subject(subject));
    }

    /// <summary>
    ///     States that no type in the subject may use any of the listed members, such as
    ///     <c>legacy.MustNotUse(arch.Member(typeof(DateTime), nameof(DateTime.Now)))</c>. A use is a
    ///     source-level member access: reading or writing a property or field, subscribing to an event,
    ///     invoking a method, naming a method group, or reaching a member through <c>using static</c>. Not
    ///     counted: a <c>nameof</c> operand, an indexer, a member a language construct consumes without
    ///     naming it (<c>await</c>'s <c>GetAwaiter</c>, <c>using</c>'s <c>Dispose</c>, <c>foreach</c>'s
    ///     enumerator), and a constructor — ban a constructed type with <c>MustNotConstruct</c> instead.
    ///     One ban covers every overload of the name and matches the member the compiler resolved at the
    ///     call, so a ban on a concrete member does not catch calls made through an interface-typed
    ///     receiver, nor the reverse. Members are named with the <c>arch.Member</c> methods on
    ///     <see cref="Arch" />; at least one is required, and a banned member the codebase never uses
    ///     raises no warning, absence being the win condition.
    /// </summary>
    public static Constraint MustNotUse(this Selection subject, Member first, params Member[] more)
    {
        return new MustNotUseConstraint(subject, Members(subject, first, more));
    }

    /// <summary>
    ///     States that no type in the subject may read or invoke any of the listed static members, named by
    ///     lambda: <c>web.MustNotUse(() =&gt; DateTime.Now, () =&gt; DateTime.UtcNow)</c>. Each lambda must
    ///     read or invoke one static property, field or value-returning method directly on its declaring
    ///     type; every overload of that name is covered, and a generic declaring type is recorded as its
    ///     open definition. A lambda of any other shape, such as an instance member, a method group without
    ///     parentheses, a constant or an object creation, is reported when the spec is loaded, with the
    ///     form to use instead. One call takes one shape: a static method that returns nothing goes through
    ///     the <c>Expression&lt;Action&gt;</c> overload, and a list mixing forms is written by wrapping
    ///     every target with <c>arch.Member</c> and passing those. A use is a source-level member access; at
    ///     least one target is required, and a banned member the codebase never uses raises no warning.
    /// </summary>
    public static Constraint MustNotUse(this Selection subject, Expression<Func<object?>> first, params Expression<Func<object?>>[] more)
    {
        return new MustNotUseConstraint(subject, ResolvedMembers(subject, first, more));
    }

    /// <summary>
    ///     States that no type in the subject may call any of the listed static methods that return
    ///     nothing, named by lambda: <c>web.MustNotUse(() =&gt; GC.Collect())</c>. Each lambda must invoke
    ///     one static method directly on its declaring type; every overload of that name is covered, and a
    ///     generic declaring type is recorded as its open definition. A lambda of any other shape, such as
    ///     an instance method or an object creation, is reported when the spec is loaded, with the form to
    ///     use instead. One call takes one shape: a static property, field or value-returning method goes
    ///     through the <c>Expression&lt;Func&lt;object?&gt;&gt;</c> overload, and a list mixing forms is
    ///     written by wrapping every target with <c>arch.Member</c> and passing those. A use is a
    ///     source-level member access; at least one target is required, and a banned method the codebase
    ///     never calls raises no warning.
    /// </summary>
    public static Constraint MustNotUse(this Selection subject, Expression<Action> first, params Expression<Action>[] more)
    {
        return new MustNotUseConstraint(subject, ResolvedMembers(subject, first, more));
    }

    /// <summary>
    ///     States that no type in the subject may create an instance of any type the operands name, such as
    ///     <c>handlers.MustNotConstruct(arch.Registered())</c>. The fact is a source-level object creation,
    ///     <c>new Foo()</c> and target-typed <c>new()</c> alike. Not counted: attribute applications,
    ///     <c>: base(...)</c> and <c>: this(...)</c> initializers, delegate creation, <c>with</c>
    ///     expressions and array creation. Construction a container or reflection performs is invisible, so
    ///     a composition root that only registers types passes, while a factory lambda that really calls
    ///     <c>new</c> fails and wants an <c>Except</c> or a baseline. Operands are ordinary selections —
    ///     what may not be constructed is a set of types, so there is no per-constructor form — and at
    ///     least one is required; a type constructing itself never counts.
    /// </summary>
    public static Constraint MustNotConstruct(this Selection subject, Selection first, params Selection[] more)
    {
        return new MustNotConstructConstraint(subject, Selections(subject, first, more));
    }

    /// <summary>
    ///     States that no type in the subject may create an instance of any of the listed types, such as
    ///     <c>handlers.MustNotConstruct(typeof(SqlConnection))</c>. Each type stands for the selection
    ///     <c>arch.Type</c> makes of it, so types from referenced packages and the framework are accepted
    ///     and a generic type is given as its open definition, <c>typeof(List&lt;&gt;)</c>. The fact is a
    ///     source-level object creation, <c>new Foo()</c> and target-typed <c>new()</c> alike; attribute
    ///     applications, <c>: base(...)</c> and <c>: this(...)</c> initializers, delegate creation,
    ///     <c>with</c> expressions, array creation and anything a container or reflection builds are not
    ///     counted. At least one type is required, and a type constructing itself never counts. To ban
    ///     selections and bare types in one rule, wrap each type with <c>arch.Type</c> and use the
    ///     selection overload.
    /// </summary>
    public static Constraint MustNotConstruct(this Selection subject, Type first, params Type[] more)
    {
        return new MustNotConstructConstraint(subject, WrappedTypes(subject, first, more));
    }

    /// <summary>
    ///     States that no type in the subject may take any type the operands name as a constructor
    ///     parameter, as in
    ///     <c>arch.Registered(Lifetime.Singleton).MustNotInject(arch.Registered(Lifetime.Scoped))</c> — the
    ///     captive-dependency rule. The fact is read from declared instance constructors, primary
    ///     constructors included: the parameter's declaration is the whole of it, whether or not any body
    ///     uses it. A compiler-supplied constructor, a record's copy constructor and a static constructor
    ///     contribute nothing. Parameter types are read down to their definitions, so a parameter typed
    ///     <c>IEnumerable&lt;IFoo&gt;</c> counts as both <c>IEnumerable&lt;&gt;</c> and <c>IFoo</c>, and an
    ///     array counts as its element type. The natural operands are the container-registration selections
    ///     (<c>arch.Registered(Lifetime.Scoped)</c>), though any selection works; at least one is required,
    ///     a type never injects itself, and operands matching nothing raise no warning, no such
    ///     registrations existing being the win condition.
    /// </summary>
    public static Constraint MustNotInject(this Selection subject, Selection first, params Selection[] more)
    {
        return new MustNotInjectConstraint(subject, Selections(subject, first, more));
    }

    /// <summary>
    ///     States that no type in the subject may take any of the listed types as a constructor parameter,
    ///     such as <c>handlers.MustNotInject(typeof(IServiceProvider))</c>. Each type stands for the
    ///     selection <c>arch.Type</c> makes of it, so types from referenced packages and the framework are
    ///     accepted and a generic type is given as its open definition, <c>typeof(IEnumerable&lt;&gt;)</c>.
    ///     The fact is read from declared instance constructors, primary constructors included: the
    ///     parameter's declaration is the whole of it, whether or not any body uses it, and parameter types
    ///     are read down to their definitions, so a parameter typed <c>IEnumerable&lt;IFoo&gt;</c> counts
    ///     as both <c>IEnumerable&lt;&gt;</c> and <c>IFoo</c>. At least one type is required, a type never
    ///     injects itself, and operands matching nothing raise no warning. To ban selections and bare types
    ///     in one rule, wrap each type with <c>arch.Type</c> and use the selection overload.
    /// </summary>
    public static Constraint MustNotInject(this Selection subject, Type first, params Type[] more)
    {
        return new MustNotInjectConstraint(subject, WrappedTypes(subject, first, more));
    }

    /// <summary>
    ///     States that no type in the subject may catch any exception type the operands name, such as
    ///     <c>domain.MustNotCatch(arch.Namespace("System.Data.*"))</c>. The fact is a source-level
    ///     <c>catch</c> clause, and a bare <c>catch</c> counts as catching <c>System.Exception</c>.
    ///     Matching is exact on the type's full name, so banning <c>Exception</c> does not reach
    ///     <c>catch (IOException)</c>: the narrow catch is the state the rule steers toward. A
    ///     <c>when</c> filter does not excuse a catch here, and neither does rethrowing; those two
    ///     conditions have verbs of their own, <c>MustNotCatchUnfiltered</c> and <c>MustNotSwallow</c>.
    ///     Operands are ordinary selections and at least one is required.
    /// </summary>
    public static Constraint MustNotCatch(this Selection subject, Selection first, params Selection[] more)
    {
        return new MustNotCatchConstraint(subject, Selections(subject, first, more));
    }

    /// <summary>
    ///     States that no type in the subject may catch any of the listed exception types, such as
    ///     <c>domain.MustNotCatch(typeof(Exception))</c>. Each type stands for the selection
    ///     <c>arch.Type</c> makes of it, so framework exception types are accepted as written. The fact is
    ///     a source-level <c>catch</c> clause, and a bare <c>catch</c> counts as catching
    ///     <c>System.Exception</c>. Matching is exact on the type's full name, so banning
    ///     <c>Exception</c> does not reach <c>catch (IOException)</c>. A <c>when</c> filter does not excuse
    ///     a catch here, and neither does rethrowing; those two conditions have verbs of their own,
    ///     <c>MustNotCatchUnfiltered</c> and <c>MustNotSwallow</c>. At least one type is required. To ban
    ///     selections and bare types in one rule, wrap each type with <c>arch.Type</c> and use the
    ///     selection overload.
    /// </summary>
    public static Constraint MustNotCatch(this Selection subject, Type first, params Type[] more)
    {
        return new MustNotCatchConstraint(subject, WrappedTypes(subject, first, more));
    }

    /// <summary>
    ///     States that no type in the subject may catch any exception type the operands name in a
    ///     <c>catch</c> clause that spells no <c>when</c> filter, such as
    ///     <c>domain.MustNotCatchUnfiltered(arch.Namespace("System.Data.*"))</c>. A bare <c>catch</c>
    ///     counts as catching <c>System.Exception</c> and counts as unfiltered; a <c>catch when (...)</c>
    ///     of any form is filtered and passes, and only the unfiltered sites are ever reported. Filter
    ///     presence is read from the source and the condition itself is never judged, so
    ///     <c>when (true)</c> counts as filtered. Matching is exact on the type's full name, so banning
    ///     <c>Exception</c> does not reach <c>catch (IOException)</c>. Operands are ordinary selections and
    ///     at least one is required.
    /// </summary>
    public static Constraint MustNotCatchUnfiltered(this Selection subject, Selection first, params Selection[] more)
    {
        return new MustNotCatchUnfilteredConstraint(subject, Selections(subject, first, more));
    }

    /// <summary>
    ///     States that no type in the subject may catch any of the listed exception types in a
    ///     <c>catch</c> clause that spells no <c>when</c> filter, such as
    ///     <c>domain.MustNotCatchUnfiltered(typeof(Exception))</c>. Each type stands for the selection
    ///     <c>arch.Type</c> makes of it, so framework exception types are accepted as written. A bare
    ///     <c>catch</c> counts as catching <c>System.Exception</c> and counts as unfiltered; a
    ///     <c>catch when (...)</c> of any form is filtered and passes, and the condition itself is never
    ///     judged, so <c>when (true)</c> counts as filtered. Matching is exact on the type's full name, so
    ///     banning <c>Exception</c> does not reach <c>catch (IOException)</c>. At least one type is
    ///     required. To ban selections and bare types in one rule, wrap each type with <c>arch.Type</c> and
    ///     use the selection overload.
    /// </summary>
    public static Constraint MustNotCatchUnfiltered(this Selection subject, Type first, params Type[] more)
    {
        return new MustNotCatchUnfilteredConstraint(subject, WrappedTypes(subject, first, more));
    }

    /// <summary>
    ///     States that no type in the subject may swallow any exception type the operands name: a
    ///     <c>catch</c> clause that spells no <c>when</c> filter and whose block does not end in a
    ///     <c>throw</c>, such as <c>domain.MustNotSwallow(arch.Namespace("System.Data.*"))</c>. A filtered
    ///     catch passes and so does one ending in <c>throw;</c> or <c>throw new X(...)</c>, so what fails
    ///     is holding a failure and carrying on. A bare <c>catch</c> counts as catching
    ///     <c>System.Exception</c> and counts as unfiltered. Both conditions are read from the source: the
    ///     filter's condition is never judged, and the throw is the block's last statement rather than an
    ///     analysis of every path, so <c>catch { if (...) return; throw; }</c> passes and
    ///     <c>catch { if (...) throw; Cleanup(); }</c> fails. Matching is exact on the type's full name.
    ///     Operands are ordinary selections and at least one is required.
    /// </summary>
    public static Constraint MustNotSwallow(this Selection subject, Selection first, params Selection[] more)
    {
        return new MustNotSwallowConstraint(subject, Selections(subject, first, more));
    }

    /// <summary>
    ///     States that no type in the subject may swallow any of the listed exception types: a
    ///     <c>catch</c> clause that spells no <c>when</c> filter and whose block does not end in a
    ///     <c>throw</c>, such as <c>domain.MustNotSwallow(typeof(Exception))</c>. Each type stands for the
    ///     selection <c>arch.Type</c> makes of it, so framework exception types are accepted as written. A
    ///     filtered catch passes and so does one ending in <c>throw;</c> or <c>throw new X(...)</c>; a bare
    ///     <c>catch</c> counts as catching <c>System.Exception</c> and counts as unfiltered. Both
    ///     conditions are read from the source: the filter's condition is never judged, and the throw is
    ///     the block's last statement rather than an analysis of every path. Matching is exact on the
    ///     type's full name, so banning <c>Exception</c> does not reach a swallowed <c>IOException</c>. At
    ///     least one type is required. To ban selections and bare types in one rule, wrap each type with
    ///     <c>arch.Type</c> and use the selection overload.
    /// </summary>
    public static Constraint MustNotSwallow(this Selection subject, Type first, params Type[] more)
    {
        return new MustNotSwallowConstraint(subject, WrappedTypes(subject, first, more));
    }

    /// <summary>
    ///     States that no public member of the subject may name any type the operands name in its
    ///     signature, such as <c>api.MustNotExpose(persistence)</c>. A signature position is a method's
    ///     return type and each of its parameter types, and a property, field or event's type; the failure
    ///     is reported at the member's declaration. Only members that are public and whose containing types
    ///     are public all the way out count, so a public member of an <c>internal</c> type exposes nothing.
    ///     Constructors are not signature positions (their parameters belong to <c>MustNotInject</c>), and
    ///     neither are base-type and interface lists, indexers, operators, conversions, accessors,
    ///     compiler-generated members or a <c>void</c> return. Signature types are read down to their
    ///     definitions and recursively, so <c>Task&lt;Order&gt;</c> exposes both <c>Task&lt;&gt;</c> and
    ///     <c>Order</c>, an array exposes its element type, and <c>int?</c> exposes both
    ///     <c>System.Nullable&lt;T&gt;</c> and <c>System.Int32</c>. A signature that says <c>object</c>
    ///     exposes only <c>System.Object</c>, whatever flows through it at run time. Operands are ordinary
    ///     selections and at least one is required.
    /// </summary>
    public static Constraint MustNotExpose(this Selection subject, Selection first, params Selection[] more)
    {
        return new MustNotExposeConstraint(subject, Selections(subject, first, more));
    }

    /// <summary>
    ///     States that no public member of the subject may name any of the listed types in its signature,
    ///     such as <c>api.MustNotExpose(typeof(DataTable))</c>. Each type stands for the selection
    ///     <c>arch.Type</c> makes of it, so types from referenced packages and the framework are accepted
    ///     and a generic type is given as its open definition, <c>typeof(Task&lt;&gt;)</c>. A signature
    ///     position is a method's return type and each of its parameter types, and a property, field or
    ///     event's type; only members that are public and whose containing types are public all the way out
    ///     count. Constructors, base-type and interface lists, indexers, operators, conversions, accessors,
    ///     compiler-generated members and a <c>void</c> return are all outside it. Signature types are read
    ///     down to their definitions and recursively, so a <c>Task&lt;Order&gt;</c> return matches a ban on
    ///     <c>typeof(Task&lt;&gt;)</c> and a ban on <c>Order</c> alike. At least one type is required. To
    ///     ban selections and bare types in one rule, wrap each type with <c>arch.Type</c> and use the
    ///     selection overload.
    /// </summary>
    public static Constraint MustNotExpose(this Selection subject, Type first, params Type[] more)
    {
        return new MustNotExposeConstraint(subject, WrappedTypes(subject, first, more));
    }

    /// <summary>
    ///     States that no type in the subject may throw any exception type the operands name, such as
    ///     <c>domain.MustNotThrow(arch.Namespace("System.Data.*"))</c>. The fact is a source-level
    ///     <c>throw</c>, statement and expression alike (<c>?? throw</c>, a conditional or
    ///     switch-expression arm, an expression-bodied <c>=&gt; throw new X()</c>), recorded as the thrown
    ///     expression's static type. A bare <c>throw;</c> introduces no thrown expression and is not
    ///     recorded, while <c>throw ex</c> is recorded as the declared type of <c>ex</c>. A throw a helper
    ///     performs, such as <c>ArgumentNullException.ThrowIfNull(x)</c>, is a method call and invisible
    ///     here; ban it with <c>MustNotUse</c>. Matching is exact on the type's full name, so banning
    ///     <c>Exception</c> does not reach a <c>TimeoutException</c> throw. Operands are ordinary
    ///     selections and at least one is required. For the opposite polarity, an allow-list of the only
    ///     types the subject may throw, use <c>MustOnlyThrow</c>.
    /// </summary>
    public static Constraint MustNotThrow(this Selection subject, Selection first, params Selection[] more)
    {
        return new MustNotThrowConstraint(subject, Selections(subject, first, more));
    }

    /// <summary>
    ///     States that no type in the subject may throw any of the listed exception types, such as
    ///     <c>domain.MustNotThrow(typeof(ApplicationException))</c>. Each type stands for the selection
    ///     <c>arch.Type</c> makes of it, so framework exception types are accepted as written. The fact is
    ///     a source-level <c>throw</c>, statement and expression alike, recorded as the thrown expression's
    ///     static type; a bare <c>throw;</c> is not recorded, <c>throw ex</c> is recorded as the declared
    ///     type of <c>ex</c>, and a throw a helper performs, such as
    ///     <c>ArgumentNullException.ThrowIfNull(x)</c>, is invisible here. Matching is exact on the type's
    ///     full name, so banning <c>Exception</c> does not reach a <c>TimeoutException</c> throw. At least
    ///     one type is required. To ban selections and bare types in one rule, wrap each type with
    ///     <c>arch.Type</c> and use the selection overload; for the opposite polarity use
    ///     <c>MustOnlyThrow</c>.
    /// </summary>
    public static Constraint MustNotThrow(this Selection subject, Type first, params Type[] more)
    {
        return new MustNotThrowConstraint(subject, WrappedTypes(subject, first, more));
    }

    /// <summary>
    ///     States that the subject may throw only the exception types the operands name: any other throw
    ///     fails the check, as in <c>domain.MustOnlyThrow(arch.Namespace("MyApp.Domain.Errors.*"))</c>.
    ///     Strict, unlike the reference verbs — types from referenced packages and the framework are
    ///     constrained too, so a <c>throw new InvalidOperationException(...)</c> inside the subject fails
    ///     unless that type is among the operands. The fact is a source-level <c>throw</c>, statement and
    ///     expression alike, recorded as the thrown expression's static type; a bare <c>throw;</c> is not
    ///     recorded, so rethrowing with <c>throw;</c> is always allowed and keeps the stack trace, while
    ///     <c>throw ex</c> is recorded as the declared type of <c>ex</c> and fails unless that type is
    ///     listed. A throw a helper performs, such as <c>ArgumentNullException.ThrowIfNull(x)</c>, is
    ///     invisible here. An allowed type the codebase never declares or references simply allows nothing.
    ///     Operands are ordinary selections and at least one is required.
    /// </summary>
    public static Constraint MustOnlyThrow(this Selection subject, Selection first, params Selection[] more)
    {
        return new MustOnlyThrowConstraint(subject, Selections(subject, first, more));
    }

    /// <summary>
    ///     States that the subject may throw only the listed exception types: any other throw fails the
    ///     check, as in <c>domain.MustOnlyThrow(typeof(DomainException), typeof(TimeoutException))</c>.
    ///     Each type stands for the selection <c>arch.Type</c> makes of it, so framework exception types
    ///     are accepted as written. Strict, unlike the reference verbs — package and framework exception
    ///     types are constrained too, so a <c>throw new InvalidOperationException(...)</c> inside the
    ///     subject fails unless that type is listed. The fact is a source-level <c>throw</c>, statement and
    ///     expression alike, recorded as the thrown expression's static type; a bare <c>throw;</c> is not
    ///     recorded, so rethrowing with <c>throw;</c> is always allowed, while <c>throw ex</c> is recorded
    ///     as the declared type of <c>ex</c>. At least one type is required. To allow selections and bare
    ///     types in one rule, wrap each type with <c>arch.Type</c> and use the selection overload.
    /// </summary>
    public static Constraint MustOnlyThrow(this Selection subject, Type first, params Type[] more)
    {
        return new MustOnlyThrowConstraint(subject, WrappedTypes(subject, first, more));
    }

    /// <summary>
    ///     States that every type in the subject lives in at least one of the places the operands name,
    ///     such as <c>arch.Types.WithSuffix("Handler").MustBelongTo(application, dispatch)</c>. The
    ///     failures are the subject types no operand contains. Operands say where a type may live, a
    ///     layer, a project, a namespace, or any selection standing for a home, and they are or-joined in
    ///     the generated agent context ("must belong to the Domain layer or the Web layer"), so the any-of
    ///     reading is in the sentence itself. At least one is required. There is no bare-type form: name a
    ///     home with <c>arch.Project</c>, <c>arch.Namespace</c> or a layer. For a single namespace or
    ///     project the tighter sentences are <see cref="MustResideInNamespace" /> and
    ///     <see cref="MustResideInProject" />.
    /// </summary>
    public static Constraint MustBelongTo(this Selection subject, Selection first, params Selection[] more)
    {
        return new MustBelongToConstraint(subject, Selections(subject, first, more));
    }

    /// <summary>
    ///     States that every type in the subject has exactly one counterpart whose name the template
    ///     derives, such as
    ///     <c>services.MustHaveExactlyOneCounterpart(among: contracts, named: "I{Name}")</c>. Every
    ///     <c>{Name}</c> in <paramref name="named" /> is replaced by the subject type's simple name;
    ///     substitution is exact and case-sensitive, so <c>{name}</c> is not a placeholder. Zero
    ///     counterparts and two or more both fail the check, keyed to the subject type either way. Matching
    ///     ignores generic arity, and a nested type matches on its own name rather than its containing
    ///     type's. Exactly-one is asked per subject and never claims a one-to-one pairing.
    ///     <paramref name="among" /> is one selection: to search several homes, union them with
    ///     <c>arch.AnyOf</c>. There is no bare-type form, this argument deriving a name rather than naming
    ///     a type. A blank template, or one containing no <c>{Name}</c> at all, is reported when the spec
    ///     is loaded; an <paramref name="among" /> selection that matches nothing fails every subject and
    ///     warns about nothing.
    /// </summary>
    public static Constraint MustHaveExactlyOneCounterpart(this Selection subject, Selection among, string named)
    {
        return new MustHaveExactlyOneCounterpartConstraint(
            Subject(subject), [NotNull(among, nameof(among))], NotNull(named, nameof(named)));
    }

    /// <summary>
    ///     States that every type in the subject is declared in a namespace matching the glob, such as
    ///     <c>handlers.MustResideInNamespace("MyApp.Application.*")</c>. Matching is by dot-separated
    ///     segment and case-sensitive: a trailing <c>.*</c> covers the namespace itself and everything
    ///     beneath it, a <c>*</c> standing alone as a segment matches exactly one segment, a <c>*</c>
    ///     inside a segment (<c>MyApp.Legacy*</c>) matches within that segment only, and a lone <c>*</c>
    ///     matches every namespace. A blank glob, or one whose text before a trailing <c>.*</c> itself
    ///     contains a <c>*</c> (which could never match), is reported when the spec is loaded. For several
    ///     permitted namespaces use <see cref="MustBelongTo" /> with <c>arch.Namespace</c> operands.
    /// </summary>
    public static Constraint MustResideInNamespace(this Selection subject, string glob)
    {
        return new MustResideInNamespaceConstraint(Subject(subject), NotNull(glob, nameof(glob)));
    }

    /// <summary>
    ///     States that every type in the subject is declared by the named project, by the project's name as
    ///     the solution lists it: <c>controllers.MustResideInProject("MyApp.Web")</c>. A source file
    ///     compiled into several projects satisfies the rule at any of them, so the verb agrees with what
    ///     <c>arch.Project</c> selects. One name only; for several permitted projects use
    ///     <see cref="MustBelongTo" /> with <c>arch.Project</c> operands. A blank name is reported when the
    ///     spec is loaded.
    /// </summary>
    public static Constraint MustResideInProject(this Selection subject, string projectName)
    {
        return new MustResideInProjectConstraint(Subject(subject), NotNull(projectName, nameof(projectName)));
    }

    /// <summary>
    ///     States that every type in the subject has a name ending with the suffix, such as
    ///     <c>handlers.MustHaveSuffix("Handler")</c>. The comparison is against the type's simple name —
    ///     no namespace, no generic arity, no containing type — and is exact and case-sensitive. The suffix
    ///     is literal text rather than a glob, so a <c>*</c> in it matches a <c>*</c>; for a pattern with
    ///     wildcards use <see cref="MustHaveNameMatching" />. A blank suffix would be true of every type
    ///     and is reported when the spec is loaded.
    /// </summary>
    public static Constraint MustHaveSuffix(this Selection subject, string suffix)
    {
        return new MustHaveSuffixConstraint(Subject(subject), NotNull(suffix, nameof(suffix)));
    }

    /// <summary>
    ///     States that every type in the subject has a name starting with the prefix, such as
    ///     <c>arch.Types.OfKind(TypeKind.Interface).MustHavePrefix("I")</c>. The comparison is against the
    ///     type's simple name (no namespace, no generic arity, no containing type), and is exact and
    ///     case-sensitive. The prefix is literal text rather than a glob, so a <c>*</c> in it matches a
    ///     <c>*</c>; for a pattern with wildcards use <see cref="MustHaveNameMatching" />. A blank prefix
    ///     would be true of every type and is reported when the spec is loaded.
    /// </summary>
    public static Constraint MustHavePrefix(this Selection subject, string prefix)
    {
        return new MustHavePrefixConstraint(Subject(subject), NotNull(prefix, nameof(prefix)));
    }

    /// <summary>
    ///     States that every type in the subject has a name matching the glob, such as
    ///     <c>legacy.MustHaveNameMatching("Legacy*Service")</c>. The comparison is against the type's
    ///     simple name (no namespace, no generic arity, no containing type), and is exact and
    ///     case-sensitive: <c>*</c> matches any run of characters including none, a glob without a
    ///     <c>*</c> is an exact name, and a lone <c>*</c> matches every name. Names have no dot-segment
    ///     structure here, so there is no subtree operator as there is in a namespace pattern. A blank glob
    ///     is reported when the spec is loaded.
    /// </summary>
    public static Constraint MustHaveNameMatching(this Selection subject, string glob)
    {
        return new MustHaveNameMatchingConstraint(Subject(subject), NotNull(glob, nameof(glob)));
    }

    /// <summary>
    ///     States that every type in the subject implements the given interface, such as
    ///     <c>handlers.MustImplement(typeof(IHandler&lt;&gt;))</c>. Matching reads the whole interface
    ///     closure, so an interface reached through a base class or through another interface counts, and
    ///     type arguments are substituted along the way: a class extending <c>HandlerBase&lt;Order&gt;</c>
    ///     where <c>HandlerBase&lt;T&gt; : IHandler&lt;T&gt;</c> satisfies
    ///     <c>MustImplement(typeof(IHandler&lt;Order&gt;))</c>. An open definition
    ///     (<c>typeof(IHandler&lt;&gt;)</c>) means any construction, and a closed one means that
    ///     construction exactly. One interface per rule: a second required interface is a second rule. For
    ///     an interface the spec project cannot compile against, name it by string with
    ///     <see cref="MustImplement(Selection,string)" />.
    ///     The type must be an interface; a class or struct is reported when the spec is loaded, naming
    ///     <c>MustDeriveFrom</c> instead.
    /// </summary>
    public static Constraint MustImplement(this Selection subject, Type type)
    {
        TypeAnchor anchor = TypeAnchor.FromType(NotNull(type, nameof(type)));
        return new MustImplementConstraint(Subject(subject), anchor);
    }

    /// <summary>
    ///     States that every type in the subject implements the named interface, the escape hatch for an
    ///     interface the spec project cannot compile against, so that naming it costs no package reference.
    ///     <paramref name="interfaceFullName" /> is the interface definition's fully-qualified name as a
    ///     report prints it, declared type-parameter names included
    ///     (<c>"MyApp.Web.IHandler&lt;T&gt;"</c>), compared exactly; it matches any construction of that
    ///     definition, and a spelling that names a construction
    ///     (<c>"MyApp.Web.IHandler&lt;MyApp.Web.Order&gt;"</c>) matches nothing. Matching reads the whole
    ///     interface closure, through base classes and on through types the solution does not declare, so a
    ///     subject type reaches an interface it inherits from an external base. One interface per rule.
    ///     Blankness is the only thing checked, and a blank name is reported when the spec is loaded; a
    ///     misspelling is a legal name that simply never matches, which is why
    ///     <see cref="MustImplement(Selection,Type)" /> is preferable whenever the interface is
    ///     referenceable — the compiler checks a <c>typeof</c>, and nothing checks a string.
    /// </summary>
    public static Constraint MustImplement(this Selection subject, string interfaceFullName)
    {
        TypeAnchor anchor = TypeAnchor.FromName(NotNull(interfaceFullName, nameof(interfaceFullName)));
        return new MustImplementConstraint(Subject(subject), anchor);
    }

    /// <summary>
    ///     States that every type in the subject derives from the given base type, such as
    ///     <c>controllers.MustDeriveFrom(typeof(ControllerBase))</c>. The whole base-type chain is read,
    ///     not just the immediate base, with type arguments substituted along the way; an open definition
    ///     (<c>typeof(Repository&lt;&gt;)</c>) means any construction, and a closed one means that
    ///     construction exactly. One base type per rule. For a base type the spec project cannot compile
    ///     against, name it by string with <see cref="MustDeriveFrom(Selection,string)" />.
    ///     The type must not be an interface; an interface is reported when the spec is loaded, naming
    ///     <c>MustImplement</c> instead.
    /// </summary>
    public static Constraint MustDeriveFrom(this Selection subject, Type type)
    {
        TypeAnchor anchor = TypeAnchor.FromType(NotNull(type, nameof(type)));
        return new MustDeriveFromConstraint(Subject(subject), anchor);
    }

    /// <summary>
    ///     States that every type in the subject derives from the named base type, the escape hatch for a
    ///     base type the spec project cannot compile against, so that naming it costs no package reference.
    ///     <paramref name="baseTypeFullName" /> is the base type definition's fully-qualified name as a
    ///     report prints it, declared type-parameter names included
    ///     (<c>"Microsoft.AspNetCore.Mvc.ControllerBase"</c>, <c>"MyApp.Data.Repository&lt;T&gt;"</c>),
    ///     compared exactly; it matches any construction of that definition, and a spelling that names a
    ///     construction matches nothing. The whole base-type chain is read, on through types the solution
    ///     does not declare, so a subject type reaches a base it inherits through an external one. One base
    ///     type per rule. Blankness is the only thing checked, and a blank name is reported when the spec is
    ///     loaded; a misspelling is a legal name that simply never matches, which is why
    ///     <see cref="MustDeriveFrom(Selection,Type)" /> is preferable whenever the base type is
    ///     referenceable.
    /// </summary>
    public static Constraint MustDeriveFrom(this Selection subject, string baseTypeFullName)
    {
        TypeAnchor anchor = TypeAnchor.FromName(NotNull(baseTypeFullName, nameof(baseTypeFullName)));
        return new MustDeriveFromConstraint(Subject(subject), anchor);
    }

    /// <summary>
    ///     States that every type in the subject carries the given attribute, such as
    ///     <c>controllers.MustBeAttributedWith(typeof(ApiControllerAttribute))</c>. Declared attributes
    ///     only: an attribute a base type carries does not count for the type deriving from it. One
    ///     attribute per rule. For an attribute the spec project cannot compile against, name it by string
    ///     with <see cref="MustBeAttributedWith(Selection,string)" />.
    ///     The type must derive from <c>System.Attribute</c>; anything else, <c>typeof(Attribute)</c> itself included,
    ///     is reported when the spec is loaded.
    /// </summary>
    public static Constraint MustBeAttributedWith(this Selection subject, Type type)
    {
        TypeAnchor anchor = TypeAnchor.FromType(NotNull(type, nameof(type)));
        return new MustBeAttributedWithConstraint(Subject(subject), anchor);
    }

    /// <summary>
    ///     States that every type in the subject carries the named attribute, the escape hatch for an
    ///     attribute the spec project cannot compile against, so that naming it costs no package reference.
    ///     <paramref name="attributeFullName" /> is the attribute definition's fully-qualified name as a
    ///     report prints it, <c>Attribute</c> suffix included
    ///     (<c>"ModelContextProtocol.Server.McpServerToolAttribute"</c>), compared exactly; it matches any
    ///     construction of that definition, and a spelling that names a construction matches nothing.
    ///     Declared attributes only, and one attribute per rule. Blankness is the only thing checked, and a
    ///     blank name is reported when the spec is loaded; a misspelling is a legal name that simply never
    ///     matches, which is why <see cref="MustBeAttributedWith(Selection,Type)" /> is preferable whenever
    ///     the attribute is referenceable — the compiler checks a <c>typeof</c>, and nothing checks a
    ///     string.
    /// </summary>
    public static Constraint MustBeAttributedWith(this Selection subject, string attributeFullName)
    {
        TypeAnchor anchor = TypeAnchor.FromName(NotNull(attributeFullName, nameof(attributeFullName)));
        return new MustBeAttributedWithConstraint(Subject(subject), anchor);
    }

    /// <summary>
    ///     States that every type in the subject implements <typeparamref name="T" />, such as
    ///     <c>handlers.MustImplement&lt;IDisposable&gt;()</c>. Matching reads the whole interface closure,
    ///     so an interface reached through a base class or through another interface counts. One interface
    ///     per rule. An open generic interface has no type-argument spelling, so
    ///     <c>MustImplement(typeof(IHandler&lt;&gt;))</c> stays the form for that.
    ///     <typeparamref name="T" /> must be an interface; a class or struct is reported when the spec is loaded,
    ///     naming <c>MustDeriveFrom</c> instead.
    /// </summary>
    public static Constraint MustImplement<T>(this Selection subject)
    {
        return subject.MustImplement(typeof(T));
    }

    /// <summary>
    ///     States that every type in the subject derives from <typeparamref name="T" />, such as
    ///     <c>controllers.MustDeriveFrom&lt;ControllerBase&gt;()</c>. The whole base-type chain is read,
    ///     not just the immediate base. One base type per rule. An open generic base has no type-argument
    ///     spelling, so <c>MustDeriveFrom(typeof(Repository&lt;&gt;))</c> stays the form for that.
    ///     <typeparamref name="T" /> must not be an interface; an interface is reported when the spec is loaded, naming
    ///     <c>MustImplement</c> instead.
    /// </summary>
    public static Constraint MustDeriveFrom<T>(this Selection subject)
    {
        return subject.MustDeriveFrom(typeof(T));
    }

    /// <summary>
    ///     States that every type in the subject carries the attribute <typeparamref name="T" />, such as
    ///     <c>controllers.MustBeAttributedWith&lt;ApiControllerAttribute&gt;()</c>. Declared attributes
    ///     only: an attribute a base type carries does not count for the type deriving from it. One
    ///     attribute per rule.
    ///     <typeparamref name="T" /> must be an attribute type other than <c>System.Attribute</c> itself, which is
    ///     reported when the spec is loaded.
    /// </summary>
    public static Constraint MustBeAttributedWith<T>(this Selection subject)
        where T : Attribute
    {
        return subject.MustBeAttributedWith(typeof(T));
    }

    /// <summary>
    ///     States that no type in the subject implements any of the given interfaces, such as
    ///     <c>domain.MustNotImplement(typeof(IDisposable), typeof(IAsyncDisposable))</c>: a subject type
    ///     implementing even one of them fails the check. Matching reads the whole interface closure, so an
    ///     interface reached through a base class or through another interface counts; an open definition
    ///     means any construction and a closed one means that construction exactly. An interface reachable
    ///     only through the bases of a type the solution does not declare never matches, so the ban passes
    ///     there in silence. At least one type is required, and one call is all <c>typeof</c>: to name an
    ///     interface the spec project cannot compile against, write the whole list as strings with
    ///     <see cref="MustNotImplement(Selection,string,string[])" />.
    ///     Every listed type must be an interface; a class or struct is reported when the spec is loaded, naming
    ///     <c>MustNotDeriveFrom</c> instead.
    /// </summary>
    public static Constraint MustNotImplement(this Selection subject, Type first, params Type[] more)
    {
        return new MustNotImplementConstraint(Subject(subject), TypeAnchor.FromTypes(first, more));
    }

    /// <summary>
    ///     States that no type in the subject implements any of the named interfaces, the escape hatch for
    ///     interfaces the spec project cannot compile against. Each name is an interface definition's
    ///     fully-qualified name as a report prints it, declared type-parameter names included
    ///     (<c>"MyApp.Web.IHandler&lt;T&gt;"</c>), compared exactly; it matches any construction of that
    ///     definition, and a spelling that names a construction matches nothing. Matching reads the whole
    ///     interface closure, through base classes and on through types the solution does not declare. At
    ///     least one name is required, and one call is all strings: write a second rule to mix these with
    ///     <c>typeof</c> forms. Blankness is the only thing checked, and a blank name is reported when the
    ///     spec is loaded; a misspelling is a legal name that simply never matches, which is why
    ///     <see cref="MustNotImplement(Selection,Type,Type[])" /> is preferable whenever the interfaces are
    ///     referenceable — the compiler checks a <c>typeof</c>, and nothing checks a string.
    /// </summary>
    public static Constraint MustNotImplement(this Selection subject, string first, params string[] more)
    {
        return new MustNotImplementConstraint(Subject(subject), TypeAnchor.FromNames(first, more));
    }

    /// <summary>
    ///     States that no type in the subject derives from any of the given base types, such as
    ///     <c>domain.MustNotDeriveFrom(typeof(DbContext), typeof(ControllerBase))</c>: a subject type
    ///     deriving from even one of them fails the check. The whole base-type chain is read, not just the
    ///     immediate base; an open definition means any construction and a closed one means that
    ///     construction exactly. A base reachable only through the bases of a type the solution does not
    ///     declare never matches, so the ban passes there in silence. At least one type is required, and
    ///     one call is all <c>typeof</c>: to name a base type the spec project cannot compile against,
    ///     write the whole list as strings with
    ///     <see cref="MustNotDeriveFrom(Selection,string,string[])" />.
    ///     No listed type may be an interface; an interface is reported when the spec is loaded, naming
    ///     <c>MustNotImplement</c> instead.
    /// </summary>
    public static Constraint MustNotDeriveFrom(this Selection subject, Type first, params Type[] more)
    {
        return new MustNotDeriveFromConstraint(Subject(subject), TypeAnchor.FromTypes(first, more));
    }

    /// <summary>
    ///     States that no type in the subject derives from any of the named base types, the escape hatch
    ///     for base types the spec project cannot compile against. Each name is a base type definition's
    ///     fully-qualified name as a report prints it, declared type-parameter names included
    ///     (<c>"Microsoft.EntityFrameworkCore.DbContext"</c>), compared exactly; it matches any
    ///     construction of that definition, and a spelling that names a construction matches nothing. The
    ///     whole base-type chain is read, on through types the solution does not declare. At least one name
    ///     is required, and one call is all strings: write a second rule to mix these with <c>typeof</c>
    ///     forms. Blankness is the only thing checked, and a blank name is reported when the spec is
    ///     loaded; a misspelling is a legal name that simply never matches, which is why
    ///     <see cref="MustNotDeriveFrom(Selection,Type,Type[])" /> is preferable whenever the base types
    ///     are referenceable.
    /// </summary>
    public static Constraint MustNotDeriveFrom(this Selection subject, string first, params string[] more)
    {
        return new MustNotDeriveFromConstraint(Subject(subject), TypeAnchor.FromNames(first, more));
    }

    /// <summary>
    ///     States that no type in the subject carries any of the given attributes, such as
    ///     <c>domain.MustNotBeAttributedWith(typeof(ObsoleteAttribute), typeof(SerializableAttribute))</c>:
    ///     a subject type carrying even one of them fails the check. Declared attributes only, so an
    ///     attribute a base type carries does not count for the type deriving from it. At least one type is
    ///     required, and one call is all <c>typeof</c>: to name an attribute the spec project cannot
    ///     compile against, write the whole list as strings with
    ///     <see cref="MustNotBeAttributedWith(Selection,string,string[])" />.
    ///     Every listed type must derive from <c>System.Attribute</c>; anything else, <c>typeof(Attribute)</c> itself
    ///     included, is reported when the spec is loaded.
    /// </summary>
    public static Constraint MustNotBeAttributedWith(this Selection subject, Type first, params Type[] more)
    {
        return new MustNotBeAttributedWithConstraint(Subject(subject), TypeAnchor.FromTypes(first, more));
    }

    /// <summary>
    ///     States that no type in the subject carries any of the named attributes, the escape hatch for
    ///     attributes the spec project cannot compile against. Each name is an attribute definition's
    ///     fully-qualified name as a report prints it, <c>Attribute</c> suffix included
    ///     (<c>"ModelContextProtocol.Server.McpServerToolAttribute"</c>), compared exactly; it matches any
    ///     construction of that definition, and a spelling that names a construction matches nothing.
    ///     Declared attributes only. At least one name is required, and one call is all strings: write a
    ///     second rule to mix these with <c>typeof</c> forms. Blankness is the only thing checked, and a
    ///     blank name is reported when the spec is loaded; a misspelling is a legal name that simply never
    ///     matches, which is why <see cref="MustNotBeAttributedWith(Selection,Type,Type[])" /> is
    ///     preferable whenever the attributes are referenceable — the compiler checks a <c>typeof</c>, and
    ///     nothing checks a string.
    /// </summary>
    public static Constraint MustNotBeAttributedWith(this Selection subject, string first, params string[] more)
    {
        return new MustNotBeAttributedWithConstraint(Subject(subject), TypeAnchor.FromNames(first, more));
    }

    /// <summary>
    ///     States that no type in the subject implements <typeparamref name="T" />, such as
    ///     <c>domain.MustNotImplement&lt;IDisposable&gt;()</c>. Matching reads the whole interface closure,
    ///     so an interface reached through a base class or through another interface counts, while one
    ///     reachable only through the bases of a type the solution does not declare never matches. An open
    ///     generic interface has no type-argument spelling, so
    ///     <c>MustNotImplement(typeof(IHandler&lt;&gt;))</c> stays the form for that.
    ///     <typeparamref name="T" /> must be an interface; a class or struct is reported when the spec is loaded,
    ///     naming <c>MustNotDeriveFrom</c> instead.
    /// </summary>
    public static Constraint MustNotImplement<T>(this Selection subject)
    {
        return subject.MustNotImplement(typeof(T));
    }

    /// <summary>
    ///     States that no type in the subject derives from <typeparamref name="T" />, such as
    ///     <c>domain.MustNotDeriveFrom&lt;DbContext&gt;()</c>. The whole base-type chain is read, not just
    ///     the immediate base, while a base reachable only through the bases of a type the solution does
    ///     not declare never matches. An open generic base has no type-argument spelling, so
    ///     <c>MustNotDeriveFrom(typeof(Repository&lt;&gt;))</c> stays the form for that.
    ///     <typeparamref name="T" /> must not be an interface; an interface is reported when the spec is loaded, naming
    ///     <c>MustNotImplement</c> instead.
    /// </summary>
    public static Constraint MustNotDeriveFrom<T>(this Selection subject)
    {
        return subject.MustNotDeriveFrom(typeof(T));
    }

    /// <summary>
    ///     States that no type in the subject carries the attribute <typeparamref name="T" />, such as
    ///     <c>publicApi.MustNotBeAttributedWith&lt;ObsoleteAttribute&gt;()</c>. Declared attributes only:
    ///     an attribute a base type carries does not count for the type deriving from it.
    ///     <typeparamref name="T" /> must be an attribute type other than <c>System.Attribute</c> itself, which is
    ///     reported when the spec is loaded.
    /// </summary>
    public static Constraint MustNotBeAttributedWith<T>(this Selection subject)
        where T : Attribute
    {
        return subject.MustNotBeAttributedWith(typeof(T));
    }

    /// <summary>
    ///     States that every type in the subject is declared <c>sealed</c>. C# declaration semantics apply,
    ///     so a struct, an enum and a delegate are sealed, while a static class is neither sealed nor
    ///     abstract. Takes no arguments.
    /// </summary>
    public static Constraint MustBeSealed(this Selection subject)
    {
        return new MustBeSealedConstraint(Subject(subject));
    }

    /// <summary>
    ///     States that every type in the subject is declared <c>static</c>. Takes no arguments.
    /// </summary>
    public static Constraint MustBeStatic(this Selection subject)
    {
        return new MustBeStaticConstraint(Subject(subject));
    }

    /// <summary>
    ///     States that every type in the subject is declared <c>abstract</c>. C# declaration semantics
    ///     apply, so an interface is abstract, while a static class is neither abstract nor sealed. Takes
    ///     no arguments.
    /// </summary>
    public static Constraint MustBeAbstract(this Selection subject)
    {
        return new MustBeAbstractConstraint(Subject(subject));
    }

    /// <summary>
    ///     States that every type in the subject is declared <c>public</c>. The test is the type's own
    ///     declared accessibility rather than whether it can be reached, so a <c>public</c> type nested in
    ///     an <c>internal</c> one counts as public. Takes no arguments.
    /// </summary>
    public static Constraint MustBePublic(this Selection subject)
    {
        return new MustBePublicConstraint(Subject(subject));
    }

    /// <summary>
    ///     States that every type in the subject is <c>internal</c>. The test is the type's own declared
    ///     accessibility, which a top-level type with no keyword already has, and it is exact: a nested
    ///     <c>protected internal</c> or <c>private protected</c> type does not count. Takes no arguments.
    /// </summary>
    public static Constraint MustBeInternal(this Selection subject)
    {
        return new MustBeInternalConstraint(Subject(subject));
    }

    /// <summary>
    ///     States that every type in the subject is registered in a dependency-injection container at some
    ///     lifetime, exactly the membership <c>arch.Registered()</c> selects. Registrations are recognized
    ///     from <c>AddSingleton</c>, <c>AddScoped</c>, <c>AddTransient</c>, their <c>TryAdd</c> twins,
    ///     <c>AddHostedService</c>, <c>AddDbContext</c>, <c>AddDbContextPool</c> and <c>AddHttpClient</c> on an
    ///     <c>IServiceCollection</c> in the solution's own source. Anything registered another way —
    ///     assembly scanning, keyed services, a raw <c>ServiceDescriptor</c>, an extension method compiled
    ///     into a package — is not seen, and under this verb that turns a correctly wired type into a
    ///     failure rather than hiding a real one, so a codebase that registers by convention should not use
    ///     it. Takes no arguments.
    /// </summary>
    public static Constraint MustBeRegistered(this Selection subject)
    {
        return new MustBeRegisteredConstraint(Subject(subject));
    }

    /// <summary>
    ///     States that every type in the subject must satisfy a predicate of your own, for what the type
    ///     verbs cannot say:
    ///     <c>arch.Types.Must(t =&gt; t.Name.Length &lt;= 40, description: "keep names under 40 characters")</c>.
    ///     <paramref name="description" /> is required and completes the sentence "must ...", so write a
    ///     bare verb phrase; it is rendered verbatim into the generated agent context and the check report,
    ///     never derived from the lambda's source. A blank or multi-line description is reported when the
    ///     spec is loaded. The predicate runs once per subject type when the check runs and reads
    ///     <see cref="ITypeInfo" />: name, namespace, kind, project, accessibility, the sealed, static,
    ///     abstract and record flags, whether the type was generated, declaration file paths, attributes,
    ///     base type and implemented interfaces. A predicate that throws fails its own rule with an error
    ///     naming the type it was reading, rather than stopping the run.
    /// </summary>
    public static Constraint Must(this Selection subject, Func<ITypeInfo, bool> predicate, string description)
    {
        return new MustConstraint(Subject(subject), NotNull(predicate, nameof(predicate)), description);
    }

    private static IReadOnlyList<Selection> Selections(Selection subject, Selection first, Selection[] more)
    {
        NotNull(subject, nameof(subject));
        return OperandList.OneOrMore(first, more, selection => selection);
    }

    private static IReadOnlyList<Selection> WrappedTypes(Selection subject, Type first, Type[] more)
    {
        NotNull(subject, nameof(subject));
        return OperandList.OneOrMore(first, more, type => Wrap(subject, type));
    }

    private static IReadOnlyList<Member> Members(Selection subject, Member first, Member[] more)
    {
        NotNull(subject, nameof(subject));
        return OperandList.OneOrMore(first, more, member => member);
    }

    // The static-form MustNotUse sugar: each lambda resolves through MemberExpressionResolver stamped with
    // the subject's owner (the Wrap precedent), minting the identical Member leaf as arch.Member(() => ...).
    // Generic over the concrete lambda type so the two delegate-shape overloads (Func<object?> / Action)
    // share one body with no array-covariance conversion. Null/empty params edges mirror the
    // Members/WrappedTypes helpers exactly.
    private static IReadOnlyList<Member> ResolvedMembers<TLambda>(Selection subject, TLambda first, TLambda[] more)
        where TLambda : LambdaExpression
    {
        NotNull(subject, nameof(subject));
        Arch owner = subject.Owner;
        return OperandList.OneOrMore(first, more, lambda => MemberExpressionResolver.Resolve(owner, lambda));
    }

    // A bare type target wraps as a single-type selection stamped with the subject's owner, so the
    // sugar overload is exactly the selection overload with arch.Type(...) written for the caller.
    private static Selection Wrap(Selection subject, Type type)
    {
        return subject.Owner.Type(type);
    }

    private static Selection Subject(Selection subject)
    {
        return NotNull(subject, nameof(subject));
    }
}
