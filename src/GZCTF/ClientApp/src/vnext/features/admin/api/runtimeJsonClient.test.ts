import { afterEach, describe, expect, it, vi } from 'vitest'
import api from '@Api'
import { runtimeJsonClient } from './runtimeJsonClient'

describe('runtimeJsonClient', () => {
  afterEach(() => vi.restoreAllMocks())

  it('rejects SPA HTML returned with HTTP 200 as an unavailable API route', async () => {
    vi.spyOn(api, 'request').mockResolvedValue({
      status: 200,
      data: '<!doctype html><html><body>SPA fallback</body></html>',
      headers: { 'content-type': 'text/html; charset=utf-8' },
    } as never)

    await expect(runtimeJsonClient.get('/api/v1/missing')).rejects.toMatchObject({
      kind: 'contract',
      status: 200,
      code: 'non_json_response',
    })
  })

  it('accepts a structured JSON response', async () => {
    vi.spyOn(api, 'request').mockResolvedValue({
      status: 200,
      data: { ok: true },
      headers: { 'content-type': 'application/json; charset=utf-8' },
    } as never)

    await expect(runtimeJsonClient.get('/api/health')).resolves.toEqual({ ok: true })
  })

  it('forwards upload progress and cancellation to the generated client', async () => {
    const onProgress = vi.fn()
    const controller = new AbortController()
    const request = vi.spyOn(api, 'request').mockImplementation(async (params) => {
      expect(params.signal).toBe(controller.signal)
      params.onUploadProgress?.({ loaded: 50, total: 200 } as never)
      params.onUploadProgress?.({ loaded: 300, total: 200 } as never)
      params.onUploadProgress?.({ loaded: 10, total: 0 } as never)
      return {
        status: 200,
        data: { id: 1 },
        headers: { 'content-type': 'application/json' },
      } as never
    })

    await runtimeJsonClient.postForm(
      '/api/v1/image-templates/upload',
      { file: new File(['archive'], 'image.tgz') },
      undefined,
      { signal: controller.signal, onProgress }
    )

    expect(request).toHaveBeenCalledOnce()
    expect(onProgress.mock.calls).toEqual([[0.25], [1], [null]])
  })
})
