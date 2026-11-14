'use client';

import { EditorContent, useEditor, useEditorState } from '@tiptap/react';
import StarterKit from '@tiptap/starter-kit';
import {
  Bold,
  Heading2,
  Heading3,
  Italic,
  Link2,
  List,
  ListOrdered,
  Pilcrow,
  Quote,
  Redo2,
  Strikethrough,
  Underline,
  Undo2,
  type LucideIcon,
} from 'lucide-react';
import { useTranslations } from 'next-intl';
import { type FormEvent, useState } from 'react';
import { safeHref, type RichNode } from '@/components/legal/RichText';
import { Button, ButtonLink } from '@/components/ui/Button';
import { TextField } from '@/components/ui/inputs';
import { ConfirmDialog } from '@/components/ui/overlays';
import { InlineAlert } from '@/components/ui/states';
import { useRouter } from '@/i18n/navigation';
import type { Locale } from '@/i18n/routing';
import { browserApi } from '@/lib/api/client';
import { ensureOk, useApiErrorMessage } from '@/lib/api/errors';
import { ApiError } from '@/lib/api/problem';
import { cn } from '@/lib/cn';

type Kind = 'terms' | 'privacy';

/**
 * The admin's editor for one language of the terms or the privacy policy (D-144): headings, paragraphs, lists, quotes,
 * bold, italic, underline, strikethrough and links — the same allowlist the API keeps and the public page renders.
 * It opens on the saved text, or on the built-in text when nothing is saved, so the admin edits what visitors see.
 */
