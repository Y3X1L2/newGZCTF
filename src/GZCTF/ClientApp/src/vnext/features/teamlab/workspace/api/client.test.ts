import { afterEach, describe, expect, it, vi } from 'vitest'
import { createWorkspaceApi } from './client'

afterEach(() => vi.unstubAllGlobals())

describe('Netlab workspace API context', () => {
  it('keeps requests, downloads and stream URLs bound to their workspace', async () => {
    const fetch = vi.fn(async () => Response.json({ id: 'environment' }))
    vi.stubGlobal('fetch', fetch)
    const admin = createWorkspaceApi('/api/teamlab/netlab/admin/api/v1')
    const player = createWorkspaceApi('/api/teamlab/netlab/game/12/api/v1')
    await admin.environment('one')
    await player.environment('two')
    await admin.environment('three')
    expect(fetch.mock.calls.map((call) => (call as unknown as [string])[0])).toEqual([
      '/api/teamlab/netlab/admin/api/v1/environments/one',
      '/api/teamlab/netlab/game/12/api/v1/environments/two',
      '/api/teamlab/netlab/admin/api/v1/environments/three',
    ])
    expect(player.fileURL('two', 'host', '/var/log/app log')).toBe(
      '/api/teamlab/netlab/game/12/api/v1/environments/two/assets/host/files/content?path=%2Fvar%2Flog%2Fapp%20log'
    )
    expect(player.path('/environments/two/assets/host/console')).toBe(
      '/api/teamlab/netlab/game/12/api/v1/environments/two/assets/host/console'
    )
    const opened: string[] = []
    vi.stubGlobal(
      'EventSource',
      class {
        constructor(url: string) {
          opened.push(url)
        }
      }
    )
    player.events('two')
    expect(opened).toEqual(['/api/teamlab/netlab/game/12/api/v1/environments/two/events'])
  })

  it('preserves server authorization failures for the caller', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => Response.json({ detail: '当前令牌没有操作权限' }, { status: 403 }))
    )
    const api = createWorkspaceApi('/api/teamlab/netlab/admin/api/v1')
    await expect(api.environment('environment')).rejects.toMatchObject({ status: 403, message: '当前令牌没有操作权限' })
  })
})
