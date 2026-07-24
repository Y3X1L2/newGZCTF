import { useCallback } from 'react'
import { useSearchParams } from 'react-router'

export function positiveInteger(value: string | null, fallback: number) {
  const parsed = Number(value)
  return Number.isInteger(parsed) && parsed > 0 ? parsed : fallback
}

export function numericEnumValue<T extends number>(value: string | null, allowed: readonly T[]) {
  if (value === null || value.trim() === '') return undefined
  const parsed = Number(value)
  return allowed.includes(parsed as T) ? (parsed as T) : undefined
}

export function patchQuery(
  current: URLSearchParams,
  patch: Record<string, string | number | null | undefined>,
  resetPage = true
) {
  const next = new URLSearchParams(current)
  for (const [key, value] of Object.entries(patch)) {
    if (value === null || value === undefined || value === '') next.delete(key)
    else next.set(key, String(value))
  }
  if (resetPage && !Object.prototype.hasOwnProperty.call(patch, 'page')) next.delete('page')
  return next
}

export function useAdminQueryState(defaultPageSize = 20) {
  const [params, setParams] = useSearchParams()
  const page = positiveInteger(params.get('page'), 1)
  const pageSize = positiveInteger(params.get('pageSize'), defaultPageSize)

  const update = useCallback(
    (
      patch: Record<string, string | number | null | undefined>,
      options: { replace?: boolean; resetPage?: boolean } = {}
    ) => {
      setParams(patchQuery(params, patch, options.resetPage ?? true), { replace: options.replace })
    },
    [params, setParams]
  )

  const setPage = useCallback(
    (nextPage: number) => {
      update({ page: nextPage <= 1 ? null : nextPage }, { resetPage: false })
      window.scrollTo({ top: 0, behavior: 'smooth' })
    },
    [update]
  )

  return { params, page, pageSize, update, setPage }
}
