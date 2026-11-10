import { useSearchParams } from './../routing'

export const metricRanges = [
  { label: '15 分钟', value: '900' },
  { label: '1 小时', value: '3600' },
  { label: '6 小时', value: '21600' },
  { label: '24 小时', value: '86400' },
  { label: '7 天', value: '604800' },
  { label: '30 天', value: '2592000' },
]

export function useMetricWindow() {
  const [params, setParams] = useSearchParams()
  const range = metricRanges.find((item) => item.value === params.get('metricsRange'))?.value ?? '900'
  const live = params.get('metricsLive') !== '0'
  const update = (key: string, value: string) =>
    setParams(
      (current) => {
        const next = new URLSearchParams(current)
        next.set(key, value)
        return next
      },
      { replace: true }
    )
  return {
    range,
    live,
    setRange: (value: string) => update('metricsRange', value),
    setLive: (value: boolean) => update('metricsLive', value ? '1' : '0'),
  }
}
