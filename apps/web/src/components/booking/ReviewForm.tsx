'use client';

import { useTranslations } from 'next-intl';
import { useState } from 'react';
import { Button, ButtonLink } from '@/components/ui/Button';
import { Icon } from '@/components/ui/icons';
import { TextareaField } from '@/components/ui/inputs';
import { RatingInput } from '@/components/ui/RatingInput';
import { Chip } from '@/components/ui/selection.client';
import { InlineAlert } from '@/components/ui/states';
import { browserApi } from '@/lib/api/client';
import { ensureOk, useApiErrorMessage } from '@/lib/api/errors';
import { ApiError } from '@/lib/api/problem';
import type { components } from '@/lib/api/schema';

type ReviewTag = components['schemas']['ReviewTag'];

export const REVIEW_TAGS: ReviewTag[] = ['Punctuality', 'Quality', 'Cleanliness', 'Manners', 'Price'];
const COMMENT_MAX = 1000;

/**
 * Rating a completed visit (c-rate 1875–1920): 1–5 stars with the design's word for each, optional "what did you like"
 * tags and comment, and the privacy note (first name and initial only; no editing after publishing). Submitting locks
 * the form: a review is written once (D-017).
 */
export function ReviewForm({ bookingId }: { bookingId: string }) {
  const t = useTranslations('review');
  const apiMessage = useApiErrorMessage();
  const [rating, setRating] = useState<number>();
  const [tags, setTags] = useState<ReviewTag[]>([]);
  const [comment, setComment] = useState('');
  const [pending, setPending] = useState(false);
  const [done, setDone] = useState(false);
  const [error, setError] = useState<string>();

  const submit = async () => {
    if (!rating) return;
    setPending(true);
    setError(undefined);
    try {
      ensureOk(
        await browserApi.POST('/api/v1/me/bookings/{bookingId}/review', {
          params: { path: { bookingId } },
          body: { rating, tags, comment: comment.trim() || null },
        }),
      );
      setDone(true);
    } catch (failure) {
      setError(apiMessage(failure instanceof ApiError ? failure : 'server.unexpected'));
      if (failure instanceof ApiError && failure.errorCode === 'review.already_exists') setDone(true);
    } finally {
      setPending(false);
    }
  };

  if (done && !error) {
    return (
      <div
        className="flex flex-col items-center gap-3 rounded-card border border-border bg-success-50 p-6 text-center"
        role="status"
      >
        <Icon name="check" className="size-8 text-success-700" />
        <p className="text-[1.0625rem] font-bold text-success-700">{t('done.title')}</p>
        <p className="text-label text-text-strong">{t('done.body')}</p>
        <ButtonLink href={`/account/bookings/${bookingId}`} variant="outline" size="sm">
          {t('backToBooking')}
        </ButtonLink>
      </div>
    );
  }

  return (
    <form
      method="post"
      noValidate
      className="flex flex-col gap-5"
      onSubmit={(event) => {
        event.preventDefault();
        void submit();
      }}
    >
      <div className="flex flex-col items-center gap-2">
        <RatingInput
          name="rating"
          value={rating}
          onValueChange={setRating}
          legend={t('starsLegend')}
          required
        />
        <p aria-live="polite" className="text-label font-bold text-text-strong" data-testid="star-label">
          {t(`stars.${(rating ?? 0) as 0 | 1 | 2 | 3 | 4 | 5}`)}
        </p>
      </div>
      <fieldset className="flex min-w-0 flex-col gap-2">
        <legend className="pb-1 text-label font-bold text-text-strong">{t('tagsLegend')}</legend>
        <div className="flex flex-wrap gap-2">
          {REVIEW_TAGS.map((tag) => (
            <Chip
              key={tag}
              pressed={tags.includes(tag)}
              onPressedChange={(on) =>
                setTags((current) => (on ? [...current, tag] : current.filter((x) => x !== tag)))
              }
            >
              {t(`tags.${tag}`)}
            </Chip>
          ))}
        </div>
      </fieldset>
      <TextareaField
        label={t('commentLabel')}
        optional
        placeholder={t('commentPlaceholder')}
        maxLength={COMMENT_MAX}
        rows={4}
        value={comment}
        onChange={(event) => setComment(event.target.value)}
      />
      <p className="flex items-start gap-2 rounded-card bg-bg-subtle p-3 text-helper text-text-secondary">
        <Icon name="eyeOff" className="mt-0.5 size-4 shrink-0" />
        {t('privacy')}
      </p>
      {error && <InlineAlert tone="danger" title={error} />}
      <Button type="submit" size="lg" fullWidth disabled={!rating || done} loading={pending}>
        {rating ? t('submit') : t('submitIdle')}
      </Button>
    </form>
  );
}
