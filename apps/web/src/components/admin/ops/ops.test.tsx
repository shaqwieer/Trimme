import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '@/test/axe';
import { renderWithIntl } from '@/test/render';
import { BookingIntervention } from './BookingIntervention';
import { ContactShopButton } from './ContactShopButton';
import { CustomerContact } from './CustomerContact';
import { ReviewActions } from './ReviewActions';
import { RoleEditor } from './RoleEditor';
import { StaffActions } from './StaffActions';

const api = vi.hoisted(() => ({ GET: vi.fn(), PUT: vi.fn(), POST: vi.fn(), DELETE: vi.fn() }));
vi.mock('@/lib/api/client', () => ({ browserApi: api }));

const ok = <T,>(data: T) => ({ data, response: new Response(null, { status: 200 }) });
const problem = (status: number, errorCode: string) => ({
  error: { status, errorCode },
  response: new Response(null, { status }),
});

beforeEach(() => {
  api.GET.mockReset();
  api.PUT.mockReset();
  api.POST.mockReset();
  api.DELETE.mockReset();
});

describe('BookingIntervention', () => {
  it('offers only the transitions the API allows and asks for a reason before sending', async () => {
    api.POST.mockResolvedValue(ok({}));
    renderWithIntl(
      <BookingIntervention
        bookingId="b1"
        version={7}
        allowed={['CancelledByShop']}
        canReschedule={false}
        today="2026-10-01"
        timeZone="Asia/Riyadh"
      />,
      { locale: 'en' },
    );
    expect(screen.queryByRole('button', { name: 'Customer arrived' })).toBeNull();
    await userEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    const dialog = await screen.findByRole('dialog');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Confirm' }));
    expect(within(dialog).getByLabelText('Reason')).toHaveAttribute('aria-invalid', 'true');
    expect(api.POST).not.toHaveBeenCalled();

    await userEvent.type(within(dialog).getByLabelText('Reason'), 'Shop closed that day');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Confirm' }));
    await waitFor(() => expect(api.POST).toHaveBeenCalledTimes(1));
    expect(api.POST.mock.calls[0]?.[0]).toBe('/api/v1/admin/bookings/{bookingId}/transitions');
    expect(api.POST.mock.calls[0]?.[1].body).toEqual({
      to: 'CancelledByShop',
      reason: 'Shop closed that day',
      version: 7,
    });
  });

  it('reschedules to a chosen free time and keeps one idempotency key across a retry', async () => {
    api.GET.mockResolvedValue(
      ok({
        timeZone: 'Asia/Riyadh',
        date: '2026-10-01',
        professionalId: 'p1',
        professionals: [{ id: 'p1', nameAr: 'فيصل', nameEn: 'Faisal' }],
        slots: [{ startsAt: '2026-10-01T11:00:00Z', localTime: '14:00', period: 'Afternoon' }],
      }),
    );
    api.POST.mockResolvedValueOnce(problem(503, 'server.unexpected')).mockResolvedValueOnce(ok({}));
    renderWithIntl(
      <BookingIntervention
        bookingId="b1"
        version={3}
        allowed={[]}
        canReschedule
        today="2026-10-01"
        timeZone="Asia/Riyadh"
      />,
      { locale: 'en' },
    );
    await userEvent.click(screen.getByRole('button', { name: 'Reschedule' }));
    const dialog = await screen.findByRole('dialog');
    await userEvent.click(await within(dialog).findByRole('radio', { name: /2:00/ }));
    await userEvent.type(within(dialog).getByLabelText('Reason'), 'Customer asked');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Confirm the new time' }));
    await waitFor(() => expect(api.POST).toHaveBeenCalledTimes(1));
    await userEvent.click(within(dialog).getByRole('button', { name: 'Confirm the new time' }));
    await waitFor(() => expect(api.POST).toHaveBeenCalledTimes(2));
    const [first, second] = api.POST.mock.calls.map((c) => c[1]);
    expect(first.body).toMatchObject({
      startsAt: '2026-10-01T11:00:00Z',
      reason: 'Customer asked',
      version: 3,
    });
    expect(first.params.header['Idempotency-Key']).toBe(second.params.header['Idempotency-Key']);
  });
});

