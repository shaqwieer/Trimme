import { act, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { expectNoAxeViolations } from '@/test/axe';
import { renderWithIntl } from '@/test/render';
import { THEME_CHANNEL, type ThemePreference } from '@/lib/theme/theme';
import { ThemeProvider } from './ThemeProvider';
import { ThemeMenu } from './ThemeSelector';
import { ThemeSettings } from './ThemeSettings';

const html = () => document.documentElement;
const meta = () => document.querySelector('meta[name="color-scheme"]');

function renderSelectors(initial: ThemePreference = 'system', locale: 'ar' | 'en' = 'en') {
  return renderWithIntl(
    <ThemeProvider initial={initial}>
      <main>
        <ThemeMenu />
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
});

describe('theme selector (D-124)', () => {
  it('names the header button after the current choice, in both languages', () => {
    const { unmount } = renderSelectors('system', 'en');
    expect(screen.getByRole('button', { name: 'Theme: System' })).toBeInTheDocument();
    unmount();
    renderSelectors('dark', 'ar');
    expect(screen.getByRole('button', { name: 'المظهر: داكن' })).toBeInTheDocument();
  });

  it('opens a labelled radio group from the keyboard with the current choice checked and focused', async () => {
    const user = userEvent.setup();
    renderSelectors('light');
    const trigger = screen.getByRole('button', { name: 'Theme: Light' });
    expect(trigger).toHaveAttribute('aria-expanded', 'false');
    trigger.focus();
    await user.keyboard('{Enter}');

    expect(trigger).toHaveAttribute('aria-expanded', 'true');
    const group = screen.getByRole('radiogroup', { name: 'Theme' });
    expect(trigger).toHaveAttribute('aria-controls', group.parentElement?.id);
    const radios = within(group).getAllByRole('radio');
    expect(radios.map((radio) => radio.closest('label')?.textContent)).toEqual(['System', 'Light', 'Dark']);
    expect(within(group).getByRole('radio', { name: 'Light' })).toBeChecked();
    expect(within(group).getByRole('radio', { name: 'Light' })).toHaveFocus();
    await expectNoAxeViolations();
  });

  it('arrow keys preview a choice, Escape closes and returns focus to the button', async () => {
    const user = userEvent.setup();
    renderSelectors('light');
    await user.click(screen.getByRole('button', { name: 'Theme: Light' }));
    await user.keyboard('{ArrowDown}');
    expect(html()).toHaveAttribute('data-theme', 'dark');
    expect(screen.getByRole('radiogroup', { name: 'Theme' })).toBeInTheDocument();

    await user.keyboard('{Escape}');
    expect(screen.queryByRole('radiogroup')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Theme: Dark' })).toHaveFocus();
  });

  it('a click outside closes it without changing the theme', async () => {
    const user = userEvent.setup();
    renderSelectors('system');
    await user.click(screen.getByRole('button', { name: 'Theme: System' }));
    await user.click(document.body);
    expect(screen.queryByRole('radiogroup')).not.toBeInTheDocument();
    expect(html()).not.toHaveAttribute('data-theme');
  });

  it('applies Dark at once, saves it in a long-lived cookie and updates every control', async () => {
    const user = userEvent.setup();
    renderSelectors('system');
    await user.click(screen.getByRole('button', { name: 'Theme: System' }));
    // Click the visible option text, as a user does (the radio itself is visually hidden).
    await user.click(within(screen.getByRole('radiogroup', { name: 'Theme' })).getByText('Dark'));

    expect(html()).toHaveAttribute('data-theme', 'dark');
    expect(meta()).toHaveAttribute('content', 'dark');
    expect(document.cookie).toContain('trimme-theme=dark');
    // A pointer choice closes the panel and hands focus back to the button.
    expect(screen.queryByRole('radiogroup')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Theme: Dark' })).toHaveFocus();
    expect(
      within(screen.getByRole('group', { name: 'Theme' })).getByRole('radio', { name: 'Dark' }),
    ).toBeChecked();
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
    expect(screen.getByRole('button', { name: 'Theme: Dark' })).toBeInTheDocument();
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
    expect(screen.getByRole('button', { name: 'Theme: Dark' })).toBeInTheDocument();
  });
});
