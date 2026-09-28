using CasCap.Signalizr.Client.Exceptions;

namespace CasCap.Signalizr.Client;

/// <summary>Consumer conveniences built on <see cref="ISignalizrClient"/>.</summary>
/// <remarks>
/// Reactions and typing indicators are feedback about work, not the work itself, so the <c>Try*</c>
/// helpers treat an unreachable gateway or a client timeout as a failed indicator rather than a fault.
/// Cancellation the caller requested still propagates.
/// </remarks>
public static class SignalizrClientExtensions
{
    /// <summary>Sets a reaction, reporting a transport failure instead of throwing it.</summary>
    /// <param name="client">The client.</param>
    /// <param name="groupName">Exact configured Signal group name.</param>
    /// <param name="reaction">The reaction emoji.</param>
    /// <param name="targetTimestamp">The target message's timestamp.</param>
    /// <param name="targetAuthor">The target message's sender, or <see langword="null"/> for a message this gateway sent.</param>
    /// <param name="onFailure">Invoked with the swallowed exception, for example to log it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> when the gateway accepted the reaction.</returns>
    public static Task<bool> TrySetReactionAsync(this ISignalizrClient client, string groupName, string reaction,
        long targetTimestamp, string? targetAuthor = null, Action<Exception>? onFailure = null,
        CancellationToken cancellationToken = default) =>
        TryAsync(() => client.SetReactionAsync(groupName, reaction, targetTimestamp, targetAuthor, cancellationToken),
            onFailure, cancellationToken);

    /// <summary>Shows the typing indicator, reporting a transport failure instead of throwing it.</summary>
    /// <param name="client">The client.</param>
    /// <param name="groupName">Exact configured Signal group name.</param>
    /// <param name="onFailure">Invoked with the swallowed exception, for example to log it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> when the gateway accepted the request.</returns>
    public static Task<bool> TryStartTypingAsync(this ISignalizrClient client, string groupName,
        Action<Exception>? onFailure = null, CancellationToken cancellationToken = default) =>
        TryAsync(() => client.StartTypingAsync(groupName, cancellationToken), onFailure, cancellationToken);

    /// <summary>Clears the typing indicator, reporting a transport failure instead of throwing it.</summary>
    /// <param name="client">The client.</param>
    /// <param name="groupName">Exact configured Signal group name.</param>
    /// <param name="onFailure">Invoked with the swallowed exception, for example to log it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> when the gateway accepted the request.</returns>
    public static Task<bool> TryStopTypingAsync(this ISignalizrClient client, string groupName,
        Action<Exception>? onFailure = null, CancellationToken cancellationToken = default) =>
        TryAsync(() => client.StopTypingAsync(groupName, cancellationToken), onFailure, cancellationToken);

    /// <summary>Waits until the gateway is reachable and serves the groups a consumer needs.</summary>
    /// <remarks>
    /// An unreachable gateway is retried every <paramref name="retryDelay"/>, because it recovers on its own.
    /// A missing required group cannot be fixed by retrying, so it throws. A missing optional group, such as an
    /// operator diagnostics group, is returned so the caller can degrade gracefully.
    /// </remarks>
    /// <param name="client">The client.</param>
    /// <param name="requiredGroups">Groups the consumer cannot run without.</param>
    /// <param name="optionalGroups">Groups whose absence only degrades the consumer; <see langword="null"/> or empty values are ignored.</param>
    /// <param name="retryDelay">Delay between attempts while the gateway is unreachable.</param>
    /// <param name="timeProvider">Clock for the retry delay, or <see langword="null"/> for <see cref="TimeProvider.System"/>.</param>
    /// <param name="onRetry">Invoked with the failure and the 1-based attempt number before each retry.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The optional groups the gateway does not serve.</returns>
    /// <exception cref="SignalizrGroupNotConfiguredException">A required group is not served by the gateway.</exception>
    public static async Task<IReadOnlyList<string>> WaitForGroupsAsync(this ISignalizrClient client,
        IReadOnlyCollection<string> requiredGroups, IReadOnlyCollection<string?>? optionalGroups, TimeSpan retryDelay,
        TimeProvider? timeProvider = null, Action<Exception, int>? onRetry = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(requiredGroups);
        timeProvider ??= TimeProvider.System;

        for (var attempt = 1; ; attempt++)
        {
            IReadOnlyList<string> groups;
            try
            {
                groups = await client.GetGroupsAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
            {
                onRetry?.Invoke(ex, attempt);
                await Task.Delay(retryDelay, timeProvider, cancellationToken).ConfigureAwait(false);
                continue;
            }

            var missingRequired = requiredGroups.Count(group => !groups.Contains(group, StringComparer.Ordinal));
            if (missingRequired > 0)
                throw new SignalizrGroupNotConfiguredException(missingRequired);

            return optionalGroups is null
                ? []
                : [.. optionalGroups.OfType<string>().Where(group => group.Length > 0
                    && !groups.Contains(group, StringComparer.Ordinal))];
        }
    }

    private static async Task<bool> TryAsync(Func<Task> operation, Action<Exception>? onFailure,
        CancellationToken cancellationToken)
    {
        try
        {
            await operation().ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            onFailure?.Invoke(ex);
            return false;
        }
    }

    //An HttpClient timeout surfaces as a cancellation the caller did not request.
    private static bool IsTransportFailure(Exception ex, CancellationToken cancellationToken) =>
        ex is HttpRequestException || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested);
}
