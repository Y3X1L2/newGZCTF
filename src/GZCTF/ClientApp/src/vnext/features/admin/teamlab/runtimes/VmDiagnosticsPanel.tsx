import { RefreshCw } from 'lucide-react'
import { useState } from 'react'
import { ActionButton, InlineFeedback } from '../../../../shared/Interaction'
import { DataState } from '../../../../shared/Primitives'
import { errorMessage } from '../../../../shared/errors'
import { formatAdminDate } from '../../shared/adminFormat'
import type { TeamLabRuntime } from '../api'
import { useVmDiagnostics } from './useVmDiagnostics'
import styles from './RuntimePanels.module.css'

const stateLabels: Record<string, string> = { running: '运行中', blocked: '等待资源', paused: '已暂停',
  'in shutdown': '正在关机', 'shut off': '已关机', crashed: '已崩溃', pmsuspended: '电源管理挂起' }

export function VmDiagnosticsPanel({ runtime }: { runtime: TeamLabRuntime }) {
  const [selected, setSelected] = useState<number>()
  const assets = runtime.assets.filter(asset => asset.kind === 'vm')
  const asset = assets.find(item => item.id === selected) ?? assets[0]
  const diagnostics = useVmDiagnostics(runtime.id, runtime.generation, asset?.id)
  if (!assets.length) return null
  return <section className={styles.panel} aria-label="VM 电源状态">
    <header className={styles.panelHeader}><h3>VM 电源状态</h3>
      <ActionButton icon={<RefreshCw size={16} />} disabled={diagnostics.isValidating}
        onClick={() => void diagnostics.mutate()} type="button">刷新 VM 状态</ActionButton>
    </header>
    <div className={styles.diagnosticsControls}><label>VM 资产<select value={asset.id}
      onChange={event => setSelected(Number(event.target.value))}>
      {assets.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}
    </select></label></div>
    {diagnostics.error ? <InlineFeedback tone="danger">{errorMessage(diagnostics.error, 'VM 状态读取失败。')}</InlineFeedback>
      : diagnostics.isLoading || !diagnostics.data ? <DataState loading title="正在读取 VM 状态" />
      : <dl className={styles.diagnosticsFacts}>
        <div><dt>libvirt 电源状态</dt><dd>{stateLabels[diagnostics.data.state] ?? diagnostics.data.state}</dd></div>
        <div><dt>域 UUID</dt><dd>{diagnostics.data.nativeId}</dd></div>
        <div><dt>采集时间</dt><dd>{formatAdminDate(diagnostics.data.observedAt)}</dd></div>
      </dl>}
  </section>
}
