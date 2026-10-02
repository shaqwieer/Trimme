'use client';

import { Tabs as RadixTabs } from 'radix-ui';
import type { ReactNode } from 'react';
import { cn } from '@/lib/cn';

type TabItem = { value: string; label: ReactNode; content: ReactNode };

type TabsProps = {
  items: TabItem[];
  defaultValue?: string;
  value?: string;
  onValueChange?: (value: string) => void;
  /** Accessible name of the tab list. */
  label: string;
  className?: string;
};

/**
 * Underline tabs (design 452–457) on Radix Tabs: roving focus with arrow keys, mirrored in RTL
 * through the Direction provider. For URL-driven tabs use <LinkTabs>.
 *
 * The baseline is an inset shadow, not a border, and the triggers do not pull down with `-mb-px`: in a horizontal
 * scroller (`overflow-x: auto` makes `overflow-y` auto too) that 1px overlap overflowed vertically, and Windows drew
 * scrollbars on every tab strip. The active trigger's 2.5px underline still covers the baseline.
 */
export function Tabs({ items, defaultValue, value, onValueChange, label, className }: TabsProps) {
  return (
    <RadixTabs.Root
      defaultValue={defaultValue ?? items[0]?.value}
      value={value}
      onValueChange={onValueChange}
      className={className}
    >
      <RadixTabs.List
        aria-label={label}
        className="flex gap-6 overflow-x-auto shadow-[inset_0_-1px_0_var(--color-border)]"
      >
        {items.map((item) => (
          <RadixTabs.Trigger
            key={item.value}
            value={item.value}
            className={cn(
              'min-h-11 border-b-[2.5px] border-transparent pb-2.5 text-button whitespace-nowrap text-text-secondary transition-colors',
              'hover:text-text-primary data-[state=active]:border-navy-900 data-[state=active]:font-bold data-[state=active]:text-navy-900',
            )}
          >
            {item.label}
          </RadixTabs.Trigger>
        ))}
      </RadixTabs.List>
      {items.map((item) => (
        <RadixTabs.Content key={item.value} value={item.value} className="pt-5 outline-none">
          {item.content}
        </RadixTabs.Content>
      ))}
    </RadixTabs.Root>
  );
}
