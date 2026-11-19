'use client';

import { useLocale, useTranslations } from 'next-intl';
import { type FormEvent, useState } from 'react';
import { SignOutButton } from '@/components/auth/SessionClient';
import { useDisplayCalendar } from '@/components/providers/CalendarProvider';
import { Button, ButtonLink } from '@/components/ui/Button';
import { TextField } from '@/components/ui/inputs';
import { ConfirmDialog } from '@/components/ui/overlays';
import { InlineAlert } from '@/components/ui/states';
import { Link, usePathname, useRouter } from '@/i18n/navigation';
import { routing } from '@/i18n/routing';
import { browserApi } from '@/lib/api/client';
import { ensureOk, useApiErrorMessage } from '@/lib/api/errors';
import { ApiError } from '@/lib/api/problem';
import type { components } from '@/lib/api/schema';
import { cn } from '@/lib/cn';
import {
  CALENDAR_COOKIE,
  CALENDAR_COOKIE_MAX_AGE,
  calendarCookieValue,
  type DisplayCalendar,
} from '@/lib/i18n/calendar';
import { type AppLocale, formatDate } from '@/lib/i18n/format';

type Account = components['schemas']['ShopAccountResponse'];

/** Each language named in itself, as the header's menu does (D-132). */
const LANGUAGE_NAMES: Record<AppLocale, string> = { ar: 'العربية', tr: 'Türkçe', en: 'English' };
const CALENDARS: DisplayCalendar[] = ['gregory', 'islamic-umalqura'];

const card = 'flex flex-col gap-3 rounded-card border border-border bg-surface p-5 shadow-e1';

/** Two or three choices as pills (the app's «ميلادي / هجري»); the pressed one is the current one. */
function Pills<T extends string>({
  options,
  value,
  label,
  onChoose,
}: {
  options: Array<{ value: T; label: string; lang?: string }>;
  value: T;
  label: string;
  onChoose: (value: T) => void;
}) {
  return (
    <div role="group" aria-label={label} className="flex flex-wrap gap-2">
      {options.map((option) => (
        <button
          key={option.value}
          type="button"
          lang={option.lang}
          aria-pressed={option.value === value}
          onClick={() => option.value !== value && onChoose(option.value)}
          className={cn(
            'min-h-10 rounded-full border-[1.5px] px-4 text-label font-semibold transition-colors focus-visible:shadow-[var(--focus-ring)] pointer-coarse:min-h-11',
            option.value === value
              ? 'border-navy-900 bg-navy-900 text-on-navy'
              : 'border-border-input bg-surface text-text-strong hover:border-brand-500',
          )}
        >
          {option.label}
        </button>
      ))}
    </div>
  );
}

/**
 * «حسابي» for the shop's people (D-151, the client's 2-2), in the app's order: the name (a barber changes their name
 * everywhere: their public page, the owner's list and new log lines), the mobile (changing it comes later), the
 * calendar dates are written in (ميلادي or هجري, on this device), the shop screens' language, then the notifications,
 * signing out and, for a barber, deleting the account (the login and its personal data go; the barber and their
 * bookings stay with the shop, whose owner is told and can give them a new login).
 */
