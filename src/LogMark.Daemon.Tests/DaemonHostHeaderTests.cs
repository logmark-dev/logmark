using System.Net;
using Xunit;

namespace LogMark.Daemon.Tests;

/// <summary>
/// DNS rebinding: a page on rebind.example resolves its own name to
/// 127.0.0.1, so the browser treats the daemon as same-origin and can read
/// every GET. The only thing that tells such a request apart is its Host
/// header, which names the attacker's host, not the loopback address.
/// </summary>
public class DaemonHostHeaderTests : IClassFixture<DaemonFixture>
{
    private readonly DaemonFixture _fx;

    public DaemonHostHeaderTests(DaemonFixture fx) => _fx = fx;

    [Theory]
    [InlineData("/api/v0/vault/summary")]
    [InlineData("/api/vault-summary")]
    [InlineData("/")]
    public async Task ForeignHost_IsRefused(string path)
    {
        var port = new Uri(_fx.BaseUrl).Port;
        using var req = new HttpRequestMessage(HttpMethod.Get, path);
        req.Headers.Host = $"rebind.example:{port}";

        using var resp = await _fx.Client.SendAsync(req);

        Assert.NotEqual(HttpStatusCode.OK, resp.StatusCode);
        Assert.DoesNotContain("route-lo", await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task LoopbackHost_IsServed()
    {
        using var resp = await _fx.Client.GetAsync("/api/v0/vault/summary");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    // SECURITY.md serves localhost:<port> as well as 127.0.0.1:<port>. On
    // macOS and Linux the managed HttpListener matched Host against its one
    // 127.0.0.1 prefix and answered localhost with its own 404 (rc7 osx QA).
    //
    // The request goes to http://localhost:<port> and lets the OS resolve the
    // name, the way a browser does. The managed listener binds its localhost
    // prefix to the first address the resolver returns for "localhost": ::1
    // where /etc/hosts lists it first (macOS, the GitHub Ubuntu runner), or
    // 127.0.0.1 where it does not (WSL). A client resolving the same name
    // connects to that same address, so this is the path users take. Forcing
    // a TCP connection to 127.0.0.1 with a localhost Host header instead
    // tests an address the listener may never have bound for that name, and
    // failed on the Ubuntu runner for exactly that reason (rc8 CI).
    [Fact]
    public async Task LocalhostHost_IsServed()
    {
        var port = new Uri(_fx.BaseUrl).Port;
        using var client = new HttpClient();

        using var resp = await client.GetAsync($"http://localhost:{port}/api/v0/vault/summary");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }
}
