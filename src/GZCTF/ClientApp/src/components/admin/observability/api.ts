import { OperationalCorrelationSummary, OperationalEventPage } from './types'

async function getJson<T>(url: string): Promise<T> {
  const response = await fetch(url)
  if (!response.ok) {
    const body = await response.json().catch(() => ({}))
    throw new Error(body.message || body.title || `Request failed with ${response.status}`)
  }
  return (await response.json()) as T
}

export const fetchOperationalEvents = (url: string) => getJson<OperationalEventPage>(url)

export const fetchCorrelationSummary = (url: string) => getJson<OperationalCorrelationSummary>(url)
