import { useVNextTheme } from '../../../../app/VNextThemeProvider'
import { SankeyChart } from 'echarts/charts'
import { TooltipComponent } from 'echarts/components'
import { init, use } from 'echarts/core'
import { CanvasRenderer } from 'echarts/renderers'
import { useEffect, useRef } from 'react'
import type { TeamLabRuntimeAsset, TeamLabTrafficFlow } from '../api'
import { formatBytes } from './runtimePresentation'
import styles from './RuntimeWorkspaces.module.css'

use([SankeyChart, TooltipComponent, CanvasRenderer])

export function TrafficConnectionChart({ flows, assets, onSelect }: {
  flows: readonly TeamLabTrafficFlow[]; assets: readonly TeamLabRuntimeAsset[]
  onSelect: (selection: { address: string } | { source: string; destination: string }) => void
}) {
  const element = useRef<HTMLDivElement>(null)
  const { theme } = useVNextTheme()
  useEffect(() => {
    const node = element.current!
    const chart = init(node)
    const tokens = getComputedStyle(node)
    const color = (name: string) => tokens.getPropertyValue(name).trim()
    const palette = [color('--yn-color-info'), color('--yn-color-brand'), color('--yn-color-warning')]
    const names = new Map(assets.filter(asset => asset.primaryIp).map(asset => [asset.primaryIp!, asset.name]))
    const links = new Map<string, { source: string; target: string; value: number }>()
    for (const flow of flows) {
      const key = `${flow.sourceIp}|${flow.destinationIp}`
      const link = links.get(key) ?? { source: `source:${flow.sourceIp}`, target: `destination:${flow.destinationIp}`, value: 0 }
      link.value += flow.bytes
      links.set(key, link)
    }
    const address = (name: string) => name.slice(name.indexOf(':') + 1)
    const label = (name: string) => names.get(address(name)) ?? address(name)
    chart.setOption({
      animation: !window.matchMedia('(prefers-reduced-motion: reduce)').matches,
      color: palette,
      tooltip: { trigger: 'item', renderMode: 'richText', backgroundColor: color('--yn-color-surface'), borderColor: color('--yn-color-border'), textStyle: { color: color('--yn-color-text') },
        formatter: (item: { dataType: string; name: string; data: { source?: string; target?: string; value?: number } }) => item.dataType === 'edge'
          ? `${label(item.data.source!)} → ${label(item.data.target!)}\n${formatBytes(item.data.value!)}` : `${label(item.name)}\n${address(item.name)}` },
      series: [{ type: 'sankey', left: 12, right: 12, top: 12, bottom: 12, nodeWidth: 10, nodeGap: 22, draggable: false,
        data: [...new Set([...links.values()].flatMap(link => [link.source, link.target]))].map((name, index) => ({ name, itemStyle: { color: palette[index % palette.length] } })),
        links: [...links.values()], lineStyle: { color: 'source', opacity: 0.2, curveness: 0.5 },
        label: { position: 'inside', color: color('--yn-color-text'), fontSize: 12, formatter: (item: { name: string }) => label(item.name) },
        emphasis: { focus: 'adjacency', lineStyle: { opacity: 0.5 } },
        levels: [{ depth: 0, label: { position: 'right' } }, { depth: 1, label: { position: 'left' } }],
      }],
    })
    chart.on('click', (item: unknown) => {
      const selected = item as { dataType: string; name: string; data: { source: string; target: string } }
      onSelect(selected.dataType === 'edge' ? { source: address(selected.data.source), destination: address(selected.data.target) } : { address: address(selected.name) })
    })
    const observer = new ResizeObserver(() => chart.resize())
    observer.observe(node)
    return () => { observer.disconnect(); chart.dispose() }
  }, [assets, flows, onSelect, theme])
  return <div className={styles.chart} ref={element} role="img" aria-label="当前流量记录的源端点、目标端点和通信量；下方表格提供全部记录" />
}
