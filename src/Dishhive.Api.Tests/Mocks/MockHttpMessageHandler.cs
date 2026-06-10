using System.Net;

namespace Dishhive.Api.Tests.Mocks;

/// <summary>
/// HttpMessageHandler returning canned responses per absolute URL prefix.
/// Unmatched requests return 404.
/// </summary>
public class MockHttpMessageHandler : HttpMessageHandler
{
    private sealed record CannedResponse(HttpStatusCode StatusCode, byte[] Content, string ContentType);

    private readonly List<(string UrlPrefix, CannedResponse Response)> _responses = new();

    public List<Uri> Requests { get; } = new();

    public MockHttpMessageHandler RespondWith(string urlPrefix, string content, string contentType = "text/html")
    {
        return RespondWith(urlPrefix, System.Text.Encoding.UTF8.GetBytes(content), contentType);
    }

    public MockHttpMessageHandler RespondWith(string urlPrefix, byte[] content, string contentType)
    {
        _responses.Add((urlPrefix, new CannedResponse(HttpStatusCode.OK, content, contentType)));
        return this;
    }

    public MockHttpMessageHandler FailWith(string urlPrefix, HttpStatusCode statusCode)
    {
        _responses.Add((urlPrefix, new CannedResponse(statusCode, [], "text/plain")));
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!);

        var match = _responses.FirstOrDefault(r =>
            request.RequestUri!.AbsoluteUri.StartsWith(r.UrlPrefix, StringComparison.OrdinalIgnoreCase));

        if (match == default)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        var response = new HttpResponseMessage(match.Response.StatusCode)
        {
            Content = new ByteArrayContent(match.Response.Content)
        };
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(match.Response.ContentType);
        return Task.FromResult(response);
    }
}
