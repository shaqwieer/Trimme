import Image from 'next/image';
import { useLocale, useTranslations } from 'next-intl';
import { buttonClasses } from '@/components/ui/Button';
import { Badge } from '@/components/ui/Badge';
import { Card } from '@/components/ui/cards';
import { Icon } from '@/components/ui/icons';
import { Link } from '@/i18n/navigation';
import { cn } from '@/lib/cn';
import type { components } from '@/lib/api/schema';
import { type AppLocale, formatNumber } from '@/lib/i18n/format';
import { localizedName } from '@/lib/i18n/localized';
import { displayUrl, QR_FORMATS, qrImageUrl, type QrScope } from '@/lib/qr';

export type QrCode = components['schemas']['QrCodeResponse'];

type Namespace = 'adminQr' | 'shopQr';

/** What a code opens, in words: «صفحة المحل» or «صفحة فيصل القحطاني». */
export function useQrTargetName(namespace: Namespace) {
  const t = useTranslations(namespace);
  const locale = useLocale() as AppLocale;
  return (code: QrCode) =>
    code.targetType === 'Professional'
      ? t('target.Professional', {
          name: localizedName(locale, code.professionalNameAr ?? '', code.professionalNameEn),
        })
      : t('target.Shop');
}

/** The code image from the API (SVG: sharp at any size); same-origin, so the session authorizes it. */
export function QrImage({
  scope,
  code,
  alt,
  size = 96,
}: {
  scope: QrScope;
  code: QrCode;
  alt: string;
  size?: number;
}) {
  return (
    <Image
      src={qrImageUrl(scope, code.id, 'svg')}
      alt={alt}
      width={size}
      height={size}
      unoptimized
      className="bg-white rounded-sm"
    />
  );
}

/** PNG, SVG and PDF downloads and the A5 poster. Files are plain links (API paths are not locale routes). */
export function QrDownloads({
  namespace,
  scope,
  code,
  posterHref,
}: {
  namespace: Namespace;
  scope: QrScope;
  code: QrCode;
  posterHref: string;
}) {
  const t = useTranslations(namespace);
  return (
    <div className="flex flex-wrap items-center gap-1.5">
      {QR_FORMATS.map((format) => (
        <a
          key={format}
          href={qrImageUrl(scope, code.id, format)}
          download
          aria-label={t('download.format', { code: code.code, format: format.toUpperCase() })}
          className={buttonClasses({ variant: format === 'png' ? 'primary' : 'outline', size: 'xs' })}
        >
          {format === 'png' && <Icon name="download" className="size-3.5" />}
          <span className="font-latin">{format.toUpperCase()}</span>
        </a>
      ))}
      <Link href={posterHref} className={buttonClasses({ variant: 'secondary', size: 'xs' })}>
        {t('download.poster')}
      </Link>
    </div>
  );
}

export function QrStatusBadge({ namespace, active }: { namespace: Namespace; active: boolean }) {
  const t = useTranslations(namespace);
  return (
    <Badge size="sm" tone={active ? 'success' : 'neutral'}>
      {active ? t('status.active') : t('status.inactive')}
    </Badge>
  );
}

/** The printed URL, left-to-right inside Arabic text. */
export function QrUrl({ url, nowrap = false }: { url: string; nowrap?: boolean }) {
  return (
    <bdi
      dir="ltr"
      className={cn(
        'self-start font-latin text-badge font-medium text-text-tertiary',
        nowrap ? 'whitespace-nowrap' : 'break-all',
      )}
    >
      {displayUrl(url)}
    </bdi>
  );
}

/** One code as a card (the shop's page): image, target, label, URL, the period's figures and the files. */
export function QrCodeCard({
  scope,
  code,
  posterHref,
}: {
  scope: QrScope;
  code: QrCode;
  posterHref: string;
}) {
  const t = useTranslations('shopQr');
  const locale = useLocale() as AppLocale;
  const target = useQrTargetName('shopQr')(code);
  return (
    <Card
      as="article"
      className="flex flex-col gap-3 p-4 sm:flex-row sm:items-center"
      data-testid="qr-code-card"
    >
      <QrImage scope={scope} code={code} alt={t('imageAlt', { name: target })} />
      <div className="flex min-w-0 flex-1 flex-col gap-1.5">
        <div className="flex flex-wrap items-center gap-2">
          <h3 className="text-label font-bold text-text-primary">{target}</h3>
          <QrStatusBadge namespace="shopQr" active={code.isActive} />
        </div>
        {code.label && <p className="text-helper text-text-secondary">{code.label}</p>}
        <QrUrl url={code.url} />
        <p className="text-helper text-text-secondary">
          {t('figures', {
            visits: formatNumber(code.visits, locale),
            bookings: formatNumber(code.bookings, locale),
          })}
        </p>
        {code.isActive && (
          <QrDownloads namespace="shopQr" scope={scope} code={code} posterHref={posterHref} />
        )}
      </div>
    </Card>
  );
}

/** The design's print notes (c-qr 2084–2114): one code per shop, one per barber, every scan counted. */
export function QrMaterials({ namespace }: { namespace: Namespace }) {
  const t = useTranslations(namespace);
  const items = [
    { icon: 'store', title: t('materials.Shop'), body: t('materials.ShopBody') },
    { icon: 'user', title: t('materials.Professional'), body: t('materials.ProfessionalBody') },
    { icon: 'chart', title: t('materials.counted'), body: t('materials.countedBody') },
  ] as const;
  return (
    <Card as="section" className="flex flex-col gap-3 p-5" aria-labelledby={`${namespace}-materials`}>
      <h2 id={`${namespace}-materials`} className="text-h3 font-bold text-navy-900">
        {t('materials.title')}
      </h2>
      <ul className="grid gap-2.5 md:grid-cols-3">
        {items.map((item) => (
          <li
            key={item.title}
            className="flex gap-2.5 rounded-button border border-border-subtle bg-bg-page p-3.5"
          >
            <Icon name={item.icon} className="size-[18px] shrink-0 text-brand-700" />
            <div className="flex flex-col gap-1">
              <span className="text-label font-bold text-text-primary">{item.title}</span>
              <span className="text-helper text-text-secondary">{item.body}</span>
            </div>
          </li>
        ))}
      </ul>
      <p className="flex items-start gap-2 text-helper text-text-secondary">
        <Icon name="shield" className="mt-0.5 size-4 shrink-0 text-brand-700" />
        {t('privacy')}
      </p>
    </Card>
  );
}
