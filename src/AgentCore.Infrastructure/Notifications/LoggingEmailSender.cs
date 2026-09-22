using AgentCore.Domain.Notifications;
using Microsoft.Extensions.Logging;

namespace AgentCore.Infrastructure.Notifications;

/// <summary>Simulated email sender: logs instead of calling a real mail provider.</summary>
public class LoggingEmailSender : IEmailSender
{
    private readonly ILogger<LoggingEmailSender> _logger;

    public LoggingEmailSender(ILogger<LoggingEmailSender> logger) => _logger = logger;

    public Task SendAsync(string to, string subject, string body, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Simulated email sent to {To} | Subject: {Subject} | Body: {Body}", to, subject, body);
        return Task.CompletedTask;
    }
}
