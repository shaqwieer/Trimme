import { fireEvent, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '@/test/axe';
import { renderWithIntl } from '@/test/render';
import { groupSaudiMobile, normalizeSaudiMobile, toE164, toLatinDigits } from './digits';
import {
  Checkbox,
  OtpField,
  PhoneField,
  SearchField,
  SelectField,
  Switch,
  TextareaField,
  TextField,
} from './inputs';

describe('digit normalisation', () => {
  it('converts Arabic-Indic and Eastern Arabic-Indic digits to Latin', () => {
    expect(toLatinDigits('٠٥١٢٣')).toBe('05123');
    expect(toLatinDigits('۰۵۱۲۳')).toBe('05123');
  });

  it.each([
    ['512345678', '512345678'],
    ['0512345678', '512345678'],
    ['+966 51 234 5678', '512345678'],
    ['00966512345678', '512345678'],
    ['٠٥١٢٣٤٥٦٧٨', '512345678'],
  ])('normalises %s to national digits', (input, expected) => {
    expect(normalizeSaudiMobile(input)).toBe(expected);
  });

  it('emits E.164 only for complete Saudi mobiles', () => {
    expect(toE164('512345678')).toBe('+966512345678');
    expect(toE164('51234')).toBeNull();
    expect(toE164('412345678')).toBeNull();
    expect(groupSaudiMobile('512345678')).toBe('51 234 5678');
  });
});

describe('TextField', () => {
  it('links the error to the input and marks it invalid', () => {
    renderWithIntl(<TextField label="الاسم الكامل" error="هذا الحقل مطلوب" />);
    const input = screen.getByLabelText('الاسم الكامل');
    expect(input).toHaveAttribute('aria-invalid', 'true');
    expect(input).toHaveAccessibleDescription('هذا الحقل مطلوب');
  });

  it('marks optional fields in the label', () => {
    renderWithIntl(<TextareaField label="ملاحظة للحلاق" optional />);
    expect(screen.getByLabelText('ملاحظة للحلاق(اختياري)')).toBeInTheDocument();
  });

  it('has no accessibility violations in a composed form', async () => {
    const { container } = renderWithIntl(
      <form>
        <TextField label="الاسم الكامل" helper="كما يظهر للمحل" />
        <PhoneField label="رقم الجوال" helper="يُستخدم لإرسال تأكيد الموعد عبر واتساب" />
        <OtpField label="رمز التحقق" error="الرمز غير صحيح" />
        <SelectField label="الترتيب">
          <option>الأقرب</option>
        </SelectField>
        <SearchField label="بحث" value="" onChange={() => {}} />
        <Checkbox label="أوافق على الشروط" />
        <Switch checked label="السماح بإشعارات واتساب" onCheckedChange={() => {}} />
      </form>,
    );
    await expectNoAxeViolations(container);
  });
});

describe('PhoneField', () => {
  it('is left-to-right, groups digits and emits E.164 (accepts Arabic-Indic input)', () => {
    const onValueChange = vi.fn();
    renderWithIntl(<PhoneField label="رقم الجوال" onValueChange={onValueChange} />);
    const input = screen.getByLabelText('رقم الجوال');
    expect(input).toHaveAttribute('dir', 'ltr');
    expect(input).toHaveAttribute('inputmode', 'numeric');

    fireEvent.change(input, { target: { value: '٠٥١٢٣٤٥٦٧٨' } });
    expect(input).toHaveValue('51 234 5678');
    expect(onValueChange).toHaveBeenLastCalledWith('512345678', '+966512345678');
  });
});

describe('OtpField', () => {
  it('accepts only 6 digits, supports one-time-code autofill and reports completion', () => {
    const onComplete = vi.fn();
    renderWithIntl(<OtpField label="رمز التحقق" onComplete={onComplete} />);
    const input = screen.getByLabelText('رمز التحقق');
    expect(input).toHaveAttribute('autocomplete', 'one-time-code');
    expect(input).toHaveAccessibleDescription('أدخل الرمز المكوّن من 6 أرقام');

    fireEvent.change(input, { target: { value: '12a3456789' } });
    expect(input).toHaveValue('123456');
    expect(onComplete).toHaveBeenCalledWith('123456');
  });
});

describe('Switch', () => {
  it('is a labelled switch that toggles', async () => {
    const onCheckedChange = vi.fn();
    renderWithIntl(<Switch checked={false} label="الأحد" onCheckedChange={onCheckedChange} />);
    const control = screen.getByRole('switch', { name: 'الأحد' });
    expect(control).toHaveAttribute('aria-checked', 'false');
    await userEvent.click(control);
    expect(onCheckedChange).toHaveBeenCalledWith(true);
  });
});

describe('SearchField', () => {
  it('offers a labelled clear button when it has a value', async () => {
    const onClear = vi.fn();
    renderWithIntl(<SearchField label="بحث" value="تهذيب" onChange={() => {}} onClear={onClear} />);
    expect(screen.getByRole('search')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'مسح البحث' }));
    expect(onClear).toHaveBeenCalled();
  });
});
