using CasCap.Abstractions;
using CasCap.Data;
using CasCap.Data.Entities;
using CasCap.Models.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace CasCap.IntegrationTests;

/// <summary>Gateway/Receiver host with fake sends, isolated attachment storage and no background execution.</summary>
public sealed class GatewayWebApplicationFactory : SignalizrWebApplicationFactory, IAsyncLifetime
{
    protected override string EnabledFeatures => $"{FeatureNames.Gateway},{FeatureNames.Receiver}";

    public const string GroupName = "Example Chat";

    public Exception? Failure { get; set; }
    public int Calls { get; private set; }
    public string? Operation { get; private set; }
    public string? RequestedGroup { get; private set; }
    public object? Request { get; private set; }
    public CancellationToken Token { get; private set; }

    public void Reset(Exception? failure = null)
    {
        Failure = failure;
        Calls = 0;
        Operation = null;
        RequestedGroup = null;
        Request = null;
        Token = default;
    }

    public async ValueTask InitializeAsync()
    {
        await using var db = await Services.GetRequiredService<IDbContextFactory<SignalizrDbContext>>()
            .CreateDbContextAsync(TestContext.Current.CancellationToken);
        db.InboundMessages.Add(new InboundMessageEntity
        {
            Id = 1,
            GroupName = GroupName,
            Attachments =
            [
                new() { Id = "example-attachment", Content = [0, 1, 2, 255], ContentType = "image/png", Filename = "example.png" },
                new() { Id = "example-empty", Content = [] },
            ],
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IMessageGateway>();
            services.AddSingleton<IMessageGateway>(new Gateway(this));
            services.RemoveAll<IGroupResolver>();
            services.AddSingleton<IGroupResolver, Groups>();
            services.RemoveAll<IDbContextFactory<SignalizrDbContext>>();
            services.RemoveAll<DbContextOptions<SignalizrDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<SignalizrDbContext>>();
            var database = $"{nameof(GatewayWebApplicationFactory)}-{Guid.NewGuid():N}";
            services.AddDbContextFactory<SignalizrDbContext>(options => options.UseInMemoryDatabase(database));
        });
    }

    private Task Record(string operation, string groupName, object? request, CancellationToken token)
    {
        Calls++;
        Operation = operation;
        RequestedGroup = groupName;
        Request = request;
        Token = token;
        token.ThrowIfCancellationRequested();
        if (Failure is { } failure)
            throw failure;
        return Task.CompletedTask;
    }

    private sealed class Groups : IGroupResolver
    {
        public IReadOnlyCollection<string> GroupNames => [GroupName];
        public bool TryGetGroupId(string groupName, out string groupId) { groupId = "example-group-id"; return groupName == GroupName; }
        public bool TryGetGroupName(string groupId, out string groupName) { groupName = GroupName; return groupId == "example-group-id"; }
        public Task RefreshAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class Gateway(GatewayWebApplicationFactory factory) : IMessageGateway
    {
        public async Task<SendMessageResponse> SendAsync(string groupName, SendMessageRequest request, CancellationToken cancellationToken = default)
        {
            await factory.Record(nameof(SendAsync), groupName, request, cancellationToken);
            return new() { GroupName = groupName, Timestamp = "123" };
        }
        public Task SetReactionAsync(string groupName, GroupReactionRequest request, CancellationToken cancellationToken = default)
            => factory.Record(nameof(SetReactionAsync), groupName, request, cancellationToken);
        public Task RemoveReactionAsync(string groupName, GroupReactionRequest request, CancellationToken cancellationToken = default)
            => factory.Record(nameof(RemoveReactionAsync), groupName, request, cancellationToken);
        public Task SetDeliveryReactionAsync(string groupName, string deliveryId, string reaction, CancellationToken cancellationToken = default)
            => factory.Record(nameof(SetDeliveryReactionAsync), groupName, (deliveryId, reaction), cancellationToken);
        public Task RemoveDeliveryReactionAsync(string groupName, string deliveryId, string reaction, CancellationToken cancellationToken = default)
            => factory.Record(nameof(RemoveDeliveryReactionAsync), groupName, (deliveryId, reaction), cancellationToken);
        public Task StartTypingAsync(string groupName, CancellationToken cancellationToken = default)
            => factory.Record(nameof(StartTypingAsync), groupName, null, cancellationToken);
        public Task StopTypingAsync(string groupName, CancellationToken cancellationToken = default)
            => factory.Record(nameof(StopTypingAsync), groupName, null, cancellationToken);
        public async Task<GroupPollResponse> CreatePollAsync(string groupName, GroupPollRequest request, CancellationToken cancellationToken = default)
        {
            await factory.Record(nameof(CreatePollAsync), groupName, request, cancellationToken);
            return new() { GroupName = groupName, PollId = "456" };
        }
        public Task ClosePollAsync(string groupName, string pollId, CancellationToken cancellationToken = default)
            => factory.Record(nameof(ClosePollAsync), groupName, pollId, cancellationToken);
    }
}
