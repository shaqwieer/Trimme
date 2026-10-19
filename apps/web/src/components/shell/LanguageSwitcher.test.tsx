import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it } from 'vitest';
import { expectNoAxeViolations } from '@/test/axe';
import { renderWithIntl, setPathname } from '@/test/render';
import { LanguageSwitcher } from './LanguageSwitcher';

afterEach(() => setPathname('/ar'));

describe('language menu', () => {
  it('names the button after the current language and links every language to the same page', async () => {
    const user = userEvent.setup();
    setPathname('/ar/shops/al-asala');
    renderWithIntl(
      <main>
        <LanguageSwitcher />
      </main>,
      { locale: 'ar' },
    );

    const button = screen.getByRole('button', { name: 'اللغة: العربية' });
    expect(button).toHaveAttribute('aria-expanded', 'false');
    expect(screen.queryByRole('link')).not.toBeInTheDocument();
    await user.click(button);
    expect(button).toHaveAttribute('aria-expanded', 'true');

    const list = screen.getByRole('list', { name: 'اللغة' });
    const links = within(list).getAllByRole('link');
    expect(
      links.map((link) => [
        link.textContent,
        link.getAttribute('href'),
        link.getAttribute('hreflang'),
        link.getAttribute('lang'),
      ]),
    ).toEqual([
      ['العربية', '/ar/shops/al-asala', 'ar', 'ar'],
      ['English', '/en/shops/al-asala', 'en', 'en'],
      ['Türkçe', '/tr/shops/al-asala', 'tr', 'tr'],
    ]);
    const current = within(list).getByRole('link', { name: 'العربية' });
    expect(current).toHaveAttribute('aria-current', 'true');
    expect(current).toHaveFocus();
    await expectNoAxeViolations();
  });

  it('closes on Escape and returns focus to the button', async () => {
    const user = userEvent.setup();
    setPathname('/tr/shops');
    renderWithIntl(<LanguageSwitcher />, { locale: 'tr' });

    const button = screen.getByRole('button', { name: /Türkçe/ });
    await user.click(button);
    expect(screen.getByRole('link', { name: 'Türkçe' })).toHaveAttribute('aria-current', 'true');
    expect(screen.getByRole('link', { name: 'English' })).toHaveAttribute('href', '/en/shops');
    await user.keyboard('{Escape}');
    expect(screen.queryByRole('list')).not.toBeInTheDocument();
    expect(button).toHaveAttribute('aria-expanded', 'false');
    expect(button).toHaveFocus();
  });
});
