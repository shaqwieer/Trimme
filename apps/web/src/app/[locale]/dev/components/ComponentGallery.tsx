'use client';

import { type ReactNode, useState } from 'react';
import { Avatar } from '@/components/ui/Avatar';
import { Badge, BOOKING_STATUSES, StatusBadge, SUBSCRIPTION_STATUSES } from '@/components/ui/Badge';
import { CalendarMonth, DateStrip, SlotGrid, Stepper } from '@/components/ui/booking';
import { Button, ButtonLink, IconButton } from '@/components/ui/Button';
import { AppointmentCard, KpiTile, ProfessionalOption, ServiceOption, ShopCard } from '@/components/ui/cards';
import { BarChart, QrCard, RatingDistribution } from '@/components/ui/charts';
import { Breadcrumb, Pagination, ResponsiveTable, Timeline } from '@/components/ui/data';
import {
  Checkbox,
  OtpField,
  PhoneField,
  SearchField,
  SelectField,
  Switch,
  TextareaField,
  TextField,
} from '@/components/ui/inputs';
import { LinkTabs } from '@/components/ui/LinkTabs';
import { ConfirmDialog, Dialog, DropdownMenu, Sheet, Tooltip } from '@/components/ui/overlays';
import { RangeSlider } from '@/components/ui/RangeSlider';
import { RatingInput, RatingStars } from '@/components/ui/Rating';
import { AddChip, Chip, RemovableChip, SegmentedControl, TagChip } from '@/components/ui/selection';
import {
  EmptyState,
  ErrorState,
  ExpiredSession,
  InlineAlert,
  PermissionDenied,
  SkeletonList,
} from '@/components/ui/states';
import { Tabs } from '@/components/ui/Tabs';
import { ToastProvider, useToast } from '@/components/ui/Toast';
import { UploadDropZone } from '@/components/ui/UploadDropZone';
import { formatPrice } from '@/lib/i18n/format';

type Locale = 'ar' | 'en';

/** Section card: white, radius 16, padding 22, E1 — like the design's component board. */
function Section({
  title,
  eyebrow,
  children,
  wide = false,
}: {
  title: string;
  eyebrow: string;
  children: ReactNode;
  wide?: boolean;
}) {
  return (
    <section
      className={`flex min-w-0 flex-col gap-4 rounded-section bg-surface p-[22px] shadow-e1 ${wide ? 'md:col-span-2 xl:col-span-3' : ''}`}
    >
      <header className="flex items-center justify-between gap-2">
        <h2 className="text-h3 font-bold text-text-primary">{title}</h2>
        <span className="font-latin text-[0.625rem] font-bold tracking-[0.14em] text-text-tertiary">
          {eyebrow}
        </span>
      </header>
      {children}
    </section>
  );
}

export function ComponentGallery({ locale }: { locale: Locale }) {
  return (
    <ToastProvider>
      <Gallery locale={locale} />
    </ToastProvider>
  );
}

