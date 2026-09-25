'use client';

import { type ChangeEvent, type DragEvent, useId, useState } from 'react';
import { useTranslations } from 'next-intl';
import { cn } from '@/lib/cn';
import { Icon } from './icons';

type UploadDropZoneProps = {
  /** Accessible label of the file input. */
  label: string;
  accept?: string[];
  maxBytes?: number;
  /** Recommended pixel size shown to the user (design: 1600×900 cover). */
  recommended?: { width: number; height: number };
  onFileSelected: (file: File) => void;
};

const DEFAULT_TYPES = ['image/jpeg', 'image/png'];

/** Client-side pre-check only — the API validates type, size and content again (spec §9). */
export function validateUpload(file: File, accept: string[], maxBytes: number): 'type' | 'size' | null {
  if (!accept.includes(file.type)) return 'type';
  if (file.size > maxBytes) return 'size';
  return null;
}

/**
 * Cover-image drop zone (design 669–672): drag & drop or keyboard/click file picker, with the
 * constraints shown up front and localized errors announced to assistive technology.
 */
export function UploadDropZone({
  label,
  accept = DEFAULT_TYPES,
  maxBytes = 4 * 1024 * 1024,
  recommended = { width: 1600, height: 900 },
  onFileSelected,
}: UploadDropZoneProps) {
  const t = useTranslations('ui.upload');
  const id = useId();
  const [dragging, setDragging] = useState(false);
  const [error, setError] = useState<'type' | 'size' | null>(null);
  const [selectedName, setSelectedName] = useState<string | null>(null);
  const maxMb = Math.round(maxBytes / (1024 * 1024));

  const handle = (file: File | undefined) => {
    if (!file) return;
    const problem = validateUpload(file, accept, maxBytes);
    setError(problem);
    if (problem) {
      setSelectedName(null);
      return;
    }
    setSelectedName(file.name);
    onFileSelected(file);
  };

  const onDrop = (event: DragEvent<HTMLLabelElement>) => {
    event.preventDefault();
    setDragging(false);
    handle(event.dataTransfer.files[0]);
  };

  return (
    <div className="flex flex-col gap-1.5">
      <label
        htmlFor={id}
        onDragOver={(event) => {
          event.preventDefault();
          setDragging(true);
        }}
        onDragLeave={() => setDragging(false)}
        onDrop={onDrop}
        className={cn(
          'relative flex cursor-pointer flex-col items-center gap-1 rounded-button border-[1.5px] border-dashed p-4 text-center transition-colors has-focus-visible:shadow-[var(--focus-ring)]',
          dragging ? 'border-brand-500 bg-brand-50' : 'border-border-dashed bg-surface',
          error && 'border-danger-500 bg-danger-surface',
        )}
      >
        <Icon name="download" className="size-5 rotate-180 text-brand-700" />
        <span className="text-caption font-bold text-text-primary">{t('prompt')}</span>
        <span className="font-latin text-helper text-text-tertiary">
          {t('constraints', { width: recommended.width, height: recommended.height, maxMb })}
        </span>
        <input
          id={id}
          type="file"
          accept={accept.join(',')}
          aria-label={label}
          aria-invalid={error ? true : undefined}
          aria-describedby={error ? `${id}-error` : undefined}
          onChange={(event: ChangeEvent<HTMLInputElement>) => handle(event.target.files?.[0])}
          className="sr-only"
        />
      </label>
      <p
        id={`${id}-error`}
        role={error ? 'alert' : 'status'}
        className={cn('text-helper', error ? 'font-medium text-danger-700' : 'text-text-secondary')}
      >
        {error === 'type' && t('typeError')}
        {error === 'size' && t('sizeError', { maxMb })}
        {!error && selectedName && t('selected', { name: selectedName })}
      </p>
    </div>
  );
}
