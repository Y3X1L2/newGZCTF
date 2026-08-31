import { Handle, Position } from '@xyflow/react'
import type { ReactNode } from 'react'
import styles from './TopologyNodeShell.module.css'

export type NodeTone = 'switch' | 'router' | 'docker' | 'linux' | 'windows'

const infrastructureTones = new Set<NodeTone>(['switch', 'router'])

export function TopologyNodeShell({
  icon,
  eyebrow,
  title,
  details,
  badge,
  selected,
  readOnly,
  tone,
}: {
  icon: ReactNode
  eyebrow: string
  title: string
  details: readonly string[]
  badge?: string
  selected: boolean
  readOnly: boolean
  tone: NodeTone
}) {
  const shape = infrastructureTones.has(tone) ? styles.infrastructure : ''
  return (
    <article
      className={`${styles.node} ${styles[tone]} ${shape} ${selected ? styles.selected : ''}`}
      data-tone={tone}
    >
      <Handle
        className={styles.handle}
        id="target"
        isConnectable={!readOnly}
        position={Position.Left}
        type="target"
      />
      <header className={styles.header}>
        <span className={styles.icon}>{icon}</span>
        <span className={styles.heading}>
          <small>{eyebrow}</small>
          <strong title={title}>{title}</strong>
        </span>
        {badge ? <span className={styles.badge}>{badge}</span> : null}
      </header>
      {details.length > 0 ? (
        <div className={styles.details}>
          {details.map((detail) => (
            <span key={detail}>{detail}</span>
          ))}
        </div>
      ) : null}
      <Handle
        className={styles.handle}
        id="source"
        isConnectable={!readOnly}
        position={Position.Right}
        type="source"
      />
    </article>
  )
}
