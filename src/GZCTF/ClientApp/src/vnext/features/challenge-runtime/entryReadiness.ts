export const PUBLIC_ENTRY_SYNC_GRACE_MS = 8_000

export function publicEntryAvailableAt(now = Date.now()) {
  return now + PUBLIC_ENTRY_SYNC_GRACE_MS
}