describe('CustomerContact', () => {
  it('shows the masked number and reveals it only with a reason', async () => {
    api.POST.mockResolvedValue(ok({ phone: '+966500100303' }));
    const { container } = renderWithIntl(
      <CustomerContact customerId="c1" masked="+966 5•• ••• •03" canReveal />,
      { locale: 'en' },
    );
    expect(screen.getByTestId('customer-phone')).toHaveTextContent('+966 5•• ••• •03');
    await expectNoAxeViolations(container);
    await userEvent.click(screen.getByRole('button', { name: 'Show number' }));
    const dialog = await screen.findByRole('dialog');
    await userEvent.type(within(dialog).getByLabelText('Reason'), 'hi');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Show number' }));
    expect(api.POST).not.toHaveBeenCalled();
    await userEvent.clear(within(dialog).getByLabelText('Reason'));
    await userEvent.type(within(dialog).getByLabelText('Reason'), 'Complaint #12');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Show number' }));
    await waitFor(() => expect(screen.getByTestId('customer-phone')).toHaveTextContent('+966500100303'));
    expect(api.POST.mock.calls[0]?.[1].body).toEqual({ reason: 'Complaint #12' });
  });

  it('offers no reveal without the permission', () => {
    renderWithIntl(<CustomerContact customerId="c1" masked="+966 5•• ••• •03" canReveal={false} />, {
      locale: 'en',
    });
    expect(screen.queryByRole('button', { name: 'Show number' })).toBeNull();
  });
});

