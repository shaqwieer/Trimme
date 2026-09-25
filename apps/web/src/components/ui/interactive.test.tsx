import { act, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { addDays, formatLocalDate, monthDays, shiftMonth, todayLocal, weekday } from '@/lib/i18n/localDate';
import { expectNoAxeViolations } from '@/test/axe';
import { renderWithIntl } from '@/test/render';
import { CalendarMonth, DateStrip, SlotGrid, Stepper } from './booking';
import { Button } from './Button';
import { ConfirmDialog, Dialog, DropdownMenu } from './overlays';
import { RangeSlider } from './RangeSlider';
import { Tabs } from './Tabs';
import { ToastProvider, useToast } from './Toast';

describe('local dates (shop time zone, no Date shifting)', () => {
  it('computes weekdays and month shapes for September 2026', () => {
    expect(weekday('2026-09-01')).toBe(2); // Tuesday
    expect(weekday('2026-09-18')).toBe(5); // Friday
    expect(monthDays('2026-09')).toHaveLength(30);
    expect(addDays('2026-09-30', 1)).toBe('2026-10-01');
    expect(shiftMonth('2026-12', 1)).toBe('2027-01');
  });

  it('resolves "today" in Asia/Riyadh, not UTC', () => {
    expect(todayLocal('Asia/Riyadh', new Date('2026-09-17T22:30:00Z'))).toBe('2026-09-18');
  });

  it('formats a local date without drifting a day', () => {
    expect(formatLocalDate('2026-09-18', 'en', { weekday: 'long', day: 'numeric', month: 'long' })).toBe(
      'Friday 18 September',
    );
  });
});

describe('booking controls', () => {
  it('DateStrip disables closed days and selects bookable ones', async () => {
    const onValueChange = vi.fn();
    renderWithIntl(
      <DateStrip
        name="date"
        today="2026-09-18"
        onValueChange={onValueChange}
        days={[
          { date: '2026-09-18', available: true },
          { date: '2026-09-19', available: false },
        ]}
      />,
    );
    expect(screen.getByRole('radio', { name: /السبت.*مغلق/ })).toBeDisabled();
    await userEvent.click(screen.getByRole('radio', { name: /الجمعة/ }));
    expect(onValueChange).toHaveBeenCalledWith('2026-09-18');
  });

  it('SlotGrid groups bookable slots into Riyadh periods and shows no disabled slots (D-009)', async () => {
    const onValueChange = vi.fn();
    const { container } = renderWithIntl(
      <SlotGrid
        name="slot"
        onValueChange={onValueChange}
        slots={[
          { start: '2026-09-18T06:00:00Z' }, // 9:00 am Riyadh
          { start: '2026-09-18T10:05:00Z' }, // 1:05 pm
          { start: '2026-09-18T14:30:00Z' }, // 5:30 pm
        ]}
      />,
    );
    expect(screen.getByRole('group', { name: 'صباحاً' })).toBeInTheDocument();
    expect(screen.getByRole('group', { name: 'ظهراً' })).toBeInTheDocument();
    const evening = screen.getByRole('group', { name: 'مساءً' });
    await userEvent.click(within(evening).getByRole('radio', { name: '٥:٣٠ م' }));
    expect(onValueChange).toHaveBeenCalledWith('2026-09-18T14:30:00Z');
    expect(screen.getAllByRole('radio').every((radio) => !(radio as HTMLInputElement).disabled)).toBe(true);
    await expectNoAxeViolations(container);
  });

  it('SlotGrid explains an empty day', () => {
    renderWithIntl(<SlotGrid name="slot" slots={[]} onValueChange={() => {}} />);
    expect(screen.getByRole('status')).toHaveTextContent('لا أوقات متاحة في هذا اليوم');
  });

  it('Stepper marks the current step', () => {
    renderWithIntl(<Stepper steps={['الخدمة', 'الحلاق', 'التاريخ', 'الوقت', 'المراجعة']} current={2} />);
    expect(screen.getByText('الخطوة 3 من 5')).toBeInTheDocument();
    expect(screen.getByText('التاريخ').closest('li')).toHaveAttribute('aria-current', 'step');
  });

  it('CalendarMonth starts weeks on Sunday, disables unavailable days and labels navigation', async () => {
    const onValueChange = vi.fn();
    const onMonthChange = vi.fn();
    renderWithIntl(
      <CalendarMonth
        month="2026-09"
        onMonthChange={onMonthChange}
        availableDates={new Set(['2026-09-18', '2026-09-20'])}
        closedDates={new Set(['2026-09-19'])}
        value="2026-09-18"
        onValueChange={onValueChange}
      />,
      { locale: 'en' },
    );
    expect(screen.getByTitle('Sunday')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Saturday 19 September — Closed' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Friday 18 September' })).toHaveAttribute(
      'aria-pressed',
      'true',
    );
    await userEvent.click(screen.getByRole('button', { name: 'Sunday 20 September' }));
    expect(onValueChange).toHaveBeenCalledWith('2026-09-20');
    await userEvent.click(screen.getByRole('button', { name: 'Next month' }));
    expect(onMonthChange).toHaveBeenCalledWith('2026-10');
  });
});

