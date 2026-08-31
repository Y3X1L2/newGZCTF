import { Container, Monitor, MonitorCog, Network, Router } from 'lucide-react'
import type { TopologyNodeType } from '../../model/topologyDocument'
import styles from './NodePalette.module.css'

export const teamLabPaletteMime = 'application/x-gzctf-teamlab-node'

type PaletteCategory = '网络基础设施' | '计算资产'

const categories: readonly PaletteCategory[] = ['网络基础设施', '计算资产']

const items: readonly {
  type: TopologyNodeType
  name: string
  description: string
  category: PaletteCategory
  icon: typeof Network
}[] = [
  { type: 'switch', name: '交换机', description: '承载一个隔离网段', category: '网络基础设施', icon: Network },
  { type: 'router', name: '路由器', description: '连接多个网段', category: '网络基础设施', icon: Router },
  { type: 'docker', name: 'Docker', description: '轻量容器服务', category: '计算资产', icon: Container },
  { type: 'linux-vm', name: 'Linux 虚拟机', description: '运行 Linux 系统的虚拟机', category: '计算资产', icon: MonitorCog },
  { type: 'windows-vm', name: 'Windows 虚拟机', description: '运行 Windows 系统的虚拟机', category: '计算资产', icon: Monitor },
]

/**
 * Device library.
 *
 * Collapsed (the default) it is an icon rail with hover tooltips, so the canvas
 * keeps the maximum width. Expanded — which focus mode requests — it shows the
 * name and purpose inline, because in focus mode the inspector is out of the way
 * and there is room to read what each device does before dragging it out.
 */
export function NodePalette({
  onAdd,
  disabled = false,
  expanded = false,
}: {
  onAdd: (type: TopologyNodeType) => void
  disabled?: boolean
  expanded?: boolean
}) {
  return (
    <aside aria-label="节点库" className={styles.palette} data-expanded={expanded || undefined}>
      <div className={styles.groups}>
        {categories.map((category) => (
          <section aria-label={category} key={category}>
            {expanded ? <h3 className={styles.groupTitle}>{category}</h3> : null}
            {items
              .filter((item) => item.category === category)
              .map((item) => {
                const Icon = item.icon
                return (
                  <button
                    aria-label={`${item.name}：${item.description}`}
                    className={styles.item}
                    disabled={disabled}
                    draggable={!disabled}
                    key={item.type}
                    onClick={() => onAdd(item.type)}
                    onDragStart={(event) => {
                      event.dataTransfer.effectAllowed = 'copy'
                      event.dataTransfer.setData(teamLabPaletteMime, item.type)
                    }}
                    type="button"
                  >
                    <Icon aria-hidden="true" size={20} />
                    <span className={styles.tooltip} role="tooltip">
                      <strong>{item.name}</strong>
                      <small>{item.description}</small>
                    </span>
                  </button>
                )
              })}
          </section>
        ))}
      </div>
    </aside>
  )
}
