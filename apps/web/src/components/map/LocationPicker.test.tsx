import { fireEvent, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useEffect } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { renderWithIntl } from '@/test/render';
import { LocationPicker, type PickedLocation } from './LocationPicker';
import type { MapViewProps } from './MapView';

const get = vi.hoisted(() => vi.fn());
vi.mock('@/lib/api/client', () => ({ browserApi: { GET: get } }));

const malqa = {
  latitude: 24.8123,
  longitude: 46.6011,
  formattedAddress: 'طريق أنس بن مالك، حي الملقا، الرياض',
  addressLine: 'طريق أنس بن مالك',
  district: 'الملقا',
  city: 'الرياض',
};

/** Fake map adapter: exposes the pin and lets the test "drag" it. */
function FakeMap({ pin, onPinChange, label }: MapViewProps) {
  return (
    <div role="region" aria-label={label}>
      <span data-testid="fake-pin">{pin ? `${pin.lat},${pin.lng}` : 'none'}</span>
      <button type="button" onClick={() => onPinChange({ lat: 24.7630123456, lng: 46.6010987654 })}>
        drag pin
      </button>
    </div>
  );
}

function NoWebGlMap({ onUnavailable }: MapViewProps) {
  useEffect(() => onUnavailable(), [onUnavailable]);
  return null;
}

beforeEach(() => {
  get.mockReset();
  get.mockImplementation(async (path: string) => {
    if (path.endsWith('/geo/search')) return { data: [malqa] };
    if (path.endsWith('/geo/reverse'))
      return { data: { ...malqa, district: 'حطين', formattedAddress: 'حي حطين، الرياض' } };
    return { data: undefined };
  });
});

describe('LocationPicker (R-SHP-02, DV-A02)', () => {
  it('searches, places the pin on the chosen result and saves it as geocoded', async () => {
    const onSave = vi.fn(async (_: PickedLocation) => {});
    renderWithIntl(<LocationPicker initial={null} scope="admin" onSave={onSave} MapComponent={FakeMap} />);

    await userEvent.type(screen.getByLabelText('ابحث عن العنوان أو الحي'), 'الملقا');
    await userEvent.click(screen.getByRole('button', { name: 'بحث' }));
    await userEvent.click(await screen.findByRole('button', { name: malqa.formattedAddress }));

    expect(get).toHaveBeenCalledWith('/api/v1/admin/geo/search', {
      params: { query: { q: 'الملقا', lang: 'ar' } },
    });
    expect(screen.getByTestId('fake-pin')).toHaveTextContent('24.8123,46.6011');
    expect(screen.getByTestId('resolved-address')).toHaveTextContent(malqa.formattedAddress);
    expect(screen.getByTestId('coordinates')).toHaveTextContent('24.812300, 46.601100');

    await userEvent.click(screen.getByRole('button', { name: 'تأكيد الموقع' }));
    expect(onSave).toHaveBeenCalledWith({
      latitude: 24.8123,
      longitude: 46.6011,
      source: 'Geocoded',
      addressLine: malqa.addressLine,
      district: malqa.district,
      city: malqa.city,
      formattedAddress: malqa.formattedAddress,
    });
    expect(await screen.findByText('حُفظ الموقع.')).toBeInTheDocument();
  });

  it('dragging the pin rounds to 6 places, marks it manual and reverse-geocodes the address', async () => {
    const onSave = vi.fn(async (_: PickedLocation) => {});
    renderWithIntl(
      <LocationPicker
        initial={{ ...malqa, source: 'Geocoded' }}
        scope="shop"
        onSave={onSave}
        MapComponent={FakeMap}
      />,
    );

    await userEvent.click(screen.getByRole('button', { name: 'drag pin' }));
    await waitFor(() => expect(screen.getByTestId('resolved-address')).toHaveTextContent('حي حطين، الرياض'));
    expect(get).toHaveBeenCalledWith('/api/v1/shop/geo/reverse', {
      params: { query: { lat: 24.763012, lng: 46.601099, lang: 'ar' } },
    });
    expect(screen.getByText('تغيير الموقع يؤثر على المسافة في نتائج البحث.')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'تأكيد الموقع' }));
    expect(onSave.mock.calls[0]![0]).toMatchObject({
      latitude: 24.763012,
      longitude: 46.601099,
      source: 'Manual',
      district: 'حطين',
    });
  });

  it('falls back to typed coordinates when the map cannot render (keyboard alternative)', async () => {
    const onSave = vi.fn(async (_: PickedLocation) => {});
    renderWithIntl(
      <LocationPicker initial={null} scope="admin" onSave={onSave} MapComponent={NoWebGlMap} />,
      {
        locale: 'en',
      },
    );

    expect(await screen.findByText(/The map is not available on this device/)).toBeInTheDocument();
    const latitude = screen.getByLabelText('Latitude');
    await userEvent.type(latitude, '24.6930');
    await userEvent.type(screen.getByLabelText('Longitude'), '46.6850');
    fireEvent.blur(screen.getByLabelText('Longitude'));

    expect(await screen.findByTestId('coordinates')).toHaveTextContent('24.693000, 46.685000');
    await userEvent.click(screen.getByRole('button', { name: 'Confirm location' }));
    expect(onSave.mock.calls[0]![0]).toMatchObject({ latitude: 24.693, longitude: 46.685, source: 'Manual' });
  });

  it('rejects impossible coordinates without saving', async () => {
    const onSave = vi.fn(async (_: PickedLocation) => {});
    renderWithIntl(<LocationPicker initial={null} scope="admin" onSave={onSave} MapComponent={FakeMap} />, {
      locale: 'en',
    });

    await userEvent.type(screen.getByLabelText('Latitude'), '124');
    await userEvent.type(screen.getByLabelText('Longitude'), '46');
    fireEvent.blur(screen.getByLabelText('Longitude'));
    expect(screen.getAllByText('Enter valid coordinates').length).toBeGreaterThan(0);

    await userEvent.click(screen.getByRole('button', { name: 'Confirm location' }));
    expect(onSave).not.toHaveBeenCalled();
  });
});
