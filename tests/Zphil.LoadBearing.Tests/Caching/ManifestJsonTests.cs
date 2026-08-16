using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Roslyn.Caching;
using Zphil.LoadBearing.Roslyn.Replay;

namespace Zphil.LoadBearing.Tests.Caching;

/// <summary>
///     Pins the two things moving the manifests onto <see cref="ManifestJsonContext" /> put at risk: that the
///     source-generated metadata writes the same bytes the reflection resolver did, and that the closed set
///     of enum converters still covers every enum the manifest graph can carry.
/// </summary>
/// <remarks>
///     Both hazards are silent. A generator-vs-reflection divergence would rewrite a cache file's shape with
///     every existing test green, because the store reads back what it wrote and agrees with itself; a fifth
///     enum joining the DTO graph would fall through to the integer default the same way. So the byte
///     comparison is made in-process against the same options with only the resolver swapped — never by
///     diffing files across runs, whose absolute temp paths differ by construction — and the converter set is
///     derived from the graph by reflection rather than restated by hand.
/// </remarks>
public sealed class ManifestJsonTests
{
    [Fact]
    public void Options_CarryAConverterForExactlyTheEnumsTheManifestGraphContains()
    {
        var inGraph = EnumFullNamesInManifestGraph();
        var converted = EnumFullNamesTheOptionsConvert();

        converted.ShouldBe(
            inGraph,
            "the manifest DTO graph and ManifestJson.Options' closed converter list have diverged — an enum " +
            "in the graph with no converter serializes as an integer, silently changing the on-disk schema, " +
            "and a converter for an enum no longer in the graph is dead weight. Add or drop the matching " +
            "JsonStringEnumConverter<T> in ManifestJson.Options.");
    }

    [Fact]
    public void SerializeCacheManifest_ThroughTheGeneratedContext_IsByteIdenticalToTheReflectionResolver()
    {
        CacheManifest manifest = FullyPopulatedCacheManifest();

        byte[] generated = JsonSerializer.SerializeToUtf8Bytes(manifest, ManifestJson.Context.CacheManifest);
        byte[] reflected = JsonSerializer.SerializeToUtf8Bytes(manifest, ReflectionResolvedOptions());

        // The non-vacuity guard, and the point of the closed converter list: all four graph enums reach the
        // wire as their names. A missing registration writes the ordinal instead — which both resolvers would
        // do identically, so the comparison below cannot catch it.
        string json = ShouldBeTheSameJson(generated, reflected);
        json.ShouldContain("\"Kind\":\"Interface\"");
        json.ShouldContain("\"Accessibility\":\"Internal\"");
        json.ShouldContain("\"MemberKind\":\"Property\"");
        json.ShouldContain("\"Lifetime\":\"Scoped\"");
    }

    [Fact]
    public void SerializeCaptureManifest_ThroughTheGeneratedContext_IsByteIdenticalToTheReflectionResolver()
    {
        CaptureManifest manifest = FullyPopulatedCaptureManifest();

        byte[] generated = JsonSerializer.SerializeToUtf8Bytes(manifest, ManifestJson.Context.CaptureManifest);
        byte[] reflected = JsonSerializer.SerializeToUtf8Bytes(manifest, ReflectionResolvedOptions());

        // The capture manifest carries no enum, so its non-vacuity guard is the pair of optional slots the
        // record defaults to null: they are the newest fields and the ones a resolver swap would most easily
        // drop.
        string json = ShouldBeTheSameJson(generated, reflected);
        json.ShouldContain("\"EvaluatedOutputPath\":\"C:/repo/src/App/bin/Debug/net10.0/\"");
        json.ShouldContain("\"IntermediateAssemblyPath\":\"C:/repo/src/App/obj/Debug/net10.0/App.dll\"");
    }

    /// <summary>
    ///     Asserts the two serializations are the same bytes and hands the payload back for further pins.
    ///     Compared as decoded text so a failure names the property that moved rather than a byte offset —
    ///     the decode is lossless, so equal text is equal bytes.
    /// </summary>
    private static string ShouldBeTheSameJson(byte[] generated, byte[] reflected)
    {
        string generatedJson = Encoding.UTF8.GetString(generated);
        string reflectedJson = Encoding.UTF8.GetString(reflected);

        generatedJson.ShouldBe(
            reflectedJson,
            "the source-generated metadata no longer writes what the reflection resolver wrote, so every " +
            "cache and capture file written from here on has a shape the previous build did not.");
        return generatedJson;
    }

    /// <summary>
    ///     The same options the manifests ship with, resolved by reflection instead of by the generated
    ///     context — the A/B's control. The copy constructor brings the converters across; only the resolver
    ///     is replaced, so any difference in the bytes is the generator's doing and nothing else's.
    /// </summary>
    private static JsonSerializerOptions ReflectionResolvedOptions()
    {
        return new JsonSerializerOptions(ManifestJson.Options) { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };
    }

