'use client';

import { useLocale, useTranslations } from 'next-intl';
import { useState } from 'react';
import * as z from 'zod';
import { Button, ButtonLink } from '@/components/ui/Button';
import { InlineAlert, StateCard } from '@/components/ui/states';
import { Link, useRouter } from '@/i18n/navigation';
import { browserApi } from '@/lib/api/client';
import { ensureOk, useApiErrorMessage } from '@/lib/api/errors';
import { ApiError } from '@/lib/api/problem';
import { homeFor } from '@/lib/auth/paths';
import { FormTextField, useZodForm } from '@/lib/forms/fields';
import { applyProblemToForm, codeToMessageKey } from '@/lib/forms/problem';
import { email, requiredText, staffIdentifier, withParams } from '@/lib/forms/validation';
import { AuthCard } from './AuthCard';

const password = z
  .string()
  .min(1, { error: 'required' })
  .min(10, { error: 'passwordTooShort' })
  .max(256, { error: withParams('tooLong', { max: 256 }) });

const newPasswordSchema = z
  .object({ password, confirmPassword: z.string().min(1, { error: 'required' }) })
  .refine((v) => v.password === v.confirmPassword, { path: ['confirmPassword'], error: 'passwordMismatch' });

function useServerError() {
  const apiMessage = useApiErrorMessage();
  const [serverError, setServerError] = useState<string>();
  const fail = (error: unknown) =>
    setServerError(apiMessage(error instanceof ApiError ? error : 'server.unexpected'));
  return { serverError, setServerError, fail };
}

/** Who is signing in, as the API takes it: an email, or a barber's mobile in E.164 (D-145). */
type StaffIdentity = { email: string } | { email: null; phone: string };

function identityOf(parsed: { kind: 'email' | 'phone'; value: string }): StaffIdentity {
  return parsed.kind === 'email' ? { email: parsed.value } : { email: null, phone: parsed.value };
}

/**
 * Shop and admin staff sign-in with email + password (D-005); a barber signs in with their mobile instead (D-145). An
 * account created with a temporary password gets no session until the user chooses a new one (D-139), so that step
 * follows here.
 */
export function StaffSignInForm({ returnTo }: { returnTo?: string }) {
  const t = useTranslations('auth.staffSignIn');
  const router = useRouter();
  const { serverError, setServerError, fail } = useServerError();
  // Held in memory only, to prove the temporary password once more when it is replaced.
  const [temporary, setTemporary] = useState<{
    identifier: string;
    identity: StaffIdentity;
    password: string;
  }>();
  const form = useZodForm(
    z
      .object({ identifier: z.string(), password: z.string().min(1, { error: 'required' }) })
      .superRefine((values, ctx) => {
        const parsed = staffIdentifier.safeParse(values.identifier);
        if (!parsed.success)
          ctx.addIssue({
            code: 'custom',
            path: ['identifier'],
            message: parsed.error.issues[0]?.message ?? 'invalid',
          });
      }),
    {
      defaultValues: { identifier: '', password: '' },
      // Validate on submit: a blur error on the autofocused empty field would shift the links below it.
      mode: 'onSubmit',
    },
  );

  const onSubmit = form.handleSubmit(async (values) => {
    setServerError(undefined);
    const identity = identityOf(staffIdentifier.parse(values.identifier));
    try {
      const { user } = ensureOk(
        await browserApi.POST('/api/v1/auth/staff/sign-in', {
          body: { ...identity, password: values.password },
        }),
      );
      router.replace(returnTo ?? homeFor(user.userType));
    } catch (error) {
      if (error instanceof ApiError && error.errorCode === 'auth.password_change_required')
        setTemporary({ identifier: values.identifier, identity, password: values.password });
      else if (error instanceof ApiError && error.isValidation) {
        const field = error.fieldErrors.email ?? error.fieldErrors.phone;
        if (field) form.setError('identifier', { type: 'server', message: codeToMessageKey(field[0]) });
        else applyProblemToForm(error, form.setError, ['password']);
      } else fail(error);
    }
  });

  if (temporary) {
    return (
      <ReplaceTemporaryPasswordForm
        username={temporary.identifier}
        identity={temporary.identity}
        temporaryPassword={temporary.password}
        onBack={() => {
          setTemporary(undefined);
          form.reset({ identifier: temporary.identifier, password: '' });
        }}
        onSignedIn={(userType) => router.replace(returnTo ?? homeFor(userType))}
      />
    );
  }

  return (
    <AuthCard
      showLogo
      title={t('title')}
      subtitle={t('subtitle')}
      footer={
        <Link href="/auth/sign-in" className="font-bold text-brand-700 hover:underline">
          {t('customer')}
        </Link>
      }
    >
      <form method="post" noValidate onSubmit={onSubmit} className="flex flex-col gap-5">
        <FormTextField
          control={form.control}
          name="identifier"
          label={t('identifier')}
          helper={t('identifierHint')}
          type="text"
          inputMode="email"
          autoComplete="username"
          dir="ltr"
          autoFocus
        />
        <FormTextField
          control={form.control}
          name="password"
          label={t('password')}
          type="password"
          autoComplete="current-password"
        />
        <Link
          href="/auth/forgot-password"
          className="-mt-2 self-end text-caption font-bold text-brand-700 hover:underline"
        >
          {t('forgot')}
        </Link>
        {serverError && <InlineAlert tone="danger" title={serverError} />}
        <Button type="submit" fullWidth loading={form.formState.isSubmitting}>
          {t('submit')}
        </Button>
      </form>
    </AuthCard>
  );
}

