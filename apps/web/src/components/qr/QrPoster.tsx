import Image from 'next/image';
import { useTranslations } from 'next-intl';
import { Logo } from '@/components/brand/Logo';
import { ButtonLink } from '@/components/ui/Button';
import { displayUrl, qrImageUrl, type QrScope } from '@/lib/qr';
import { PrintButton } from './PrintButton';

/**
 * Print only the poster: the dashboard around it is hidden, the page is A5 without margins, and the navy background is
 * kept (browsers drop backgrounds unless asked). The browser shapes the Arabic text, which a server-made PDF could not
 * do reliably (DV-A14, D-114).
 */
const PRINT_CSS = `
@media print {
  @page { size: A5 portrait; margin: 0; }
  body * { visibility: hidden !important; }
  #qr-poster, #qr-poster * { visibility: visible !important; }
  #qr-poster { position: fixed; inset: 0; width: 148mm; height: 210mm; margin: 0; border-radius: 0; box-shadow: none;
    -webkit-print-color-adjust: exact; print-color-adjust: exact; }
}`;

/**
 * The A5 poster (c-qr print card 2090–2096): navy card, the TRIMME logo, the code on white, the shop's (or barber's)
 * name and «امسح الرمز واحجز دورك», with the short URL for people who type it. Screen: a preview with Print and Back.
 */
export function QrPoster({
  scope,
  codeId,
  name,
  subtitle,
  url,
  backHref,
}: {
  scope: QrScope;
  codeId: string;
  name: string;
  subtitle?: string;
  url: string;
  backHref: string;
}) {
  const t = useTranslations('qrPoster');
  return (
    <div className="flex flex-col items-center gap-4">
      <style>{PRINT_CSS}</style>
      <div className="flex w-full max-w-[420px] flex-wrap items-center justify-between gap-2">
        <ButtonLink href={backHref} variant="ghost" size="sm" icon="chevL">
          {t('back')}
        </ButtonLink>
        <PrintButton label={t('print')} />
      </div>
      <p className="max-w-[420px] text-center text-helper text-text-secondary">{t('hint')}</p>
      <section
        id="qr-poster"
        aria-label={t('title')}
        data-testid="qr-poster"
        className="flex aspect-[148/210] w-full max-w-[420px] flex-col items-center justify-center gap-4 overflow-hidden rounded-card bg-navy-900 px-[8%] py-[6%] text-center text-on-navy shadow-e3 [&>*]:shrink-0"
      >
        <Logo height={40} tone="onDark" />
        <div className="bg-white w-[72%] rounded-[18px] p-[5%]">
          <Image
            src={qrImageUrl(scope, codeId, 'svg')}
            alt={t('qrAlt', { name })}
            width={600}
            height={600}
            unoptimized
            priority
            className="block aspect-square h-auto w-full"
          />
        </div>
        <div className="flex flex-col gap-1">
          <p className="text-[1.375rem] leading-snug font-extrabold">{name}</p>
          {subtitle && <p className="text-body font-bold text-on-navy-muted">{subtitle}</p>}
          <p className="text-[1.0625rem] font-bold text-on-navy-accent">{t('scan')}</p>
        </div>
        <bdi dir="ltr" className="font-latin text-helper text-on-navy-muted">
          {displayUrl(url)}
        </bdi>
      </section>
    </div>
  );
}
