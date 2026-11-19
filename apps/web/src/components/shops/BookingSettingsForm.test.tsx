import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '@/test/axe';
import { renderWithIntl } from '@/test/render';
// After the render helper, which mocks Next's router before anything imports it.
import { BookingSettingsForm } from './BookingSettingsForm';

const api = vi.hoisted(() => ({ PUT: vi.fn() }));
vi.mock('@/lib/api/client', () => ({ browserApi: api }));

const SETTINGS = {
  slotStepMinutes: 15,
  minLeadTimeMinutes: 60,
  bookingHorizonDays: 14,
  bufferMinutes: 0,
};

beforeEach(() => api.PUT.mockReset());

describe('a shop’s own booking settings (D-149, the owner’s item 5)', () => {
  it('shows the values in force and saves the owner’s choices', async () => {
    const user = userEvent.setup();
    api.PUT.mockResolvedValue({ data: SETTINGS, response: new Response(null, { status: 200 }) });
    renderWithIntl(
      <main>
        <BookingSettingsForm scope={{ kind: 'shop' }} settings={SETTINGS} canEdit />
      </main>,
    );

    expect(screen.getByLabelText(/تبدأ المواعيد/)).toHaveValue('15');
    // The start interval is not the service's length, and the strip follows the furthest day.
    expect(screen.getByLabelText(/تبدأ المواعيد/)).toHaveAccessibleDescription(/وليس مدة الخدمة/);
    expect(screen.getByLabelText(/أبعد يوم يُحجز/)).toHaveValue('14');
    expect(screen.getByRole('option', { name: '١٤ يوماً' })).toBeInTheDocument();
    // Always confirmed (D-155): no switch; whole hours as hours.
    expect(screen.queryByRole('switch')).not.toBeInTheDocument();
    const notice = within(screen.getByLabelText(/أقل مهلة قبل الموعد/));
    expect(notice.getByRole('option', { name: 'ساعة واحدة' })).toBeInTheDocument();
    expect(notice.getByRole('option', { name: '٦ ساعات' })).toBeInTheDocument();
    expect(notice.getByRole('option', { name: '٢٤ ساعة' })).toBeInTheDocument();
    expect(screen.getByRole('option', { name: 'كل ٥ دقائق' })).toBeInTheDocument();
    await expectNoAxeViolations();

    await user.selectOptions(screen.getByLabelText(/تبدأ المواعيد/), '30');
    await user.selectOptions(screen.getByLabelText(/وقت التنظيف/), '10');
    await user.click(screen.getByRole('button', { name: 'حفظ إعدادات الحجز' }));

    await waitFor(() => expect(api.PUT).toHaveBeenCalled());
    expect(api.PUT).toHaveBeenCalledWith('/api/v1/shop/booking-settings', {
      body: {
        slotStepMinutes: 30,
        minLeadTimeMinutes: 60,
        bookingHorizonDays: 14,
        bufferMinutes: 10,
      },
    });
    expect(await screen.findByText(/حُفظت إعدادات الحجز/)).toBeInTheDocument();
  });

  it('keeps a value set elsewhere choosable, and the admin saves through the shop’s admin endpoint', async () => {
    const user = userEvent.setup();
    api.PUT.mockResolvedValue({ data: SETTINGS, response: new Response(null, { status: 200 }) });
    renderWithIntl(
      <BookingSettingsForm
        scope={{ kind: 'admin', shopId: 'shop-1' }}
        settings={{ ...SETTINGS, minLeadTimeMinutes: 45 }}
        canEdit
      />,
    );

    expect(screen.getByLabelText(/أقل مهلة قبل الموعد/)).toHaveValue('45');
    await user.click(screen.getByRole('button', { name: 'حفظ إعدادات الحجز' }));
    await waitFor(() =>
      expect(api.PUT).toHaveBeenCalledWith('/api/v1/admin/shops/{shopId}/booking-settings', {
        params: { path: { shopId: 'shop-1' } },
        body: { ...SETTINGS, minLeadTimeMinutes: 45 },
      }),
    );
  });

  it('shows the field the API refused', async () => {
    const user = userEvent.setup();
    api.PUT.mockResolvedValue({
      error: { errorCode: 'validation.failed', errors: { bufferMinutes: ['validation.out_of_range'] } },
      response: new Response(null, { status: 400 }),
    });
    renderWithIntl(<BookingSettingsForm scope={{ kind: 'shop' }} settings={SETTINGS} canEdit />);

    await user.click(screen.getByRole('button', { name: 'حفظ إعدادات الحجز' }));
    await waitFor(() =>
      expect(screen.getByLabelText(/وقت التنظيف/)).toHaveAccessibleDescription(/خارج النطاق/),
    );
  });

  it('is read-only without the right to edit', () => {
    renderWithIntl(<BookingSettingsForm scope={{ kind: 'shop' }} settings={SETTINGS} canEdit={false} />);
    expect(screen.getByLabelText(/تبدأ المواعيد/)).toBeDisabled();
    expect(screen.queryByRole('button', { name: 'حفظ إعدادات الحجز' })).not.toBeInTheDocument();
  });
});