function Gallery({ locale }: { locale: Locale }) {
  const tx = (ar: string, en: string) => (locale === 'ar' ? ar : en);
  const { show } = useToast();
  const [phone, setPhone] = useState('');
  const [search, setSearch] = useState('');
  const [openNow, setOpenNow] = useState(true);
  const [view, setView] = useState('list');
  const [service, setService] = useState('s3');
  const [pro, setPro] = useState('p2');
  const [date, setDate] = useState('2026-09-18');
  const [slot, setSlot] = useState('2026-09-18T14:30:00Z');
  const [month, setMonth] = useState('2026-09');
  const [price, setPrice] = useState<[number, number]>([25, 90]);
  const [rating, setRating] = useState(4);
  const [sunday, setSunday] = useState(true);
  const [saturday, setSaturday] = useState(false);
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [sheetOpen, setSheetOpen] = useState(false);
  const [breaks, setBreaks] = useState([
    tx('صلاة العصر · ٣:٣٠ — ٤:٠٠', 'Asr prayer · 3:30 — 4:00'),
    tx('غداء · ١:٠٠ — ١:٤٥', 'Lunch · 1:00 — 1:45'),
  ]);

  const shopRows = [
    {
      id: '1',
      name: tx('صالون الأصالة', 'Al Asala Salon'),
      district: tx('الملقا', 'Al Malqa'),
      pros: 6,
      status: 'Active' as const,
    },
    {
      id: '2',
      name: tx('باربر هاوس', 'Barber House'),
      district: tx('حطين', 'Hittin'),
      pros: 4,
      status: 'ExpiringSoon' as const,
    },
    {
      id: '3',
      name: tx('لمسة الرجل', 'Gentleman Touch'),
      district: tx('النرجس', 'An Narjis'),
      pros: 3,
      status: 'Expired' as const,
    },
  ];

  return (
    <div className="mx-auto flex max-w-[1280px] flex-col gap-6 px-4 py-8 md:px-6">
      <header className="flex flex-col gap-1">
        <p className="font-latin text-eyebrow font-bold tracking-[0.16em] text-text-tertiary">
          TRIMME · DESIGN SYSTEM
        </p>
        <h1 className="text-h1 font-bold text-navy-900">{tx('مكتبة المكونات', 'Component library')}</h1>
      </header>

      <div className="grid grid-cols-1 gap-5 md:grid-cols-2 xl:grid-cols-3">
        <Section title={tx('الأزرار', 'Buttons')} eyebrow="BUTTON">
          <div className="flex flex-wrap gap-2.5">
            <Button>{tx('احجز الآن', 'Book now')}</Button>
            <Button variant="secondary">{tx('ثانوي', 'Secondary')}</Button>
            <Button variant="outline">{tx('محدد', 'Outline')}</Button>
            <Button variant="danger">{tx('إلغاء الموعد', 'Cancel booking')}</Button>
            <Button loading loadingText={tx('جارٍ التأكيد', 'Confirming…')}>
              {tx('تأكيد', 'Confirm')}
            </Button>
            <Button disabled>{tx('معطّل', 'Disabled')}</Button>
            <IconButton
              icon="heart"
              label={tx('إضافة إلى المفضلة', 'Add to favorites')}
              variant="outline"
              size="lg"
            />
            <IconButton icon="msg" label={tx('مراسلة', 'Message')} variant="secondary" size="lg" />
            <ButtonLink href="/search" variant="outline" icon="search">
              {tx('استكشف المحلات', 'Explore shops')}
            </ButtonLink>
          </div>
        </Section>

        <Section title={tx('الحقول والتحقق', 'Inputs & validation')} eyebrow="INPUT">
          <TextField
            label={tx('الاسم الكامل', 'Full name')}
            defaultValue={tx('عبدالله الشمري', 'Abdullah Alshammari')}
            valid
          />
          <PhoneField
            label={tx('رقم الجوال', 'Mobile number')}
            helper={tx(
              'يُستخدم لإرسال تأكيد الموعد عبر واتساب',
              'Used to send the booking confirmation on WhatsApp',
            )}
            value={phone}
            onValueChange={(national) => setPhone(national)}
          />
          <OtpField
            label={tx('رمز التحقق', 'Verification code')}
            error={tx('الرمز غير صحيح — تبقى محاولتان', 'Incorrect code — 2 attempts left')}
          />
          <div className="grid grid-cols-[1fr_auto] items-end gap-2">
            <SearchField
              label={tx('بحث', 'Search')}
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              onClear={() => setSearch('')}
            />
            <SelectField label={tx('الترتيب', 'Sort')} hideLabel>
              <option>{tx('الأقرب', 'Nearest')}</option>
              <option>{tx('الأعلى تقييماً', 'Top rated')}</option>
            </SelectField>
          </div>
          <TextareaField
            label={tx('ملاحظة للحلاق', 'Note for the barber')}
            optional
            placeholder={tx('مثال: تدريج قصير من الجانبين', 'e.g. short fade on the sides')}
          />
          <Checkbox label={tx('أوافق على شروط الاستخدام', 'I accept the terms of use')} />
        </Section>

        <Section title={tx('الحالات والشارات', 'Statuses & badges')} eyebrow="BADGE">
          <div className="flex flex-wrap gap-2">
            {BOOKING_STATUSES.map((status) => (
              <StatusBadge key={status} kind="booking" status={status} />
            ))}
          </div>
          <div className="flex flex-wrap gap-2">
            {SUBSCRIPTION_STATUSES.map((status) => (
              <StatusBadge key={status} kind="subscription" status={status} />
            ))}
          </div>
          <div className="flex flex-wrap gap-2">
            <TagChip>{tx('حلاقة', 'Haircut')}</TagChip>
            <TagChip>{tx('لحية', 'Beard')}</TagChip>
            <Badge tone="warning" dot={false}>
              {tx('في إجازة', 'On leave')}
            </Badge>
          </div>
          <InlineAlert
            tone="info"
            title={tx(
              'كل شارة تحمل نقطة لون + نصاً صريحاً',
              'Every badge has a colour dot and explicit text',
            )}
          />
        </Section>

        <Section title={tx('التبويبات والقوائم', 'Tabs & menus')} eyebrow="TABS · MENU">
          <SegmentedControl
            legend={tx('المواعيد', 'Appointments')}
            name="appointments-tab"
            value={view}
            onValueChange={setView}
            options={[
              { value: 'list', label: tx('قادمة', 'Upcoming') },
              { value: 'past', label: tx('سابقة', 'Past') },
              { value: 'cancelled', label: tx('ملغاة', 'Cancelled') },
            ]}
          />
          <Tabs
            label={tx('أقسام المحل', 'Shop sections')}
            items={[
              {
                value: 'services',
                label: tx('الخدمات', 'Services'),
                content: (
                  <p className="text-caption text-text-secondary">
                    {tx('قائمة خدمات المحل وأسعاره', 'The shop’s own services and prices')}
                  </p>
                ),
              },
              {
                value: 'pros',
                label: tx('الحلاقون', 'Barbers'),
                content: (
                  <p className="text-caption text-text-secondary">
                    {tx('حلاقو المحل', 'The shop’s barbers')}
                  </p>
                ),
              },
              {
                value: 'reviews',
                label: tx('التقييمات', 'Reviews'),
                content: (
                  <p className="text-caption text-text-secondary">{tx('آراء العملاء', 'Customer reviews')}</p>
                ),
              },
            ]}
          />
          <LinkTabs
            label={tx('روابط الأقسام', 'Section links')}
            tabs={[
              { href: '/dev/components?tab=about', label: tx('عن المحل', 'About'), active: true },
              { href: '/dev/components?tab=map', label: tx('الموقع', 'Location'), active: false },
            ]}
          />
          <DropdownMenu
            trigger={
              <IconButton icon="more" label={tx('خيارات الموعد', 'Appointment options')} variant="outline" />
            }
            items={[
              {
                label: tx('تأكيد الموعد', 'Confirm'),
                icon: 'check',
                onSelect: () => show({ title: tx('تم التأكيد', 'Confirmed') }),
              },
              { label: tx('إعادة جدولة', 'Reschedule'), icon: 'calendar', onSelect: () => {} },
              { type: 'separator' },
              {
                label: tx('إلغاء', 'Cancel'),
                icon: 'ban',
                destructive: true,
                onSelect: () => setConfirmOpen(true),
              },
            ]}
          />
        </Section>

        <Section title={tx('التقويم ومنتقي التاريخ', 'Calendar & date picker')} eyebrow="DATE PICKER">
          <CalendarMonth
            month={month}
            onMonthChange={setMonth}
            availableDates={
              new Set([
                '2026-09-15',
                '2026-09-16',
                '2026-09-17',
                '2026-09-18',
                '2026-09-20',
                '2026-09-21',
                '2026-09-22',
                '2026-09-23',
                '2026-09-24',
                '2026-09-25',
                '2026-09-27',
                '2026-09-28',
                '2026-09-29',
                '2026-09-30',
              ])
            }
            closedDates={new Set(['2026-09-19', '2026-09-26'])}
            value={date}
            onValueChange={setDate}
          />
          <DateStrip
            name="gallery-date"
            today="2026-09-18"
            value={date}
            onValueChange={setDate}
            days={[
              '2026-09-18',
              '2026-09-19',
              '2026-09-20',
              '2026-09-21',
              '2026-09-22',
              '2026-09-23',
              '2026-09-24',
            ].map((d) => ({ date: d, available: d !== '2026-09-19' }))}
          />
        </Section>

        <Section title={tx('منتقي الأوقات', 'Time slots')} eyebrow="TIME SLOTS">
          <Stepper
            steps={[
              tx('الخدمة', 'Service'),
              tx('الحلاق', 'Barber'),
              tx('التاريخ', 'Date'),
              tx('الوقت', 'Time'),
              tx('المراجعة', 'Review'),
            ]}
            current={3}
          />
          <SlotGrid
            name="gallery-slot"
            value={slot}
            onValueChange={setSlot}
            slots={[
              '06:00',
              '06:15',
              '06:30',
              '07:05',
              '08:00',
              '10:05',
              '13:00',
              '13:30',
              '14:30',
              '15:30',
              '17:05',
            ].map((t) => ({ start: `2026-09-18T${t}:00Z` }))}
          />
        </Section>

        <Section title={tx('بطاقات الخدمة والحلاق', 'Service & barber cards')} eyebrow="CARDS">
          <div className="flex flex-col gap-2.5" role="radiogroup" aria-label={tx('الخدمة', 'Service')}>
            <ServiceOption
              name="gallery-service"
              value="s3"
              checked={service === 's3'}
              onChange={() => setService('s3')}
              title={tx('باقة شعر ولحية', 'Hair & beard package')}
              description={tx('قص + تشذيب + تهذيب وترتيب اللحية', 'Cut, trim and beard shaping')}
              price={85}
              durationMinutes={50}
            />
            <ServiceOption
              name="gallery-service"
              value="s4"
              checked={service === 's4'}
              onChange={() => setService('s4')}
              title={tx('عناية بالوجه', 'Facial care')}
              description={tx('تنظيف عميق وماسك مرطب', 'Deep cleanse and hydrating mask')}
              price={70}
              durationMinutes={40}
            />
          </div>
          <div className="grid grid-cols-2 gap-2.5" role="radiogroup" aria-label={tx('الحلاق', 'Barber')}>
            <ProfessionalOption
              name="gallery-pro"
              value="p1"
              checked={pro === 'p1'}
              onChange={() => setPro('p1')}
              displayName={tx('فيصل القحطاني', 'Faisal Alqahtani')}
              specialty={tx('تدريج وفيد', 'Fades')}
              rating={4.9}
              reviewCount={212}
            />
            <ProfessionalOption
              name="gallery-pro"
              value="p2"
              checked={pro === 'p2'}
              onChange={() => setPro('p2')}
              displayName={tx('ماجد العتيبي', 'Majed Alotaibi')}
              specialty={tx('حلاقة كلاسيك', 'Classic')}
              unavailable
            />
          </div>
        </Section>

        <Section title={tx('بطاقة المحل والموعد', 'Shop & appointment cards')} eyebrow="SHOP · APPOINTMENT">
          <ShopCard
            shop={{
              href: '/shops/al-asala',
              name: tx('صالون الأصالة للحلاقة', 'Al Asala Barbers'),
              verified: true,
              rating: 4.8,
              reviewCount: 346,
              district: tx('حي الملقا', 'Al Malqa'),
              distanceKm: 2.4,
              openingLabel: tx('مفتوح حتى ١١:٠٠ م', 'Open until 11:00 pm'),
              isOpen: true,
              fromPrice: 35,
              tags: [tx('حلاقة', 'Haircut'), tx('لحية', 'Beard'), tx('عناية بالوجه', 'Facial')],
            }}
            favorite={
              <IconButton
                icon="heart"
                label={tx('إضافة إلى المفضلة', 'Add to favorites')}
                variant="outline"
                size="sm"
                pressed={false}
              />
            }
          />
          <AppointmentCard
            appointment={{
              href: '/account/bookings/demo',
              dayNumber: '18',
              monthLabel: tx('سبتمبر', 'Sep'),
              serviceName: tx('باقة شعر ولحية', 'Hair & beard package'),
              status: 'Confirmed',
              shopName: tx('صالون الأصالة', 'Al Asala'),
              professionalName: tx('فيصل القحطاني', 'Faisal Alqahtani'),
              timeRange: tx('٥:٣٠ م — ٦:٢٠ م', '5:30 pm — 6:20 pm'),
            }}
          />
        </Section>

        <Section title={tx('التقييم والمراجعة', 'Rating & review')} eyebrow="RATING">
          <div className="flex items-center gap-3">
            <span className="font-latin text-[2rem] font-bold text-navy-900">4.8</span>
            <RatingStars value={4.8} count={346} showValue={false} size="md" />
          </div>
          <RatingDistribution counts={{ 5: 268, 4: 52, 3: 15, 2: 7, 1: 4 }} />
          <div className="flex items-start gap-3 border-t border-border-subtle pt-3">
            <Avatar name={tx('محمد العنزي', 'Mohammed Alanazi')} />
            <div className="flex flex-col gap-1">
              <span className="text-[0.875rem] font-bold">{tx('محمد ع.', 'Mohammed A.')}</span>
              <RatingStars value={5} showValue={false} />
              <p className="text-caption text-text-strong">
                {tx('التزام دقيق بالموعد، والتدريج نظيف جداً.', 'Right on time and a very clean fade.')}
              </p>
            </div>
          </div>
          <RatingInput name="gallery-rating" value={rating} onValueChange={setRating} />
        </Section>

        <Section title={tx('مؤشرات ورسوم', 'KPIs & charts')} eyebrow="KPI">
          <div className="grid grid-cols-2 gap-2.5">
            <KpiTile
              label={tx('مواعيد اليوم', 'Today’s bookings')}
              value="34"
              icon="calendar"
              delta={{ text: tx('+12% عن أمس', '+12% vs yesterday'), tone: 'good' }}
            />
            <KpiTile
              label={tx('معدل عدم الحضور', 'No-show rate')}
              value="6.2%"
              icon="ban"
              delta={{ text: tx('+1.4 نقطة', '+1.4 pts'), tone: 'bad' }}
            />
          </div>
          <BarChart
            title={tx('الحجوزات حسب ساعة اليوم', 'Bookings by hour of day')}
            subtitle={tx('آخر ٧ أيام', 'Last 7 days')}
            labelHeader={tx('الساعة', 'Hour')}
            valueHeader={tx('الحجوزات', 'Bookings')}
            data={(
              [
                ['9', 22],
                ['10', 34],
                ['11', 41],
                ['12', 28],
                ['1', 18],
                ['2', 24],
                ['3', 46],
                ['4', 62],
                ['5', 88],
                ['6', 96],
                ['7', 74],
                ['8', 52],
              ] as const
            ).map(([label, value]) => ({ label, value }))}
          />
          <RangeSlider
            label={tx('نطاق السعر', 'Price range')}
            min={25}
            max={200}
            value={price}
            onValueChange={setPrice}
            thumbLabels={[tx('أقل سعر', 'Minimum price'), tx('أعلى سعر', 'Maximum price')]}
            format={(value) => formatPrice(value, locale)}
          />
        </Section>

        <Section title={tx('رمز QR والصور', 'QR & images')} eyebrow="QR · UPLOAD">
          <div className="flex items-center gap-3">
            <Avatar name={tx('صالون الأصالة', 'Al Asala')} size="xl" />
            <Avatar name="Faisal Alqahtani" size="lg" />
          </div>
          <QrCard
            name={tx('صالون الأصالة — الملقا', 'Al Asala — Al Malqa')}
            url="trimme.sa/s/alasalah-malqa"
            downloadHref="/dev/components"
            share={
              <Button size="xs" variant="outline">
                {tx('مشاركة', 'Share')}
              </Button>
            }
          />
          <UploadDropZone
            label={tx('صورة الغلاف', 'Cover image')}
            onFileSelected={(file) => show({ title: file.name, tone: 'info' })}
          />
        </Section>

        <Section title={tx('محرر الدوام والاستراحات', 'Hours & breaks')} eyebrow="HOURS EDITOR">
          <div className="flex flex-col divide-y divide-border-row">
            <div className="flex items-center justify-between py-1">
              <Switch checked={sunday} onCheckedChange={setSunday} label={tx('الأحد', 'Sunday')} />
              <span className="text-caption text-text-strong">
                {tx('٩:٠٠ ص — ١١:٠٠ م', '9:00 am — 11:00 pm')}
              </span>
            </div>
            <div className="flex items-center justify-between py-1">
              <Switch checked={saturday} onCheckedChange={setSaturday} label={tx('السبت', 'Saturday')} />
              <span className="text-caption text-text-tertiary">{tx('مغلق', 'Closed')}</span>
            </div>
          </div>
          <div className="flex flex-wrap items-center gap-2 rounded-button bg-bg-page p-3.5">
            {breaks.map((item) => (
              <RemovableChip
                key={item}
                removeLabel={tx(`حذف ${item}`, `Remove ${item}`)}
                onRemove={() => setBreaks(breaks.filter((b) => b !== item))}
              >
                {item}
              </RemovableChip>
            ))}
            <AddChip
              onClick={() => setBreaks([...breaks, tx('استراحة · ٦:٠٥ — ٦:٢٥', 'Break · 6:05 — 6:25')])}
            >
              {tx('إضافة', 'Add')}
            </AddChip>
          </div>
          <div className="flex flex-wrap gap-2">
            <Chip pressed={openNow} onPressedChange={setOpenNow} icon="clock">
              {tx('مفتوح الآن', 'Open now')}
            </Chip>
            <Chip>{tx('الأقرب', 'Nearest')}</Chip>
          </div>
        </Section>

        <Section title={tx('التنبيهات والرسائل', 'Toasts & dialogs')} eyebrow="TOAST · TOOLTIP · DIALOG">
          <div className="flex flex-wrap gap-2.5">
            <Button
              size="sm"
              onClick={() =>
                show({
                  title: tx(
                    'تم تأكيد موعدك وأُرسلت رسالة واتساب',
                    'Your booking is confirmed and a WhatsApp message was sent',
                  ),
                })
              }
            >
              {tx('إظهار تنبيه', 'Show toast')}
            </Button>
            <Button
              size="sm"
              variant="danger"
              onClick={() =>
                show({ title: tx('تعذّر الحجز — أعد المحاولة', 'Booking failed — try again'), tone: 'error' })
              }
            >
              {tx('تنبيه خطأ', 'Error toast')}
            </Button>
            <Tooltip content={tx('هذا الوقت داخل استراحة الحلاق', 'This time is within the barber’s break')}>
              <Button size="sm" variant="outline">
                {tx('تلميح', 'Tooltip')}
              </Button>
            </Tooltip>
            <Dialog
              title={tx('تفاصيل الموعد', 'Booking details')}
              description={tx('باقة شعر ولحية · الجمعة ٥:٣٠ م', 'Hair & beard package · Friday 5:30 pm')}
              trigger={
                <Button size="sm" variant="secondary">
                  {tx('نافذة', 'Dialog')}
                </Button>
              }
            >
              <p className="text-caption text-text-secondary">
                {tx('يُدفع المبلغ في المحل.', 'Paid at the shop.')}
              </p>
            </Dialog>
            <Button size="sm" variant="outline" onClick={() => setSheetOpen(true)}>
              {tx('درج جانبي', 'Sheet')}
            </Button>
            <Button size="sm" variant="danger" onClick={() => setConfirmOpen(true)}>
              {tx('تأكيد الإلغاء', 'Confirm cancel')}
            </Button>
          </div>
          <ConfirmDialog
            open={confirmOpen}
            onOpenChange={setConfirmOpen}
            title={tx('إلغاء موعد الجمعة ٥:٣٠ م؟', 'Cancel Friday’s 5:30 pm booking?')}
            body={tx(
              'سيُخطر المحل فوراً ويُتاح الوقت لعملاء آخرين. لا يمكن التراجع عن هذا الإجراء.',
              'The shop is notified immediately and the time is released. This cannot be undone.',
            )}
            confirmLabel={tx('نعم، ألغِ الموعد', 'Yes, cancel it')}
            onConfirm={() => setConfirmOpen(false)}
          />
          <Sheet open={sheetOpen} onOpenChange={setSheetOpen} title={tx('تفاصيل الموعد', 'Booking details')}>
            <div className="px-5 pb-5">
              <Timeline
                items={[
                  {
                    id: '1',
                    title: tx('تم إنشاء الحجز', 'Booking created'),
                    meta: tx('عبر التطبيق · ٤:١٠ م', 'Online · 4:10 pm'),
                  },
                  {
                    id: '2',
                    title: tx('تم التأكيد', 'Confirmed'),
                    meta: tx('٤:١١ م', '4:11 pm'),
                    tone: 'success',
                  },
                ]}
              />
            </div>
          </Sheet>
        </Section>

        <Section
          title={tx('الحالات الاستثنائية', 'Empty, error & loading')}
          eyebrow="EMPTY · SKELETON · ERROR"
        >
          <EmptyState
            icon="calendar"
            title={tx('لا توجد مواعيد قادمة', 'No upcoming appointments')}
            body={tx(
              'احجز حلاقتك القادمة من المحلات القريبة منك',
              'Book your next haircut from shops near you',
            )}
            action={
              <ButtonLink href="/search" size="md">
                {tx('استكشف المحلات', 'Explore shops')}
              </ButtonLink>
            }
          />
          <SkeletonList label={tx('جارٍ تحميل المواعيد', 'Loading appointments')} rows={2} />
          <InlineAlert
            tone="danger"
            title={tx('تعذّر تحميل الأوقات المتاحة', 'Couldn’t load available times')}
            action={
              <Button size="xs" variant="outline" icon="refresh">
                {tx('إعادة المحاولة', 'Try again')}
              </Button>
            }
          >
            {tx(
              'تحقق من الاتصال ثم أعد المحاولة. لن يُحجز أي وقت قبل تأكيدك.',
              'Check your connection and try again. No time is booked before you confirm.',
            )}
          </InlineAlert>
        </Section>

        <Section title={tx('الصلاحيات والجلسة', 'Permission & session')} eyebrow="PERMISSION · SESSION">
          <PermissionDenied />
          <ExpiredSession signInHref="/auth/sign-in" />
          <ErrorState />
        </Section>

        <Section
          title={tx('الجداول والتنقل الثانوي', 'Tables & secondary navigation')}
          eyebrow="TABLE · PAGINATION · BREADCRUMB"
          wide
        >
          <Breadcrumb
            items={[
              { label: tx('الإدارة', 'Admin'), href: '/admin' },
              { label: tx('المحلات', 'Shops'), href: '/admin/shops' },
              { label: tx('صالون الأصالة', 'Al Asala Salon') },
            ]}
          />
          <ResponsiveTable
            caption={tx('المحلات', 'Shops')}
            rows={shopRows}
            rowKey={(row) => row.id}
            columns={[
              {
                key: 'name',
                header: tx('المحل', 'Shop'),
                cell: (row) => row.name,
                mobile: 'primary',
                width: '34%',
              },
              { key: 'district', header: tx('الحي', 'District'), cell: (row) => row.district },
              {
                key: 'pros',
                header: tx('الحلاقون', 'Barbers'),
                cell: (row) => <span className="font-latin font-semibold">{row.pros}</span>,
              },
              {
                key: 'status',
                header: tx('الاشتراك', 'Subscription'),
                cell: (row) => <StatusBadge kind="subscription" status={row.status} />,
              },
            ]}
            actions={() => (
              <DropdownMenu
                trigger={<IconButton icon="more" label={tx('خيارات المحل', 'Shop options')} size="sm" />}
                items={[{ label: tx('عرض التفاصيل', 'View details'), icon: 'store', onSelect: () => {} }]}
              />
            )}
          />
          <Pagination
            page={1}
            pageSize={4}
            total={128}
            hrefForPage={(page) => `/dev/components?page=${page}`}
          />
        </Section>
      </div>
    </div>
  );
}
