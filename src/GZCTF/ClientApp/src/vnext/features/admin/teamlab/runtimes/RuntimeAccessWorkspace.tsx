import { ShieldCheck } from 'lucide-react'
import { useState } from 'react'
import { ActionButton, VNextDrawer } from '../../../../shared/Interaction'
import type { TeamLabRuntime } from '../api'
import { RuntimeAccessPanel } from './RuntimeAccessPanel'
import { RuntimeGrantPanel } from './RuntimeGrantPanel'
import { ServiceAccessPanel } from './ServiceAccessPanel'
import styles from './RuntimeAccessWorkspace.module.css'

export function RuntimeAccessWorkspace({ runtime }: { runtime: TeamLabRuntime }) {
  const [vpnOpen, setVpnOpen] = useState(false)
  const [grantsOpen, setGrantsOpen] = useState(false)
  return <div className={styles.workspace}>
    <div className={styles.permissionRow}><p>业务入口仅建立网络转发；平台操作权限不会对外部端口的访问者做身份校验。</p>
      <ActionButton icon={<ShieldCheck size={16} />} onClick={() => setGrantsOpen(true)} type="button">平台操作权限</ActionButton></div>
    <ServiceAccessPanel runtime={runtime} />
    <section className={styles.vpn}>
      <button aria-expanded={vpnOpen} onClick={() => setVpnOpen(value => !value)} type="button">
        <strong>VPN 整网接入</strong><span>{vpnOpen ? '收起' : '查看授权'}</span>
      </button>
      {vpnOpen ? <RuntimeAccessPanel canCreate={runtime.status === 'running' && !runtime.managedRolloutId} runtimeId={runtime.id} /> : null}
    </section>
    <VNextDrawer eyebrow="" open={grantsOpen} onClose={() => setGrantsOpen(false)} title="平台操作权限">
      {grantsOpen ? <RuntimeGrantPanel runtime={runtime} /> : null}
    </VNextDrawer>
  </div>
}
