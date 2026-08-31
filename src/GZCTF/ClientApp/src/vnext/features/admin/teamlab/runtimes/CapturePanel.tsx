import { Download, Radio, Square } from 'lucide-react'
import { useEffect, useId, useState } from 'react'
import useSWR from 'swr'
import { ActionButton, InlineFeedback } from '../../../../shared/Interaction'
import { DataState } from '../../../../shared/Primitives'
import { errorMessage } from '../../../../shared/errors'
import { StatusBadge } from '../../shared/AdminWorkbench'
import type { TeamLabRuntimeNetwork } from '../api'
import { teamLabRuntimeApi, teamLabRuntimeKeys } from '../api'
import { captureStatusLabels, formatBytes } from './runtimePresentation'
import styles from './RuntimePanels.module.css'

const liveCaptureStatuses = new Set(['pending', 'running', 'stopping', 'cleanup-pending'])

export function CapturePanel({ runtimeId, networks }: { runtimeId: string; networks: readonly TeamLabRuntimeNetwork[] }) {
  const scopeId = useId()
  const networkId = useId()
  const durationId = useId()
  const sizeId = useId()
  const retentionId = useId()
  const [scope, setScope] = useState<'runtime' | 'network'>('runtime')
  const [networkKey, setNetworkKey] = useState(networks[0]?.key ?? '')
  const [maxSeconds, setMaxSeconds] = useState(300)
  const [maxMiB, setMaxMiB] = useState(256)
  const [retentionHours, setRetentionHours] = useState(24)
  const [captureId, setCaptureId] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)
  const [actionError, setActionError] = useState<unknown>(null)
  const capture = useSWR(
    captureId ? teamLabRuntimeKeys.capture(runtimeId, captureId) : null,
    () => teamLabRuntimeApi.getCapture(runtimeId, captureId!),
    {
      keepPreviousData: true,
      revalidateOnFocus: true,
      refreshInterval: (latest) => latest && liveCaptureStatuses.has(latest.status) ? 2_000 : 0,
    }
  )

  // Keep the page-level capture alive across tab switches: the panel unmounts when the
  // user leaves the capture tab, so restore the most recent capture from the server on
  // mount instead of relying on component state.
  useEffect(() => {
    let disposed = false
    async function restoreLatest() {
      if (captureId) return
      try {
        const captures = await teamLabRuntimeApi.listCaptures(runtimeId)
        const latest = captures[0]
        if (latest && !disposed) setCaptureId(latest.id)
      } catch {
        // The list endpoint is an enhancement; if it is unavailable, keep the
        // existing "尚未启动抓包" empty state rather than blocking the panel.
      }
    }
    void restoreLatest()
    return () => {
      disposed = true
    }
  }, [captureId, runtimeId])

  const start = async () => {
    if (submitting || (scope === 'network' && !networkKey)) return
    setSubmitting(true)
    setActionError(null)
    try {
      const created = await teamLabRuntimeApi.startCapture(runtimeId, {
        scope,
        networkKey: scope === 'network' ? networkKey : null,
        maxSeconds,
        maxBytes: maxMiB * 1024 * 1024,
        expiresInSeconds: retentionHours * 3600,
      })
      setCaptureId(created.id)
      await capture.mutate(created, { revalidate: false })
    } catch (error) {
      setActionError(error)
    } finally {
      setSubmitting(false)
    }
  }

  const stop = async () => {
    if (!captureId || submitting) return
    setSubmitting(true)
    setActionError(null)
    try {
      const stopped = await teamLabRuntimeApi.stopCapture(runtimeId, captureId)
      await capture.mutate(stopped, { revalidate: false })
    } catch (error) {
      setActionError(error)
    } finally {
      setSubmitting(false)
    }
  }

  const active = capture.data && liveCaptureStatuses.has(capture.data.status)
  return (
    <section className={styles.panel} aria-labelledby="capture-title">
      <header className={styles.panelHeader}>
        <div><span>抓包取证</span><h3 id="capture-title">按需抓包</h3></div>
        {capture.data ? <StatusBadge pulse={Boolean(active)} tone={capture.data.status === 'completed' ? 'success' : capture.data.status === 'failed' ? 'danger' : 'info'}>{captureStatusLabels[capture.data.status]}</StatusBadge> : null}
      </header>
      <div className={styles.captureLayout}>
        <form className={styles.captureForm} onSubmit={(event) => { event.preventDefault(); void start() }}>
          <label htmlFor={scopeId}><span>抓包范围</span><select disabled={Boolean(active)} id={scopeId} onChange={(event) => setScope(event.currentTarget.value as 'runtime' | 'network')} value={scope}><option value="runtime">整个运行环境</option><option value="network">指定网段</option></select></label>
          {scope === 'network' ? <label htmlFor={networkId}><span>目标网段</span><select disabled={Boolean(active)} id={networkId} onChange={(event) => setNetworkKey(event.currentTarget.value)} value={networkKey}>{networks.map((network) => <option key={network.key} value={network.key}>{network.name} ({network.cidr})</option>)}</select></label> : null}
          <label htmlFor={durationId}><span>最长时长（秒）</span><input disabled={Boolean(active)} id={durationId} max={86400} min={1} onChange={(event) => setMaxSeconds(event.currentTarget.valueAsNumber)} type="number" value={maxSeconds} /></label>
          <label htmlFor={sizeId}><span>最大文件（MiB）</span><input disabled={Boolean(active)} id={sizeId} max={10240} min={1} onChange={(event) => setMaxMiB(event.currentTarget.valueAsNumber)} type="number" value={maxMiB} /></label>
          <label htmlFor={retentionId}><span>保留时间（小时）</span><input disabled={Boolean(active)} id={retentionId} max={168} min={1} onChange={(event) => setRetentionHours(event.currentTarget.valueAsNumber)} type="number" value={retentionHours} /></label>
          <div className={styles.captureActions}>
            <ActionButton disabled={submitting || Boolean(active)} icon={<Radio size={16} />} tone="primary" type="submit">{submitting && !active ? '正在启动' : '开始抓包'}</ActionButton>
            {active ? <ActionButton disabled={submitting} icon={<Square size={15} />} onClick={() => void stop()} type="button">停止</ActionButton> : null}
          </div>
        </form>
        <div className={styles.captureStatus}>
          {!captureId ? <DataState description="设置范围和资源上限后启动抓包。" title="尚未启动抓包" /> : capture.error ? <InlineFeedback tone="danger">{errorMessage(capture.error, '抓包状态读取失败。')}</InlineFeedback> : !capture.data ? <DataState description="正在读取抓包任务状态。" loading title="抓包任务加载中" /> : (
            <>
              <dl>
                <div><dt>任务标识</dt><dd><code>{capture.data.id}</code></dd></div>
                <div><dt>范围</dt><dd>{capture.data.networkKey ?? '全部观测点'}</dd></div>
                <div><dt>已捕获</dt><dd>{formatBytes(capture.data.capturedBytes)} / {formatBytes(capture.data.maxBytes)}</dd></div>
                <div><dt>分段</dt><dd>{capture.data.segments.length} 个观测点</dd></div>
              </dl>
              {capture.data.error ? <InlineFeedback tone="danger">{capture.data.error}</InlineFeedback> : null}
              {capture.data.status === 'completed' ? <a className={styles.downloadLink} href={teamLabRuntimeApi.captureDownloadPath(runtimeId, capture.data.id)}><Download size={16} />下载 PCAP</a> : null}
            </>
          )}
        </div>
      </div>
      {actionError ? <InlineFeedback tone="danger">{errorMessage(actionError, '抓包操作失败。')}</InlineFeedback> : null}
    </section>
  )
}
