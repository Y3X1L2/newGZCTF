import { describe, expect, it } from 'vitest'
import type { AdminLogEntry } from '../api'
import { adminLogKey, mergeAdminLogs } from './adminLogPresentation'

const log: AdminLogEntry = {
  id: 1,
  time: 100,
  name: 'admin',
  level: 'Information',
  ip: '10.24.0.27',
  msg: 'Runtime started.',
  status: 'Success',
}

describe('admin log presentation', () => {
  it('prefers durable ids and creates a stable fallback key', () => {
    expect(adminLogKey(log)).toBe('id:1')
    expect(adminLogKey({ ...log, id: undefined })).toBe(adminLogKey({ ...log, id: undefined }))
  })

  it('deduplicates and sorts merged history and realtime logs', () => {
    expect(mergeAdminLogs([log], [{ ...log }, { ...log, id: 2, time: 200 }]).map((item) => item.id)).toEqual([2, 1])
  })
})
