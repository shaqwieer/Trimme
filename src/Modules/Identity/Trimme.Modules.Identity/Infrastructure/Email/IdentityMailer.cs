using System.Globalization;
using System.Net;
using Microsoft.Extensions.Options;
using Trimme.Modules.Identity.Application;
using Trimme.Modules.Identity.Domain;

namespace Trimme.Modules.Identity.Infrastructure.Email;

/// <summary>
/// Composes the Identity emails in Arabic (RTL), English or Turkish (D-132). Links point at the web app; tokens appear
/// only in the link and are never logged.
/// </summary>
internal sealed class IdentityMailer(IEmailSender sender, IOptions<WebLinkOptions> web, TimeProvider clock) : IIdentityMailer
{
    public Task SendPasswordResetAsync(PasswordResetTicket ticket, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ticket);

        var lang = Language(ticket.PreferredLocale);
        var link = Link(lang, "auth/reset-password", ("uid", ticket.UserId.ToString()), ("token", ticket.Token));
        var named = !string.IsNullOrWhiteSpace(ticket.DisplayName);

        var message = lang switch
        {
            Locales.English => Compose(
                ticket.Email, lang, "Reset your Groom Here password", named ? $"Hello {ticket.DisplayName}," : "Hello,",
                "You asked to reset the password of your Groom Here account. The link is valid for one hour.",
                "Set a new password", link,
                "If you did not ask for this, ignore this email and nothing will change."),
            Locales.Turkish => Compose(
                ticket.Email, lang, "Groom Here şifrenizi sıfırlayın", named ? $"Merhaba {ticket.DisplayName}," : "Merhaba,",
                "Groom Here hesabınızın şifresini sıfırlamak istediniz. Bağlantı bir saat geçerlidir.",
                "Yeni şifre belirleyin", link,
                "Bunu siz istemediyseniz bu e-postayı yok sayın; hesabınızda hiçbir şey değişmez."),
            _ => Compose(
                ticket.Email, lang, "إعادة تعيين كلمة المرور في Groom Here", named ? $"مرحباً {ticket.DisplayName}،" : "مرحباً،",
                "طلبت إعادة تعيين كلمة المرور لحسابك في Groom Here. الرابط صالح لمدة ساعة واحدة.",
                "تعيين كلمة مرور جديدة", link,
                "إذا لم تطلب ذلك فتجاهل هذه الرسالة، ولن يتغير شيء في حسابك."),
        };

        return sender.SendAsync(message, cancellationToken);
    }

    public Task SendInvitationAsync(string email, string locale, string roleName, string token, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        var lang = Language(locale);
        var link = Link(lang, "auth/accept-invite", ("token", token));
        var days = Math.Max(1, (int)Math.Round((expiresAt - clock.GetUtcNow()).TotalDays)).ToString(CultureInfo.InvariantCulture);
        var role = RoleLabel(roleName, lang);

        var message = lang switch
        {
            Locales.English => Compose(
                email, lang, "You're invited to Groom Here", "Hello,",
                $"You have been invited to join Groom Here as {role}. The invitation is valid for {days} days.",
                "Accept and create your account", link,
                "If you were not expecting this invitation, ignore this email."),
            Locales.Turkish => Compose(
                email, lang, "Groom Here’a davet edildiniz", "Merhaba,",
                $"Groom Here’a {role} olarak katılmaya davet edildiniz. Davet {days} gün geçerlidir.",
                "Daveti kabul edin ve hesabınızı oluşturun", link,
                "Bu daveti beklemiyorsanız bu e-postayı yok sayın."),
            _ => Compose(
                email, lang, "دعوة للانضمام إلى Groom Here", "مرحباً،",
                $"تمت دعوتك للانضمام إلى Groom Here بصلاحية «{role}». الدعوة صالحة لمدة {days} أيام.",
                "قبول الدعوة وإنشاء الحساب", link,
                "إذا لم تكن تتوقع هذه الدعوة فتجاهل هذه الرسالة."),
        };

        return sender.SendAsync(message, cancellationToken);
    }

    /// <summary>The email's language: the account's or request's supported locale, otherwise Arabic.</summary>
    private static string Language(string? locale) => Locales.IsSupported(locale) ? locale! : Locales.Arabic;

    private string Link(string lang, string path, params (string Name, string Value)[] query)
    {
        var baseUrl = web.Value.PublicBaseUrl.TrimEnd('/');
        var queryString = string.Join('&', query.Select(q => $"{q.Name}={Uri.EscapeDataString(q.Value)}"));
        return $"{baseUrl}/{lang}/{path}?{queryString}";
    }

    private static EmailMessage Compose(string to, string lang, string subject, string greeting, string body, string action, string link, string footer)
    {
        var dir = lang == "ar" ? "rtl" : "ltr";
        var text = $"{greeting}\n\n{body}\n\n{action}:\n{link}\n\n{footer}\n\nGroom Here";
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

    private static string RoleLabel(string roleName, string lang) => (roleName, lang) switch
    {
        (SystemRoles.SuperAdmin, Locales.Arabic) => "مدير عام",
        (SystemRoles.SuperAdmin, Locales.Turkish) => "süper yönetici",
        (SystemRoles.SuperAdmin, _) => "a super admin",
        (SystemRoles.OperationsManager, Locales.Arabic) => "مدير عمليات",
        (SystemRoles.OperationsManager, Locales.Turkish) => "operasyon yöneticisi",
        (SystemRoles.OperationsManager, _) => "an operations manager",
        (SystemRoles.Support, Locales.Arabic) => "دعم",
        (SystemRoles.Support, Locales.Turkish) => "destek",
        (SystemRoles.Support, _) => "support",
        (SystemRoles.ShopOwner, Locales.Arabic) => "مالك المحل",
        (SystemRoles.ShopOwner, Locales.Turkish) => "salon sahibi",
        (SystemRoles.ShopOwner, _) => "the shop owner",
        (SystemRoles.ShopStaff, Locales.Arabic) => "موظف المحل",
        (SystemRoles.ShopStaff, Locales.Turkish) => "salon personeli",
        (SystemRoles.ShopStaff, _) => "shop staff",
        _ => roleName,
    };
}
