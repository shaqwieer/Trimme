using System.Globalization;
using System.Net;
using Microsoft.Extensions.Options;
using Trimme.Modules.Identity.Application;
using Trimme.Modules.Identity.Domain;

namespace Trimme.Modules.Identity.Infrastructure.Email;

/// <summary>
/// Composes the Identity emails in Arabic (RTL) or English. Links point at the web app; tokens appear only in the
/// link and are never logged.
/// </summary>
internal sealed class IdentityMailer(IEmailSender sender, IOptions<WebLinkOptions> web, TimeProvider clock) : IIdentityMailer
{
    public Task SendPasswordResetAsync(PasswordResetTicket ticket, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ticket);

        var arabic = ticket.PreferredLocale != Locales.English;
        var link = Link(ticket.PreferredLocale, "auth/reset-password", ("uid", ticket.UserId.ToString()), ("token", ticket.Token));
        var greeting = string.IsNullOrWhiteSpace(ticket.DisplayName)
            ? (arabic ? "مرحباً،" : "Hello,")
            : (arabic ? $"مرحباً {ticket.DisplayName}،" : $"Hello {ticket.DisplayName},");

        var message = arabic
            ? Compose(
                ticket.Email, "ar", "إعادة تعيين كلمة المرور في تريمي", greeting,
                "طلبت إعادة تعيين كلمة المرور لحسابك في تريمي. الرابط صالح لمدة ساعة واحدة.",
                "تعيين كلمة مرور جديدة", link,
                "إذا لم تطلب ذلك فتجاهل هذه الرسالة، ولن يتغير شيء في حسابك.")
            : Compose(
                ticket.Email, "en", "Reset your TRIMME password", greeting,
                "You asked to reset the password of your TRIMME account. The link is valid for one hour.",
                "Set a new password", link,
                "If you did not ask for this, ignore this email and nothing will change.");

        return sender.SendAsync(message, cancellationToken);
    }

    public Task SendInvitationAsync(string email, string locale, string roleName, string token, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        var arabic = locale != Locales.English;
        var link = Link(locale, "auth/accept-invite", ("token", token));
        var days = Math.Max(1, (int)Math.Round((expiresAt - clock.GetUtcNow()).TotalDays));

        var message = arabic
            ? Compose(
                email, "ar", "دعوة للانضمام إلى تريمي", "مرحباً،",
                $"تمت دعوتك للانضمام إلى تريمي بصلاحية «{RoleLabel(roleName, arabic: true)}». الدعوة صالحة لمدة {days.ToString(CultureInfo.InvariantCulture)} أيام.",
                "قبول الدعوة وإنشاء الحساب", link,
                "إذا لم تكن تتوقع هذه الدعوة فتجاهل هذه الرسالة.")
            : Compose(
                email, "en", "You're invited to TRIMME", "Hello,",
                $"You have been invited to join TRIMME as {RoleLabel(roleName, arabic: false)}. The invitation is valid for {days.ToString(CultureInfo.InvariantCulture)} days.",
                "Accept and create your account", link,
                "If you were not expecting this invitation, ignore this email.");

        return sender.SendAsync(message, cancellationToken);
    }

    private string Link(string locale, string path, params (string Name, string Value)[] query)
    {
        var baseUrl = web.Value.PublicBaseUrl.TrimEnd('/');
        var segment = locale == Locales.English ? Locales.English : Locales.Arabic;
        var queryString = string.Join('&', query.Select(q => $"{q.Name}={Uri.EscapeDataString(q.Value)}"));
        return $"{baseUrl}/{segment}/{path}?{queryString}";
    }

    private static EmailMessage Compose(string to, string lang, string subject, string greeting, string body, string action, string link, string footer)
    {
        var dir = lang == "ar" ? "rtl" : "ltr";
        var text = $"{greeting}\n\n{body}\n\n{action}:\n{link}\n\n{footer}\n\nTRIMME";
        var html = $"""
            <!doctype html>
            <html lang="{lang}" dir="{dir}">
            <body style="margin:0;padding:24px;background:#F4F7FA;font-family:Tahoma,Arial,sans-serif;color:#13263A">
              <div style="max-width:520px;margin:0 auto;background:#FFFFFF;border-radius:16px;padding:28px">
                <p style="font-size:16px;margin:0 0 12px">{Encode(greeting)}</p>
                <p style="font-size:15px;line-height:1.8;margin:0 0 20px">{Encode(body)}</p>
                <p style="margin:0 0 20px"><a href="{Encode(link)}" style="display:inline-block;background:#13263A;color:#FFFFFF;text-decoration:none;padding:12px 20px;border-radius:12px;font-weight:bold">{Encode(action)}</a></p>
                <p style="font-size:13px;color:#5F6F80;line-height:1.7;margin:0">{Encode(footer)}</p>
              </div>
            </body>
            </html>
            """;
        return new EmailMessage(to, subject, text, html);
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);

    private static string RoleLabel(string roleName, bool arabic) => (roleName, arabic) switch
    {
        (SystemRoles.SuperAdmin, true) => "مدير عام",
        (SystemRoles.SuperAdmin, false) => "a super admin",
        (SystemRoles.OperationsManager, true) => "مدير عمليات",
        (SystemRoles.OperationsManager, false) => "an operations manager",
        (SystemRoles.Support, true) => "دعم",
        (SystemRoles.Support, false) => "support",
        (SystemRoles.ShopOwner, true) => "مالك المحل",
        (SystemRoles.ShopOwner, false) => "the shop owner",
        (SystemRoles.ShopStaff, true) => "موظف المحل",
        (SystemRoles.ShopStaff, false) => "shop staff",
        _ => roleName,
    };
}
