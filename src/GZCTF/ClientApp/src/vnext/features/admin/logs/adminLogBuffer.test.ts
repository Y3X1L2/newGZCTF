import { describe, expect, it } from 'vitest'
import type { AdminLogEntry } from '../api'
import { appendAdminLogBuffer, MAX_BUFFERED_ADMIN_LOGS } from './adminLogBuffer'

function createLog(id: number): AdminLogEntry {
  return {
    id,
    time: id,
    name: 'admin',
    level: 'Information',
    ip: '10.24.0.27',
    msg: `Runtime event ${id}`,
    status: 'Success',
  }
}

describe('admin log buffer', () => {
  it('keeps the newest 500 unique logs and reports evictions', () => {
    let items: AdminLogEntry[] = []
    let dropped = 0

    for (let id = 1; id <= MAX_BUFFERED_ADMIN_LOGS + 1; id += 1) {
      const result = appendAdminLogBuffer(items, createLog(id))
      items = result.items
      dropped += result.dropped
    }

    expect(items).toHaveLength(MAX_BUFFERED_ADMIN_LOGS)
    expect(items[0]?.id).toBe(MAX_BUFFERED_ADMIN_LOGS + 1)
    expect(items.at(-1)?.id).toBe(2)
    expect(dropped).toBe(1)
  })

  it('does not duplicate an existing log or report it as dropped', () => {
    const existing = [createLog(2), createLog(1)]
    const result = appendAdminLogBuffer(existing, createLog(2))

    expect(result.items).toBe(existing)
    expect(result.dropped).toBe(0)
  })

  it('supports a smaller explicit limit for deterministic callers', () => {
    const result = appendAdminLogBuffer([createLog(2), createLog(1)], createLog(3), 2)

    expect(result.items.map((item) => item.id)).toEqual([3, 2])
    expect(result.dropped).toBe(1)
  })
})
