using System.Globalization;
using System.Net;

namespace Zphil.LoadBearing.Tests.DocHygiene;

/// <summary>
///     Asks one cited page whether it is still there. A <c>HEAD</c> is enough for the pages this corpus
///     cites, and a server that refuses the verb is retried with a <c>GET</c> whose body is never read, so
///     the probe costs headers rather than pages.
/// </summary>
/// <remarks>
///     <para>
///         <b>Three buckets, not two.</b> Only <c>404</c> and <c>410</c> mean the page is gone; a
///         transport failure, a timeout, a throttle, a block and a server error all mean the run could not
///         find out. Folding the second group into "dead" would red the suite on a proxy, and folding it
///         into "live" would make an offline run a green claim about pages nobody reached.
///     </para>
///     <para>
///         The <see cref="HttpClient" /> is the caller's — its lifetime, its timeout, its headers — so
///         this type owns nothing to dispose and a test can hand it a scripted handler instead.
///     </para>
///     <para>
///         A cancelled request is bucketed only while the caller's own token is still live: that is the
///         per-request timeout. A cancel of the run itself must travel, not be reported as a page that
///         could not be reached.
///     </para>
/// </remarks>
/// <param name="client">The client every probe is sent on.</param>
internal sealed class CitationProbe(HttpClient client)
{
    /// <summary>What one run could establish about a cited page.</summary>
    public enum PageBucket
    {
        /// <summary>The page answered a success status.</summary>
        Live,

        /// <summary>The page answered <c>404</c> or <c>410</c> — it is gone, and the citation is stale.</summary>
        Dead,

        /// <summary>The run could not reach the page, so it says nothing either way.</summary>
        Unverifiable
    }

    /// <summary>
    ///     Which bucket a status code falls in: <c>2xx</c> live, <c>404</c> and <c>410</c> dead, everything
    ///     else — a throttle, a block, a server error, a redirect the client did not follow to an answer —
    ///     unverifiable.
    /// </summary>
    public static PageBucket Classify(HttpStatusCode status)
    {
        var code = (int)status;
        if (code is >= 200 and <= 299) return PageBucket.Live;
        if (status is HttpStatusCode.NotFound or HttpStatusCode.Gone) return PageBucket.Dead;

        return PageBucket.Unverifiable;
    }

    /// <summary>
    ///     Probes <paramref name="url" /> and reports the bucket it fell in alongside the detail a failure
    ///     needs to be actionable — the status code, the two codes of a retried verb, or the exception the
    ///     attempt ended on.
    /// </summary>
    public async Task<PageStatus> ProbeAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage head = await SendAsync(HttpMethod.Head, url, cancellationToken);
            if (head.StatusCode is not (HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotImplemented))
                return new PageStatus(Classify(head.StatusCode), Code(head));

            using HttpResponseMessage get = await SendAsync(HttpMethod.Get, url, cancellationToken);
            var retried = $"HEAD {Code(head)} → GET {Code(get)}";

            return new PageStatus(Classify(get.StatusCode), retried);
        }
        catch (HttpRequestException ex)
        {
            return Unreachable(ex);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            return Unreachable(ex);
        }
    }

    private static PageStatus Unreachable(Exception ex)
    {
        return new PageStatus(PageBucket.Unverifiable, $"{ex.GetType().Name}: {ex.Message}");
    }

    private static string Code(HttpResponseMessage response)
    {
        return ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(method, url);

        return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    /// <summary>
    ///     What one probe established: the bucket, and the detail a report quotes after the page —
    ///     <c>404</c>, <c>HEAD 405 → GET 200</c>, or the exception type and message.
    /// </summary>
    /// <param name="Bucket">Whether the page is live, gone, or simply not reached on this run.</param>
    /// <param name="Detail">The evidence behind that verdict, as a failure or a skip quotes it.</param>
    public sealed record PageStatus(PageBucket Bucket, string Detail);
}
