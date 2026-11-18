import { useTranslations } from 'next-intl';
import { PublicShell } from '@/components/shell/PublicShell';
import { Icon } from '@/components/ui/icons';
import { Link } from '@/i18n/navigation';

/**
 * A shop link that leads nowhere (D-148, «٤-١١»): a wrong address, a shop the platform suspended or one that is not
 * listed right now. A clear page with a way to nearby shops, rather than the site's generic 404. The status stays 404.
 */
export default function ShopNotFound() {
  const t = useTranslations('shopPage.unavailable');

  return (
    <PublicShell>
      <section
        className="mx-auto flex max-w-[36rem] flex-col items-center gap-4 px-4 py-20 text-center"
        data-testid="shop-unavailable"
      >
        <span className="flex size-16 items-center justify-center rounded-full bg-bg-subtle text-text-secondary">
          <Icon name="store" className="size-8" />
        </span>
        <h1 className="text-h1 font-bold text-navy-900">{t('title')}</h1>
        <p className="text-body text-text-secondary">{t('body')}</p>
        <Link
          href="/discover"
          className="inline-flex min-h-11 items-center rounded-button bg-navy-900 px-5 text-button font-bold text-on-navy hover:bg-navy-800"
        >
          {t('nearby')}
        </Link>
      </section>
    </PublicShell>
  );
}