/** The second sign-in step for a temporary password: the API checks it again, saves the new one and signs in. */
function ReplaceTemporaryPasswordForm({
  username,
  identity,
  temporaryPassword,
  onBack,
  onSignedIn,
}: {
  username: string;
  identity: StaffIdentity;
  temporaryPassword: string;
  onBack: () => void;
  onSignedIn: (userType: string) => void;
}) {
  const t = useTranslations('auth.staffSignIn');
  const tReset = useTranslations('auth.reset');
  const { serverError, setServerError, fail } = useServerError();
  const form = useZodForm(newPasswordSchema, { defaultValues: { password: '', confirmPassword: '' } });

  const onSubmit = form.handleSubmit(async (values) => {
    setServerError(undefined);
    try {
      const { user } = ensureOk(
        await browserApi.POST('/api/v1/auth/password/change-temporary', {
          body: { ...identity, password: temporaryPassword, newPassword: values.password },
        }),
      );
      onSignedIn(user.userType);
    } catch (error) {
      if (error instanceof ApiError && error.isValidation)
        applyProblemToForm(error, form.setError, ['password']);
      else fail(error);
    }
  });

  return (
    <AuthCard showLogo title={t('changeTitle')} subtitle={t('changeSubtitle')}>
      <form method="post" noValidate onSubmit={onSubmit} className="flex flex-col gap-5">
        {/* Lets a password manager save the new password against the right account. */}
        <input type="text" name="username" value={username} autoComplete="username" readOnly hidden />
        <FormTextField
          control={form.control}
          name="password"
          label={tReset('newPassword')}
          helper={tReset('passwordHint')}
          type="password"
          autoComplete="new-password"
          autoFocus
        />
        <FormTextField
          control={form.control}
          name="confirmPassword"
          label={tReset('confirmPassword')}
          type="password"
          autoComplete="new-password"
        />
        {serverError && <InlineAlert tone="danger" title={serverError} />}
        <Button type="submit" fullWidth loading={form.formState.isSubmitting}>
          {t('changeSubmit')}
        </Button>
        <button
          type="button"
          onClick={onBack}
          className="self-center text-caption font-bold text-brand-700 hover:underline"
        >
          {t('changeBack')}
        </button>
      </form>
    </AuthCard>
  );
}

/** Always shows the same confirmation, whether or not the email exists (enumeration-safe). */
export function ForgotPasswordForm() {
  const t = useTranslations('auth.forgot');
  const locale = useLocale();
  const { serverError, setServerError, fail } = useServerError();
  const [sent, setSent] = useState(false);
  const form = useZodForm(z.object({ email }), { defaultValues: { email: '' }, mode: 'onSubmit' });

  const onSubmit = form.handleSubmit(async (values) => {
    setServerError(undefined);
    try {
      ensureOk(
        await browserApi.POST('/api/v1/auth/password/forgot', { body: { email: values.email, locale } }),
      );
      setSent(true);
    } catch (error) {
      if (error instanceof ApiError && error.isValidation)
        applyProblemToForm(error, form.setError, ['email']);
      else fail(error);
    }
  });

  if (sent) {
    return (
      <AuthCard title={t('sentTitle')}>
        <StateCard
          icon="check"
          tone="flat"
          live
          title={t('sentTitle')}
          body={t('sentBody')}
          action={
            <ButtonLink href="/auth/staff/sign-in" variant="outline" size="sm">
              {t('backToSignIn')}
            </ButtonLink>
          }
        />
      </AuthCard>
    );
  }

  return (
    <AuthCard title={t('title')} subtitle={t('subtitle')}>
      <form method="post" noValidate onSubmit={onSubmit} className="flex flex-col gap-5">
        <FormTextField
          control={form.control}
          name="email"
          label={t('title')}
          hideLabel
          type="email"
          autoComplete="email"
          dir="ltr"
          autoFocus
        />
        {serverError && <InlineAlert tone="danger" title={serverError} />}
        <Button type="submit" fullWidth loading={form.formState.isSubmitting}>
          {t('submit')}
        </Button>
        {/* Barbers sign in with their mobile and have no email (D-145). */}
        <p className="text-center text-caption text-text-secondary">{t('barber')}</p>
        <Link
          href="/auth/staff/sign-in"
          className="text-center text-caption font-bold text-brand-700 hover:underline"
        >
          {t('backToSignIn')}
        </Link>
      </form>
    </AuthCard>
  );
}