export function LegalEditor({
  kind,
  locale,
  label,
  initial,
  saved,
  canEdit,
}: {
  kind: Kind;
  locale: Locale;
  label: string;
  initial: RichNode;
  saved: boolean;
  canEdit: boolean;
}) {
  const t = useTranslations('legalAdmin');
  const router = useRouter();
  const apiMessage = useApiErrorMessage();
  const [dirty, setDirty] = useState(false);
  const [pending, setPending] = useState<'save' | 'restore' | null>(null);
  const [notice, setNotice] = useState<{ tone: 'success' | 'danger'; text: string } | null>(null);
  const [confirmRestore, setConfirmRestore] = useState(false);
  const [linkOpen, setLinkOpen] = useState(false);
  const [linkValue, setLinkValue] = useState('');
  const [linkError, setLinkError] = useState(false);

  const editor = useEditor({
    extensions: [
      StarterKit.configure({
        heading: { levels: [2, 3] },
        code: false,
        codeBlock: false,
        link: { openOnClick: false, autolink: true, protocols: ['mailto', 'tel'] },
      }),
    ],
    content: initial,
    editable: canEdit,
    immediatelyRender: false,
    editorProps: {
      attributes: {
        'aria-label': label,
        'aria-multiline': 'true',
        role: 'textbox',
        dir: locale === 'ar' ? 'rtl' : 'ltr',
        lang: locale,
        class: cn(
          'min-h-[420px] px-5 py-4 text-body leading-[1.9] text-text-strong outline-none',
          '[&_h2]:pt-3 [&_h2]:text-h3 [&_h2]:font-bold [&_h2]:text-navy-900 [&_h3]:pt-2 [&_h3]:font-bold [&_h3]:text-navy-900',
          '[&_p]:my-2 [&_ul]:list-disc [&_ul]:ps-6 [&_ol]:list-decimal [&_ol]:ps-6',
          '[&_blockquote]:border-s-4 [&_blockquote]:border-brand-200 [&_blockquote]:ps-4 [&_blockquote]:text-text-secondary',
          '[&_a]:font-bold [&_a]:text-text-link [&_a]:underline [&_hr]:my-4 [&_hr]:border-border',
        ),
      },
    },
    onUpdate: () => {
      setDirty(true);
      setNotice(null);
    },
  });

  const state = useEditorState({
    editor,
    selector: ({ editor: e }) =>
      e
        ? {
            h2: e.isActive('heading', { level: 2 }),
            h3: e.isActive('heading', { level: 3 }),
            paragraph: e.isActive('paragraph'),
            bold: e.isActive('bold'),
            italic: e.isActive('italic'),
            underline: e.isActive('underline'),
            strike: e.isActive('strike'),
            bulletList: e.isActive('bulletList'),
            orderedList: e.isActive('orderedList'),
            blockquote: e.isActive('blockquote'),
            link: e.isActive('link'),
            canUndo: e.can().undo(),
            canRedo: e.can().redo(),
          }
        : null,
  });

  const save = async () => {
    if (!editor) return;
    setPending('save');
    setNotice(null);
    try {
      ensureOk(
        await browserApi.PUT('/api/v1/admin/legal/{kind}/{locale}', {
          params: { path: { kind, locale } },
          body: { content: editor.getJSON() },
        }),
      );
      setDirty(false);
      setNotice({ tone: 'success', text: t('saved') });
      router.refresh();
    } catch (error) {
      setNotice({
        tone: 'danger',
        text: apiMessage(error instanceof ApiError ? error : 'server.unexpected') ?? '',
      });
    } finally {
      setPending(null);
    }
  };

  const restore = async () => {
    setPending('restore');
    try {
      ensureOk(
        await browserApi.DELETE('/api/v1/admin/legal/{kind}/{locale}', {
          params: { path: { kind, locale } },
        }),
      );
      setConfirmRestore(false);
      setDirty(false);
      router.refresh();
    } catch (error) {
      setNotice({
        tone: 'danger',
        text: apiMessage(error instanceof ApiError ? error : 'server.unexpected') ?? '',
      });
    } finally {
      setPending(null);
    }
  };

  const applyLink = (event: FormEvent) => {
    event.preventDefault();
    if (!editor) return;
    const href = safeHref(linkValue);
    if (!href) {
      setLinkError(true);
      return;
    }
    editor.chain().focus().extendMarkRange('link').setLink({ href }).run();
    setLinkOpen(false);
  };

  const tool = (
    key: keyof NonNullable<typeof state>,
    icon: LucideIcon,
    run: () => void,
    disabled = false,
  ) => {
    const Glyph = icon;
    const pressed = Boolean(state?.[key]);
    return (
      <button
        key={key}
        type="button"
        title={t(`toolbar.${key === 'canUndo' ? 'undo' : key === 'canRedo' ? 'redo' : key}`)}
        aria-label={t(`toolbar.${key === 'canUndo' ? 'undo' : key === 'canRedo' ? 'redo' : key}`)}
        aria-pressed={key === 'canUndo' || key === 'canRedo' ? undefined : pressed}
        disabled={!canEdit || !editor || disabled}
        onMouseDown={(event) => event.preventDefault()}
        onClick={run}
        className={cn(
          'inline-flex size-9 items-center justify-center rounded-button text-text-strong transition-colors hover:bg-bg-subtle focus-visible:shadow-[var(--focus-ring)] focus-visible:outline-none disabled:opacity-40',
          pressed && key !== 'canUndo' && key !== 'canRedo' && 'bg-navy-900 text-on-navy hover:bg-navy-900',
        )}
      >
        <Glyph aria-hidden="true" className="size-[18px]" />
      </button>
    );
  };

  const chain = () => editor!.chain().focus();

  return (
    <div className="flex flex-col gap-4" data-testid="legal-editor">
      <div className="flex flex-wrap items-center gap-3">
        <span
          className={cn(
            'rounded-badge px-2.5 py-1 text-badge font-bold',
            saved ? 'bg-success-50 text-success-700' : 'bg-bg-muted text-text-secondary',
          )}
        >
          {saved ? t('customBadge') : t('builtInBadge')}
        </span>
        {dirty && <span className="text-helper font-bold text-warning-700">{t('unsaved')}</span>}
        <ButtonLink
          href={`/${kind}`}
          locale={locale}
          variant="ghost"
          size="sm"
          target="_blank"
          className="ms-auto"
        >
          {t('view')}
        </ButtonLink>
      </div>

      {!canEdit && <InlineAlert tone="info" title={t('readOnly')} />}

      <div className="overflow-hidden rounded-card border border-border bg-surface">
        <div
          role="toolbar"
          aria-label={t('toolbar.label')}
          className="flex flex-wrap items-center gap-1 border-b border-border bg-bg-subtle px-2 py-1.5"
        >
          {tool('h2', Heading2, () => chain().toggleHeading({ level: 2 }).run())}
          {tool('h3', Heading3, () => chain().toggleHeading({ level: 3 }).run())}
          {tool('paragraph', Pilcrow, () => chain().setParagraph().run())}
          <span aria-hidden="true" className="mx-1 h-6 w-px bg-border" />
          {tool('bold', Bold, () => chain().toggleBold().run())}
          {tool('italic', Italic, () => chain().toggleItalic().run())}
          {tool('underline', Underline, () => chain().toggleUnderline().run())}
          {tool('strike', Strikethrough, () => chain().toggleStrike().run())}
          {tool('link', Link2, () => {
            setLinkValue(String(editor?.getAttributes('link').href ?? ''));
            setLinkError(false);
            setLinkOpen(!linkOpen);
          })}
          <span aria-hidden="true" className="mx-1 h-6 w-px bg-border" />
          {tool('bulletList', List, () => chain().toggleBulletList().run())}
          {tool('orderedList', ListOrdered, () => chain().toggleOrderedList().run())}
          {tool('blockquote', Quote, () => chain().toggleBlockquote().run())}
          <span aria-hidden="true" className="mx-1 h-6 w-px bg-border" />
          {tool('canUndo', Undo2, () => chain().undo().run(), !state?.canUndo)}
          {tool('canRedo', Redo2, () => chain().redo().run(), !state?.canRedo)}
        </div>
        {linkOpen && (
          <form
            method="post"
            onSubmit={applyLink}
            className="flex flex-wrap items-end gap-2 border-b border-border px-3 py-2"
          >
            <TextField
              label={t('link.label')}
              placeholder={t('link.placeholder')}
              dir="ltr"
              inputMode="url"
              value={linkValue}
              onChange={(event) => {
                setLinkValue(event.target.value);
                setLinkError(false);
              }}
              error={linkError ? t('link.invalid') : undefined}
              className="min-w-0 flex-1"
            />
            <Button type="submit" size="sm" variant="secondary">
              {t('link.apply')}
            </Button>
            {state?.link && (
              <Button
                type="button"
                size="sm"
                variant="outline"
                onClick={() => {
                  chain().extendMarkRange('link').unsetLink().run();
                  setLinkOpen(false);
                }}
              >
                {t('link.remove')}
              </Button>
            )}
          </form>
        )}
        <EditorContent editor={editor} />
      </div>

      {notice && <InlineAlert tone={notice.tone} title={notice.text} />}

      {canEdit && (
        <div className="flex flex-wrap items-center gap-3">
          <Button icon="check" loading={pending === 'save'} disabled={!editor} onClick={() => void save()}>
            {t('save')}
          </Button>
          {saved && (
            <Button variant="outline" icon="refresh" onClick={() => setConfirmRestore(true)}>
              {t('restore')}
            </Button>
          )}
        </div>
      )}

      <ConfirmDialog
        open={confirmRestore}
        onOpenChange={setConfirmRestore}
        title={t('restoreTitle')}
        body={t('restoreBody')}
        confirmLabel={t('restoreConfirm')}
        cancelLabel={t('cancel')}
        onConfirm={() => void restore()}
        loading={pending === 'restore'}
      />
    </div>
  );
}
