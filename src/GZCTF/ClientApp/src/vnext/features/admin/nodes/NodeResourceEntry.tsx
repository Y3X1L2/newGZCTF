import { ExternalLink } from 'lucide-react'
import type { NodeResourceItem } from '../api'
import styles from './NodeResourceTab.module.css'

export function NodeResourceEntry({
  resource,
}: {
  resource: Pick<NodeResourceItem, 'isActive' | 'entry' | 'ip' | 'port'>
}) {
  if (!resource.isActive) return <span>暂无可用入口</span>
  const entry = resource.entry?.trim()
  if (entry) {
    try {
      const address = /^(?:\[[0-9a-f:]+\]|[a-z0-9.-]+):\d+(?:\/|$)/i.test(entry) ? `http://${entry}` : entry
      const url = new URL(address)
      if ((url.protocol === 'http:' || url.protocol === 'https:') && !url.username && !url.password) {
        return (
          <a className={styles.entry} href={url.href} rel="noreferrer" target="_blank">
            打开入口
            <ExternalLink size={13} />
          </a>
        )
      }
    } catch {
      /* A malformed address is not an actionable entry. */
    }
    return <span>入口暂不可用</span>
  }
  return (
    <span className={styles.mono}>
      {resource.ip || '—'}
      {resource.port ? `:${resource.port}` : ''}
    </span>
  )
}
