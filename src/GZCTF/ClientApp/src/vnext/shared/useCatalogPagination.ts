import { useEffect } from 'react'
import { useSearchParams } from 'react-router'

export function useCatalogPagination(actualPage?: number) {
  const [params, setParams] = useSearchParams()
  const rawPage = Number(params.get('page') ?? 1)
  const rawSize = Number(params.get('pageSize') ?? 20)
  const page = Number.isInteger(rawPage) && rawPage >= 1 && rawPage <= 1000000 ? rawPage : 1
  const pageSize = [20, 50, 100].includes(rawSize) ? rawSize : 20

  useEffect(() => {
    if (rawPage === page && rawSize === pageSize && (!actualPage || actualPage === page)) return
    const next = new URLSearchParams(params)
    next.set('page', String(actualPage ?? page))
    next.set('pageSize', String(pageSize))
    setParams(next, { replace: true })
  }, [actualPage, page, pageSize, params, rawPage, rawSize, setParams])

  return {
    page, pageSize,
    setPage: (value: number) => {
      const next = new URLSearchParams(params)
      next.set('page', String(value))
      setParams(next)
    },
    setPageSize: (value: number) => {
      const next = new URLSearchParams(params)
      next.delete('page')
      next.set('pageSize', String(value))
      setParams(next)
    },
  }
}
