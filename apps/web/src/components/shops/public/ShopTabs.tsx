'use client';

import { Tabs as RadixTabs } from 'radix-ui';
import { type ReactNode, useState } from 'react';

export type ShopTab = { value: string; label: ReactNode; content: ReactNode };

/**
 * The shop page tabs (c-shop 1276–1280, underline style). Every panel is rendered on the server and stays in the HTML
 * (inactive ones are hidden), so services and reviews are crawlable; this island only switches the visible panel and
 * keeps `?tab=` in the address so the view survives refresh and sharing. Arrow keys follow the reading direction.
 */
export function ShopTabs({ tabs, initial, label }: { tabs: ShopTab[]; initial: string; label: string }) {
  const [value, setValue] = useState(tabs.some((t) => t.value === initial) ? initial : (tabs[0]?.value ?? ''));

  function change(next: string) {
    setValue(next);
    const url = new URL(window.location.href);
    url.searchParams.set('tab', next);
    url.searchParams.delete('reviewsPage');
    window.history.replaceState(window.history.state, '', url);
  }

  return (
    <RadixTabs.Root value={value} onValueChange={change}>
      <RadixTabs.List aria-label={label} className="flex gap-6 overflow-x-auto border-b border-border">
        {tabs.map((tab) => (
          <RadixTabs.Trigger
            key={tab.value}
            value={tab.value}
            className="-mb-px min-h-11 border-b-[2.5px] border-transparent pb-2.5 text-button whitespace-nowrap text-text-secondary transition-colors hover:text-text-primary data-[state=active]:border-navy-900 data-[state=active]:font-bold data-[state=active]:text-navy-900"
          >
            {tab.label}
          </RadixTabs.Trigger>
        ))}
      </RadixTabs.List>
      {tabs.map((tab) => (
        <RadixTabs.Content key={tab.value} value={tab.value} forceMount hidden={tab.value !== value} className="pt-5 outline-none">
          {tab.content}
        </RadixTabs.Content>
      ))}
    </RadixTabs.Root>
  );
}