    /// <summary>
    ///     Every enum type reachable from the two manifest roots, walked through the records' constructor
    ///     parameters — the shape the JSON serializer sees, since the positional properties are what it
    ///     writes.
    /// </summary>
    private static IReadOnlyList<string> EnumFullNamesInManifestGraph()
    {
        var walked = new HashSet<Type>();
        var enums = new HashSet<Type>();
        var pending = new Queue<Type>([typeof(CacheManifest), typeof(CaptureManifest)]);

        while (pending.Count > 0)
        {
            Type record = pending.Dequeue();
            if (!walked.Add(record)) continue;

            ConstructorInfo constructor = record.GetConstructors()
                .Single();
            foreach (ParameterInfo parameter in constructor.GetParameters())
            {
                Type slot = UnwrapSlot(parameter.ParameterType);
                if (slot.IsEnum) enums.Add(slot);
                else if (slot.Assembly == typeof(CacheManifest).Assembly) pending.Enqueue(slot);
            }
        }

        return OrderedFullNames(enums);
    }

    /// <summary>The enums <see cref="ManifestJson.Options" /> registers a string converter for.</summary>
    /// <remarks>
    ///     Read from each converter's own type argument rather than from <c>JsonConverter.Type</c>, so the
    ///     assertion is about the closed <see cref="JsonStringEnumConverter{TEnum}" /> form specifically:
    ///     falling back to the open factory would satisfy the serializer but give up the visible, trim-safe
    ///     list this is here to keep.
    /// </remarks>
    private static IReadOnlyList<string> EnumFullNamesTheOptionsConvert()
    {
        var converted = ManifestJson.Options.Converters
            .Select(converter => converter.GetType())
            .Where(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(JsonStringEnumConverter<>))
            .Select(type => type.GetGenericArguments()
                .Single());
        return OrderedFullNames(converted);
    }

