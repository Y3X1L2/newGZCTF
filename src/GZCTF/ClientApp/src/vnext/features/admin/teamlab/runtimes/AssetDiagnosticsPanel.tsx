import { RefreshCw } from 'lucide-react'
import { useId, useState } from 'react'
import { ActionButton, InlineFeedback } from '../../../../shared/Interaction'
import { DataState } from '../../../../shared/Primitives'
import { errorMessage } from '../../../../shared/errors'
import { formatAdminDate } from '../../shared/adminFormat'
import type { TeamLabRuntime } from '../api'
import { useAssetDiagnostics } from './useAssetDiagnostics'
import styles from './RuntimePanels.module.css'

const stateLabels: Record<string, string> = {
  created: '已创建', running: '运行中', paused: '已暂停', restarting: '重启中',
  removing: '正在移除', exited: '已退出', dead: '异常终止',
}

function formatContainerDate(value: string) {
  const timestamp = Date.parse(value)
  return !Number.isFinite(timestamp) || value.startsWith('0001-') ? '-' : formatAdminDate(timestamp)
}

export function AssetDiagnosticsPanel({ runtime }: { runtime: TeamLabRuntime }) {
  const titleId = useId()
  const [selected, setSelected] = useState<number>()
  const [tail, setTail] = useState(200)
  const assets = runtime.assets.filter(asset => asset.kind === 'docker')
  const asset = assets.find(item => item.id === selected) ?? assets[0]
  const diagnostics = useAssetDiagnostics(runtime.id, runtime.generation, asset?.id, tail)
  const data = diagnostics.data
  return <section className={styles.panel} aria-labelledby={titleId}>
    <header className={styles.panelHeader}>
      <h3 id={titleId}>容器状态与输出</h3>
      <ActionButton icon={<RefreshCw size={16} />} disabled={!asset || diagnostics.isValidating}
        onClick={() => void diagnostics.mutate()} type="button">刷新诊断</ActionButton>
    </header>
    {!assets.length ? <DataState title="暂无可诊断的容器资产" /> : <>
      <div className={styles.diagnosticsControls}>
        <label>资产<select value={asset?.id} onChange={event => setSelected(Number(event.target.value))}>
          {assets.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}
        </select></label>
        <label>最近日志<select value={tail} onChange={event => setTail(Number(event.target.value))}>
          {[100, 200, 500, 1000].map(count => <option key={count} value={count}>{count} 行</option>)}
        </select></label>
      </div>
      {diagnostics.error ? <InlineFeedback tone="danger">{errorMessage(diagnostics.error, '资产诊断读取失败。')}</InlineFeedback>
        : diagnostics.isLoading || !data ? <DataState loading title="正在读取资产诊断" /> : <>
          <dl className={styles.diagnosticsFacts}>
            <div><dt>节点实际状态</dt><dd>{data.paused ? '已暂停' : stateLabels[data.state] ?? data.state}</dd></div>
            <div><dt>退出码</dt><dd>{data.state === 'exited' || data.state === 'dead' ? data.exitCode : '-'}</dd></div>
            <div><dt>重启次数</dt><dd>{data.restartCount}</dd></div>
            <div><dt>启动时间</dt><dd>{formatContainerDate(data.startedAt)}</dd></div>
            <div><dt>退出时间</dt><dd>{formatContainerDate(data.finishedAt)}</dd></div>
            <div><dt>采集时间</dt><dd>{formatAdminDate(data.observedAt)}</dd></div>
          </dl>
          {data.truncated ? <InlineFeedback tone="neutral">日志已达到 64 KiB 上限，当前结果不完整。</InlineFeedback> : null}
          {data.logsError ? <InlineFeedback tone="danger">{data.logsError === 'diagnostics.logs_timeout'
            ? '日志读取超时，当前输出可能不完整。' : '容器日志读取失败，请检查节点日志驱动后重试。'}</InlineFeedback> : null}
          {data.logs ? <pre className={styles.assetOutput} tabIndex={0} aria-label={`${asset?.name} 容器输出`}>{data.logs}</pre>
            : !data.logsError ? <DataState title="容器暂无标准输出或错误输出" /> : null}
        </>}
    </>}
  </section>
}
