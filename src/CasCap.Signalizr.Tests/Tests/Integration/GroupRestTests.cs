using CasCap.Abstractions;
using CasCap.Controllers;
using CasCap.Grpc;
using CasCap.Models.Dtos;
using CasCap.Signalizr.Client;
using CasCap.Tests.Fakes;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace CasCap.Tests;

/// <summary>Verifies exact Signal group names round-trip through the client and HTTP query binding.</summary>
[Trait("Category", "Integration")]
public sealed class GroupRestTests
{
    [Theory]
    [InlineData("My Test Group Name")]
    [InlineData("Ops/Alerts")]
    [InlineData("Literal%2FName")]
    [InlineData("A+B & C?#")]
    [InlineData("Gruppe \u00dcberblick")]
    public async Task GroupOperations_PreserveExactName(string groupName)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var gateway = new RecordingGateway();
        builder.Services.AddSingleton<IMessageGateway>(gateway);
        builder.Services.AddSingleton<IGroupResolver>(new FakeGroupResolver(new() { ["example-id"] = groupName }));
        builder.Services.AddControllers().AddApplicationPart(typeof(GroupsController).Assembly);
        await using var app = builder.Build();
        app.MapControllers();
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        using var grpc = GrpcChannel.ForAddress(http.BaseAddress);
        var client = new SignalizrClient(http, new Inbound.InboundClient(grpc),
            Options.Create(new SignalizrClientConfig
            {
                BaseAddress = http.BaseAddress.ToString(),
                GrpcAddress = http.BaseAddress.ToString(),
                SubscriberName = "example-client"
            }));
        var token = TestContext.Current.CancellationToken;

        Assert.Equal([groupName], await client.GetGroupsAsync(token));
        Assert.Equal("123", await client.SendAsync(groupName, "example", token));
        await client.SetReactionAsync(groupName, "ok", 123, cancellationToken: token);
        await client.RemoveReactionAsync(groupName, "ok", 123, cancellationToken: token);
        var delivery = new SignalizrMessage { DeliveryId = "1", GroupName = groupName };
        await client.SetReactionAsync(delivery, "ok", token);
        await client.RemoveReactionAsync(delivery, "ok", token);
        await client.StartTypingAsync(groupName, token);
        await client.StopTypingAsync(groupName, token);
        Assert.Equal("123", await client.CreatePollAsync(groupName, "Question?", ["Yes", "No"], cancellationToken: token));
        await client.ClosePollAsync(groupName, "123", token);

        Assert.Equal(9, gateway.Groups.Count);
        Assert.All(gateway.Groups, actual => Assert.Equal(groupName, actual));

        using var missing = await http.PostAsJsonAsync("api/v1/groups/messages", new { message = "example" }, token);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal(9, gateway.Groups.Count);
    }

    private sealed class RecordingGateway : IMessageGateway
    {
        public List<string> Groups { get; } = [];

        public Task<SendMessageResponse> SendAsync(string groupName, SendMessageRequest request, CancellationToken cancellationToken = default)
        {
            Groups.Add(groupName);
            return Task.FromResult(new SendMessageResponse { GroupName = groupName, Timestamp = "123" });
        }

        public Task SetReactionAsync(string groupName, GroupReactionRequest request, CancellationToken cancellationToken = default)
            => Record(groupName);

        public Task RemoveReactionAsync(string groupName, GroupReactionRequest request, CancellationToken cancellationToken = default)
            => Record(groupName);

        public Task SetDeliveryReactionAsync(string groupName, string deliveryId, string reaction, CancellationToken cancellationToken = default)
            => Record(groupName);

        public Task RemoveDeliveryReactionAsync(string groupName, string deliveryId, string reaction, CancellationToken cancellationToken = default)
            => Record(groupName);

        public Task StartTypingAsync(string groupName, CancellationToken cancellationToken = default) => Record(groupName);

        public Task StopTypingAsync(string groupName, CancellationToken cancellationToken = default) => Record(groupName);

        public Task<GroupPollResponse> CreatePollAsync(string groupName, GroupPollRequest request, CancellationToken cancellationToken = default)
        {
            Groups.Add(groupName);
            return Task.FromResult(new GroupPollResponse { GroupName = groupName, PollId = "123" });
        }

        public Task ClosePollAsync(string groupName, string pollId, CancellationToken cancellationToken = default) => Record(groupName);

        private Task Record(string groupName)
        {
            Groups.Add(groupName);
            return Task.CompletedTask;
        }
    }
}
