import { act, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { expectNoAxeViolations } from '@/test/axe';
import { renderWithIntl } from '@/test/render';
import { THEME_CHANNEL, type ThemePreference } from '@/lib/theme/theme';
import { ThemeProvider } from './ThemeProvider';
import { ThemeToggle } from './ThemeSelector';
import { ThemeSettings } from './ThemeSettings';

const html = () => document.documentElement;
const meta = () => document.querySelector('meta[name="color-scheme"]');

function renderSelectors(initial: ThemePreference = 'system', locale: 'ar' | 'en' = 'en') {
  return renderWithIntl(
    <ThemeProvider initial={initial}>
      <main>
        <ThemeToggle />
        <ThemeSettings />
      </main>
    </ThemeProvider>,
    { locale },
  );
}

beforeEach(() => {
  html().removeAttribute('data-theme');
  document.cookie = 'trimme-theme=; Path=/; Max-Age=0';
  const tag = document.createElement('meta');
  tag.name = 'color-scheme';
  tag.content = 'light dark';
  document.head.append(tag);
});

afterEach(() => {
  meta()?.remove();
  Reflect.deleteProperty(window, 'matchMedia');
});

/** The device's theme under «تلقائي» (jsdom has no `matchMedia`). */
function deviceTheme(dark: boolean) {
  window.matchMedia = ((query: string) => ({
    matches: dark && query.includes('dark'),
    media: query,
  })) as never;
}

const toggle = (name = 'Switch between light and dark mode') => screen.getByRole('button', { name });
const settingChecked = (option: 'System' | 'Light' | 'Dark') =>
  within(screen.getByRole('group', { name: 'Theme' })).getByRole('radio', { name: option });

describe('theme switch and setting (D-124, D-156)', () => {
  it('the header switch has a fixed name in both languages and draws a sun and a moon for CSS to pick', () => {
    const { unmount } = renderSelectors('system', 'en');
    const button = toggle();
    // The `dark:` variant shows one of them, so «تلقائي» on a dark device paints the moon with no script.
    expect(button.querySelector('[data-icon="sun"]')).toHaveClass('dark:hidden');
    expect(button.querySelector('[data-icon="moon"]')).toHaveClass('hidden', 'dark:block');
    expect(button).not.toHaveAttribute('aria-expanded');
    unmount();
    renderSelectors('dark', 'ar');
    expect(toggle('تبديل الوضع النهاري والليلي')).toBeInTheDocument();
  });

  it('one press flips an explicit choice at once and saves it in a long-lived cookie', async () => {
    const user = userEvent.setup();
    renderSelectors('light');
    await user.click(toggle());

    expect(html()).toHaveAttribute('data-theme', 'dark');
    expect(meta()).toHaveAttribute('content', 'dark');
    expect(document.cookie).toContain('trimme-theme=dark');
    expect(settingChecked('Dark')).toBeChecked();
    expect(toggle()).toHaveFocus();

    await user.click(toggle());
    expect(html()).toHaveAttribute('data-theme', 'light');
    expect(settingChecked('Light')).toBeChecked();
    await expectNoAxeViolations();
  });

  it('under System it flips what the device shows: dark to Light, light to Dark', async () => {
    const user = userEvent.setup();
    deviceTheme(true);
    const { unmount } = renderSelectors('system');
    await user.click(toggle());
    expect(html()).toHaveAttribute('data-theme', 'light');
    unmount();

    deviceTheme(false);
    renderSelectors('system');
    await user.click(toggle());
    expect(html()).toHaveAttribute('data-theme', 'dark');
  });

  it('works from the keyboard (a native button: Enter and Space)', async () => {
    const user = userEvent.setup();
    renderSelectors('light');
    toggle().focus();
    await user.keyboard('{Enter}');
    expect(html()).toHaveAttribute('data-theme', 'dark');
    await user.keyboard(' ');
    expect(html()).toHaveAttribute('data-theme', 'light');
  });

  it('System removes the attribute so the stylesheet follows the OS (no script, no matchMedia)', async () => {
    const user = userEvent.setup();
    html().setAttribute('data-theme', 'dark');
    renderSelectors('dark');
    await user.click(screen.getByRole('radio', { name: 'System' }));

    expect(html()).not.toHaveAttribute('data-theme');
    expect(meta()).toHaveAttribute('content', 'light dark');
    expect(document.cookie).toContain('trimme-theme=system');
  });

  it('the account control is a labelled native radio group (arrow keys move the choice)', async () => {
    const user = userEvent.setup();
    renderSelectors('system');
    const group = screen.getByRole('group', { name: 'Theme' });
    const system = within(group).getByRole('radio', { name: 'System' });
    expect(system).toBeChecked();

    system.focus();
    await user.keyboard('{ArrowRight}');
    expect(within(group).getByRole('radio', { name: 'Light' })).toBeChecked();
    expect(html()).toHaveAttribute('data-theme', 'light');
    await expectNoAxeViolations();
  });

  it('follows a change made in another tab', async () => {
    renderSelectors('system');
    const otherTab = new BroadcastChannel(THEME_CHANNEL);
    otherTab.postMessage('dark');
    await waitFor(() => expect(html()).toHaveAttribute('data-theme', 'dark'));
    expect(settingChecked('Dark')).toBeChecked();
    // Garbage from the channel falls back to the default rather than breaking the page.
    otherTab.postMessage({ unexpected: true });
    await waitFor(() => expect(html()).not.toHaveAttribute('data-theme'));
    otherTab.close();
  });

  it('catches up with the cookie when the page comes back from the back/forward cache', () => {
    renderSelectors('light');
    document.cookie = 'trimme-theme=dark; Path=/';
    const event = new Event('pageshow');
    Object.defineProperty(event, 'persisted', { value: true });
    act(() => {
      window.dispatchEvent(event);
    });
    expect(html()).toHaveAttribute('data-theme', 'dark');
    expect(settingChecked('Dark')).toBeChecked();
  });
});
