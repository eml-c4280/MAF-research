namespace AgentCore.Domain.Notifications;

/// <summary>
/// Sends an email. The only implementation in this project simulates sending by logging
/// instead of calling a real mail provider - see docs/plan.md section 5 (Claim workflow):
/// approving a queued action is expected to actually "do" it, but nothing in this demo talks
/// to a real SMTP server.
/// </summary>
public interface IEmailSender
{
    Task SendAsync(string to, string subject, string body, CancellationToken ct = default);
}
