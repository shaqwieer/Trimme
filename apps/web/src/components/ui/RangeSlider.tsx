'use client';

import { Slider } from 'radix-ui';
import type { ReactNode } from 'react';

type RangeSliderProps = {
  label: ReactNode;
  min: number;
  max: number;
  step?: number;
  value: [number, number];
  onValueChange: (value: [number, number]) => void;
  /** Accessible names for the two thumbs, e.g. "Minimum price" / "Maximum price". */
  thumbLabels: [string, string];
  /** Formats the current values for display, e.g. price with currency (D-040). */
  format: (value: number) => string;
};

/**
 * Dual-thumb range (design filter drawer "25 — 90 ر.س"). Radix Slider reads the Direction provider,
 * so in Arabic the minimum sits on the right and arrow keys follow the reading direction.
 */
export function RangeSlider({
  label,
  min,
  max,
  step = 5,
  value,
  onValueChange,
  thumbLabels,
  format,
}: RangeSliderProps) {
  return (
    <div className="flex flex-col gap-3">
      <div className="flex items-center justify-between gap-3">
        <span className="text-label font-bold text-text-primary">{label}</span>
        <span className="font-latin text-label font-semibold text-text-strong">
          {format(value[0])} — {format(value[1])}
        </span>
      </div>
      <Slider.Root
        min={min}
        max={max}
        step={step}
        value={value}
        minStepsBetweenThumbs={1}
        onValueChange={(next) => onValueChange([next[0] ?? min, next[1] ?? max])}
        className="relative flex h-11 touch-none items-center select-none"
      >
        <Slider.Track className="relative h-1.5 grow rounded-full bg-bg-subtle">
          <Slider.Range className="absolute h-full rounded-full bg-brand-500" />
        </Slider.Track>
        {thumbLabels.map((thumbLabel, index) => (
          <Slider.Thumb
            key={thumbLabel}
            aria-label={thumbLabel}
            aria-valuetext={format(value[index] ?? min)}
            className="block size-6 rounded-full border-2 border-navy-900 bg-surface shadow-e2 transition-transform hover:scale-110"
          />
        ))}
      </Slider.Root>
    </div>
  );
}
