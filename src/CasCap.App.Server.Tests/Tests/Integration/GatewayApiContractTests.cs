using CasCap.Abstractions;
using CasCap.Controllers;
using CasCap.Exceptions;
using CasCap.Models.Dtos;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using System.Text;
using System.Text.Json;

namespace CasCap.IntegrationTests;

/// <summary>Credential-free HTTP contracts with fake gateway operations and in-memory attachment rows.</summary>
[Trait("Category", "Integration")]
public sealed class GatewayApiContractTests(GatewayWebApplicationFactory factory) : IClassFixture<GatewayWebApplicationFactory>
{
    [Theory]
    [InlineData("POST", "messages", """{"message":"example"}""", "SendAsync", 200)]
    [InlineData("POST", "reactions", """{"reaction":"x","targetTimestamp":123}""", "SetReactionAsync", 204)]
    [InlineData("DELETE", "reactions", """{"reaction":"x","targetTimestamp":123}""", "RemoveReactionAsync", 204)]
    [InlineData("POST", "messages/example-delivery/reactions", """{"reaction":"x"}""", "SetDeliveryReactionAsync", 204)]
    [InlineData("DELETE", "messages/example-delivery/reactions", """{"reaction":"x"}""", "RemoveDeliveryReactionAsync", 204)]
    [InlineData("PUT", "typing", null, "StartTypingAsync", 204)]
    [InlineData("DELETE", "typing", null, "StopTypingAsync", 204)]
    [InlineData("POST", "polls", """{"question":"example?","answers":["one","two"]}""", "CreatePollAsync", 200)]
    [InlineData("DELETE", "polls/example-poll", null, "ClosePollAsync", 204)]
    public async Task Operations(string method, string path, string? json, string operation, int status)
    {
        factory.Reset();
        using var client = factory.CreateClient();
        using var request = CreateRequest(method, path, json);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal(1, factory.Calls);
        Assert.Equal(operation, factory.Operation);
        Assert.Equal(GatewayWebApplicationFactory.GroupName, factory.RequestedGroup);
        Assert.True(factory.Token.CanBeCanceled);
        if (status == 200)
        {
            Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            Assert.Equal(GatewayWebApplicationFactory.GroupName, body.RootElement.GetProperty("groupName").GetString());
            Assert.Equal(operation == "SendAsync" ? "123" : "456",
                body.RootElement.GetProperty(operation == "SendAsync" ? "timestamp" : "pollId").GetString());
        }
        else
            Assert.Empty(await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Groups()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/v1/groups", TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.Equal(GatewayWebApplicationFactory.GroupName, Assert.Single(body.RootElement.EnumerateArray()).GetString());
    }

    [Theory]
    [InlineData("unknown-group", "application/json", 404)]
    [InlineData("unknown-delivery", "text/plain", 404)]
    [InlineData("role", "text/html", 501)]
    [InlineData("upstream", "application/json", 502)]
    [InlineData("upstream", "text/plain", 502)]
    [InlineData("upstream", "text/html", 502)]
    public async Task Problems(string kind, string accept, int status)
    {
        Exception failure = kind switch
        {
            "unknown-group" => new UnknownGroupException(GatewayWebApplicationFactory.GroupName),
            "unknown-delivery" => new UnknownDeliveryException(GatewayWebApplicationFactory.GroupName, "example-delivery"),
            "role" => new NotSupportedException("sensitive-example-detail"),
            "upstream" => new HttpRequestException("sensitive-example-detail"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        factory.Reset(failure);
        using var client = factory.CreateClient();
        using var request = CreateRequest("POST", "messages", """{"message":"example"}""");
        request.Headers.Accept.ParseAdd(accept);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var body = JsonDocument.Parse(json);

        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(status, body.RootElement.GetProperty("status").GetInt32());
        Assert.DoesNotContain("sensitive-example-detail", json, StringComparison.Ordinal);
        Assert.Equal(1, factory.Calls);
    }

    [Theory]
    [InlineData("messages", "{}")]
    [InlineData("messages", """{"message":""}""")]
    [InlineData("reactions", """{"reaction":"x","targetTimestamp":0}""")]
    [InlineData("polls", """{"question":"example?","answers":["one"]}""")]
    public async Task Validation(string path, string json)
    {
        factory.Reset();
        using var client = factory.CreateClient();
        using var request = CreateRequest("POST", path, json);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.NotEmpty(body.RootElement.GetProperty("errors").EnumerateObject());
        Assert.Equal(0, factory.Calls);
    }

    [Theory]
    [InlineData("example-attachment", 200, 4)]
    [InlineData("example-empty", 200, 0)]
    [InlineData("example-missing", 404, 0)]
    public async Task Attachments(string id, int status, int length)
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync($"/api/v1/attachments/{id}", TestContext.Current.CancellationToken);
        var content = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);

        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal(length, content.Length);
        if (status == 200)
        {
            Assert.Equal("application/octet-stream", response.Content.Headers.ContentType?.MediaType);
            Assert.Null(response.Content.Headers.ContentDisposition);
            if (length > 0)
                Assert.Equal(new byte[] { 0, 1, 2, 255 }, content);
        }
    }

    [Fact]
    public async Task Cancellation()
    {
        factory.Reset();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var controller = new GroupsController(factory.Services.GetRequiredService<IGroupResolver>(),
            factory.Services.GetRequiredService<IMessageGateway>());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            controller.SendMessage(GatewayWebApplicationFactory.GroupName, new SendMessageRequest { Message = "example" }, cancellation.Token));
        Assert.Equal(cancellation.Token, factory.Token);
        Assert.Equal(1, factory.Calls);
    }

    [Fact]
    public async Task DisabledFeatures()
    {
        await using var disabled = new SignalizrWebApplicationFactory();
        using var client = disabled.CreateClient();
        using var groups = await client.GetAsync("/api/v1/groups", TestContext.Current.CancellationToken);
        using var attachments = await client.GetAsync("/api/v1/attachments/example-attachment", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, groups.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, attachments.StatusCode);
    }

    [Fact]
    public void ApiMetadata()
    {
        var descriptions = factory.Services.GetRequiredService<IApiDescriptionGroupCollectionProvider>()
            .ApiDescriptionGroups.Items.SelectMany(group => group.Items).ToArray();
        var send = Assert.Single(descriptions, action => action.RelativePath == "api/v1/groups/messages" && action.HttpMethod == "POST");
        Assert.Contains(send.SupportedResponseTypes, response => response.StatusCode == 200 && response.Type == typeof(SendMessageResponse));
        Assert.Contains(send.SupportedResponseTypes, response => response.StatusCode == 400);
        Assert.Contains(send.SupportedResponseTypes, response => response.StatusCode == 502);
        var attachment = Assert.Single(descriptions, action => action.RelativePath == "api/v1/attachments/{attachmentId}");
        var success = Assert.Single(attachment.SupportedResponseTypes, response => response.StatusCode == 200);
        Assert.Equal(typeof(byte[]), success.Type);
        Assert.Contains(success.ApiResponseFormats, format => format.MediaType == "application/octet-stream");
        Assert.DoesNotContain(success.ApiResponseFormats, format => format.MediaType == "application/json");
        Assert.Contains(attachment.SupportedResponseTypes, response => response.StatusCode == 404);
    }

    private static HttpRequestMessage CreateRequest(string method, string path, string? json)
    {
        var request = new HttpRequestMessage(new HttpMethod(method),
            $"/api/v1/groups/{path}?groupName={Uri.EscapeDataString(GatewayWebApplicationFactory.GroupName)}");
        if (json is not null)
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return request;
    }
}
