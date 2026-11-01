using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartFactory.Core.Messaging;
using SmartFactory.Infrastructure.Identity;

namespace SmartFactory.Infrastructure.Messaging;

/// <summary>Development: writes the email to the log instead of sending.</summary>
public class LoggingEmailSender : IEmailSender
{
    private readonly ILogger<LoggingEmailSender> _log;
    public LoggingEmailSender(ILogger<LoggingEmailSender> log) { _log = log; }
    public Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        _log.LogInformation("Email to {To}: {Subject}\n{Body}", message.To, message.Subject, message.Body);
        return Task.CompletedTask;
    }
}

/// <summary>Production: posts to the company Exchange API at the one permitted server address.</summary>
public class ExchangeApiEmailSender : IEmailSender
{
    private readonly HttpClient _http;
    private readonly CompanyApiOptions _options;
    private readonly ILogger<ExchangeApiEmailSender> _log;
    public ExchangeApiEmailSender(HttpClient http, IOptions<CompanyApiOptions> options, ILogger<ExchangeApiEmailSender> log) { _http = http; _options = options.Value; _log = log; }

    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ExchangeApiUrl)) { _log.LogWarning("CompanyApi:ExchangeApiUrl not configured; email to {To} not sent", message.To); return; }
        using var resp = await _http.PostAsJsonAsync(_options.ExchangeApiUrl, new { from = _options.ExchangeSender, to = message.To, subject = message.Subject, body = message.Body }, ct);
        if (!resp.IsSuccessStatusCode) _log.LogError("Exchange API returned {Status} for email to {To}", resp.StatusCode, message.To);
    }
}
