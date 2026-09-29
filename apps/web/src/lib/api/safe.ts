/**
 * Runs an optional public read: a failure (API unreachable, 5xx) yields `null` and is logged, so a secondary section of a
 * public page degrades to its empty state instead of taking the whole page down.
 */
export async function optional<T>(
  read: () => Promise<{ data?: T; response: Response }>,
  what: string,
): Promise<T | null> {
  try {
    const result = await read();
    return result.response.ok && result.data !== undefined ? result.data : null;
  } catch (error) {
    console.error(`Public read failed: ${what}`, error instanceof Error ? error.message : error);
    return null;
  }
}
