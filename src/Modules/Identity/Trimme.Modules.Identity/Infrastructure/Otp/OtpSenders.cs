using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Trimme.Modules.Identity.Application;

namespace Trimme.Modules.Identity.Infrastructure.Otp;

/// <summary>Which OTP sender is active. Bound from <c>Identity:Otp:Sender</c>.</summary>
internal enum OtpSenderKind
{
    /// <summary>No delivery channel: codes cannot be sent (production until the WhatsApp adapter lands in Phase 15).</summary>
    None,

    /// <summary>Development and Testing only: codes go to an in-memory inbox read through a dev-only endpoint.</summary>
    DevInbox,
}

/// <summary>OTP delivery settings. Bound from <c>Identity:Otp</c>.</summary>
internal sealed class OtpDeliveryOptions
{
    public const string SectionName = "Identity:Otp";

    /// <summary>Explicit sender; when unset: <see cref="OtpSenderKind.DevInbox"/> in Development/Testing, otherwise <see cref="OtpSenderKind.None"/>.</summary>
    public OtpSenderKind? Sender { get; set; }

    public OtpSenderKind ResolveSender(IHostEnvironment environment) =>
        Sender ?? (IsLocal(environment) ? OtpSenderKind.DevInbox : OtpSenderKind.None);

    public static bool IsLocal(IHostEnvironment environment) =>
        environment.IsDevelopment() || environment.IsEnvironment("Testing");
}

/// <summary>In-memory record of the last codes "sent" by <see cref="DevInboxOtpSender"/>. Development and Testing only.</summary>
internal sealed class DevOtpInbox
{
    private const int Capacity = 200;
    private readonly ConcurrentQueue<OtpMessage> _messages = new();

    public void Add(OtpMessage message)
    {
        _messages.Enqueue(message);
        while (_messages.Count > Capacity && _messages.TryDequeue(out _))
        {
        }
    }

    public OtpMessage? Latest(string phoneE164) =>
        _messages.Where(m => m.PhoneE164 == phoneE164).LastOrDefault();
}

/// <summary>
/// Development/Testing sender: stores the code in <see cref="DevOtpInbox"/> for the dev inbox endpoint and E2E tests.
/// It never logs the code or the number.
/// </summary>
internal sealed partial class DevInboxOtpSender(DevOtpInbox inbox, ILogger<DevInboxOtpSender> logger) : IOtpSender
{
    public Task<bool> TrySendAsync(OtpMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        inbox.Add(message);
        LogStored(logger);
        return Task.FromResult(true);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Sign-in code stored in the development OTP inbox")]
    private static partial void LogStored(ILogger logger);
}

/// <summary>Used when no delivery channel is configured: requests fail with <c>otp.delivery_unavailable</c>.</summary>
internal sealed partial class UnavailableOtpSender(ILogger<UnavailableOtpSender> logger) : IOtpSender
{
    public Task<bool> TrySendAsync(OtpMessage message, CancellationToken cancellationToken)
    {
        LogUnavailable(logger);
        return Task.FromResult(false);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "A sign-in code was requested but no OTP delivery channel is configured (Identity:Otp:Sender)")]
    private static partial void LogUnavailable(ILogger logger);
}
