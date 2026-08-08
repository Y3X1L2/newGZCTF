import { Container, Monitor, MonitorCog, Network, Router } from 'lucide-react'
import type { TopologyNodeType } from '../../model/topologyDocument'
import styles from './NodePalette.module.css'

export const teamLabPaletteMime = 'application/x-gzctf-teamlab-node'

const items: readonly {
  type: TopologyNodeType
  name: string
  description: string
  category: '网络基础设施' | '计算资产'
  icon: typeof Network
}[] = [
  { type: 'switch', name: '交换机', description: '承载一个隔离网段', category: '网络基础设施', icon: Network },
  { type: 'router', name: '路由器', description: '连接多个网段', category: '网络基础设施', icon: Router },
  { type: 'docker', name: 'Docker', description: '轻量容器服务', category: '计算资产', icon: Container },
  { type: 'linux-vm', name: 'Linux 虚拟机', description: '运行 Linux 系统的虚拟机', category: '计算资产', icon: MonitorCog },
  { type: 'windows-vm', name: 'Windows 虚拟机', description: '运行 Windows 系统的虚拟机', category: '计算资产', icon: Monitor },
]

export function NodePalette({ onAdd, disabled = false }: { onAdd: (type: TopologyNodeType) => void; disabled?: boolean }) {
  return (
    <aside aria-label="节点库" className={styles.palette}>
      <div className={styles.groups}>
        {(['网络基础设施', '计算资产'] as const).map((category) => (
          <section aria-label={category} key={category}>
            {items.filter((item) => item.category === category).map((item) => {
              const Icon = item.icon
              const label = `${item.name}：${item.description}`
              return (
                <button
                  aria-label={label}
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