describe('ReviewActions', () => {
  it('offers report to staff, hide and publish to moderators, depending on the state', () => {
    const staff = renderWithIntl(
      <ReviewActions
        reviewId="r1"
        version={1}
        status="Published"
        reported={false}
        canFlag
        canModerate={false}
      />,
      { locale: 'en' },
    );
    expect(screen.getByRole('button', { name: 'Report' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Hide' })).toBeNull();
    staff.unmount();

    const reported = renderWithIntl(
      <ReviewActions reviewId="r1" version={1} status="Published" reported canFlag canModerate />,
      {
        locale: 'en',
      },
    );
    expect(screen.queryByRole('button', { name: 'Report' })).toBeNull();
    expect(screen.getByRole('button', { name: 'Hide' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Publish' })).toBeInTheDocument();
    reported.unmount();

    renderWithIntl(
      <ReviewActions reviewId="r1" version={1} status="Hidden" reported={false} canFlag canModerate />,
      { locale: 'en' },
    );
    expect(screen.queryByRole('button', { name: 'Hide' })).toBeNull();
    expect(screen.getByRole('button', { name: 'Publish' })).toBeInTheDocument();
  });

  it('hides with a reason and the version read', async () => {
    api.POST.mockResolvedValue(ok({}));
    renderWithIntl(
      <ReviewActions
        reviewId="r1"
        version={9}
        status="Published"
        reported={false}
        canFlag={false}
        canModerate
      />,
      {
        locale: 'en',
      },
    );
    await userEvent.click(screen.getByRole('button', { name: 'Hide' }));
    const dialog = await screen.findByRole('dialog');
    await userEvent.type(within(dialog).getByLabelText('Reason'), 'Contains a phone number');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Hide' }));
    await waitFor(() => expect(api.POST).toHaveBeenCalledTimes(1));
    expect(api.POST.mock.calls[0]?.[0]).toBe('/api/v1/admin/reviews/{reviewId}/hide');
    expect(api.POST.mock.calls[0]?.[1].body).toEqual({ reason: 'Contains a phone number', version: 9 });
  });
});

describe('RoleEditor', () => {
  const catalogue = ['Admin.Audit.View', 'Admin.Settings.Edit', 'SuperAdmin.Subscriptions.Override'];

  it('is read-only for a managed role', () => {
    renderWithIntl(
      <RoleEditor
        role={{ id: 'r1', name: 'SuperAdmin', managed: true, seed: true, permissions: catalogue }}
        catalogue={catalogue}
        held={catalogue}
        canManage
      />,
      { locale: 'en' },
    );
    expect(
      screen.getByText('This role is managed by the platform and cannot be changed here.'),
    ).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Save permissions' })).toBeNull();
    expect(screen.getByLabelText('View the activity log')).toBeDisabled();
  });

  it('never offers SuperAdmin permissions and locks what the admin does not hold', async () => {
    api.PUT.mockResolvedValue(ok({}));
    renderWithIntl(
      <RoleEditor
        role={{ id: 'r2', name: 'Auditors', managed: false, seed: false, permissions: [] }}
        catalogue={catalogue}
        held={['Admin.Audit.View']}
        canManage
      />,
      { locale: 'en' },
    );
    expect(screen.queryByLabelText("Override a shop's subscription")).toBeNull();
    expect(screen.getByLabelText('Edit platform settings')).toBeDisabled();
    await userEvent.click(screen.getByLabelText('View the activity log'));
    await userEvent.click(screen.getByRole('button', { name: 'Save permissions' }));
    await waitFor(() => expect(api.PUT).toHaveBeenCalledTimes(1));
    expect(api.PUT.mock.calls[0]?.[1].body).toEqual({ permissions: ['Admin.Audit.View'] });
  });
});

describe('StaffActions', () => {
  it('shows nothing to change on your own account', () => {
    renderWithIntl(
      <StaffActions userId="u1" name="Noura" current={['Support']} roles={[]} disabled={false} self />,
      { locale: 'en' },
    );
    expect(screen.getByText('You')).toBeInTheDocument();
    expect(screen.queryByRole('button')).toBeNull();
  });

  it('shows a refused change, such as a role with permissions you lack', async () => {
    api.PUT.mockResolvedValue(problem(403, 'role.escalation'));
    renderWithIntl(
      <StaffActions
        userId="u2"
        name="Omar"
        current={['Support']}
        roles={[
          { name: 'Support', label: 'Support' },
          { name: 'Auditors', label: 'Auditors' },
        ]}
        disabled={false}
        self={false}
      />,
      { locale: 'en' },
    );
    await userEvent.click(screen.getByRole('button', { name: "Change Omar's roles" }));
    const dialog = await screen.findByRole('dialog');
    await userEvent.click(within(dialog).getByLabelText('Auditors'));
    await userEvent.click(within(dialog).getByRole('button', { name: 'Save roles' }));
    expect(await screen.findByText('You can only grant permissions you hold yourself.')).toBeInTheDocument();
    expect(api.PUT.mock.calls[0]?.[1].body).toEqual({ roles: ['Support', 'Auditors'] });
  });
});

describe('ContactShopButton', () => {
  it('asks for a 5–500 character message, then sends it to the shop of the review', async () => {
    api.POST.mockResolvedValue({ data: undefined, response: new Response(null, { status: 204 }) });
    renderWithIntl(<ContactShopButton reviewId="r1" />, { locale: 'en' });
    await userEvent.click(screen.getByRole('button', { name: 'Contact the shop' }));
    const dialog = await screen.findByRole('dialog');
    await userEvent.type(within(dialog).getByLabelText('Message'), 'hi');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Send' }));
    expect(within(dialog).getByText('Write a message of 5 to 500 characters.')).toBeInTheDocument();
    expect(api.POST).not.toHaveBeenCalled();

    await userEvent.type(within(dialog).getByLabelText('Message'), ' — please call the customer back');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Send' }));
    await waitFor(() =>
      expect(api.POST).toHaveBeenCalledWith('/api/v1/admin/reviews/{reviewId}/contact-shop', {
        params: { path: { reviewId: 'r1' } },
        body: { message: 'hi — please call the customer back' },
      }),
    );
    expect(await screen.findByText('The message was sent to the shop.')).toBeInTheDocument();
  });
});