/** Sets a new password from the emailed link (`uid` + `token`); every session is signed out by the API. */
export function ResetPasswordForm({ userId, token }: { userId?: string; token?: string }) {
  const t = useTranslations('auth.reset');
  const tSignIn = useTranslations('auth.staffSignIn');
  const { serverError, setServerError, fail } = useServerError();
  const [state, setState] = useState<'form' | 'done' | 'invalid'>(userId && token ? 'form' : 'invalid');
  const form = useZodForm(newPasswordSchema, { defaultValues: { password: '', confirmPassword: '' } });

  const onSubmit = form.handleSubmit(async (values) => {
    setServerError(undefined);
    try {
      ensureOk(
        await browserApi.POST('/api/v1/auth/password/reset', {
          body: { userId: userId!, token: token!, newPassword: values.password },
        }),
      );
      setState('done');
    } catch (error) {
      if (error instanceof ApiError && error.errorCode === 'auth.reset_invalid') setState('invalid');
      else if (error instanceof ApiError && error.isValidation)
        applyProblemToForm(error, form.setError, ['password']);
      else fail(error);
    }
  });

  if (state !== 'form') {
    const done = state === 'done';
    return (
      <AuthCard title={done ? t('doneTitle') : t('invalidTitle')}>
        <StateCard
          icon={done ? 'check' : 'alert'}
          tone={done ? 'flat' : 'warn'}
          live
          title={done ? t('doneTitle') : t('invalidTitle')}
          body={done ? t('doneBody') : t('invalidBody')}
          action={
            <ButtonLink href={done ? '/auth/staff/sign-in' : '/auth/forgot-password'} size="sm">
              {done ? tSignIn('submit') : t('requestNew')}
            </ButtonLink>
          }
        />
      </AuthCard>
    );
  }

  return (
    <AuthCard title={t('title')}>
      <form method="post" noValidate onSubmit={onSubmit} className="flex flex-col gap-5">
        <FormTextField
          control={form.control}
          name="password"
          label={t('newPassword')}
          helper={t('passwordHint')}
          type="password"
          autoComplete="new-password"
          autoFocus
        />
        <FormTextField
          control={form.control}
          name="confirmPassword"
          label={t('confirmPassword')}
          type="password"
          autoComplete="new-password"
        />
        {serverError && <InlineAlert tone="danger" title={serverError} />}
        <Button type="submit" fullWidth loading={form.formState.isSubmitting}>
          {t('submit')}
        </Button>
      </form>
    </AuthCard>
  );
}

/** Creates a staff account from an emailed invitation and signs it in. */
export function AcceptInviteForm({ token }: { token?: string }) {
  const t = useTranslations('auth.acceptInvite');
  const tReset = useTranslations('auth.reset');
  const router = useRouter();
  const { serverError, setServerError, fail } = useServerError();
  const [invalid, setInvalid] = useState(!token);
  const schema = z
    .object({
      displayName: requiredText(60),
      password,
      confirmPassword: z.string().min(1, { error: 'required' }),
    })
    .refine((v) => v.password === v.confirmPassword, {
      path: ['confirmPassword'],
      error: 'passwordMismatch',
    });
  const form = useZodForm(schema, { defaultValues: { displayName: '', password: '', confirmPassword: '' } });

  const onSubmit = form.handleSubmit(async (values) => {
    setServerError(undefined);
    try {
      const { user } = ensureOk(
        await browserApi.POST('/api/v1/auth/invitations/accept', {
          body: { token: token!, displayName: values.displayName, password: values.password },
        }),
      );
      router.replace(homeFor(user.userType));
    } catch (error) {
      if (error instanceof ApiError && error.errorCode === 'invitation.invalid') setInvalid(true);
      else if (error instanceof ApiError && error.isValidation)
        applyProblemToForm(error, form.setError, ['displayName', 'password']);
      else fail(error);
    }
  });

  if (invalid) {
    return (
      <AuthCard title={t('invalidTitle')}>
        <StateCard icon="alert" tone="warn" live title={t('invalidTitle')} body={t('invalidBody')} />
      </AuthCard>
    );
  }

  return (
    <AuthCard showLogo title={t('title')} subtitle={t('subtitle')}>
      <form method="post" noValidate onSubmit={onSubmit} className="flex flex-col gap-5">
        <FormTextField
          control={form.control}
          name="displayName"
          label={t('nameLabel')}
          autoComplete="name"
          autoFocus
        />
        <FormTextField
          control={form.control}
          name="password"
          label={tReset('newPassword')}
          helper={tReset('passwordHint')}
          type="password"
          autoComplete="new-password"
        />
        <FormTextField
          control={form.control}
          name="confirmPassword"
          label={tReset('confirmPassword')}
          type="password"
          autoComplete="new-password"
        />
        {serverError && <InlineAlert tone="danger" title={serverError} />}
        <Button type="submit" fullWidth loading={form.formState.isSubmitting}>
          {t('submit')}
        </Button>
      </form>
    </AuthCard>
  );
}
