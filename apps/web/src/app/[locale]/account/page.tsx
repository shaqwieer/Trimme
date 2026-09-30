import { getTranslations } from 'next-intl/server';
import { SignOutButton } from '@/components/auth/SessionClient';
import { Ltr } from '@/components/text/Ltr';
import { Avatar } from '@/components/ui/Avatar';
import { Icon, type DesignIconName } from '@/components/ui/icons';
import { InlineAlert, PermissionDenied } from '@/components/ui/states';
import { Link } from '@/i18n/navigation';
import { asLocale } from '@/i18n/routing';
import { firstParam } from '@/lib/auth/paths';
import { requireCustomer } from '@/lib/auth/server';

type SettingsRow = { icon: DesignIconName; label: string; value?: string; href?: string; tone?: 'muted' };

function Row({ row }: { row: SettingsRow }) {
  const content = (
    <>
      <Icon name={row.icon} className="size-5 shrink-0 text-brand-700" />
      <span className="flex-1">{row.label}</span>
      {row.value && <span className="text-helper font-medium text-text-secondary">{row.value}</span>}
      {row.href && <Icon name="chevL" className="size-4 text-text-tertiary" />}
    </>
  );
  const className = 'flex min-h-14 items-center gap-3 px-4 text-label font-bold text-text-strong';
  return row.href ? (
    <Link href={row.href} className={`${className} hover:bg-bg-subtle`}>
      {content}
    </Link>
  ) : (
    <div className={className}>{content}</div>
  );
}

/**
 * The customer's account (c-profile 1995–2036): who they are (own masked mobile, LTR), then preferences, account and
 * support rows, and sign out. Payment is shown read-only as "at the shop" (DV-S20: no payment in v1); WhatsApp
 * notification preferences arrive with the notification centre (Phase 15).
 */
export default async function AccountPage({ params, searchParams }: PageProps<'/[locale]/account'>) {
  const [{ locale: raw }, query] = await Promise.all([params, searchParams]);
  const locale = asLocale(raw);
  const me = await requireCustomer(locale, '/account');
  if (!me) return <PermissionDenied homeHref="/" />;
  const t = await getTranslations({ locale, namespace: 'account' });
  const name = me.displayName ?? '';

  const groups: Array<{ title: string; rows: SettingsRow[] }> = [
    {
      title: t('groups.preferences'),
      rows: [
        {
          icon: 'msg',
          label: t('rows.language'),
          value: me.preferredLocale === 'en' ? t('profile.english') : t('profile.arabic'),
          href: '/account/profile',
        },
        {
          icon: 'bell',
          label: t('rows.notifications'),
          value: t('rows.notificationsValue'),
          href: '/account/notifications',
        },
      ],
    },
    {
      title: t('groups.account'),
      rows: [
        { icon: 'user', label: t('rows.personal'), href: '/account/profile' },
        { icon: 'calendar', label: t('rows.bookings'), href: '/account/bookings' },
        { icon: 'heart', label: t('rows.favorites'), href: '/account/favorites' },
        { icon: 'card', label: t('rows.payment'), value: t('rows.paymentValue') },
        { icon: 'shield', label: t('security'), href: '/account/security' },
        { icon: 'eyeOff', label: t('rows.privacy'), href: '/privacy' },
      ],
    },
    {
      title: t('groups.support'),
      rows: [{ icon: 'info', label: t('rows.terms'), href: '/terms' }],
    },
  ];

  return (
    <div className="mx-auto flex max-w-[720px] flex-col gap-6 px-4 py-6 md:px-6">
      <h1 className="sr-only">{t('title')}</h1>
      {firstParam(query.saved) === '1' && <InlineAlert tone="success" title={t('profile.saved')} />}
      <section className="flex items-center gap-4 rounded-card border border-border bg-surface p-5 shadow-e1">
        <Avatar name={name} size="xl" />
        <div className="flex min-w-0 flex-1 flex-col gap-1">
          <p className="truncate text-h3 font-bold text-navy-900">{name}</p>
          <p className="font-latin text-label text-text-secondary">
            <span className="sr-only">{t('phone')}: </span>
            <Ltr>{me.phoneMasked}</Ltr>
          </p>
        </div>
        <Link
          href="/account/profile"
          aria-label={t('editProfile')}
          className="inline-flex size-11 items-center justify-center rounded-button border border-border text-text-strong hover:bg-bg-subtle"
        >
          <Icon name="edit" className="size-5" />
        </Link>
      </section>

      {groups.map((group) => (
        <section key={group.title} aria-label={group.title} className="flex flex-col gap-2">
          <h2 className="px-1 text-eyebrow font-bold tracking-[0.1em] text-text-tertiary">{group.title}</h2>
          <ul className="flex flex-col divide-y divide-border-row overflow-hidden rounded-card border border-border bg-surface">
            {group.rows.map((row) => (
              <li key={row.label}>
                <Row row={row} />
              </li>
            ))}
          </ul>
        </section>
      ))}
      <SignOutButton className="self-start" />
    </div>
  );
}
