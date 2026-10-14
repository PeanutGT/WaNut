using System.Net;

namespace WaNut.Core.Tests.TestSupport;

/// <summary>Serves canned responses by URL so tests never touch the network.</summary>
internal sealed class StubHttpHandler : HttpMessageHandler
{
    private readonly Dictionary<string, byte[]> _responses = [];

    public List<Uri> Requests { get; } = [];

    public StubHttpHandler Add(Uri url, byte[] body)
    {
        _responses[url.AbsoluteUri] = body;
        return this;
    }

    public StubHttpHandler Add(Uri url, string body) => Add(url, System.Text.Encoding.UTF8.GetBytes(body));

    public HttpClient CreateClient() => new(this);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!);
        var response = _responses.TryGetValue(request.RequestUri!.AbsoluteUri, out var body)
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) }
            : new HttpResponseMessage(HttpStatusCode.NotFound);
        return Task.FromResult(response);
    }
}