    private static IReadOnlyList<string> OrderedFullNames(IEnumerable<Type> types)
    {
        return types.Select(type => type.FullName!)
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    ///     Peels a constructor parameter down to the type whose members the serializer will write: the
    ///     underlying type of a nullable slot, or the element of a single-argument generic. Every collection
    ///     in the graph today is an <see cref="IReadOnlyList{T}" />, but taking any one-argument generic
    ///     keeps a slot retyped to <c>List</c> or <c>IEnumerable</c> from quietly opening a hole in the walk.
    /// </summary>
    private static Type UnwrapSlot(Type slot)
    {
        Type? underlying = Nullable.GetUnderlyingType(slot);
        if (underlying is not null) return underlying;
        if (!slot.IsGenericType) return slot;

        var arguments = slot.GetGenericArguments();
        return arguments.Length == 1 ? arguments[0] : slot;
    }

    /// <summary>
    ///     A cache manifest with every slot filled and every enum set to a non-zero member, so a converter
    ///     that stopped applying writes a visibly different value rather than the name's own ordinal zero.
    ///     The schema version is arbitrary — nothing here reads it back, and pinning the real one would only
    ///     add a place to bump.
    /// </summary>
    private static CacheManifest FullyPopulatedCacheManifest()
    {
        var structuralStamp = new FileStamp(
            Path: "C:/repo/App.slnx",
            Exists: true,
            LastWriteTimeUtcTicks: 638_000_000_000_000_000,
            Length: 512,
            Sha256: "9f2c",
            Promoted: true);

        // An absent file, so the nullable hash and the false flags are exercised alongside their opposites.
        var documentStamp = new FileStamp(
            Path: "C:/repo/src/App/Widget.cs",
            Exists: false,
            LastWriteTimeUtcTicks: 0,
            Length: 0,
            Sha256: null,
            Promoted: false);

        var project = new ProjectCacheEntry(
            ProjectName: "App",
            CsprojPath: "C:/repo/src/App/App.csproj",
            ProjectDirectory: "C:/repo/src/App",
            ProjectReferences: ["Lib"],
            Documents: [documentStamp],
            ContentKey: "content-9f2c",
            MerkleKey: "merkle-4d81");

        var specResolution = new SpecResolutionRecord(
            NormalizedSpecArgument: "C:/repo/arch/Arch.csproj",
            SpecProjectName: "Arch",
            ExcludeProjectNames: ["Arch"],
            OutputFilePaths: ["C:/repo/arch/bin/Debug/net10.0/Arch.dll"],
            IntermediateAssemblyPath: "C:/repo/arch/obj/Debug/net10.0/Arch.dll");

        return new CacheManifest(
            SchemaVersion: 7,
            ToolVersion: "0.0.0-manifest-json-test",
            StructuralStamps: [structuralStamp],
            Projects: [project],
            SpecResolutions: [specResolution],
            Diagnostics: ["MSB4181: the workspace said something"],
            FailedProjects: ["C:/repo/src/Broken/Broken.csproj"],
            UncheckedProjects: ["C:/repo/src/Filtered/Filtered.csproj"],
            RestoreFailedProjects: ["C:/repo/src/Unrestored/Unrestored.csproj"],
            Fragments: [FullyPopulatedFragment()]);
    }

    /// <summary>
    ///     One fragment carrying at least one instance of every DTO the fragment graph can hold and every
    ///     edge family, so the byte comparison covers the whole reachable shape rather than the manifest's
    ///     top layer.
    /// </summary>
    private static CodebaseFragment FullyPopulatedFragment()
    {
        var site = new FragmentSite("Widget.cs", 12);
        var construction = new FragmentConstruction("N.IHandler<T>", "N.IHandler<N.Msg>");
        var parameter = new ParameterFacts("message", "N.Msg");

        var memberFacts = new MemberFacts(
            SymbolId: "M:N.Widget.Handle(N.Msg)",
            Name: "Handle",
            Kind: MemberKind.Property,
            Accessibility: Accessibility.Internal,
            IsStatic: true,
            IsAbstract: false,
            IsVirtual: true,
            IsAsync: false,
            ReturnTypeFullName: "System.Threading.Tasks.Task",
            MemberTypeFullName: "N.Msg",
            Parameters: [parameter],
            Attributes: [construction]);
        var member = new FragmentMember(memberFacts, [site]);

        var typeFacts = new TypeFacts(
            FullName: "N.Widget",
            SymbolId: "T:N.Widget",
            Name: "Widget",
            Namespace: "N",
            Kind: TypeKind.Interface,
            Accessibility: Accessibility.Internal,
            IsSealed: true,
            IsStatic: false,
            IsAbstract: true,
            IsRecord: false,
            IsGenerated: true);
        var declaredType = new FragmentType(
            Facts: typeFacts,
            DeclarationSites: [site],
            BaseTypeFullName: "N.Base",
            Interfaces: ["N.IHandler<N.Msg>"],
            Attributes: ["N.MarkAttribute"],
            AllInterfaces: [construction],
            BaseTypeChain: [construction],
            AttributeConstructions: [construction],
            DeclaredMembers: [member]);

        TypeFacts externalFacts = typeFacts with
        {
            FullName = "System.Exception", SymbolId = "T:System.Exception", Name = "Exception", Namespace = "System",
            Kind = TypeKind.Class
        };
        var external = new FragmentExternal(externalFacts, "System.Runtime");

        var edge = new FragmentEdge("N.Widget", "N.Msg", [site]);
        var memberEdge = new FragmentMemberEdge("N.Widget", "N.Msg", "Body", "P:N.Msg.Body", MemberKind.Property, [site]);
        var constructorEdge = new FragmentConstructorEdge("N.Widget", "N.Msg", [site]);
        var injectionEdge = new FragmentInjectionEdge("N.Widget", "N.IClock", [site]);
        var catchEdge = new FragmentCatchEdge("N.Widget", "System.Exception", [site], [site], [site]);
        var throwEdge = new FragmentThrowEdge("N.Widget", "System.Exception", [site]);
        var exposureEdge = new FragmentExposureEdge("N.Widget", "N.Msg", [site]);
        var registration = new FragmentServiceRegistration(Lifetime.Scoped, "N.IClock", "N.Clock", [site]);

        return new CodebaseFragment(
            ProjectName: "App",
            TargetFramework: "net10.0",
            ProjectReferences: ["Lib"],
            DeclaredTypes: [declaredType],
            Externals: [external],
            Edges: [edge],
            MemberEdges: [memberEdge],
            ConstructorEdges: [constructorEdge],
            InjectionEdges: [injectionEdge],
            CatchEdges: [catchEdge],
            ThrowEdges: [throwEdge],
            ExposureEdges: [exposureEdge],
            ServiceRegistrations: [registration]);
    }

    /// <summary>
    ///     A capture manifest with every slot filled, including the two optional project slots the record
    ///     defaults to null.
    /// </summary>
    private static CaptureManifest FullyPopulatedCaptureManifest()
    {
        var structuralStamp = new FileStamp(
            Path: "C:/repo/App.slnx",
            Exists: true,
            LastWriteTimeUtcTicks: 638_000_000_000_000_000,
            Length: 512,
            Sha256: "9f2c",
            Promoted: true);
        var binlogCopyStamp = new FileStamp(
            Path: "C:/repo/.cache/capture.binlog",
            Exists: true,
            LastWriteTimeUtcTicks: 638_000_000_000_000_001,
            Length: 1_048_576,
            Sha256: null,
            Promoted: false);

        var project = new CaptureProjectEntry(
            ProjectName: "App",
            CsprojPath: "C:/repo/src/App/App.csproj",
            ProjectDirectory: "C:/repo/src/App",
            DocumentPaths: ["C:/repo/src/App/Widget.cs", "C:/repo/src/App/obj/Debug/net10.0/App.GlobalUsings.g.cs"],
            ConeFiles: ["C:/repo/src/App/Widget.cs", "C:/repo/src/App/Excluded.cs"],
            EvaluatedOutputPath: "C:/repo/src/App/bin/Debug/net10.0/",
            IntermediateAssemblyPath: "C:/repo/src/App/obj/Debug/net10.0/App.dll");

        return new CaptureManifest(
            SchemaVersion: 3,
            ToolVersion: "0.0.0-manifest-json-test",
            StructuralStamps: [structuralStamp],
            Projects: [project],
            BinlogCopyStamp: binlogCopyStamp);
    }
}
