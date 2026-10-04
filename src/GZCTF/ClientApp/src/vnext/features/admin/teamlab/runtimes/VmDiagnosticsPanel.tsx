import { RefreshCw } from 'lucide-react'
import { ActionButton, InlineFeedback } from '../../../../shared/Interaction'
import { DataState } from '../../../../shared/Primitives'
import { errorMessage } from '../../../../shared/errors'
import { formatAdminDate } from '../../shared/adminFormat'
import type { TeamLabRuntime } from '../api'
import { useVmDiagnostics } from './useVmDiagnostics'
import styles from './RuntimePanels.module.css'

const stateLabels: Record<string, string> = { running: '运行中', blocked: '等待资源', paused: '已暂停',
  'in shutdown': '正在关机', 'shut off': '已关机', crashed: '已崩溃', pmsuspended: '电源管理挂起' }

export function VmDiagnosticsPanel({ runtime, assetId }: { runtime: TeamLabRuntime; assetId: number }) {
  const asset = runtime.assets.find(item => item.id === assetId && item.kind === 'vm')
  const diagnostics = useVmDiagnostics(runtime.id, runtime.generation, asset?.id)
  if (!asset) return null
  return <section className={styles.panel} aria-label="虚拟机诊断">
    <header className={styles.panelHeader}><h3>虚拟机诊断</h3>
      <ActionButton icon={<RefreshCw size={16} />} disabled={diagnostics.isValidating}
        onClick={() => void diagnostics.mutate()} type="button">刷新 VM 状态</ActionButton>
    </header>
    {diagnostics.error ? <InlineFeedback tone="danger">{errorMessage(diagnostics.error, 'VM 状态读取失败。')}</InlineFeedback>
      : diagnostics.isLoading || !diagnostics.data ? <DataState loading title="正在读取 VM 状态" />
      : <dl className={styles.diagnosticsFacts}>
        <div><dt>libvirt 电源状态</dt><dd>{stateLabels[diagnostics.data.state] ?? diagnostics.data.state}</dd></div>
        <div><dt>采集时间</dt><dd>{formatAdminDate(diagnostics.data.observedAt)}</dd></div>
      </dl>}
  </section>
}
