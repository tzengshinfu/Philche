using System.Net;
using System.Text.Json;
using Philche.Core.Correlation;

namespace Philche.Core.Test;

public sealed class NvdApiClientTests
{
    [Fact]
    public async Task QueryByCpeAsync_BuildsCorrectUrl()
    {
        var capturedRequests = new List<string>();

        var handler = new CaptureHandler(capturedRequests, new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { vulnerabilities = Array.Empty<object>() }))
        });

        var httpClient = new HttpClient(handler);
        var client = new NvdApiClient(httpClient);

        await client.QueryByCpeAsync("cpe:2.3:a:lodash:lodash:*:*:*:*:*:*:*:*", "4.17.21");

        Assert.Single(capturedRequests);
        var url = capturedRequests[0];
        Assert.Contains("virtualMatchString=", url);
        Assert.Contains("versionEndIncluding=", url);
        Assert.Contains("4.17.21", url);
        Assert.DoesNotContain("keywordSearch", url);
    }

    [Fact]
    public async Task QueryByCpeAsync_EncodesSpecialCharactersInCpe()
    {
        var capturedRequests = new List<string>();

        var handler = new CaptureHandler(capturedRequests, new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { vulnerabilities = Array.Empty<object>() }))
        });

        var httpClient = new HttpClient(handler);
        var client = new NvdApiClient(httpClient);

        await client.QueryByCpeAsync("cpe:2.3:a:vendor:product:*:*:*:*:*:*:*:*", "1.0.0");

        Assert.Single(capturedRequests);
        var url = capturedRequests[0];
        // Colons should be percent-encoded in the query string value
        Assert.Contains("%3A", url);
    }

    [Fact]
    public async Task QueryByCpeAsync_ReturnsEmptyOnNonSuccessStatusCode()
    {
        var capturedRequests = new List<string>();
        var handler = new CaptureHandler(capturedRequests, new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        var httpClient = new HttpClient(handler);
        var client = new NvdApiClient(httpClient);

        var results = await client.QueryByCpeAsync("cpe:2.3:a:a:b:*:*:*:*:*:*:*:*", "1.0.0");

        Assert.Empty(results);
    }

    private sealed class CaptureHandler(List<string> captured, HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            captured.Add(request.RequestUri!.ToString());
            return Task.FromResult(response);
        }
    }
}
