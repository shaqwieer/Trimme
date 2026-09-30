import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { TemplateDetail } from '@/lib/admin/whatsapp';
import { renderWithIntl } from '@/test/render';
import { TemplateEditor } from './TemplateEditor';

const api = vi.hoisted(() => ({ GET: vi.fn(), PUT: vi.fn(), POST: vi.fn() }));
vi.mock('@/lib/api/client', () => ({ browserApi: api }));

const ok = <T,>(data: T) => ({ data, response: new Response(null, { status: 200 }) });

const detail = (overrides: Partial<TemplateDetail> = {}): TemplateDetail => ({
  id: 't1',
  event: 'BookingConfirmed',
  audience: 'Customer',
  locale: 'en',
  allowedPlaceholders: ['customer_name', 'shop_name', 'booking_time', 'manage_url'],
  activeVersionId: 'v1',
  versions: [
    {
      id: 'v1',
      number: 1,
      body: 'Hi {{customer_name}}',
      buttons: [],
      providerTemplateName: 'trimme_customer_booking_confirmed',
      status: 'Active',
      createdAt: '2026-09-17T09:00:00Z',
      createdByName: null,
      activatedAt: '2026-09-17T09:00:00Z',
      activatedByName: null,
    },
  ],
  version: 11,
  ...overrides,
});

const previewOf = (
  body: string,
  issues: Array<{ field: string; code: string; placeholder: string | null }> = [],
) => ok({ valid: issues.length === 0, issues, body, buttons: [], usedPlaceholders: [] });

beforeEach(() => {
  api.GET.mockReset();
  api.PUT.mockReset();
  api.POST.mockReset();
});

describe('TemplateEditor', () => {
  it('inserts a placeholder chip at the caret and previews through the API with sample data', async () => {
    api.POST.mockResolvedValue(previewOf('Hi Sara Alotaibi'));
    renderWithIntl(<TemplateEditor initial={detail()} canEdit canActivate canTestSend />, { locale: 'en' });
    const body = screen.getByLabelText('Text') as HTMLTextAreaElement;
    body.setSelectionRange(body.value.length, body.value.length);
    await userEvent.click(screen.getByRole('button', { name: /Shop name/ }));
    expect(body.value).toBe('Hi {{customer_name}}{{shop_name}}');
    await waitFor(() =>
      expect(within(screen.getByTestId('bubble-preview')).getByText('Hi Sara Alotaibi')).toBeInTheDocument(),
    );
    expect(api.POST.mock.calls.at(-1)?.[0]).toBe('/api/v1/admin/whatsapp/templates/{templateId}/preview');
  });

  it('shows the API validation issues and blocks saving and activation while any exists', async () => {
    api.POST.mockResolvedValue(
      previewOf('', [{ field: 'body', code: 'template.unknown_placeholder', placeholder: 'customer_phone' }]),
    );
    renderWithIntl(<TemplateEditor initial={detail()} canEdit canActivate canTestSend={false} />, {
      locale: 'en',
    });
    await userEvent.clear(screen.getByLabelText('Text'));
    await userEvent.type(screen.getByLabelText('Text'), 'Call {{{{customer_phone}}');
    expect(await screen.findByText('The placeholder customer_phone is not known.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Save draft' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Activate' })).toBeDisabled();
    expect(screen.queryByRole('button', { name: 'Test send' })).toBeNull();
  });

  it('saves the draft with the version read', async () => {
    api.POST.mockResolvedValue(previewOf('Hello Sara'));
    api.PUT.mockResolvedValue(
      ok(
        detail({
          version: 12,
          versions: [
            {
              ...detail().versions[0]!,
              id: 'v2',
              number: 2,
              status: 'Draft',
              body: 'Hello {{customer_name}}',
              activatedAt: null,
            },
            ...detail().versions,
          ],
        }),
      ),
    );
    renderWithIntl(<TemplateEditor initial={detail()} canEdit canActivate canTestSend />, { locale: 'en' });
    await userEvent.clear(screen.getByLabelText('Text'));
    await userEvent.type(screen.getByLabelText('Text'), 'Hello {{{{customer_name}}');
    await waitFor(() => expect(screen.getByRole('button', { name: 'Save draft' })).toBeEnabled());
    await userEvent.click(screen.getByRole('button', { name: 'Save draft' }));
    await waitFor(() => expect(api.PUT).toHaveBeenCalledTimes(1));
    expect(api.PUT.mock.calls[0]?.[1].body).toMatchObject({ body: 'Hello {{customer_name}}', version: 11 });
    expect(await screen.findByText('Draft saved.')).toBeInTheDocument();
    expect(screen.getByText('Draft · version 2')).toBeInTheDocument();
  });

  it('offers professionals no manage-booking button and keeps the text read-only without the edit permission', async () => {
    api.POST.mockResolvedValue(previewOf('x'));
    renderWithIntl(
      <TemplateEditor
        initial={detail({ audience: 'Professional', allowedPlaceholders: ['customer_name'] })}
        canEdit={false}
        canActivate={false}
        canTestSend={false}
      />,
      { locale: 'en' },
    );
    expect(screen.getByLabelText('Text')).toHaveAttribute('readonly');
    expect(screen.queryByRole('button', { name: /Customer name/ })).toBeNull();
    expect(screen.queryByRole('button', { name: 'Add a button' })).toBeNull();
  });

  it('never prefills the test recipient and needs the explicit confirmation', async () => {
    api.POST.mockImplementation((path: string) =>
      Promise.resolve(
        path.endsWith('/test-send')
          ? ok({ id: 'd1', status: 'Delivered', recipientMasked: '+966 5•• ••• •77', lastError: null })
          : previewOf('Hi Sara'),
      ),
    );
    renderWithIntl(<TemplateEditor initial={detail()} canEdit canActivate canTestSend />, { locale: 'en' });
    await userEvent.click(screen.getByRole('button', { name: 'Test send' }));
    const dialog = await screen.findByRole('dialog');
    const phone = within(dialog).getByLabelText('Test number');
    expect(phone).toHaveValue('');
    const send = within(dialog).getByRole('button', { name: 'Send' });
    await userEvent.type(phone, '512300077');
    expect(send).toBeDisabled();
    await userEvent.click(
      within(dialog).getByLabelText("I confirm this is a test number, not a customer's."),
    );
    await userEvent.click(send);
    await waitFor(() =>
      expect(api.POST).toHaveBeenCalledWith('/api/v1/admin/whatsapp/templates/{templateId}/test-send', {
        params: { path: { templateId: 't1' } },
        body: { versionId: null, recipient: '+966512300077', confirmTestRecipient: true },
      }),
    );
    expect(
      await within(dialog).findByText('The test message was sent to +966 5•• ••• •77.'),
    ).toBeInTheDocument();
  });
});
