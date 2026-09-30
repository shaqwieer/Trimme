import type { BadgeTone } from '@/components/ui/Badge';
import type { components } from '@/lib/api/schema';

export type TemplateSummary = components['schemas']['TemplateSummaryResponse'];
export type TemplateDetail = components['schemas']['TemplateDetailResponse'];
export type TemplateVersion = components['schemas']['TemplateVersionResponse'];
export type TemplateButton = components['schemas']['TemplateButton'];
export type TemplatePreview = components['schemas']['TemplatePreviewResponse'];
export type Dispatch = components['schemas']['DispatchResponse'];
export type MessageEvent = TemplateSummary['event'];
export type MessageAudience = TemplateSummary['audience'];
export type DispatchStatus = Dispatch['status'];

/** Lifecycle order of the events, as the template list shows them. */
export const MESSAGE_EVENTS: readonly MessageEvent[] = [
  'BookingConfirmed',
  'BookingPending',
  'BookingRescheduled',
  'BookingCancelled',
  'BookingReminder',
];

export const DISPATCH_STATUSES: readonly DispatchStatus[] = ['Queued', 'Sent', 'Delivered', 'Read', 'Failed'];

export const MAX_BODY_LENGTH = 1024;
export const MAX_BUTTONS = 2;

/** A template's locale as a message key (the API sends a plain string; v1 has Arabic and English only). */
export function localeKey(locale: string): 'ar' | 'en' {
  return locale === 'en' ? 'en' : 'ar';
}

/** Badge tone of a dispatch status: delivered/read success, failed danger, queued warning, sent brand. */
export function dispatchTone(status: DispatchStatus): BadgeTone {
  switch (status) {
    case 'Delivered':
    case 'Read':
      return 'success';
    case 'Failed':
      return 'danger';
    case 'Queued':
      return 'warning';
    default:
      return 'info';
  }
}

/** How a placeholder is written in a template: `{{name}}`. */
export function placeholderToken(name: string): string {
  return `{{${name}}}`;
}

/** Inserts `{{name}}` at the caret (replacing a selection) and returns the new text and caret position. */
export function insertPlaceholder(
  text: string,
  selectionStart: number,
  selectionEnd: number,
  name: string,
): { text: string; caret: number } {
  const start = Math.max(0, Math.min(selectionStart, text.length));
  const end = Math.max(start, Math.min(selectionEnd, text.length));
  const token = placeholderToken(name);
  return { text: text.slice(0, start) + token + text.slice(end), caret: start + token.length };
}

/** Placeholders used in a body, in order of first appearance (mirrors the API's rule). */
export function usedPlaceholders(body: string): string[] {
  const seen: string[] = [];
  for (const match of body.matchAll(/\{\{\s*([a-z_]+)\s*\}\}/g)) {
    const name = match[1];
    if (name && !seen.includes(name)) seen.push(name);
  }
  return seen;
}

/** The draft, if the template has one (at most one), else undefined. */
export function draftOf(detail: Pick<TemplateDetail, 'versions'>): TemplateVersion | undefined {
  return detail.versions.find((v) => v.status === 'Draft');
}

/** The active version, if any. */
export function activeOf(
  detail: Pick<TemplateDetail, 'versions' | 'activeVersionId'>,
): TemplateVersion | undefined {
  return detail.versions.find((v) => v.id === detail.activeVersionId);
}

/** Whether the editor's text differs from what is stored as the draft (or the active version when there is none). */
export function isDirty(
  current: { body: string; buttons: TemplateButton[]; providerTemplateName: string },
  stored: TemplateVersion | undefined,
): boolean {
  if (!stored) return current.body.trim().length > 0;
  return (
    current.body !== stored.body ||
    (current.providerTemplateName.trim() || null) !== (stored.providerTemplateName ?? null) ||
    JSON.stringify(current.buttons) !== JSON.stringify(stored.buttons)
  );
}
