using System.Net.Http.Headers;

namespace WaNut.Core.Http;

public static class WaNutHttp
{
    /// <summary>
    /// Creates the single HttpClient the app shares. Mojang, Paper and Adoptium ask callers to identify themselves.
    /// </summary>
    public static HttpClient CreateClient(string appVersion)
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(10),
        };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("WaNut", appVersion));
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("(github.com/PeanutGT/WaNut)"));
        return client;
    }
}
