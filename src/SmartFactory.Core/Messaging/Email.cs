namespace SmartFactory.Core.Messaging;

public record EmailMessage(string To, string Subject, string Body);

/// <summary>Email goes only through the company Exchange API at the single permitted server address.</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct = default);
}
