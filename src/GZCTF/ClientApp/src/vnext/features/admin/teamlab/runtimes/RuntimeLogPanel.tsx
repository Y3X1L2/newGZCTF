import { Search } from 'lucide-react'
import { memo, useDeferredValue, useState } from 'react'
import { InlineFeedback } from '../../../../shared/Interaction'
import { DataState } from '../../../../shared/Primitives'
import { errorMessage } from '../../../../shared/errors'
import { FilterToolbar, ToolbarGroup } from '../../shared/AdminWorkbench'
import { formatAdminDate } from '../../shared/adminFormat'
import type { TeamLabRuntimeStatus } from '../api'
import styles from './RuntimePanels.module.css'
import { useRuntimeLogs } from './useRuntimeLogs'

export const RuntimeLogPanel = memo(function RuntimeLogPanel({ runtimeId, status }: {
  runtimeId: string
  status: TeamLabRuntimeStatus
}) {
  const [level, setLevel] = useState('')
  const [keyword, setKeyword] = useState('')
  const [eventCode, setEventCode] = useState('')
  const { logs, error, hasMore, isLoading: loading, loadMore } = useRuntimeLogs(runtimeId, status, {
    level,
    eventCode,
    keyword: useDeferredValue(keyword.trim()),
  })
  const ordered = [...logs].sort((left, right) => right.time - left.time)

  return (
    <section className={styles.panel} aria-labelledby="runtime-logs-title">
      <header className={styles.panelHeader}>
        <div><span>结构化日志</span><h3 id="runtime-logs-title">对象日志</h3></div>
        <span className={styles.muted}>持久化事件投影</span>
      </header>
      <FilterToolbar>
        <ToolbarGroup grow>
          <label className={styles.filterInput}><Search aria-hidden="true" size={15} /><input aria-label="检索运行日志" onChange={(event) => setKeyword(event.target.value)} placeholder="检索消息、对象或事件代码" value={keyword} /></label>
        </ToolbarGroup>
        <ToolbarGroup>
          <select aria-label="日志级别" className={styles.filterSelect} onChange={(event) => setLevel(event.target.value)} value={level}>
            <option value="">全部级别</option><option value="Information">信息</option><option value="Success">成功</option><option value="Warning">警告</option><option value="Error">错误</option>
          </select>
          <input aria-label="精确事件代码" className={styles.filterSelect} onChange={(event) => setEventCode(event.target.value)} placeholder="事件代码" value={eventCode} />
        </ToolbarGroup>
      </FilterToolbar>
      {loading ? <DataState description="正在读取结构化日志。" loading title="日志加载中" /> : error ? (
        <InlineFeedback tone="danger">{errorMessage(error, '结构化日志加载失败。')}</InlineFeedback>
      ) : ordered.length ? (
        <>
          <div className={styles.logFrame} role="log" aria-label="TeamLab 运行日志">
            {ordered.map((entry, index) => (
              <div className={styles.logRow} data-level={entry.level?.toLowerCase()} key={entry.id ?? `${entry.time}:${index}`}>
                <time>{formatAdminDate(entry.time)}</time>
                <strong>{entry.level ?? 'Info'}</strong>
                <code>{entry.eventCode ?? entry.status ?? 'runtime'}</code>
                <span>{entry.resourceDisplayName ?? entry.resourceId ?? 'runtime'}</span>
                <p>{entry.msg ?? '-'}</p>
              </div>
            ))}
          </div>
          {hasMore ? (
            <button className={styles.loadMore} onClick={loadMore} type="button">
              加载更早的日志（已显示 {ordered.length} 条）
            </button>
          ) : null}
        </>
      ) : <DataState description="当前条件下没有结构化日志。" title="暂无日志" />}
    </section>
  )
})
