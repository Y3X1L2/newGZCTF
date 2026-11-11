import { useState, type CSSProperties } from 'react'
import { MetricChart } from '../../foundation/MetricChart'
import type { History } from '../../foundation/metrics'
import styles from './TrafficWorkspace.module.css'

const measures: History['series'][number]['metric'][] = ['receive', 'transmit']

export function TrafficTimeline({
  history,
  names,
  colors,
  ...props
}: Omit<Parameters<typeof MetricChart>[0], 'measures' | 'seriesBy' | 'timeline' | 'showLegend' | 'focusedAsset'> & {
  names: ReadonlyMap<string, string>
  colors: string[]
}) {
  const [focusedAsset, setFocusedAsset] = useState<string>()
  const identities = [...new Set([...names.keys(), ...history.series.map((series) => series.assetId)])]
  const focus = identities.includes(focusedAsset ?? '') ? focusedAsset : undefined
  return (
    <>
      <div className={styles.assetLegend} aria-label="流量资产筛选">
        {identities.map((id, index) => (
          <button
            key={id}
            aria-pressed={focus === id}
            onClick={() => setFocusedAsset(focus === id ? undefined : id)}
            style={{ '--series-color': colors[index % colors.length] } as CSSProperties}
          >
            <i />
            {names.get(id) ?? '历史资产'}
          </button>
        ))}
      </div>
      <div className={styles.chart}>
        <MetricChart
          {...props}
          history={history}
          names={names}
          colors={colors}
          measures={measures}
          seriesBy="asset"
          timeline
          showLegend={false}
          focusedAsset={focus}
        />
      </div>
    </>
  )
}
