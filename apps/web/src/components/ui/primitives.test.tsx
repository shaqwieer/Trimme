import { fireEvent, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '@/test/axe';
import { renderWithIntl } from '@/test/render';
import { Badge, BOOKING_STATUSES, StatusBadge, SUBSCRIPTION_STATUSES } from './Badge';
import { Button, ButtonLink, buttonClasses, IconButton } from './Button';
import { DIRECTIONAL_ICONS, designIcons, Icon } from './icons';
import { RatingStars } from './Rating';
import { RatingInput } from './RatingInput';
import { RadioCard } from './selection';
import { Chip, SegmentedControl } from './selection.client';

describe('icons (design ICONS → Lucide)', () => {
  it('maps all 46 design icons', () => {
    expect(Object.keys(designIcons)).toHaveLength(46);
  });

  it('mirrors directional icons in RTL only', () => {
    const { container } = renderWithIntl(
      <>
        <Icon name="chevR" />
        <Icon name="home" />
      </>,
    );
    const [chevron, home] = container.querySelectorAll('svg');
    expect(DIRECTIONAL_ICONS.has('chevR')).toBe(true);
    expect(chevron?.getAttribute('class')).toContain('rtl:-scale-x-100');
    expect(home?.getAttribute('class')).not.toContain('rtl:-scale-x-100');
  });

  it('hides decorative icons and names labelled ones', () => {
    renderWithIntl(<Icon name="shield" label="محل موثّق" />);
    expect(screen.getByRole('img', { name: 'محل موثّق' })).toBeInTheDocument();
  });
});

describe('Button', () => {
  it('uses the navy primary style with a 46px touch target by default', () => {
    expect(buttonClasses()).toContain('bg-navy-900');
    expect(buttonClasses()).toContain('min-h-[46px]');
  });

  it('keeps the font-size token and colour token together (no class-merge collisions)', () => {
    const classes = buttonClasses({ variant: 'primary', size: 'lg' }).split(' ');
    expect(classes).toContain('text-button');
    expect(classes).toContain('text-on-navy');
  });

  it('shows a busy, non-clickable state while loading', async () => {
    const onClick = vi.fn();
    renderWithIntl(
      <Button loading loadingText="جارٍ التأكيد" onClick={onClick}>
        احجز الآن
      </Button>,
    );
    const button = screen.getByRole('button', { name: 'جارٍ التأكيد' });
    expect(button).toHaveAttribute('aria-busy', 'true');
    expect(button).toBeDisabled();
    await userEvent.click(button);
    expect(onClick).not.toHaveBeenCalled();
  });

  it('renders navigation as a locale-aware link, not a button', () => {
    renderWithIntl(<ButtonLink href="/search">استكشف المحلات</ButtonLink>);
    expect(screen.getByRole('link', { name: 'استكشف المحلات' })).toHaveAttribute('href', '/ar/search');
  });

  it('requires a label on icon-only buttons', async () => {
    const { container } = renderWithIntl(
      <IconButton icon="heart" label="إضافة إلى المفضلة" pressed={false} />,
    );
    expect(screen.getByRole('button', { name: 'إضافة إلى المفضلة' })).toHaveAttribute(
      'aria-pressed',
      'false',
    );
    await expectNoAxeViolations(container);
  });
});

describe('StatusBadge (D-016 enums)', () => {
  it.each(BOOKING_STATUSES)('renders a labelled booking badge for %s', (status) => {
    renderWithIntl(<StatusBadge kind="booking" status={status} />);
    expect(screen.getByText(/.+/)).toBeInTheDocument();
  });

  it('labels the two cancellation states differently while sharing the danger colour', () => {
    const { container } = renderWithIntl(
      <>
        <StatusBadge kind="booking" status="CancelledByCustomer" />
        <StatusBadge kind="booking" status="CancelledByShop" />
      </>,
    );
    expect(screen.getByText('ألغاه العميل')).toBeInTheDocument();
    expect(screen.getByText('ألغاه المحل')).toBeInTheDocument();
    const badges = container.querySelectorAll('span.inline-flex');
    expect(badges[0]?.className).toBe(badges[1]?.className);
  });

  it.each(SUBSCRIPTION_STATUSES)('renders a subscription badge for %s in English', (status) => {
    renderWithIntl(<StatusBadge kind="subscription" status={status} />, { locale: 'en' });
    expect(screen.getByText(/Active|Expiring soon|Expired|Suspended/)).toBeInTheDocument();
  });

  it('carries a colour dot plus text (never colour alone)', () => {
    const { container } = renderWithIntl(<Badge tone="success">مكتمل</Badge>);
    expect(container.querySelector('[aria-hidden="true"]')).not.toBeNull();
    expect(screen.getByText('مكتمل')).toBeInTheDocument();
  });
});

describe('selection controls', () => {
  it('RadioCard is a native radio with its label', async () => {
    const onChange = vi.fn();
    renderWithIntl(
      <RadioCard name="service" value="beard" onChange={onChange}>
        تهذيب لحية
      </RadioCard>,
    );
    await userEvent.click(screen.getByRole('radio', { name: 'تهذيب لحية' }));
    expect(onChange).toHaveBeenCalled();
    expect(screen.getByRole('radio', { name: 'تهذيب لحية' })).toBeChecked();
  });

  it('Chip toggles aria-pressed', async () => {
    const onPressedChange = vi.fn();
    renderWithIntl(
      <Chip pressed={false} onPressedChange={onPressedChange}>
        مفتوح الآن
      </Chip>,
    );
    await userEvent.click(screen.getByRole('button', { name: 'مفتوح الآن', pressed: false }));
    expect(onPressedChange).toHaveBeenCalledWith(true);
  });

  it('SegmentedControl is a labelled radio group', async () => {
    const onValueChange = vi.fn();
    const { container } = renderWithIntl(
      <SegmentedControl
        legend="طريقة العرض"
        name="view"
        value="list"
        onValueChange={onValueChange}
        options={[
          { value: 'list', label: 'قائمة' },
          { value: 'map', label: 'خريطة' },
        ]}
      />,
    );
    expect(screen.getByRole('group', { name: 'طريقة العرض' })).toBeInTheDocument();
    await userEvent.click(screen.getByRole('radio', { name: 'خريطة' }));
    expect(onValueChange).toHaveBeenCalledWith('map');
    await expectNoAxeViolations(container);
  });
});

describe('Rating', () => {
  it('announces the rating once with Latin digits', () => {
    renderWithIntl(<RatingStars value={4.8} count={346} />);
    expect(screen.getByRole('img', { name: 'التقييم 4.8 من 5' })).toBeInTheDocument();
  });

  it('RatingInput exposes five labelled radios', () => {
    const onValueChange = vi.fn();
    renderWithIntl(<RatingInput name="rating" value={0} onValueChange={onValueChange} />, { locale: 'en' });
    expect(screen.getAllByRole('radio')).toHaveLength(5);
    fireEvent.click(screen.getByRole('radio', { name: '4 out of 5' }));
    expect(onValueChange).toHaveBeenCalledWith(4);
  });
});
