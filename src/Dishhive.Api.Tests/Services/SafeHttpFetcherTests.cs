using Dishhive.Api.Services;
using Dishhive.Api.Services.Import;
using Dishhive.Api.Tests.Mocks;
using FluentAssertions;

namespace Dishhive.Api.Tests.Services;

public class SafeHttpFetcherTests
{
    [Theory]
    [InlineData("http://127.0.0.1/private")]
    [InlineData("http://10.0.0.2/private")]
    [InlineData("http://192.168.1.10/private")]
    [InlineData("http://[::1]/private")]
    public async Task UrlGuard_PrivateAddress_IsRejected(string url)
    {
        var result = await UrlGuard.ValidateAsync(url);

        result.Ok.Should().BeFalse();
        result.Error.Should().Contain("non-public");
    }

    [Fact]
    public async Task Get_RedirectDestinationIsRejected_DoesNotFetchDestination()
    {
        var handler = new MockHttpMessageHandler()
            .Redirect("https://public.example/start", "http://127.0.0.1/private")
            .RespondWith("http://127.0.0.1/private", "secret");
        var validator = new RejectPrivateValidator();
        var fetcher = new SafeHttpFetcher(new HttpClient(handler), validator);

        var act = () => fetcher.GetAsync("https://public.example/start", 1024);

        await act.Should().ThrowAsync<SafeHttpFetchException>()
            .Where(ex => ex.UnsafeUrl);
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task Get_ResponseExceedsLimit_Throws()
    {
        var handler = new MockHttpMessageHandler()
            .RespondWith("https://public.example/large", new string('x', 1025));
        var fetcher = new SafeHttpFetcher(new HttpClient(handler), new AllowAllPublicUrlValidator());

        var act = () => fetcher.GetAsync("https://public.example/large", 1024);

        await act.Should().ThrowAsync<SafeHttpFetchException>()
            .WithMessage("*exceeds*");
    }

    private sealed class RejectPrivateValidator : IPublicUrlValidator
    {
        public Task<(bool Ok, Uri? Uri, string? Error)> ValidateAsync(
            string? url,
            CancellationToken cancellationToken = default)
        {
            var uri = new Uri(url!);
            return Task.FromResult(uri.IsLoopback
                ? (false, (Uri?)null, "Private destination rejected.")
                : (true, (Uri?)uri, (string?)null));
        }
    }
}
