using CasCap.Abstractions;
using CasCap.Constants;
using CasCap.Data;
using CasCap.Data.Entities;
using CasCap.Diagnostics;
using CasCap.Extensions;
using CasCap.Models;
using CasCap.Services;
using CasCap.Tests.Fakes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace CasCap.Tests;

/// <summary>Exercises the real HTTP MCP transport with local SQLite and the live subscriber registry.</summary>
[Trait("Category", "Integration")]
public sealed class SignalizrMcpTests
{
    [Fact]
    public async Task DisabledRole_Returns404()
    {
        await using var fixture = await Fixture.CreateAsync(mcpEnabled: false);
        using var httpClient = new HttpClient();

        using var response = await httpClient.GetAsync(fixture.Endpoint, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task BrowserOrigin_IsRejected()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("Origin", "https://example.com");
        httpClient.DefaultRequestHeaders.Add("Accept", "application/json, text/event-stream");

        using var response = await httpClient.PostAsJsonAsync(fixture.Endpoint,
            new { jsonrpc = "2.0", id = 1, method = "tools/list" }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Tools_AreReadOnlyStructuredAndExplicitlyRegistered()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var client = await fixture.ConnectAsync();

        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["get_signalizr_groups", "get_signalizr_status"], tools.Select(tool => tool.Name).Order());
        foreach (var tool in tools)
        {
            Assert.True(tool.ProtocolTool.Annotations?.ReadOnlyHint);
            Assert.False(tool.ProtocolTool.Annotations?.DestructiveHint);
            Assert.True(tool.ProtocolTool.Annotations?.IdempotentHint);
            Assert.False(tool.ProtocolTool.Annotations?.OpenWorldHint);
            Assert.NotNull(tool.ProtocolTool.OutputSchema);
            Assert.Equal(JsonValueKind.Object, tool.ProtocolTool.InputSchema.GetProperty("properties").ValueKind);
            Assert.Empty(tool.ProtocolTool.InputSchema.GetProperty("properties").EnumerateObject());
        }
    }

    [Theory]
    [InlineData(false, false, 2)]
    [InlineData(true, false, 3)]
    [InlineData(false, true, 3)]
    [InlineData(true, true, 4)]
    public async Task Sending_AndHistoryHaveIndependentFeatureGates(bool historyEnabled, bool sendingEnabled, int count)
    {
        await using var fixture = await Fixture.CreateAsync(historyEnabled: historyEnabled, sendingEnabled: sendingEnabled);
        await using var client = await fixture.ConnectAsync();
        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(count, tools.Count);
        Assert.Equal(sendingEnabled, tools.Any(tool => tool.Name == "send_signalizr_message"));
        Assert.Equal(historyEnabled, tools.Any(tool => tool.Name == "get_signalizr_messages"));
        Assert.Equal(0, fixture.Gateway.SendCalls);
        if (sendingEnabled)
        {
            var send = Assert.Single(tools, tool => tool.Name == "send_signalizr_message");
            Assert.False(send.ProtocolTool.Annotations?.ReadOnlyHint);
            Assert.False(send.ProtocolTool.Annotations?.DestructiveHint);
            Assert.False(send.ProtocolTool.Annotations?.IdempotentHint);
            Assert.True(send.ProtocolTool.Annotations?.OpenWorldHint);
            Assert.NotNull(send.ProtocolTool.OutputSchema);
            Assert.Equal(["groupName", "message"],
                send.ProtocolTool.InputSchema.GetProperty("properties").EnumerateObject().Select(property => property.Name).Order());
        }
        else
        {
            await Assert.ThrowsAsync<McpProtocolException>(() => client.CallToolAsync("send_signalizr_message",
                new Dictionary<string, object?> { ["groupName"] = "system", ["message"] = "example" },
                cancellationToken: TestContext.Current.CancellationToken).AsTask());
            Assert.Equal(0, fixture.Gateway.SendCalls);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4096)]
    public async Task Sending_UsesGatewayAndReturnsOnlyAcknowledgement(int length)
    {
        await using var fixture = await Fixture.CreateAsync(sendingEnabled: true);
        const string GroupName = "My Test Group/Name";
        fixture.Groups["example-send-group"] = GroupName;
        await using var client = await fixture.ConnectAsync();
        var message = new string('x', length);

        var result = await CallAsync(client, "send_signalizr_message",
            new Dictionary<string, object?> { ["groupName"] = GroupName, ["message"] = message });

        Assert.Equal(1, fixture.Gateway.SendCalls);
        Assert.Equal(GroupName, fixture.Gateway.LastGroupName);
        Assert.Equal(message, fixture.Gateway.LastRequest?.Message);
        Assert.Null(fixture.Gateway.LastRequest?.Base64Attachments);
        Assert.True(fixture.Gateway.LastCancellationToken.CanBeCanceled);
        Assert.Equal("timestamp", Assert.Single(result.EnumerateObject()).Name);
        Assert.Equal(fixture.Gateway.Timestamp, result.GetProperty("timestamp").GetString());
    }

    [Theory]
    [InlineData("", "example")]
    [InlineData(" ", "example")]
    [InlineData("system", "")]
    [InlineData("system", " \r\n ")]
    public async Task Sending_RejectsBlankInputsBeforeGateway(string groupName, string message)
    {
        await using var fixture = await Fixture.CreateAsync(sendingEnabled: true);
        await using var client = await fixture.ConnectAsync();

        var result = await client.CallToolAsync("send_signalizr_message",
            new Dictionary<string, object?> { ["groupName"] = groupName, ["message"] = message },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Null(result.StructuredContent);
        Assert.Equal(0, fixture.Gateway.SendCalls);
    }

    [Fact]
    public async Task Sending_RejectsOversizedTextBeforeGateway()
    {
        await using var fixture = await Fixture.CreateAsync(sendingEnabled: true);
        await using var client = await fixture.ConnectAsync();

        var result = await client.CallToolAsync("send_signalizr_message",
            new Dictionary<string, object?> { ["groupName"] = "system", ["message"] = new string('x', 4097) },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(0, fixture.Gateway.SendCalls);
    }

    [Theory]
    [InlineData("SYSTEM")]
    [InlineData("Unknown Group")]
    public async Task Sending_RejectsUnknownGroupsWithoutEchoingInput(string groupName)
    {
        await using var fixture = await Fixture.CreateAsync(sendingEnabled: true);
        await using var client = await fixture.ConnectAsync();

        var result = await client.CallToolAsync("send_signalizr_message",
            new Dictionary<string, object?> { ["groupName"] = groupName, ["message"] = "example" },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(1, fixture.Gateway.SendCalls);
        Assert.Null(fixture.Gateway.LastRequest);
        Assert.DoesNotContain(groupName, JsonSerializer.Serialize(result));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Sending_ReportsUncertainOutcomeWithoutRetryOrExceptionDetail(bool timeout)
    {
        await using var fixture = await Fixture.CreateAsync(sendingEnabled: true);
        const string PrivateDetail = "synthetic-sensitive-upstream-detail";
        fixture.Gateway.Failure = timeout ? new OperationCanceledException(PrivateDetail) : new HttpRequestException(PrivateDetail);
        await using var client = await fixture.ConnectAsync();

        var result = await client.CallToolAsync("send_signalizr_message",
            new Dictionary<string, object?> { ["groupName"] = "system", ["message"] = "example" },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Null(result.StructuredContent);
        Assert.Equal(1, fixture.Gateway.SendCalls);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Contains("may have been sent", text);
        Assert.Contains("do not retry automatically", text);
        Assert.DoesNotContain(PrivateDetail, text);
    }

    [Fact]
    public async Task Sending_RequiresAnAcknowledgementTimestamp()
    {
        await using var fixture = await Fixture.CreateAsync(sendingEnabled: true);
        fixture.Gateway.Timestamp = string.Empty;
        await using var client = await fixture.ConnectAsync();

        var result = await client.CallToolAsync("send_signalizr_message",
            new Dictionary<string, object?> { ["groupName"] = "system", ["message"] = "example" },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Null(result.StructuredContent);
        Assert.Equal(1, fixture.Gateway.SendCalls);
    }

    [Fact]
    public async Task Sending_CancellationBeforeDispatchDoesNotReachGateway()
    {
        await using var fixture = await Fixture.CreateAsync(sendingEnabled: true);
        var service = new SignalizrMcpMessagingService(NullLogger<SignalizrMcpMessagingService>.Instance, fixture.Gateway);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.SendSignalizrMessage("system", "example", cancellation.Token));

        Assert.Equal(0, fixture.Gateway.SendCalls);
    }

    [Fact]
    public async Task Sending_CancellationDuringDispatchPropagatesWithoutRetry()
    {
        await using var fixture = await Fixture.CreateAsync(sendingEnabled: true);
        using var cancellation = new CancellationTokenSource();
        fixture.Gateway.BeforeSend = cancellation.Cancel;
        var service = new SignalizrMcpMessagingService(NullLogger<SignalizrMcpMessagingService>.Instance, fixture.Gateway);

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.SendSignalizrMessage("system", "example", cancellation.Token));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.Equal(cancellation.Token, fixture.Gateway.LastCancellationToken);
        Assert.Equal(1, fixture.Gateway.SendCalls);
    }

    [Fact]
    public async Task InitializeHandshake_RemainsCompatibleWithEditorClients()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("Accept", "application/json, text/event-stream");
        using var initialize = await httpClient.PostAsJsonAsync(fixture.Endpoint, new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "initialize",
            @params = new
            {
                protocolVersion = "2025-11-25",
                capabilities = new { },
                clientInfo = new { name = "example-client", version = "1.0.0" }
            }
        }, TestContext.Current.CancellationToken);
        var initialized = await ReadProtocolResultAsync(initialize);
        Assert.Equal("2025-11-25", initialized.GetProperty("protocolVersion").GetString());
        Assert.True(initialized.GetProperty("capabilities").TryGetProperty("prompts", out _));
        Assert.False(initialize.Headers.Contains("Mcp-Session-Id"));
        httpClient.DefaultRequestHeaders.Add("MCP-Protocol-Version", "2025-11-25");

        using var notification = await httpClient.PostAsJsonAsync(fixture.Endpoint,
            new { jsonrpc = "2.0", method = "notifications/initialized" }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Accepted, notification.StatusCode);

        using var tools = await httpClient.PostAsJsonAsync(fixture.Endpoint,
            new { jsonrpc = "2.0", id = 2, method = "tools/list", @params = new { } },
            TestContext.Current.CancellationToken);
        Assert.Equal(2, (await ReadProtocolResultAsync(tools)).GetProperty("tools").GetArrayLength());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Prompt_DiscoveryAndRetrievalProvideMetadataOnlyGuidance(bool historyEnabled)
    {
        await using var fixture = await Fixture.CreateAsync(historyEnabled: historyEnabled);
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("Accept", "application/json, text/event-stream");
        httpClient.DefaultRequestHeaders.Add("MCP-Protocol-Version", "2025-11-25");

        using var listResponse = await httpClient.PostAsJsonAsync(fixture.Endpoint,
            new { jsonrpc = "2.0", id = 1, method = "prompts/list", @params = new { } },
            TestContext.Current.CancellationToken);
        var list = await ReadProtocolResultAsync(listResponse);
        var prompt = Assert.Single(list.GetProperty("prompts").EnumerateArray());
        Assert.Equal("summarise_signalizr_status", prompt.GetProperty("name").GetString());
        Assert.False(string.IsNullOrWhiteSpace(prompt.GetProperty("description").GetString()));
        if (prompt.TryGetProperty("arguments", out var arguments))
            Assert.Empty(arguments.EnumerateArray());

        using var getResponse = await httpClient.PostAsJsonAsync(fixture.Endpoint,
            new
            {
                jsonrpc = "2.0",
                id = 2,
                method = "prompts/get",
                @params = new { name = "summarise_signalizr_status" }
            },
            TestContext.Current.CancellationToken);
        var result = await ReadProtocolResultAsync(getResponse);
        var message = Assert.Single(result.GetProperty("messages").EnumerateArray());
        Assert.Equal("user", message.GetProperty("role").GetString());
        Assert.Equal("text", message.GetProperty("content").GetProperty("type").GetString());
        var text = message.GetProperty("content").GetProperty("text").GetString();
        Assert.NotNull(text);
        Assert.Contains("get_signalizr_status", text);
        Assert.Contains("get_signalizr_groups", text);
        Assert.Contains("Do not retrieve message history", text);
        Assert.DoesNotContain("get_signalizr_messages", text);
    }

    [Fact]
    public async Task History_DisabledToolCannotBeCalled()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var client = await fixture.ConnectAsync();

        var exception = await Assert.ThrowsAsync<McpProtocolException>(() =>
            client.CallToolAsync("get_signalizr_messages",
                new Dictionary<string, object?> { ["groupName"] = "system" },
                cancellationToken: TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("Unknown tool", exception.Message);
    }

    [Fact]
    public async Task Status_TracksLiveConnectionsNotPersistedCursorsOrMcpSessions()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var client = await fixture.ConnectAsync();
        var registry = fixture.App.Services.GetRequiredService<IInboundSubscriberRegistry>();
        await AssertConnectedClientsAsync(client, 0);

        using var first = await registry.SubscribeAsync("example-client-one", TestContext.Current.CancellationToken);
        using var second = await registry.SubscribeAsync("example-client-two", TestContext.Current.CancellationToken);
        await AssertConnectedClientsAsync(client, 2);

        registry.Unsubscribe(first);
        await AssertConnectedClientsAsync(client, 1);
        registry.Unsubscribe(second);
        await AssertConnectedClientsAsync(client, 0);

        await using var db = await fixture.Database.CreateDbContextAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, await db.SubscriberCursors.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Groups_UseCurrentResolverWithoutGroupIdentifiers()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var client = await fixture.ConnectAsync();

        var result = await CallAsync(client, "get_signalizr_groups");
        Assert.Equal(["alerts", "system"], result.GetProperty("groups").EnumerateArray().Select(item => item.GetString()));
        Assert.Single(result.EnumerateObject());

        fixture.Groups.Clear();
        result = await CallAsync(client, "get_signalizr_groups");
        Assert.Empty(result.GetProperty("groups").EnumerateArray());
    }

    [Fact]
    public async Task Groups_AndHistoryUseExactGroupNamesWithSpaces()
    {
        await using var fixture = await Fixture.CreateAsync(historyEnabled: true);
        fixture.Groups.Clear();
        fixture.Groups["example-group"] = "My Test Group Name";
        await using (var db = await fixture.Database.CreateDbContextAsync(TestContext.Current.CancellationToken))
        {
            db.InboundMessages.Add(new InboundMessageEntity { GroupName = "My Test Group Name", Message = "example" });
            db.InboundMessages.Add(new InboundMessageEntity { GroupName = "Other Group", Message = "other group" });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        await using var client = await fixture.ConnectAsync();

        var groups = await CallAsync(client, "get_signalizr_groups");
        Assert.Equal("My Test Group Name", Assert.Single(groups.GetProperty("groups").EnumerateArray()).GetString());
        var history = await CallAsync(client, "get_signalizr_messages",
            new Dictionary<string, object?> { ["groupName"] = "My Test Group Name" });
        Assert.Equal("My Test Group Name", history.GetProperty("groupName").GetString());
        Assert.Equal("example", Assert.Single(history.GetProperty("messages").EnumerateArray()).GetProperty("text").GetString());

        var unknownGroup = await client.CallToolAsync("get_signalizr_messages",
            new Dictionary<string, object?> { ["groupName"] = "Other Group" },
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(unknownGroup.IsError);
    }

    [Fact]
    public async Task History_OptInBoundsTextAndProjectsOnlyRequestedGroup()
    {
        await using var fixture = await Fixture.CreateAsync(historyEnabled: true);
        await using var client = await fixture.ConnectAsync();
        await fixture.SeedMessagesAsync();

        var result = await CallAsync(client, "get_signalizr_messages",
            new Dictionary<string, object?> { ["groupName"] = "system", ["count"] = 2 });
        Assert.Equal("system", result.GetProperty("groupName").GetString());
        var messages = result.GetProperty("messages").EnumerateArray().ToArray();
        Assert.Equal(2, messages.Length);
        Assert.Null(messages[0].GetProperty("text").GetString());
        Assert.False(messages[0].GetProperty("truncated").GetBoolean());
        Assert.Equal(2000, messages[1].GetProperty("text").GetString()!.Length);
        Assert.True(messages[1].GetProperty("truncated").GetBoolean());
        foreach (var message in messages)
            Assert.Equal(["persistedAtUnixMilliseconds", "text", "timestamp", "truncated"],
                message.EnumerateObject().Select(property => property.Name).Order());
        Assert.Equal(["groupName", "messages"], result.EnumerateObject().Select(property => property.Name).Order());
    }

    [Fact]
    public async Task History_UsesDefaultAndMaximumCounts()
    {
        await using var fixture = await Fixture.CreateAsync(historyEnabled: true);
        await using var client = await fixture.ConnectAsync();
        await using (var db = await fixture.Database.CreateDbContextAsync(TestContext.Current.CancellationToken))
        {
            for (var index = 0; index < 60; index++)
                db.InboundMessages.Add(new InboundMessageEntity { GroupName = "system", Message = $"example {index}" });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var result = await CallAsync(client, "get_signalizr_messages",
            new Dictionary<string, object?> { ["groupName"] = "system" });
        Assert.Equal(10, result.GetProperty("messages").GetArrayLength());
        Assert.Equal("example 59", result.GetProperty("messages")[0].GetProperty("text").GetString());

        result = await CallAsync(client, "get_signalizr_messages",
            new Dictionary<string, object?> { ["groupName"] = "system", ["count"] = 50 });
        Assert.Equal(50, result.GetProperty("messages").GetArrayLength());
    }

    [Theory]
    [InlineData("system", 0)]
    [InlineData("system", 51)]
    [InlineData("system", -1)]
    [InlineData("not-a-group", 1)]
    [InlineData("SYSTEM", 1)]
    [InlineData("", 1)]
    public async Task History_RejectsInvalidArguments(string groupName, int count)
    {
        await using var fixture = await Fixture.CreateAsync(historyEnabled: true);
        await using var client = await fixture.ConnectAsync();

        var result = await client.CallToolAsync("get_signalizr_messages",
            new Dictionary<string, object?> { ["groupName"] = groupName, ["count"] = count },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Null(result.StructuredContent);
    }

    [Fact]
    public async Task History_EmptyGroupHasNoMessages()
    {
        await using var fixture = await Fixture.CreateAsync(historyEnabled: true);
        await using var client = await fixture.ConnectAsync();

        var result = await CallAsync(client, "get_signalizr_messages",
            new Dictionary<string, object?> { ["groupName"] = "alerts", ["count"] = 1 });

        Assert.Empty(result.GetProperty("messages").EnumerateArray());
    }

    [Fact]
    public async Task History_CancellationPropagates()
    {
        await using var fixture = await Fixture.CreateAsync(historyEnabled: true);
        var service = new SignalizrMcpMessageHistoryQueryService(
            fixture.App.Services.GetRequiredService<IGroupResolver>(), fixture.Database);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.GetSignalizrMessages("system", cancellationToken: cancellation.Token));
    }

    private static async Task AssertConnectedClientsAsync(McpClient client, int expected)
    {
        var result = await CallAsync(client, "get_signalizr_status");
        Assert.Equal(expected, result.GetProperty("connectedClients").GetInt32());
        Assert.Equal(TimeSpan.Zero, result.GetProperty("observedAtUtc").GetDateTimeOffset().Offset);
        Assert.Equal(["connectedClients", "observedAtUtc"],
            result.EnumerateObject().Select(property => property.Name).Order());
    }

    private static async Task<JsonElement> ReadProtocolResultAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var json = response.Content.Headers.ContentType?.MediaType == "text/event-stream"
            ? body.Split('\n').Single(line => line.StartsWith("data: ", StringComparison.Ordinal))[6..]
            : body;
        return JsonSerializer.Deserialize<JsonElement>(json).GetProperty("result");
    }

    private static async Task<JsonElement> CallAsync(
        McpClient client, string name, Dictionary<string, object?>? arguments = null)
    {
        var result = await client.CallToolAsync(name, arguments, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotEqual(true, result.IsError);
        Assert.NotNull(result.StructuredContent);
        var content = Assert.Single(result.Content);
        var text = Assert.IsType<TextContentBlock>(content);
        Assert.True(JsonElement.DeepEquals(result.StructuredContent.Value, JsonSerializer.Deserialize<JsonElement>(text.Text)));
        return result.StructuredContent.Value;
    }

    private sealed class Fixture(WebApplication app, SqliteConnection connection, Dictionary<string, string> groups) : IAsyncDisposable
    {
        public WebApplication App => app;

        public Dictionary<string, string> Groups => groups;

        public Uri Endpoint => new(new Uri(app.Urls.Single()), McpRoutes.Endpoint);

        public IDbContextFactory<SignalizrDbContext> Database => app.Services.GetRequiredService<IDbContextFactory<SignalizrDbContext>>();

        public FakeMcpMessageGateway Gateway => app.Services.GetRequiredService<FakeMcpMessageGateway>();

        public static async Task<Fixture> CreateAsync(
            bool mcpEnabled = true, bool historyEnabled = false, bool sendingEnabled = false)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Test" });
            builder.Configuration.Sources.Clear();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{McpConfig.ConfigurationSectionName}:{nameof(McpConfig.MessageHistoryEnabled)}"] = historyEnabled.ToString(),
                [$"{McpConfig.ConfigurationSectionName}:{nameof(McpConfig.MessageSendingEnabled)}"] = sendingEnabled.ToString()
            });
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            var groups = new Dictionary<string, string>
            {
                ["example-group-system"] = "system",
                ["example-group-alerts"] = "alerts"
            };
            builder.Services.AddSingleton<IGroupResolver>(new FakeGroupResolver(groups));
            builder.Services.AddSingleton<FakeMcpMessageGateway>();
            builder.Services.AddSingleton<IMessageGateway>(sp => sp.GetRequiredService<FakeMcpMessageGateway>());
            builder.Services.AddSingleton(TimeProvider.System);
            builder.Services.AddSingleton<SignalizrMetrics>();
            builder.Services.Configure<SubscriberConfig>(_ => { });
            builder.Services.AddSingleton<IOperatorNotifier, FakeOperatorNotifier>();
            builder.Services.AddDbContextFactory<SignalizrDbContext>(options => options.UseSqlite(connection));
            builder.Services.AddSingleton<IInboundSubscriberRegistry, InboundSubscriberRegistry>();
            var features = new FeatureConfig
            {
                EnabledFeatures = mcpEnabled ? "Gateway,Receiver,Mcp" : "Gateway,Receiver"
            }.GetEnabledFeatures();
            builder.Services.AddSignalizrMcp(builder.Configuration, features);
            var app = builder.Build();
            app.MapSignalizrMcp(features);
            var fixture = new Fixture(app, connection, groups);
            await using (var db = await fixture.Database.CreateDbContextAsync(TestContext.Current.CancellationToken))
                await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
            await app.StartAsync(TestContext.Current.CancellationToken);
            return fixture;
        }

        public Task<McpClient> ConnectAsync() => McpClient.CreateAsync(
            new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = Endpoint,
                TransportMode = HttpTransportMode.StreamableHttp
            }),
            cancellationToken: TestContext.Current.CancellationToken);

        public async Task SeedMessagesAsync()
        {
            await using var db = await Database.CreateDbContextAsync(TestContext.Current.CancellationToken);
            db.InboundMessages.AddRange(
                new InboundMessageEntity { GroupName = "system", Message = "older example", Sender = "+10000000000" },
                new InboundMessageEntity { GroupName = "system", Message = new string('x', 2001), Sender = "+10000000000" },
                new InboundMessageEntity { GroupName = "alerts", Message = "excluded group text", Sender = "+10000000000" },
                new InboundMessageEntity { GroupName = null, Message = "excluded unresolved text", Sender = "+10000000000" },
                new InboundMessageEntity { GroupName = "system", Message = null, Sender = "+10000000000" });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            await app.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
