# TRIMME high-conversion landing-page redesign prompt

Act as a senior conversion designer, Arabic UX writer, and Next.js product engineer. Redesign and implement the TRIMME public landing page using the existing repository, components, APIs, design tokens, and localization system.

## Business context

TRIMME is an Arabic-first, mobile-first marketplace for men's salons and barbers in Saudi Arabia. It serves two audiences:

1. Customers discover nearby shops, compare services, prices, ratings, and barbers, see genuinely available appointment times, book online, and receive WhatsApp confirmation/reminders.
2. Shop owners manage appointments, walk-ins, staff schedules, services, time off, and customer confirmations from one dashboard.

The landing page must prioritize customer bookings. Shop-owner acquisition is an important secondary conversion, but it must not compete with the main customer journey above the fold.

## Conversion objective

Increase the percentage of visitors who start discovering a shop or booking a service. The primary action is to search or browse nearby shops. The secondary action is for a salon owner to become a partner.

The page should answer these questions within seconds:

- What is TRIMME?
- Is it available near me?
- Why is it easier than calling or messaging a shop?
- Can I trust the available times, barbers, prices, and reviews?
- What should I do next?

## Brand and experience direction

Create a confident, premium, approachable Saudi product experience—not a generic SaaS page and not an overly luxurious grooming campaign. Use TRIMME's existing steel-blue, deep-navy, white, warm-neutral palette and existing design tokens. Keep the interface clean, tactile, and highly legible with generous whitespace, restrained shadows, real photography, and clear action hierarchy.

Arabic is the default experience and must feel designed for RTL rather than mirrored as an afterthought. English must be equally complete in LTR. Use the existing Tajawal/Inter typography. Do not introduce unrelated colors, gradients, fonts, icon libraries, or a second design system.

## Page structure

### 1. Header

- Keep the TRIMME logo prominent but compact.
- Provide only the highest-value navigation: discover shops, how it works, for shop owners, sign in, and language switcher.
- Use one visually dominant customer CTA: "Find a barber" / "ابحث عن حلاق".
- Keep the header sticky on desktop only if it remains lightweight and does not obscure content.

### 2. Hero: outcome first

Use a balanced two-column layout on desktop and a single-column mobile layout.

Copy direction:

- Eyebrow: availability/location signal such as "Now available in Riyadh" when supported by real API data.
- H1: concise, benefit-led, and specific. Recommended English direction: "Your next haircut, booked in a minute." Recommended Arabic direction: "حلاقتك الجاية، احجزها في دقيقة."
- Supporting copy: communicate real available times, the customer's preferred barber, and WhatsApp confirmation without repeating the headline.
- Primary conversion control: a prominent search/discovery module for service or shop plus location. Make its button explicit: "Find available times" rather than a vague "Search".
- Add a low-friction browse link below it: "Browse top-rated shops".
- Show only real dynamic proof—shop count, barber count, review count, or average rating. Hide zero or unavailable values. Never invent customer counts, ratings, testimonials, or appointments.
- Use the supplied hero photo at `/brand/trimme-hero-barbershop.png`. Render it with `next/image`, an accurate localized alt, responsive `sizes`, and hero-appropriate priority. Preserve the important faces when cropping. Overlay the existing appointment-confirmed card with a subtle WhatsApp/reminder message, but do not cover faces or tools.

On mobile, the H1, supporting copy, and primary action must appear before the image. The primary CTA must be usable without horizontal scrolling and should be visible within the first viewport on common 390px-wide phones.

### 3. Trust strip

Immediately after the hero, show three short reasons to trust the product:

- Real bookable times, not a request form
- Choose a specific barber or the earliest available
- Instant confirmation and reminder on WhatsApp

Use short labels and compact icons. Avoid paragraph-heavy feature cards.

### 4. Top-rated nearby shops

- Move real inventory close to the top because seeing bookable shops is stronger proof than marketing claims.
- Reuse `DiscoveryShopCard` and the existing rating-sorted public search API.
- Show service price, rating/review count, area/distance when available, and earliest appointment when supported by existing data.
- Use a clear section CTA: "View all nearby shops".
- Provide a polished empty state without making false availability claims.

