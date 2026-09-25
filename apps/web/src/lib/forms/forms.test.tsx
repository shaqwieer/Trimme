import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { z } from 'zod';
import { Button } from '@/components/ui/Button';
import { toApiError } from '@/lib/api/problem';
import { renderWithIntl } from '@/test/render';
import { FormPhoneField, FormTextField, useValidationMessage, useZodForm } from './fields';
import { applyProblemToForm, codeToMessageKey } from './problem';
import { otpCode, parseValidationMessage, requiredText, saudiMobile, withParams } from './validation';

describe('validation schemas return message keys, never text', () => {
  it('requiredText', () => {
    expect(requiredText().safeParse('  ').error?.issues[0]?.message).toBe('required');
    expect(requiredText(3).safeParse('abcd').error?.issues[0]?.message).toBe('tooLong|max=3');
  });

  it('saudiMobile normalises and explains what to fix', () => {
    expect(saudiMobile.parse('+966 51 234 5678')).toBe('512345678');
    expect(saudiMobile.safeParse('').error?.issues[0]?.message).toBe('required');
    expect(saudiMobile.safeParse('5123').error?.issues[0]?.message).toBe('phoneIncomplete');
    expect(saudiMobile.safeParse('412345678').error?.issues[0]?.message).toBe('phoneInvalid');
  });

  it('otpCode requires exactly six digits (D-037)', () => {
    expect(otpCode().parse('١٢٣٤٥٦')).toBe('123456');
    expect(otpCode().safeParse('1234').error?.issues[0]?.message).toBe('otpIncomplete');
  });

  it('parses encoded parameters and falls back to generic', () => {
    expect(parseValidationMessage(withParams('tooLong', { max: 120 }))).toEqual({
      key: 'tooLong',
      params: { max: '120' },
    });
    expect(parseValidationMessage('something.unknown').key).toBe('generic');
  });
});

describe('API codes → message keys', () => {
  it.each([
    ['name.required', 'required'],
    ['required', 'required'],
    ['customer.phone_incomplete', 'phoneIncomplete'],
    ['NotEmptyValidator', 'required'],
    ['booking.slot_unavailable', 'generic'],
    [undefined, 'generic'],
  ])('%s → %s', (code, key) => {
    expect(codeToMessageKey(code)).toBe(key);
  });
});

const schema = z.object({ name: requiredText(60), phone: saudiMobile });

/** Test form: renders fields, applies a canned API problem on submit, shows the form-level error. */
function ProfileForm({ problem }: { problem?: Response }) {
  const form = useZodForm(schema, { defaultValues: { name: '', phone: '' } });
  const message = useValidationMessage();
  const onSubmit = form.handleSubmit(async () => {
    if (problem) {
      applyProblemToForm(await toApiError(problem), form.setError, ['name', 'phone']);
    }
  });
  const rootError = form.formState.errors.root?.server;
  return (
    <form onSubmit={onSubmit} noValidate>
      <FormTextField control={form.control} name="name" label="الاسم الكامل" />
      <FormPhoneField control={form.control} name="phone" label="رقم الجوال" />
      {rootError && <p role="alert">{message(rootError)}</p>}
      <Button type="submit">حفظ</Button>
    </form>
  );
}

describe('form kit', () => {
  it('shows translated client-side validation messages', async () => {
    renderWithIntl(<ProfileForm />);
    await userEvent.click(screen.getByRole('button', { name: 'حفظ' }));
    const name = screen.getByLabelText('الاسم الكامل');
    await waitFor(() => expect(name).toHaveAccessibleDescription('هذا الحقل مطلوب'));
    expect(name).toHaveAttribute('aria-invalid', 'true');
    expect(screen.getByLabelText('رقم الجوال')).toHaveAccessibleDescription('هذا الحقل مطلوب');
  });

  it('problemDetails_maps_to_field_errors (R-WEB-14)', async () => {
    // Exact shape produced by the API's exception handler for validation.failed.
    const problem = new Response(
      JSON.stringify({
        type: 'https://tools.ietf.org/html/rfc9110#section-15.5.1',
        title: 'One or more validation errors occurred.',
        status: 400,
        instance: '/api/v1/me/profile',
        errorCode: 'validation.failed',
        correlationId: '01a0d7e2842b79f6',
        errors: {
          name: ['name.required'],
          phone: ['customer.phone_incomplete'],
          nickname: ['nickname.required'],
        },
      }),
      { status: 400, headers: { 'content-type': 'application/problem+json' } },
    );

    renderWithIntl(<ProfileForm problem={problem} />, { locale: 'en' });
    await userEvent.type(screen.getByLabelText('الاسم الكامل'), 'Sara');
    await userEvent.type(screen.getByLabelText('رقم الجوال'), '512345678');
    await userEvent.click(screen.getByRole('button', { name: 'حفظ' }));

    await waitFor(() =>
      expect(screen.getByLabelText('الاسم الكامل')).toHaveAccessibleDescription('This field is required'),
    );
    expect(screen.getByLabelText('رقم الجوال')).toHaveAccessibleDescription('Enter the 9 digits after +966');
    // "nickname" is not on this form, so the user gets a form-level message instead of a silent failure.
    expect(screen.getByRole('alert')).toHaveTextContent(
      'Could not save — review the highlighted fields and try again',
    );
  });
});
