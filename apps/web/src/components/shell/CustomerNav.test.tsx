import { screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { renderWithIntl, setPathname } from '@/test/render';
import { CustomerNav } from './CustomerNav';

describe('CustomerNav (bottom bar)', () => {
  it('renders five destinations with the active one marked for assistive technology', () => {
    setPathname('/ar/account/bookings/42');
    renderWithIntl(<CustomerNav placement="bottom" />);

    const nav = screen.getByRole('navigation', { name: 'التنقل الرئيسي' });
    const links = within(nav).getAllByRole('link');
    expect(links).toHaveLength(5);
    expect(within(nav).getByRole('link', { current: 'page' })).toHaveTextContent('مواعيدي');
  });

  it('localizes labels in English', () => {
    setPathname('/en/discover');
    renderWithIntl(<CustomerNav placement="bottom" />, { locale: 'en' });
    expect(screen.getByRole('link', { current: 'page' })).toHaveTextContent('Home');
  });
});