### 5. How booking works

Explain the flow in three highly scannable steps:

1. Pick a service and shop.
2. Choose your barber and real available time.
3. Confirm and receive the details on WhatsApp.

Use simple numbered steps. Do not use a fake app screenshot or render text inside generated imagery.

### 6. Customer value section

Turn features into outcomes: no calling around, no waiting for a reply, transparent choices, and an appointment that fits the selected service duration and barber schedule. Keep this section visually distinct but concise.

If social proof is shown, source it from stored, published reviews through real product data. Never write fictional quotes or names.

### 7. Shop-owner conversion band

Create a separate dark-navy section later in the page for salon owners.

- Headline direction: "More bookings. Less front-desk chaos."
- Mention appointments, walk-ins, staff schedules, services, time off, and automatic WhatsApp confirmations.
- Use one CTA: "Grow your shop with TRIMME" / "نمّ صالونك مع تريمي".
- Only show the CTA if the configured partner contact URL exists, matching current behavior.

### 8. FAQ and final CTA

Add a concise FAQ that removes conversion friction: whether booking costs the customer, whether an app download is required, how confirmation works, whether a specific barber can be selected, and how cancellation/rescheduling works. Answers must match implemented product behavior and published policies—do not invent guarantees.

End with one customer-focused CTA that returns users to discovery/search.

## UX and copy requirements

- Write plain, local, natural Arabic; avoid stiff translated phrasing. Keep English concise and idiomatic.
- Prefer concrete verbs: find, choose, book, confirm.
- Keep one dominant action per section.
- Do not use urgency tricks, fake scarcity, fake live counters, fake discounts, or unsupported "best"/"number one" claims.
- Reduce cognitive load: short headings, short supporting copy, obvious buttons, and no carousel.
- Use progressive disclosure for location selection and filters.
- Preserve keyboard navigation, visible focus states, semantic landmarks, valid heading order, and WCAG AA contrast.
- Respect `prefers-reduced-motion`; animations should be subtle and optional.

## Technical constraints

- Work in `apps/web/src/app/[locale]/page.tsx` and the smallest reasonable set of supporting components/files.
- Keep the page RSC-first. Add client components only when interaction genuinely requires them.
- Use existing `next-intl` routing and add every new key to both `apps/web/messages/ar.json` and `apps/web/messages/en.json` with parity.
- Reuse current APIs for public stats, areas, location, and rating-sorted shops. Handle API failure gracefully through the existing safe/optional patterns.
- Reuse existing UI primitives, icons, `PublicShell`, `DiscoveryShopCard`, design tokens, radii, and shadows.
- Use `next/image` for the hero photo. Do not add a new image CDN or dependency.
- Preserve localized metadata, canonical/alternate URLs, JSON-LD, and indexability.
- Avoid layout shift, oversized JavaScript, auto-playing media, and decorative images that delay the main CTA.
- Do not hardcode Riyadh if the real areas API indicates another or multiple cities.
- Do not change booking, authentication, or location semantics as part of this visual redesign.

## Deliverables

1. Production-ready responsive implementation for Arabic RTL and English LTR.
2. Updated localized conversion copy in both message files.
3. Hero image integration using `/brand/trimme-hero-barbershop.png`.
4. Relevant unit tests for conditional dynamic data, empty states, localized links, and partner CTA visibility.
5. A brief summary of conversion decisions and any assumptions.

## Acceptance criteria

- At 390px width, no horizontal overflow; the primary action is easy to reach and at least 44px high.
- At desktop width, the hero has one clear reading path and the photo supports rather than competes with the CTA.
- Arabic layout, punctuation, alignment, icon direction, and content order are correct in RTL.
- No invented statistics, testimonials, availability, discounts, or partner claims.
- The page remains useful when stats, location, top shops, or partner URL are unavailable.
- Accessibility checks pass and focus states are visible.
- Existing lint, typecheck, tests, and production build pass.

