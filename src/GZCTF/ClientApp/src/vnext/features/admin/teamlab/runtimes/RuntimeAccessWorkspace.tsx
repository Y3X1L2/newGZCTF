import { useState } from 'react'
import type { TeamLabRuntime } from '../api'
import { RuntimeAccessPanel } from './RuntimeAccessPanel'
import { ServiceAccessPanel } from './ServiceAccessPanel'
import styles from './RuntimeAccessWorkspace.module.css'

export function RuntimeAccessWorkspace({ runtime }: { runtime: TeamLabRuntime }) {
  const [vpnOpen, setVpnOpen] = useState(false)
  return <div className={styles.workspace}>
    <ServiceAccessPanel runtime={runtime} />
    <section className={styles.vpn}>
      <button aria-expanded={vpnOpen} onClick={() => setVpnOpen(value => !value)} type="button">
        <strong>VPN 整网接入</strong><span>{vpnOpen ? '收起' : '查看授权'}</span>
      </button>
      {vpnOpen ? <RuntimeAccessPanel canCreate={runtime.status === 'running' && !runtime.managedRolloutId} runtimeId={runtime.id} /> : null}
    </section>
  </div>
}
