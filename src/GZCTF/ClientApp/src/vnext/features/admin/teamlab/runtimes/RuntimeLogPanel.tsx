import { AlertTriangle, CheckCircle2, CircleX, Info, Search } from 'lucide-react'
import { memo, useDeferredValue, useState } from 'react'
import { InlineFeedback, VNextDrawer } from '../../../../shared/Interaction'
import { DataState } from '../../../../shared/Primitives'
import { errorMessage } from '../../../../shared/errors'
import { formatAdminDate } from '../../shared/adminFormat'
import type { TeamLabRuntimeStatus } from '../api'
import styles from './RuntimeWorkspaces.module.css'
import { useRuntimeLogs } from './useRuntimeLogs'

export const RuntimeLogPanel = memo(function RuntimeLogPanel({ runtimeId, status }: {
  runtimeId: string
  status: TeamLabRuntimeStatus
}) {
  const [level, setLevel] = useState('')
  const [keyword, setKeyword] = useState('')
  const [eventCode, setEventCode] = useState('')
  const [selected, setSelected] = useState<ReturnType<typeof useRuntimeLogs>['logs'][number] | null>(null)
  const { logs, error, hasMore, isLoading: loading, loadMore } = useRuntimeLogs(runtimeId, status, {
    level,
    eventCode,
    keyword: useDeferredValue(keyword.trim()),
  })
  const ordered = [...logs].sort((left, right) => right.time - left.time)

  return (
    <section aria-label="诊断日志">
      <div className={styles.toolbar}>
          <label className={styles.search}><Search aria-hidden="true" size={15} /><input aria-label="检索运行日志" onChange={(event) => setKeyword(event.target.value)} placeholder="搜索日志" value={keyword} /></label>
          <select aria-label="日志级别" onChange={(event) => setLevel(event.target.value)} value={level}>
            <option value="">全部级别</option><option value="Information">信息</option><option value="Success">成功</option><option value="Warning">警告</option><option value="Error">错误</option>
          </select>
          <details><summary>高级筛选</summary><label className={styles.search}><input aria-label="精确事件代码" onChange={(event) => setEventCode(event.target.value)} placeholder="事件代码" value={eventCode} /></label></details>
      </div>
      {loading ? <DataState loading title="日志加载中" /> : error ? (
        <InlineFeedback tone="danger">{errorMessage(error, '结构化日志加载失败。')}</InlineFeedback>
      ) : ordered.length ? (
        <>
          <ul className={styles.eventList} aria-label="TeamLab 运行日志">
            {ordered.map((entry, index) => {
              const level = entry.level?.toLowerCase()
              const Icon = level === 'error' ? CircleX : level === 'warning' ? AlertTriangle : level === 'success' ? CheckCircle2 : Info
              return <li data-level={level} key={entry.id ?? `${entry.time}:${index}`}><button onClick={() => setSelected(entry)} type="button">
                <Icon size={18} /><strong>{entry.msg ?? entry.eventCode ?? '运行日志'}</strong><time>{formatAdminDate(entry.time)}</time>
              </button></li>
            })}
          </ul>
          {hasMore ? (
            <button onClick={loadMore} type="button">
              更早记录
            </button>
          ) : null}
        </>
      ) : <DataState title="暂无日志" />}
      <VNextDrawer eyebrow="" open={selected !== null} onClose={() => setSelected(null)} title="日志详情">
        {selected ? <><p>{selected.msg}</p><dl className={styles.facts}>
          <div><dt>时间</dt><dd>{formatAdminDate(selected.time)}</dd></div>
          {selected.eventCode ? <div><dt>事件代码</dt><dd>{selected.eventCode}</dd></div> : null}
          {selected.resourceDisplayName ? <div><dt>对象</dt><dd>{selected.resourceDisplayName}</dd></div> : null}
          {selected.resourceId ? <div><dt>对象编号</dt><dd>{selected.resourceId}</dd></div> : null}
        </dl></> : null}
      </VNextDrawer>
    </section>
  )
})
