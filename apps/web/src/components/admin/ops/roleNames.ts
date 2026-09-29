import { isSeedRole } from '@/lib/admin/admin';

/** A role's display name: seed roles are translated (SuperAdmin = مدير عام, D-019), custom roles show their own name. */
export function roleLabel(
  name: string,
  translate: (key: 'SuperAdmin' | 'OperationsManager' | 'Support') => string,
): string {
  return isSeedRole(name) && (name === 'SuperAdmin' || name === 'OperationsManager' || name === 'Support')
    ? translate(name)
    : name;
}
