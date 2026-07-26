import { Handle, Position } from '@xyflow/react'
import type { ReactNode } from 'react'
import styles from './TopologyNodeShell.module.css'

export type NodeTone = 'switch' | 'router' | 'docker' | 'linux' | 'windows'

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
  return (
    <article className={`${styles.node} ${styles[tone]} ${selected ? styles.selected : ''}`}>
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
      <div className={styles.details}>
        {details.map((detail) => (
          <span key={detail}>{detail}</span>
        ))}
      </div>
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
