import { RefreshCw, Search } from 'lucide-react'
import { useState } from 'react'
import { Link } from 'react-router'
import { ActionButton, InlineFeedback } from '../../../../shared/Interaction'
import { DataState } from '../../../../shared/Primitives'
import { errorMessage } from '../../../../shared/errors'
import { CursorPaginationBar } from '../../shared/AdminWorkbench'
import { formatAdminDate } from '../../shared/adminFormat'
import { emptyRuntimeSearch, useRuntimeSearch } from './useRuntimeSearch'
import styles from '../runtimes/RuntimePanels.module.css'
import searchStyles from './TeamLabLibraryPage.module.css'

const statusOptions = ['等待调度', '规划中', '已调度', '部署中', '探测中', '运行中', '失败', '清理待处理', '已暂停', '销毁中', '已销毁']
const statuses: Record<string, string> = { pending: '等待调度', planning: '规划中', scheduled: '已调度', deploying: '部署中',
  probing: '探测中', ready: '运行中', failed: '失败', 'cleanup-pending': '清理待处理', paused: '已暂停', destroying: '销毁中', destroyed: '已销毁' }
const uuidPattern = '[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}'

export function RuntimeSearchPanel() {
  const [draft, setDraft] = useState(emptyRuntimeSearch)
  const result = useRuntimeSearch()
  return <section className={styles.panel} aria-label="运行实例检索">
    <header className={styles.panelHeader}><h3>运行实例</h3>
      <ActionButton icon={<RefreshCw size={16} />} disabled={result.isValidating} onClick={() => void result.mutate()} type="button">刷新实例</ActionButton>
    </header>
    <form className={`${styles.captureForm} ${searchStyles.runtimeSearchForm}`} onSubmit={event => { event.preventDefault(); result.setFilters({ ...draft }) }}>
      <label>实例引用或资产<input type="search" maxLength={128} value={draft.search} onChange={event => setDraft({ ...draft, search: event.target.value })} /></label>
      <label>运行状态<select value={draft.status} onChange={event => setDraft({ ...draft, status: event.target.value })}>
        <option value="">全部状态</option>{statusOptions.map((label, index) => <option key={label} value={index}>{label}</option>)}
      </select></label>
      <label>节点名称<input maxLength={128} value={draft.node} onChange={event => setDraft({ ...draft, node: event.target.value })} /></label>
      <label>运行代次<input type="number" min={1} step={1} value={draft.generation} onChange={event => setDraft({ ...draft, generation: event.target.value })} /></label>
      <label>发布 ID<input pattern={uuidPattern} value={draft.releaseId} onChange={event => setDraft({ ...draft, releaseId: event.target.value })} /></label>
      <label>创建人 ID<input pattern={uuidPattern} value={draft.createdById} onChange={event => setDraft({ ...draft, createdById: event.target.value })} /></label>
      <label className={searchStyles.errorFilter}><input type="checkbox" checked={draft.errorsOnly} onChange={event => setDraft({ ...draft, errorsOnly: event.target.checked })} />仅异常实例</label>
      <ActionButton icon={<Search size={16} />} type="submit">查询实例</ActionButton>
    </form>
    {result.error ? <InlineFeedback tone="danger">{errorMessage(result.error, '运行实例读取失败。')}</InlineFeedback>
      : result.isLoading ? <DataState loading title="正在查询运行实例" /> : !result.data?.items.length ? <DataState title="没有符合条件的运行实例" />
      : <div className={styles.sessionTableScroll}><table className={styles.sessionTable}>
        <thead><tr><th>实例</th><th>状态</th><th>代次 / 资产</th><th>创建时间</th><th>操作</th></tr></thead>
        <tbody>{result.data.items.map(item => <tr key={item.id}>
          <td>{item.reference || item.id}<small>{item.id}</small><small>发布 {item.releaseId}</small></td>
          <td>{statuses[item.status] ?? item.status}{item.hasError ? <small>存在异常</small> : null}</td>
          <td>第 {item.generation} 代<small>{item.assetCount} 个资产</small></td><td>{formatAdminDate(item.createdAt)}</td>
          <td>{item.topologyId ? <Link to={`/admin/teamlab/${item.topologyId}/runtimes/${item.id}?tab=operations&from=runtime-search`}>资产运维</Link> : '发布关联缺失'}</td>
        </tr>)}</tbody></table></div>}
    <CursorPaginationBar page={result.cursor.page} hasNext={!result.error && !!result.data?.nextCursor}
      onPrevious={result.cursor.previous} onNext={() => result.data?.nextCursor && result.cursor.next(result.data.nextCursor)} label="运行实例分页" />
  </section>
}
