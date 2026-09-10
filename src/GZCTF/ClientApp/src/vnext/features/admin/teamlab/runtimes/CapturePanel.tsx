import { Download, Radio, Square, RefreshCw } from 'lucide-react'
import { useEffect, useId, useState } from 'react'
import useSWR from 'swr'
import { ActionButton, InlineFeedback } from '../../../../shared/Interaction'
import { DataState } from '../../../../shared/Primitives'
import { errorMessage } from '../../../../shared/errors'
import { StatusBadge } from '../../shared/AdminWorkbench'
import { formatAdminDate } from '../../shared/adminFormat'
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
  const [after, setAfter] = useState<string | undefined>()
  const [statusFilter, setStatusFilter] = useState('')
  const history = useSWR(['teamlab:capture-history', runtimeId, after],
    () => teamLabRuntimeApi.listCaptureHistory(runtimeId, after), { refreshInterval: 5000 })
  const capture = useSWR(
    captureId ? teamLabRuntimeKeys.capture(runtimeId, captureId) : null,
    () => teamLabRuntimeApi.getCapture(runtimeId, captureId!),
    {
      keepPreviousData: false,
      revalidateOnFocus: true,
      refreshInterval: (latest) => latest && liveCaptureStatuses.has(latest.status) ? 2_000 : 0,
    }
  )

  useEffect(() => {
    if (!captureId && history.data?.items[0]) setCaptureId(history.data.items[0].id)
  }, [captureId, history.data])

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
      await history.mutate()
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
      await history.mutate()
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
              <p>保留至 {capture.data.expiresAt ? formatAdminDate(capture.data.expiresAt) : '未设置'}</p>
              {capture.data.segments.length ? <div className={styles.sessionTableScroll}><table className={styles.sessionTable}><thead><tr><th>观测点 / 资产</th><th>分段状态</th><th>捕获 / 上传</th><th>摘要 / 错误</th></tr></thead><tbody>{capture.data.segments.map((segment) => <tr key={segment.id}><td>{segment.assetKey ?? segment.networkKey ?? segment.observationPointId}</td><td>{segment.status}</td><td>{formatBytes(segment.capturedBytes)} / {formatBytes(segment.uploadedBytes)}</td><td>{segment.error ?? segment.sha256 ?? '待上传'}</td></tr>)}</tbody></table></div> : null}
              {capture.data.status === 'completed' ? <a className={styles.downloadLink} href={teamLabRuntimeApi.captureDownloadPath(runtimeId, capture.data.id)}><Download size={16} />下载 PCAP</a> : null}
            </>
          )}
        </div>
      </div>
      <header className={styles.panelHeader}><h3>抓包历史</h3><ActionButton icon={<RefreshCw size={14} />} onClick={() => void history.mutate()} type="button">刷新历史</ActionButton></header>
      <label>当前页状态筛选 <select value={statusFilter} onChange={(event) => setStatusFilter(event.currentTarget.value)}><option value="">全部状态</option>{Object.entries(captureStatusLabels).map(([value, label]) => <option key={value} value={value}>{label}</option>)}</select></label>
      {history.error ? <InlineFeedback tone="danger">{errorMessage(history.error, '抓包历史读取失败。')}</InlineFeedback> : history.isLoading ? <DataState loading title="正在读取历史" /> : <div className={styles.sessionTableScroll}><table className={styles.sessionTable}><thead><tr><th>任务</th><th>范围</th><th>状态</th><th>大小 / 创建时间</th><th>操作</th></tr></thead><tbody>{history.data?.items.filter((item) => !statusFilter || item.status === statusFilter).map((item) => <tr key={item.id}><td><code>{item.id}</code></td><td>{item.networkKey ?? item.scope}</td><td>{captureStatusLabels[item.status]}{item.error ? <small>{item.error}</small> : null}</td><td>{formatBytes(item.capturedBytes)}<small>{formatAdminDate(item.createdAt)}</small></td><td><ActionButton disabled={submitting} onClick={() => setCaptureId(item.id)} type="button">查看详情</ActionButton></td></tr>)}</tbody></table></div>}
      <div className={styles.captureActions}>{after ? <ActionButton onClick={() => setAfter(undefined)} type="button">返回首页</ActionButton> : null}{history.data?.next ? <ActionButton onClick={() => setAfter(history.data!.next!)} type="button">下一页</ActionButton> : null}</div>
      {actionError ? <InlineFeedback tone="danger">{errorMessage(actionError, '抓包操作失败。')}</InlineFeedback> : null}
    </section>
  )
}
