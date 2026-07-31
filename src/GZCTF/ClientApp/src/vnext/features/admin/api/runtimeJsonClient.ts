import api, { ContentType, FullRequestParams } from '@Api'

export type RuntimeQueryValue = string | number | boolean | null | undefined
export type RuntimeQuery = Record<string, RuntimeQueryValue>

export interface RuntimeUploadOptions {
  signal?: AbortSignal
  onProgress?: (ratio: number | null) => void
}

export type RuntimeApiErrorKind = 'contract' | 'http' | 'network'

interface RuntimeApiErrorOptions {
  kind: RuntimeApiErrorKind
  status?: number
  code?: string
  payload?: unknown
}

export class RuntimeApiError extends Error {
  readonly kind: RuntimeApiErrorKind
  readonly status?: number
  readonly code?: string
  readonly payload?: unknown

  constructor(message: string, options: RuntimeApiErrorOptions) {
    super(message)
    this.name = 'RuntimeApiError'
    this.kind = options.kind
    this.status = options.status
    this.code = options.code
    this.payload = options.payload
  }
}

export interface RuntimeJsonClient {
  get(path: string, query?: RuntimeQuery): Promise<unknown>
  postJson(path: string, body?: unknown, query?: RuntimeQuery): Promise<unknown>
  postJsonWithHeaders?(
    path: string,
    body: unknown,
    headers: Readonly<Record<string, string>>,
    query?: RuntimeQuery
  ): Promise<unknown>
  postForm(
    path: string,
    body: Record<string, unknown>,
    query?: RuntimeQuery,
    options?: RuntimeUploadOptions
  ): Promise<unknown>
  putJson(path: string, body: unknown, query?: RuntimeQuery): Promise<unknown>
  patchJson(path: string, body: unknown, query?: RuntimeQuery): Promise<unknown>
  deleteJson?(path: string, query?: RuntimeQuery): Promise<unknown>
  delete(path: string, query?: RuntimeQuery): Promise<void>
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function responseContentType(headers: unknown) {
  if (!isRecord(headers)) return ''
  const value = headers['content-type']
  return typeof value === 'string' ? value.toLowerCase() : ''
}

function responseError(error: unknown) {
  if (error instanceof RuntimeApiError) return error

  if (isRecord(error)) {
    const response = isRecord(error.response) ? error.response : null
    const status = typeof response?.status === 'number' ? response.status : undefined
    const payload = response?.data
    const payloadRecord = isRecord(payload) ? payload : null
    const code = typeof payloadRecord?.code === 'string' ? payloadRecord.code : undefined
    const message =
      (typeof payloadRecord?.message === 'string' && payloadRecord.message) ||
      (typeof payloadRecord?.title === 'string' && payloadRecord.title) ||
      (typeof error.message === 'string' && error.message) ||
      'API request failed.'

    return new RuntimeApiError(message, {
      kind: status === undefined ? 'network' : 'http',
      status,
      code,
      payload,
    })
  }

  return new RuntimeApiError('API request failed.', { kind: 'network', payload: error })
}

async function requestJson(params: FullRequestParams, allowEmpty: boolean) {
  try {
    const response = await api.request<unknown>(params)
    if (response.status === 204 || response.data === undefined || response.data === '') {
      if (allowEmpty) return undefined
      throw new RuntimeApiError(`Expected JSON from ${params.path}, but the response was empty.`, {
        kind: 'contract',
        status: response.status,
        code: 'empty_response',
      })
    }

    const contentType = responseContentType(response.headers)
    if (!contentType.includes('/json') && !contentType.includes('+json')) {
      throw new RuntimeApiError(
        `Expected JSON from ${params.path}, but received ${contentType || 'unknown content'}.`,
        {
          kind: 'contract',
          status: response.status,
          code: 'non_json_response',
          payload: typeof response.data === 'string' ? response.data.slice(0, 240) : response.data,
        }
      )
    }

    return response.data
  } catch (error) {
    throw responseError(error)
  }
}

export function isUnavailableEndpoint(error: unknown) {
  return (
    error instanceof RuntimeApiError &&
    (error.status === 404 || error.code === 'non_json_response' || error.code === 'empty_response')
  )
}

export const runtimeJsonClient: RuntimeJsonClient = {
  get(path, query) {
    return requestJson({ path, method: 'GET', query }, false)
  },
  postJson(path, body, query) {
    return requestJson({ path, method: 'POST', query, body, type: ContentType.Json }, true)
  },
  postJsonWithHeaders(path, body, headers, query) {
    return requestJson({ path, method: 'POST', query, body, headers: { ...headers }, type: ContentType.Json }, true)
  },
  postForm(path, body, query, options) {
    const onUploadProgress: FullRequestParams['onUploadProgress'] = options?.onProgress
      ? (event) => options.onProgress?.(event.total ? Math.min(1, event.loaded / event.total) : null)
      : undefined
    return requestJson(
      { path, method: 'POST', query, body, type: ContentType.FormData, signal: options?.signal, onUploadProgress },
      true
    )
  },
  putJson(path, body, query) {
    return requestJson({ path, method: 'PUT', query, body, type: ContentType.Json }, true)
  },
  patchJson(path, body, query) {
    return requestJson({ path, method: 'PATCH', query, body, type: ContentType.Json }, true)
  },
  deleteJson(path, query) {
    return requestJson({ path, method: 'DELETE', query }, false)
  },
  async delete(path, query) {
    await requestJson({ path, method: 'DELETE', query }, true)
  },
}
