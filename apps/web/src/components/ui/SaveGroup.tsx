'use client';

import { useTranslations } from 'next-intl';
import {
  createContext,
  type ReactNode,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
} from 'react';
import { Button } from '@/components/ui/Button';
import { InlineAlert } from '@/components/ui/states';

/** What a form in the group gives it: whether it has unsaved changes, and how to save them (true when saved). */
type Member = { dirty: boolean; save: () => Promise<boolean> };

type Group = { report: (id: string, member: Member | null) => void };

const SaveGroupContext = createContext<Group | null>(null);

/**
 * One save for several forms on a page (D-155, the client's 3-8: the barber page had «حفظ الإسناد» and «حفظ الملف»,
 * and saving one lost the other's unsaved changes without a word). The forms inside hide their own buttons; this group
 * shows «عندك تعديلات ما انحفظت» while any of them has changes, saves the changed ones in page order with one button,
 * and asks before the tab is closed or reloaded with changes left.
 */
export function SaveGroup({ children, testId }: { children: ReactNode; testId?: string }) {
  const t = useTranslations('ui.saveGroup');
  const members = useRef(new Map<string, Member>());
  const [dirtyIds, setDirtyIds] = useState<string[]>([]);
  const [pending, setPending] = useState(false);
  const [result, setResult] = useState<'saved' | 'failed'>();

  const report = useCallback((id: string, member: Member | null) => {
    if (member) members.current.set(id, member);
    else members.current.delete(id);
    const dirty = member?.dirty ?? false;
    setDirtyIds((current) =>
      dirty === current.includes(id) ? current : dirty ? [...current, id] : current.filter((x) => x !== id),
    );
    if (dirty) setResult(undefined);
  }, []);
  const group = useMemo(() => ({ report }), [report]);
  const dirty = dirtyIds.length > 0;

  useEffect(() => {
    if (!dirty) return;
    const warn = (event: BeforeUnloadEvent) => event.preventDefault();
    window.addEventListener('beforeunload', warn);
    return () => window.removeEventListener('beforeunload', warn);
  }, [dirty]);

  const saveAll = async () => {
    setPending(true);
    setResult(undefined);
    let ok = true;
    // In the order the forms were added to the page; one that fails stops the rest, so nothing is lost.
    for (const [, member] of members.current) {
      if (!member.dirty) continue;
      if (!(await member.save())) {
        ok = false;
        break;
      }
    }
    setPending(false);
    setResult(ok ? 'saved' : 'failed');
  };

  return (
    <SaveGroupContext.Provider value={group}>
      {children}
      <div
        className="sticky bottom-[calc(var(--layout-bottom-nav-height)+0.75rem+env(safe-area-inset-bottom))] z-10 flex flex-col gap-3 rounded-card border border-border bg-surface p-4 shadow-e2 lg:bottom-4"
        data-testid={testId ?? 'save-group'}
        data-dirty={dirty}
      >
        <p role="status" className={dirty ? 'text-label font-bold text-warning-700' : 'sr-only'}>
          {dirty ? t('unsaved') : ''}
        </p>
        {!dirty && result === 'saved' && <InlineAlert tone="success" title={t('saved')} />}
        <Button
          icon="check"
          loading={pending}
          disabled={!dirty}
          onClick={() => void saveAll()}
          className="self-start"
        >
          {t('save')}
        </Button>
      </div>
    </SaveGroupContext.Provider>
  );
}

/**
 * A form's link to the page's {@link SaveGroup}, if it is in one: it reports its changes and its save, and returns
 * whether it is grouped (then it hides its own button). Outside a group it does nothing.
 */
export function useSaveGroup(id: string, dirty: boolean, save: () => Promise<boolean>): boolean {
  const group = useContext(SaveGroupContext);
  const saveRef = useRef(save);
  // The latest save, read when the group's button is pressed (never during render).
  useEffect(() => {
    saveRef.current = save;
  });
  useEffect(() => {
    group?.report(id, { dirty, save: () => saveRef.current() });
  }, [group, id, dirty]);
  useEffect(() => () => group?.report(id, null), [group, id]);
  return group !== null;
}
