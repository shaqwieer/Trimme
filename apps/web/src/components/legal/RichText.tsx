import { Fragment, type ReactNode } from 'react';

/** A ProseMirror node as the API stores it after its allowlist (D-144). */
export type RichNode = {
  type: string;
  attrs?: Record<string, unknown>;
  content?: RichNode[];
  text?: string;
  marks?: Array<{ type: string; attrs?: Record<string, unknown> }>;
};

/** https, http, mailto and tel links, or a path on this site; anything else renders as plain text. */
export function safeHref(value: unknown): string | null {
  if (typeof value !== 'string') return null;
  const href = value.trim();
  if (href.startsWith('/') && !href.startsWith('//') && !href.includes('\\')) return href;
  return /^(https?:\/\/|mailto:|tel:)/i.test(href) ? href : null;
}

function marked(text: string, marks: RichNode['marks'], key: number): ReactNode {
  const wrapped = (marks ?? []).reduce<ReactNode>((inner, mark) => {
    switch (mark.type) {
      case 'bold':
        return <strong className="font-bold">{inner}</strong>;
      case 'italic':
        return <em>{inner}</em>;
      case 'underline':
        return <u>{inner}</u>;
      case 'strike':
        return <s>{inner}</s>;
      case 'link': {
        const href = safeHref(mark.attrs?.href);
        if (!href) return inner;
        const external = !href.startsWith('/');
        return (
          <a
            href={href}
            className="font-bold text-text-link underline underline-offset-2"
            {...(external ? { target: '_blank', rel: 'noopener noreferrer' } : {})}
          >
            {inner}
          </a>
        );
      }
      default:
        return inner;
    }
  }, text);
  return <Fragment key={key}>{wrapped}</Fragment>;
}

function children(node: RichNode): ReactNode[] {
  return (node.content ?? []).map((child, index) => render(child, index));
}

function render(node: RichNode, key: number): ReactNode {
  switch (node.type) {
    case 'doc':
      return <div key={key}>{children(node)}</div>;
    case 'paragraph':
      return (
        <p key={key} className="text-body leading-[1.9] text-text-strong">
          {node.content?.length ? children(node) : <br />}
        </p>
      );
    case 'heading':
      return node.attrs?.level === 3 ? (
        <h3 key={key} className="pt-1 text-[1.0625rem] font-bold text-navy-900">
          {children(node)}
        </h3>
      ) : (
        <h2 key={key} className="pt-2 text-h3 font-bold text-navy-900">
          {children(node)}
        </h2>
      );
    case 'bulletList':
      return (
        <ul key={key} className="flex list-disc flex-col gap-1 ps-6 text-body text-text-strong">
          {children(node)}
        </ul>
      );
    case 'orderedList':
      return (
        <ol
          key={key}
          start={typeof node.attrs?.start === 'number' ? node.attrs.start : undefined}
          className="flex list-decimal flex-col gap-1 ps-6 text-body text-text-strong"
        >
          {children(node)}
        </ol>
      );
    case 'listItem':
      return <li key={key}>{children(node)}</li>;
    case 'blockquote':
      return (
        <blockquote
          key={key}
          className="flex flex-col gap-2 border-s-4 border-brand-200 ps-4 text-text-secondary"
        >
          {children(node)}
        </blockquote>
      );
    case 'horizontalRule':
      return <hr key={key} className="border-border" />;
    case 'hardBreak':
      return <br key={key} />;
    case 'text':
      return typeof node.text === 'string' ? marked(node.text, node.marks, key) : null;
    default:
      return null;
  }
}

/**
 * The admin's legal text (D-144), rendered as React elements from the same allowlist the API applies on save: never
 * as HTML, so nothing in a document can run script or add markup the page did not choose.
 */
export function RichText({ document }: { document: RichNode }) {
  return <div className="flex flex-col gap-3">{children(document)}</div>;
}
