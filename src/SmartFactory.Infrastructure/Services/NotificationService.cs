using Microsoft.EntityFrameworkCore;
using SmartFactory.Core.Messaging;
using SmartFactory.Infrastructure.Data;
using SmartFactory.Performance.Entities;

namespace SmartFactory.Infrastructure.Services;

/// <summary>Reminders and approval emails through the company Exchange API.</summary>
public class NotificationService
{
    private readonly SmartFactoryDbContext _db;
    private readonly IEmailSender _email;
    private readonly OrganizationService _org;

    public NotificationService(SmartFactoryDbContext db, IEmailSender email, OrganizationService org) { _db = db; _email = email; _org = org; }

    private async Task SendToAsync(IEnumerable<string?> addresses, string subject, string body)
    {
        foreach (var to in addresses.Where(a => !string.IsNullOrWhiteSpace(a)).Distinct())
            await _email.SendAsync(new EmailMessage(to!, subject, body));
    }

    private async Task<(string personName, IEnumerable<string?> superiors, string? ownerEmail)> ContextAsync(Kra kra)
    {
        var person = await _db.People.Include(p => p.Position).FirstAsync(p => p.Id == kra.PersonId);
        var approvers = await _org.ApproversOfAsync(person);
        return (person.Name, approvers.Select(a => a.Email), person.Email);
    }

    public async Task KraSubmittedAsync(Kra kra)
    {
        var (name, superiors, _) = await ContextAsync(kra);
        await SendToAsync(superiors, $"KRA submitted for approval: {name}", $"{name} has submitted their KRA for approval. Open Approvals in the Smart Factory Platform to review it against the Top Goal lines and the position.");
    }

    public async Task KraApprovedAsync(Kra kra)
    {
        var (name, _, owner) = await ContextAsync(kra);
        await SendToAsync(new[] { owner }, $"Your KRA is approved and LOCKED (v{kra.Version})", $"Hello {name}, {kra.ApprovedByName} approved your KRA. It is now LOCKED as version {kra.Version}. Enter this week's actuals by Friday.");
    }

    public async Task KraReturnedAsync(Kra kra)
    {
        var (name, _, owner) = await ContextAsync(kra);
        await SendToAsync(new[] { owner }, "Your KRA was returned with comments", $"Hello {name}, your superior returned your KRA: {kra.ReturnComment}. Change it in SETUP and submit again.");
    }

    public async Task UnlockRequestedAsync(Kra kra)
    {
        var (name, superiors, _) = await ContextAsync(kra);
        await SendToAsync(superiors, $"Unlock requested: {name}", $"{name} asks to unlock their KRA: {kra.UnlockReason}. Decide in Approvals.");
    }

    public async Task KraUnlockedAsync(Kra kra)
    {
        var (name, _, owner) = await ContextAsync(kra);
        await SendToAsync(new[] { owner }, "Your KRA is unlocked (SETUP)", $"Hello {name}, your KRA is back in SETUP. Prefer changing a target or rating basis; to change a measure's meaning, retire the slot and open a new one so weekly history stays truthful. Submit again when done.");
    }

    public async Task WeeklyReminderAsync(string? to, string name, string weekLabel)
        => await SendToAsync(new[] { to }, $"Enter this week's actuals ({weekLabel})", $"Hello {name}, enter the actual for each active slot for {weekLabel} by Friday in My week.");
}
