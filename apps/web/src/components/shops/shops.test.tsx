import { screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { renderWithIntl } from '@/test/render';
import ar from '../../../messages/ar.json';
import en from '../../../messages/en.json';
import type { ShopProfileData } from './shopApi';
import { ShopProfileEditor } from './ShopProfileEditor';

vi.mock('@/lib/api/client', () => ({ browserApi: {} }));

const profile: ShopProfileData = {
  nameAr: 'صالون الأصالة',
  nameEn: 'Al Asala',
  descriptionAr: 'وصف',
  descriptionEn: null,
  category: 'Barbershop',
  publicPhone: '+966114567890',
  amenities: ['Parking'],
  isVerified: true,
  logoUrl: null,
  coverUrl: null,
  gallery: [],
  location: null,
  editableFields: ['Description', 'PublicPhone'],
  version: 7,
};

describe('ShopProfileEditor (R-SD-09, DV-S16)', () => {
  it('locks the fields outside the admin policy for the shop, with a visible note', () => {
    renderWithIntl(<ShopProfileEditor mode={{ kind: 'shop' }} profile={profile} canEdit />);

    expect(screen.getByLabelText('اسم المحل بالعربية')).toBeDisabled();
    expect(screen.getByLabelText('نوع المحل')).toBeDisabled();
    expect(screen.getByLabelText(/الوصف بالعربية/)).toBeEnabled();
    expect(screen.getByLabelText(/رقم هاتف المحل/)).toBeEnabled();
    expect(screen.getAllByText('مقفل من الإدارة').length).toBeGreaterThanOrEqual(3);
    expect(screen.queryByRole('switch', { name: 'محل موثّق' })).not.toBeInTheDocument();
  });

  it('gives the admin every field and the verification switch', () => {
    renderWithIntl(<ShopProfileEditor mode={{ kind: 'admin', shopId: 's1' }} profile={profile} canEdit />, {
      locale: 'en',
    });

    expect(screen.getByLabelText('Shop name in Arabic')).toBeEnabled();
    expect(screen.getByLabelText('Shop type')).toBeEnabled();
    expect(screen.getByRole('switch', { name: 'Verified shop' })).toHaveAttribute('aria-checked', 'true');
    expect(screen.queryByText('Locked by the admin')).not.toBeInTheDocument();
  });

  it('is read-only without the edit permission (no save button)', () => {
    renderWithIntl(<ShopProfileEditor mode={{ kind: 'shop' }} profile={profile} canEdit={false} />);
    expect(screen.getByLabelText(/الوصف بالعربية/)).toBeDisabled();
    expect(screen.queryByRole('button', { name: 'حفظ الملف' })).not.toBeInTheDocument();
  });
});

describe('no barber transfer anywhere in the UI copy (R-NEG-01, DV-S01)', () => {
  const texts = (catalog: object): string[] =>
    Object.values(catalog).flatMap((value) => (typeof value === 'string' ? [value] : texts(value as object)));

  it.each([
    ['ar', ar, /نقل (ال)?حلاق|تنفيذ النقل|(^|\s)نقل\s|تحويل (ال)?حلاق/], // not "التنقل" (navigation)
    ['en', en, /transfer|move (the )?professional|reassign/i],
  ] as const)('the %s catalog offers no transfer action', (_, catalog, pattern) => {
    expect(texts(catalog).filter((text) => pattern.test(text))).toEqual([]);
  });
});
