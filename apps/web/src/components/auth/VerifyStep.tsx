'use client';

import { useLocale, useTranslations } from 'next-intl';
import { useEffect, useState } from 'react';
import { Button, IconButton } from '@/components/ui/Button';
import { groupSaudiMobile } from '@/components/ui/digits';
import { OtpField } from '@/components/ui/inputs';
import { InlineAlert } from '@/components/ui/states';
import { Ltr } from '@/components/text/Ltr';
import { Link, useRouter } from '@/i18n/navigation';
import { browserApi } from '@/lib/api/client';
import { ensureOk, useApiErrorMessage } from '@/lib/api/errors';
import { ApiError } from '@/lib/api/problem';
import { homeFor, withReturnTo } from '@/lib/auth/paths';
import { AuthCard } from './AuthCard';
import { useOtpFlow } from './OtpFlow';

/** Seconds until `iso`, re-evaluated every second. */
function useCountdown(iso: string | undefined): number {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const timer = window.setInterval(() => setNow(Date.now()), 1000);
    return () => window.clearInterval(timer);
  }, []);
  return iso ? Math.max(0, Math.ceil((Date.parse(iso) - now) / 1000)) : 0;
}

function mmss(seconds: number): string {
  return `${String(Math.floor(seconds / 60)).padStart(2, '0')}:${String(seconds % 60).padStart(2, '0')}`;
}

/**
 * Customer sign-in step 2 (design c-auth "OTP VERIFICATION"): 6-digit code (D-037, DV-C01), resend countdown,
 * remaining attempts on a wrong code. New customers continue to profile completion.
 */
export function VerifyStep({ returnTo }: { returnTo?: string }) {
  const t = useTranslations('auth');
  const locale = useLocale();
  const router = useRouter();
  const apiMessage = useApiErrorMessage();
  const { challenge, setChallenge } = useOtpFlow();
  const [code, setCode] = useState('');
  const [fieldError, setFieldError] = useState<string>();
  const [alert, setAlert] = useState<{ tone: 'danger' | 'success'; text: string }>();
  const [submitting, setSubmitting] = useState(false);
  const [resending, setResending] = useState(false);
  const resendIn = useCountdown(challenge?.resendAvailableAt);

  if (!challenge) {
    return (
      <AuthCard title={t('verify.title')}>
        <InlineAlert tone="info" title={t('verify.restart')} />
        <Link
          href={withReturnTo('/auth/sign-in', returnTo)}
          className="text-center font-bold text-brand-700 hover:underline"
        >
          {t('signIn.title')}
        </Link>
      </AuthCard>
    );
  }

  const verify = async () => {
    setFieldError(undefined);
    setAlert(undefined);
    setSubmitting(true);
    try {
      const { user } = ensureOk(
        await browserApi.POST('/api/v1/auth/otp/verify', {
          body: { challengeId: challenge.challengeId, code },
        }),
      );
      setChallenge(null);
      const destination = returnTo ?? homeFor(user.userType);
      router.replace(
        user.profileComplete ? destination : withReturnTo('/auth/complete-profile', destination),
      );
    } catch (error) {
      setSubmitting(false);
      if (error instanceof ApiError && error.errorCode === 'otp.incorrect') {
        setFieldError(t('verify.attemptsLeft', { count: error.details.attemptsRemaining ?? 0 }));
      } else {
        setAlert({
          tone: 'danger',
          text: apiMessage(error instanceof ApiError ? error : 'server.unexpected') ?? '',
        });
      }
    }
  };

  const resend = async () => {
    setResending(true);
    setAlert(undefined);
    setFieldError(undefined);
    try {
      const next = ensureOk(
        await browserApi.POST('/api/v1/auth/otp/request', {
          body: { phone: challenge.phoneE164, termsAccepted: challenge.termsAccepted, locale },
        }),
      );
      setChallenge({ ...challenge, ...next });
      setCode('');
      setAlert({ tone: 'success', text: t('verify.resent') });
    } catch (error) {
      setAlert({
        tone: 'danger',
        text: apiMessage(error instanceof ApiError ? error : 'server.unexpected') ?? '',
      });
    } finally {
      setResending(false);
    }
  };

  const phoneDisplay = `+966 ${groupSaudiMobile(challenge.phoneE164.slice(4))}`;

  return (
    <AuthCard
      align="start"
      title={t('verify.title')}
      subtitle={
        <>
          {t('verify.sentTo', { length: challenge.codeLength })}{' '}
          <Ltr className="font-latin font-semibold text-text-strong">{phoneDisplay}</Ltr>
        </>
      }
      leading={
        <IconButton
          icon="chevR"
          label={t('back')}
          onClick={() => router.back()}
          className="self-start border border-border-input"
        />
      }
    >
      <form
        noValidate
        className="flex flex-col gap-5"
        onSubmit={(event) => {
          event.preventDefault();
          if (code.length === challenge.codeLength) void verify();
        }}
      >
        <OtpField
          label={t('verify.codeLabel')}
          length={challenge.codeLength}
          value={code}
          onValueChange={setCode}
          error={fieldError}
          autoFocus
        />
        <div className="flex items-center justify-between gap-3 text-caption">
          <span className="text-text-secondary" aria-live="polite">
            {resendIn > 0 && t('verify.resendIn', { time: mmss(resendIn) })}
          </span>
          <Button
            variant="ghost"
            size="xs"
            disabled={resendIn > 0}
            loading={resending}
            onClick={() => void resend()}
          >
            {t('verify.resend')}
          </Button>
        </div>
        <InlineAlert tone="info" title={t('verify.help')} />
        {alert && <InlineAlert tone={alert.tone} title={alert.text} />}
        <Button type="submit" fullWidth disabled={code.length !== challenge.codeLength} loading={submitting}>
          {t('verify.submit')}
        </Button>
      </form>
    </AuthCard>
  );
}
