import { useTranslations } from 'next-intl';
import { InlineAlert } from '@/components/ui/states';
import { Link } from '@/i18n/navigation';
import type { components } from '@/lib/api/schema';

type Subscription = components['schemas']['ShopSubscriptionResponse'];

/**
 * Dashboard-wide notices (spec §13, §15; s-overview subscription card): online booking paused, the subscription ending
 * soon, ended or suspended. Existing appointments always stay valid; only discovery and new online bookings change.
 */
export function ShopBanners({
  paused,
  subscription,
}: {
  paused: boolean;
  subscription: Subscription | null;
}) {
  const t = useTranslations('shopBoard.banners');
  const status = subscription?.status;
  return (
    <div className="flex flex-col gap-3 empty:hidden" data-testid="shop-banners">
      {paused && (
        <InlineAlert
          tone="warning"
          title={t('paused.title')}
          action={
            <Link href="/shop/schedule" className="text-label font-bold text-brand-700 hover:underline">
              {t('paused.action')}
            </Link>
          }
        >
          {t('paused.body')}
        </InlineAlert>
      )}
      {subscription && status === 'ExpiringSoon' && (
        <InlineAlert tone="warning" title={t('expiring.title', { days: subscription.daysRemaining })}>
          {t('expiring.body')}
        </InlineAlert>
      )}
      {subscription && (status === 'Expired' || status === 'Suspended' || status === 'None') && (
        <InlineAlert
          tone="danger"
          title={t(`${status === 'None' ? 'none' : status === 'Expired' ? 'expired' : 'suspended'}.title`)}
        >
          {t(subscription.hiddenFromDiscovery ? 'hiddenBody' : 'visibleBody')}
        </InlineAlert>
      )}
    </div>
  );
}
