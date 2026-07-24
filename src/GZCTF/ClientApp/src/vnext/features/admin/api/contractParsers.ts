import { RuntimeApiError } from './runtimeJsonClient'

export function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

export function isString(value: unknown): value is string {
  return typeof value === 'string'
}

export function isNumber(value: unknown): value is number {
  return typeof value === 'number' && Number.isFinite(value)
}

export function isBoolean(value: unknown): value is boolean {
  return typeof value === 'boolean'
}

export function isOptionalBoolean(value: unknown): value is boolean | null | undefined {
  return value === undefined || value === null || isBoolean(value)
}

export function isNullableString(value: unknown): value is string | null {
  return value === null || isString(value)
}

export function isNullableNumber(value: unknown): value is number | null {
  return value === null || isNumber(value)
}

export function isOptionalString(value: unknown): value is string | null | undefined {
  return value === undefined || isNullableString(value)
}

export function isOptionalNumber(value: unknown): value is number | null | undefined {
  return value === undefined || isNullableNumber(value)
}

export function isStringArray(value: unknown): value is string[] {
  return Array.isArray(value) && value.every(isString)
}

export function contractFailure(label: string, payload: unknown): never {
  throw new RuntimeApiError(`${label} returned an unexpected response shape.`, {
    kind: 'contract',
    code: 'invalid_response_shape',
    payload,
  })
}

export function parseRecordArray<T>(value: unknown, guard: (item: unknown) => item is T, label: string) {
  if (!Array.isArray(value) || !value.every(guard)) return contractFailure(label, value)
  return value
}

export function parseNumberPage<T>(value: unknown, guard: (item: unknown) => item is T, label: string) {
  if (
    !isRecord(value) ||
    !isNumber(value.total) ||
    !isNumber(value.page) ||
    !isNumber(value.pageSize) ||
    !Array.isArray(value.items) ||
    !value.items.every(guard)
  ) {
    return contractFailure(label, value)
  }

  return {
    total: value.total,
    page: value.page,
    pageSize: value.pageSize,
    items: value.items,
  }
}
