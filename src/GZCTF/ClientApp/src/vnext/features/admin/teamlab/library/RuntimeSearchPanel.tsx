import { ArrowRight, RefreshCw, Search, SlidersHorizontal } from 'lucide-react'
import { useEffect, useState } from 'react'
import { Link } from 'react-router'
import { ActionButton, InlineFeedback } from '../../../../shared/Interaction'
import { DataState } from '../../../../shared/Primitives'
import { errorMessage } from '../../../../shared/errors'
import { CursorPaginationBar } from '../../shared/AdminWorkbench'
import { formatAdminDate } from '../../shared/adminFormat'
import { useRuntimeSearch } from './useRuntimeSearch'
import { TeamLabRuntimeStatusBadge } from '../shared/TeamLabStatusBadge'
import type { TeamLabRuntimeStatus } from '../api'
import styles from '../runtimes/RuntimePanels.module.css'
import searchStyles from './TeamLabLibraryPage.module.css'

const statusOptions = ['等待调度', '规划中', '已调度', '部署中', '探测中', '运行中', '失败', '清理待处理', '已暂停', '销毁中', '已销毁']
const uuidPattern = '[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}'

export function RuntimeSearchPanel() {
  const result = useRuntimeSearch()
  const [draft, setDraft] = useState(result.filters)
  const [advanced, setAdvanced] = useState(false)
  useEffect(() => setDraft(result.filters), [result.filters])
  return <section className={styles.panel} aria-label="运行环境检索">
    <header className={styles.panelHeader}><h3>运行环境</h3>
      <ActionButton aria-label="刷新环境" title="刷新环境" icon={<RefreshCw size={16} />} disabled={result.isValidating} onClick={() => void result.mutate()} type="button" />
    </header>
    <form className={`${styles.captureForm} ${searchStyles.runtimeSearchForm}`} onSubmit={event => { event.preventDefault(); result.setFilters({ ...draft }) }}>
      <label>名称、引用或资产<input placeholder="搜索运行环境" type="search" maxLength={128} value={draft.search} onChange={event => setDraft({ ...draft, search: event.target.value })} /></label>
      <label>运行状态<select value={draft.status} onChange={event => setDraft({ ...draft, status: event.target.value })}>
        <option value="">全部状态</option>{statusOptions.map((label, index) => <option key={label} value={index}>{label}</option>)}
      </select></label>
      <label className={searchStyles.errorFilter}><input type="checkbox" checked={draft.errorsOnly} onChange={event => setDraft({ ...draft, errorsOnly: event.target.checked })} />仅异常</label>
      <ActionButton icon={<Search size={16} />} type="submit">查询实例</ActionButton>
      <ActionButton aria-label="高级筛选" title="高级筛选" aria-expanded={advanced} icon={<SlidersHorizontal size={16} />} onClick={() => setAdvanced(value => !value)} type="button" />
      {advanced ? <div className={searchStyles.advancedFilters}>
        <label>节点名称<input maxLength={128} value={draft.node} onChange={event => setDraft({ ...draft, node: event.target.value })} /></label>
        <label>运行代次<input type="number" min={1} step={1} value={draft.generation} onChange={event => setDraft({ ...draft, generation: event.target.value })} /></label>
        <label>发布 ID<input pattern={uuidPattern} value={draft.releaseId} onChange={event => setDraft({ ...draft, releaseId: event.target.value })} /></label>
        <label>创建人 ID<input pattern={uuidPattern} value={draft.createdById} onChange={event => setDraft({ ...draft, createdById: event.target.value })} /></label>
      </div> : null}
    </form>
    {result.error ? <InlineFeedback tone="danger">{errorMessage(result.error, '运行实例读取失败。')}</InlineFeedback>
      : result.isLoading ? <DataState loading title="正在查询运行实例" /> : !result.data?.items.length ? <DataState title="没有符合条件的运行实例" />
      : <div className={styles.sessionTableScroll}><table className={styles.sessionTable}>
        <thead><tr><th>环境</th><th>场景 / 版本</th><th>来源 / 创建人</th><th>状态</th><th>资产</th><th>创建时间</th><th /></tr></thead>
        <tbody>{result.data.items.map(item => <tr key={item.id}>
          <td><strong>{item.reference || item.scenarioName || '未命名环境'}</strong></td>
          <td>{item.scenarioName || '场景关联未知'}{item.releaseVersion ? <small>v{item.releaseVersion}</small> : <small>版本未知</small>}</td>
          <td><span>来源未知</span><small>{item.createdById ? `创建人 ID ${item.createdById}` : '创建人未知'}</small></td>
          <td><TeamLabRuntimeStatusBadge status={(item.status === 'ready' ? 'running' : item.status === 'queued' ? 'scheduled' : item.status) as TeamLabRuntimeStatus} />{item.hasError && item.status !== 'failed' ? <small>存在异常</small> : null}</td>
          <td>{item.assetCount}</td><td>{formatAdminDate(item.createdAt)}</td>
          <td><Link aria-label={`进入环境 ${item.reference || item.scenarioName || item.id}`} title="进入环境" to={`/admin/teamlab/runtimes/${item.id}?from=runtime-search`}><ArrowRight size={17} /></Link></td>
        </tr>)}</tbody></table></div>}
    <CursorPaginationBar page={result.cursor.page} hasNext={!result.error && !!result.data?.nextCursor}
      onPrevious={result.cursor.previous} onNext={() => result.data?.nextCursor && result.cursor.next(result.data.nextCursor)} label="运行实例分页" />
  </section>
}
