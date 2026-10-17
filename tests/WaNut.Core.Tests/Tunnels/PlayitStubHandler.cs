using System.Net;
using System.Text;

namespace WaNut.Core.Tests.Tunnels;

/// <summary>Answers playit API calls by path, one queued answer per call (the last one repeats), and keeps the requests.</summary>
internal sealed class PlayitStubHandler : HttpMessageHandler
{
    private readonly Dictionary<string, Queue<string>> _answers = [];

    public List<(string Path, string? Authorization, string Body)> Requests { get; } = [];

    public PlayitStubHandler On(string path, params string[] answers)
    {
        _answers[path] = new Queue<string>(answers);
        return this;
    }

    public int Count(string path) => Requests.Count(r => r.Path == path);

    public HttpClient CreateClient() => new(this);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath.TrimStart('/');
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((path, request.Headers.TryGetValues("Authorization", out var auth) ? auth.Single() : null, body));
        if (!_answers.TryGetValue(path, out var queue))
        {
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        var answer = queue.Count > 1 ? queue.Dequeue() : queue.Peek();
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(answer, Encoding.UTF8, "application/json") };
    }
}
