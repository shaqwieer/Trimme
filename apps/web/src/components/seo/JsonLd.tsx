import { serializeJsonLd } from '@/lib/seo/jsonld';

/** Inline structured data (server-rendered; the JSON is escaped so shop text cannot break out of the script). */
export function JsonLd({ data }: { data: Record<string, unknown> | Array<Record<string, unknown>> }) {
  return <script type="application/ld+json" dangerouslySetInnerHTML={{ __html: serializeJsonLd(data) }} />;
}
