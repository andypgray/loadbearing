using System.Net;
using Shouldly;
using Xunit;

namespace Zphil.LoadBearing.Tests.DocHygiene;

/// <summary>
///     Unit tests for <see cref="CitationProbe" />, driven by a scripted message handler so the whole
///     status table — including the answers a real run only sees on a bad day — is pinned without a
///     network. They cover the classification itself, the <c>GET</c> retry a server that refuses
///     <c>HEAD</c> earns, and the two failure shapes that must land as unverifiable rather than dead.
/// </summary>
public sealed class CitationProbeTests
{
    private const string Page = "https://learn.microsoft.com/dotnet/fundamentals/networking/http/httpclient-guidelines";

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.NoContent)]
    public void Classify_SuccessStatus_Live(HttpStatusCode status)
    {
        // Act & Assert
        CitationProbe.Classify(status)
            .ShouldBe(CitationProbe.PageBucket.Live);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Gone)]
    public void Classify_PageGoneStatus_Dead(HttpStatusCode status)
    {
        // Act & Assert: these two are the whole of what "the citation is stale" can be read from.
        CitationProbe.Classify(status)
            .ShouldBe(CitationProbe.PageBucket.Dead);
    }

    [Theory]
    [InlineData(HttpStatusCode.MovedPermanently)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public void Classify_AnyOtherStatus_Unverifiable(HttpStatusCode status)
    {
        // Act & Assert: a throttle, a block, a server error and an unfollowed redirect all mean the run
        // learned nothing, and reporting either of the other two would be a claim it cannot make.
        CitationProbe.Classify(status)
            .ShouldBe(CitationProbe.PageBucket.Unverifiable);
    }

    [Fact]
    public async Task ProbeAsync_PageAnswersHead_LiveWithItsStatusCode()
    {
        // Arrange
        ScriptedHandler handler = new(HttpStatusCode.OK);

        // Act
        CitationProbe.PageStatus status = await ProbeAsync(handler);

        // Assert: one request, and the body was never asked for.
        status.Bucket.ShouldBe(CitationProbe.PageBucket.Live);
        status.Detail.ShouldBe("200");
        handler.Methods.ShouldBe([HttpMethod.Head]);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Gone)]
    public async Task ProbeAsync_PageIsGone_Dead(HttpStatusCode gone)
    {
        // Arrange
        ScriptedHandler handler = new(gone);

        // Act
        CitationProbe.PageStatus status = await ProbeAsync(handler);

        // Assert
        status.Bucket.ShouldBe(CitationProbe.PageBucket.Dead);
    }

    [Theory]
    [InlineData(HttpStatusCode.MethodNotAllowed)]
    [InlineData(HttpStatusCode.NotImplemented)]
    public async Task ProbeAsync_ServerRefusesHead_RetriesAsGetAndTheGetDecides(HttpStatusCode refusal)
    {
        // Arrange: a server that will not answer the cheap verb has said nothing about the page.
        ScriptedHandler handler = new(refusal, HttpStatusCode.OK);

        // Act
        CitationProbe.PageStatus status = await ProbeAsync(handler);

        // Assert: the detail carries both codes, so a reader can tell a retried answer from a first one.
        status.Bucket.ShouldBe(CitationProbe.PageBucket.Live);
        status.Detail.ShouldBe($"HEAD {(int)refusal} → GET 200");
        handler.Methods.ShouldBe([HttpMethod.Head, HttpMethod.Get]);
    }

    [Fact]
    public async Task ProbeAsync_RetriedGetIsGone_Dead()
    {
        // Arrange
        ScriptedHandler handler = new(HttpStatusCode.MethodNotAllowed, HttpStatusCode.NotFound);

        // Act
        CitationProbe.PageStatus status = await ProbeAsync(handler);

        // Assert
        status.Bucket.ShouldBe(CitationProbe.PageBucket.Dead);
        status.Detail.ShouldBe("HEAD 405 → GET 404");
    }

    [Fact]
    public async Task ProbeAsync_TransportFailure_UnverifiableNamingTheException()
    {
        // Arrange: no route to the host is the shape an offline clone and a closed proxy both take.
        FailingHandler handler = new(new HttpRequestException("No such host is known."));

        // Act
        CitationProbe.PageStatus status = await ProbeAsync(handler);

        // Assert
        status.Bucket.ShouldBe(CitationProbe.PageBucket.Unverifiable);
        status.Detail.ShouldStartWith("HttpRequestException: ");
        status.Detail.ShouldContain("No such host is known.");
    }

    [Fact]
    public async Task ProbeAsync_RequestTimesOut_UnverifiableNamingTheException()
    {
        // Arrange: the per-request timeout surfaces as a cancellation the caller never asked for.
        FailingHandler handler = new(new TaskCanceledException("A task was canceled."));

        // Act
        CitationProbe.PageStatus status = await ProbeAsync(handler);

        // Assert
        status.Bucket.ShouldBe(CitationProbe.PageBucket.Unverifiable);
        status.Detail.ShouldStartWith("TaskCanceledException: ");
    }

    [Fact]
    public async Task ProbeAsync_RunItselfCancelled_Propagates()
    {
        // Arrange: the one cancellation that is not a slow page. Bucketing it would report the run's own
        // shutdown as a site that could not be reached.
        StalledHandler handler = new();
        using HttpClient client = new(handler);
        CitationProbe probe = new(client);
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();

        // Act & Assert
        await Should.ThrowAsync<TaskCanceledException>(() => probe.ProbeAsync(Page, cancelled.Token));
    }

    private static async Task<CitationProbe.PageStatus> ProbeAsync(HttpMessageHandler handler)
    {
        using HttpClient client = new(handler);
        CitationProbe probe = new(client);

        return await probe.ProbeAsync(Page, Ct);
    }

    /// <summary>Answers each request with the next status of a fixed script, recording the verbs asked.</summary>
    private sealed class ScriptedHandler(params HttpStatusCode[] statuses) : HttpMessageHandler
    {
        private int _sent;

        /// <summary>The verbs the probe issued, in order — how the HEAD-then-GET retry is observed.</summary>
        public List<HttpMethod> Methods { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Methods.Add(request.Method);
            HttpStatusCode status = statuses[_sent++];

            return Task.FromResult(new HttpResponseMessage(status));
        }
    }

    /// <summary>Fails every request with one exception — the transport and timeout shapes.</summary>
    private sealed class FailingHandler(Exception failure) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromException<HttpResponseMessage>(failure);
        }
    }

    /// <summary>Waits on the caller's token instead of answering — a request in flight when the run stops.</summary>
    private sealed class StalledHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