describe('Radix primitives follow the reading direction (D-048)', () => {
  const items = [
    { value: 'services', label: 'الخدمات', content: 'خدمات' },
    { value: 'pros', label: 'الحلاقون', content: 'حلاقون' },
  ];

  it('Tabs: ArrowLeft moves forward in Arabic', async () => {
    renderWithIntl(<Tabs label="أقسام المحل" items={items} />);
    await userEvent.click(screen.getByRole('tab', { name: 'الخدمات' }));
    await userEvent.keyboard('{ArrowLeft}');
    expect(screen.getByRole('tab', { name: 'الحلاقون' })).toHaveFocus();
  });

  it('Tabs: ArrowRight moves forward in English', async () => {
    renderWithIntl(<Tabs label="Shop sections" items={items} />, { locale: 'en' });
    await userEvent.click(screen.getByRole('tab', { name: 'الخدمات' }));
    await userEvent.keyboard('{ArrowRight}');
    expect(screen.getByRole('tab', { name: 'الحلاقون' })).toHaveFocus();
  });

  it('RangeSlider thumbs are labelled with formatted values', () => {
    renderWithIntl(
      <RangeSlider
        label="نطاق السعر"
        min={25}
        max={200}
        value={[25, 90]}
        onValueChange={() => {}}
        thumbLabels={['أقل سعر', 'أعلى سعر']}
        format={(v) => `${v} ر.س`}
      />,
    );
    const [low, high] = screen.getAllByRole('slider');
    expect(low).toHaveAccessibleName('أقل سعر');
    expect(high).toHaveAttribute('aria-valuetext', '90 ر.س');
  });
});

describe('overlays', () => {
  it('Dialog opens from its trigger, is named by its title and returns focus on Escape', async () => {
    renderWithIntl(
      <Dialog title="تفاصيل الموعد" trigger={<Button>عرض</Button>}>
        <p>المحتوى</p>
      </Dialog>,
    );
    const trigger = screen.getByRole('button', { name: 'عرض' });
    await userEvent.click(trigger);
    expect(screen.getByRole('dialog', { name: 'تفاصيل الموعد' })).toBeInTheDocument();
    await userEvent.keyboard('{Escape}');
    await waitFor(() => expect(trigger).toHaveFocus());
  });

  it('ConfirmDialog is an alertdialog that focuses the safe choice first', async () => {
    const onConfirm = vi.fn();
    renderWithIntl(
      <ConfirmDialog
        open
        onOpenChange={() => {}}
        title="إلغاء موعد الجمعة ٥:٣٠ م؟"
        body="سيُخطر المحل فوراً."
        confirmLabel="نعم، ألغِ الموعد"
        onConfirm={onConfirm}
      />,
    );
    const dialog = screen.getByRole('alertdialog', { name: 'إلغاء موعد الجمعة ٥:٣٠ م؟' });
    await waitFor(() => expect(within(dialog).getByRole('button', { name: 'تراجع' })).toHaveFocus());
    await userEvent.click(within(dialog).getByRole('button', { name: 'نعم، ألغِ الموعد' }));
    expect(onConfirm).toHaveBeenCalled();
  });

  it('DropdownMenu lists actions without any transfer entry', async () => {
    const onSelect = vi.fn();
    renderWithIntl(
      <DropdownMenu
        trigger={<Button>المزيد</Button>}
        items={[
          { label: 'تأكيد الموعد', icon: 'check', onSelect },
          { label: 'إعادة جدولة', icon: 'calendar', onSelect },
          { type: 'separator' },
          { label: 'إلغاء', icon: 'ban', onSelect, destructive: true },
        ]}
      />,
    );
    await userEvent.click(screen.getByRole('button', { name: 'المزيد' }));
    // Radix names the menu after its trigger (aria-labelledby).
    const menu = screen.getByRole('menu', { name: 'المزيد' });
    expect(
      within(menu)
        .getAllByRole('menuitem')
        .map((i) => i.textContent),
    ).toEqual(['تأكيد الموعد', 'إعادة جدولة', 'إلغاء']);
    await userEvent.click(within(menu).getByRole('menuitem', { name: 'تأكيد الموعد' }));
    expect(onSelect).toHaveBeenCalled();
  });
});

describe('Toast', () => {
  function Trigger({ tone, withAction = false }: { tone?: 'success' | 'error'; withAction?: boolean }) {
    const { show } = useToast();
    const [count, setCount] = useState(0);
    return (
      <button
        type="button"
        onClick={() => {
          setCount(count + 1);
          show({
            title: `رسالة ${count + 1}`,
            tone,
            action: withAction ? { label: 'تراجع', onClick: () => {} } : undefined,
          });
        }}
      >
        إظهار
      </button>
    );
  }

  it('announces success politely and auto-dismisses', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
    renderWithIntl(
      <ToastProvider>
        <Trigger />
      </ToastProvider>,
    );
    await user.click(screen.getByRole('button', { name: 'إظهار' }));
    expect(screen.getByRole('status')).toHaveTextContent('رسالة 1');
    act(() => {
      vi.advanceTimersByTime(5100);
    });
    expect(screen.getByRole('status')).not.toHaveTextContent('رسالة 1');
    vi.useRealTimers();
  });

  it('announces errors assertively and never auto-dismisses toasts with an action', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
    renderWithIntl(
      <ToastProvider>
        <Trigger tone="error" withAction />
      </ToastProvider>,
    );
    await user.click(screen.getByRole('button', { name: 'إظهار' }));
    act(() => {
      vi.advanceTimersByTime(20_000);
    });
    expect(screen.getByRole('alert')).toHaveTextContent('رسالة 1');
    vi.useRealTimers();
  });
});
