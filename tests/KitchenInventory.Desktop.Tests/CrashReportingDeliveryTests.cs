using System;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace KitchenInventory.Desktop.Tests;

/// <summary>
/// Proves that the DSN the app would report crashes to actually accepts an event.
///
/// This is deliberately not a test of our own code. The Sentry SDK fails quietly by design: when a
/// key is revoked, the project is deleted, or the quota runs out, the server answers with an error
/// and the app carries on as though the report had gone. Nothing in the product ever notices, so
/// the only way to know crash reporting still works is to send an event to the configured DSN and
/// require the server to take it.
///
/// Opt-in: set SENTRY_DSN and it runs; leave it unset and it does nothing. That keeps ordinary runs
/// offline, and it keeps the answer honest — an unreachable DSN fails here, it never passes.
/// </summary>
public class CrashReportingDeliveryTests
{
    private readonly ITestOutputHelper _output;

    public CrashReportingDeliveryTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Dsn_AcceptsADeliveredEvent()
    {
        var dsn = Environment.GetEnvironmentVariable("SENTRY_DSN");
        if (string.IsNullOrWhiteSpace(dsn))
        {
            _output.WriteLine("SENTRY_DSN is not set, so crash-reporting delivery was not exercised.");
            return;
        }

        var target = ParseStoreEndpoint(dsn!);
        _output.WriteLine($"Sending a test event to {target.StoreUrl} (project {target.ProjectId}).");

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        using var request = new HttpRequestMessage(HttpMethod.Post, target.StoreUrl);
        request.Headers.TryAddWithoutValidation(
            "X-Sentry-Auth",
            $"Sentry sentry_version=7, sentry_key={target.PublicKey}, sentry_client=kitcheninventory-tests/1.0");
        request.Content = new StringContent(BuildEvent(), Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode,
            $"Sentry refused the event: {(int)response.StatusCode} {response.ReasonPhrase}. Body: {body}");

        using var document = JsonDocument.Parse(body);
        Assert.True(document.RootElement.TryGetProperty("id", out var acceptedId),
            $"Sentry accepted the request but returned no event id. Body: {body}");
        _output.WriteLine($"Sentry accepted the event as {acceptedId.GetString()}.");
    }

    /// <summary>
    /// Reads a DSN back into the three things needed to post to it. A DSN looks like
    /// <c>https://publicKey@host/projectId</c>, with an optional path prefix in front of the
    /// project id on self-hosted installations.
    /// </summary>
    private static (string PublicKey, string ProjectId, string StoreUrl) ParseStoreEndpoint(string dsn)
    {
        var uri = new Uri(dsn);

        // A DSN may carry a secret key after the public one ("publicKey:secretKey"); sending only
        // needs the public one.
        var publicKey = uri.UserInfo.Split(':')[0];
        Assert.False(string.IsNullOrWhiteSpace(publicKey), $"The Sentry DSN carries no public key: '{dsn}'.");

        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        Assert.True(segments.Length >= 1, $"The Sentry DSN carries no project id: '{dsn}'.");

        var projectId = segments[^1];
        var prefix = segments.Length > 1 ? "/" + string.Join('/', segments[..^1]) : string.Empty;

        return (publicKey, projectId, $"{uri.Scheme}://{uri.Authority}{prefix}/api/{projectId}/store/");
    }

    private static string BuildEvent() => JsonSerializer.Serialize(new
    {
        // The store endpoint wants a 32-character hex id, which is the same shape Sentry's own SDKs use.
        event_id = Guid.NewGuid().ToString("N"),
        timestamp = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture),
        level = "error",
        platform = "csharp",
        logger = "KitchenInventory.Tests",
        message = new { message = "KitchenInventory crash-reporting delivery check" },
        tags = new { source = "delivery-test", app = "KitchenInventory" }
    });
}