export function ShopAccount({ account }: { account: Account }) {
  const t = useTranslations('shopAccount');
  const locale = useLocale() as AppLocale;
  const calendar = useDisplayCalendar();
  const router = useRouter();
  const pathname = usePathname();
  const apiMessage = useApiErrorMessage();
  const [editing, setEditing] = useState(false);
  const [nameAr, setNameAr] = useState(account.nameAr ?? account.displayName ?? '');
  const [nameEn, setNameEn] = useState(account.nameEn ?? '');
  const [saving, setSaving] = useState(false);
  const [saved, setSaved] = useState(false);
  const [nameError, setNameError] = useState<string>();
  const [deleteOpen, setDeleteOpen] = useState(false);
  const [deleting, setDeleting] = useState(false);
  const [deleteError, setDeleteError] = useState<string>();

  const name = account.canRename
    ? (account.nameAr ?? account.displayName ?? '')
    : (account.displayName ?? '');

  const saveName = async (event: FormEvent) => {
    event.preventDefault();
    if (nameAr.trim().length < 2) {
      setNameError(t('name.tooShort'));
      return;
    }
    setSaving(true);
    setNameError(undefined);
    try {
      ensureOk(
        await browserApi.PUT('/api/v1/shop/account/name', {
          body: {
            nameAr: nameAr.trim(),
            nameEn: nameEn.trim() || null,
            version: account.version ?? 0,
          },
        }),
      );
      setEditing(false);
      setSaved(true);
      router.refresh();
    } catch (failure) {
      setNameError(apiMessage(failure instanceof ApiError ? failure : 'server.unexpected'));
    } finally {
      setSaving(false);
    }
  };

  const chooseCalendar = (next: DisplayCalendar) => {
    document.cookie = `${CALENDAR_COOKIE}=${calendarCookieValue(next)}; path=/; max-age=${CALENDAR_COOKIE_MAX_AGE}; samesite=lax`;
    router.refresh();
  };

  const deleteAccount = async () => {
    setDeleting(true);
    setDeleteError(undefined);
    try {
      ensureOk(await browserApi.DELETE('/api/v1/shop/account'));
      // A full load: nothing about the deleted login should stay in memory.
      window.location.replace(`/${locale}/auth/staff/sign-in`);
    } catch (failure) {
      setDeleting(false);
      setDeleteOpen(false);
      setDeleteError(apiMessage(failure instanceof ApiError ? failure : 'server.unexpected'));
    }
  };

  return (
    <div className="flex max-w-[640px] flex-col gap-4" data-testid="shop-account">
      <section className={card} aria-labelledby="account-name">
        <h2 id="account-name" className="text-helper font-semibold text-text-secondary">
          {t('name.title')}
        </h2>
        {editing ? (
          <form
            method="post"
            className="flex flex-col gap-3"
            onSubmit={(event) => void saveName(event)}
            noValidate
          >
            <TextField
              label={t('name.ar')}
              value={nameAr}
              maxLength={100}
              autoComplete="name"
              error={nameError}
              onChange={(event) => setNameAr(event.target.value)}
            />
            <TextField
              label={t('name.en')}
              optional
              dir="ltr"
              value={nameEn}
              maxLength={100}
              onChange={(event) => setNameEn(event.target.value)}
            />
            <p className="text-helper text-text-secondary">{t('name.everywhere')}</p>
            <div className="flex gap-2">
              <Button type="submit" size="sm" loading={saving}>
                {t('name.save')}
              </Button>
              <Button
                size="sm"
                variant="ghost"
                onClick={() => {
                  setEditing(false);
                  setNameError(undefined);
                }}
              >
                {t('name.cancel')}
              </Button>
            </div>
          </form>
        ) : (
          <>
            <p className="text-h3 font-bold text-navy-900" data-testid="account-name-value">
              {name}
            </p>
            {saved && (
              <p role="status" className="text-helper font-bold text-success-700">
                {t('name.saved')}
              </p>
            )}
            {account.canRename && (
              <Button
                size="sm"
                variant="secondary"
                icon="edit"
                className="self-start"
                onClick={() => {
                  setEditing(true);
                  setSaved(false);
                }}
              >
                {t('name.edit')}
              </Button>
            )}
          </>
        )}
        <div className="flex flex-col gap-1 border-t border-border-row pt-3">
          <h3 className="text-helper font-semibold text-text-secondary">
            {account.mobileMasked ? t('mobile.title') : t('email')}
          </h3>
          <p dir="ltr" className="self-start font-latin text-label font-bold text-text-primary">
            {account.mobileMasked ?? account.email ?? '—'}
          </p>
          {account.mobileMasked && <p className="text-helper text-text-tertiary">{t('mobile.later')}</p>}
        </div>
      </section>

      <section className={card} aria-labelledby="account-calendar">
        <h2 id="account-calendar" className="text-h3 font-bold text-navy-900">
          {t('calendar.title')}
        </h2>
        <p className="text-helper text-text-secondary">{t('calendar.body')}</p>
        <Pills
          label={t('calendar.title')}
          value={calendar}
          options={CALENDARS.map((value) => ({ value, label: t(`calendar.${value}`) }))}
          onChoose={chooseCalendar}
        />
        <p className="text-label font-bold text-text-strong" data-testid="calendar-preview">
          {formatDate(new Date(), locale, { withYear: true, calendar })}
        </p>
      </section>

      <section className={card} aria-labelledby="account-language">
        <h2 id="account-language" className="text-h3 font-bold text-navy-900">
          {t('language.title')}
        </h2>
        <p className="text-helper text-text-secondary">{t('language.body')}</p>
        <div role="group" aria-labelledby="account-language" className="flex flex-wrap gap-2">
          {routing.locales.map((value) => (
            <Link
              key={value}
              href={pathname}
              locale={value}
              lang={value}
              aria-current={value === locale ? 'true' : undefined}
              className={cn(
                'inline-flex min-h-10 items-center rounded-full border-[1.5px] px-4 text-label font-semibold transition-colors focus-visible:shadow-[var(--focus-ring)] pointer-coarse:min-h-11',
                value === locale
                  ? 'border-navy-900 bg-navy-900 text-on-navy'
                  : 'border-border-input bg-surface text-text-strong hover:border-brand-500',
              )}
            >
              {LANGUAGE_NAMES[value]}
            </Link>
          ))}
        </div>
      </section>

      <section className={card} aria-labelledby="account-notifications">
        <h2 id="account-notifications" className="text-h3 font-bold text-navy-900">
          {t('notifications.title')}
        </h2>
        <p className="text-helper text-text-secondary">{t('notifications.body')}</p>
        <ButtonLink href="/shop/notifications" variant="outline" size="sm" icon="bell" className="self-start">
          {t('notifications.open')}
        </ButtonLink>
      </section>

      <section className={card} aria-label={t('signOut')}>
        <SignOutButton staff className="self-start" />
      </section>

      {account.canDelete && (
        <section
          aria-labelledby="account-delete"
          className="flex flex-col gap-3 rounded-card border border-danger-100 bg-surface p-5"
          data-testid="delete-shop-account"
        >
          <h2 id="account-delete" className="text-label font-bold text-danger-700">
            {t('delete.title')}
          </h2>
          <p className="text-helper text-text-secondary">{t('delete.body')}</p>
          {deleteError && <InlineAlert tone="danger" title={deleteError} />}
          <Button
            variant="danger"
            size="sm"
            icon="trash"
            className="self-start"
            onClick={() => setDeleteOpen(true)}
          >
            {t('delete.button')}
          </Button>
          <ConfirmDialog
            open={deleteOpen}
            onOpenChange={setDeleteOpen}
            tone="danger"
            title={t('delete.confirmTitle')}
            body={t('delete.confirmBody')}
            confirmLabel={t('delete.confirm')}
            cancelLabel={t('delete.cancel')}
            onConfirm={() => void deleteAccount()}
            loading={deleting}
          />
        </section>
      )}
    </div>
  );
}
