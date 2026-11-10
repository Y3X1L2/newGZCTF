import { ActionIcon, Button, TextInput } from '@mantine/core'
import { useDebouncedValue } from '@mantine/hooks'
import { useQuery } from '@tanstack/react-query'
import { Boxes, GitBranch, Layers3, List, Network, Plus, Search } from 'lucide-react'
import { useState } from 'react'
import { useWorkspaceApi } from '../../api/client'
import { Empty, ErrorMessage, Loading } from '../../foundation/Feedback'
import { LoadMore } from '../../foundation/LoadMore'
import { Status } from '../../foundation/Status'
import { dateTime, stateLabel } from '../../foundation/format'
import { useCursorList } from '../../foundation/useCursorList'
import { allowsProject } from '../access/permissions'
import { Link } from './../../routing'
import { CreateEnvironmentDialog } from './CreateEnvironmentDialog'
import styles from './Environments.module.css'

export function EnvironmentsPage() {
  const api = useWorkspaceApi()
  const identity = useQuery({ queryKey: ['identity'], queryFn: api.identity })
  const canCreate = allowsProject(identity.data, 'compose')
  const [query, setQuery] = useState('')
  const [filter, setFilter] = useState('all')
  const [search] = useDebouncedValue(query, 250)
  const counts = useQuery({
    queryKey: ['environments', 'summary', search],
    queryFn: ({ signal }) => api.environmentCounts(search, signal),
  })
  const status = filter === 'all' ? '' : filter
  const environments = useCursorList(['environments', { search, status }], (page) =>
    api.environments({ ...page, search, status })
  )
  const [creating, setCreating] = useState(false)
  const items = environments.data ?? []
  const states = [
    ...new Set(['running', 'stopped', 'draft', 'failed', ...Object.keys(counts.data?.statuses ?? {})]),
  ].filter(
    (state) => state === 'running' || state === 'stopped' || state === filter || (counts.data?.statuses[state] ?? 0) > 0
  )
  return (
    <main className="collection-page">
      <div className="page-heading">
        <div>
          <h1>环境</h1>
        </div>
        {canCreate && (
          <Button leftSection={<Plus size={16} />} onClick={() => setCreating(true)}>
            新建环境
          </Button>
        )}
      </div>
      <div className={styles.summary} role="group" aria-label="环境状态">
        {['all', ...states].map((value) => (
          <button
            key={value}
            aria-label={value === 'all' ? '全部环境' : stateLabel(value)}
            aria-pressed={filter === value}
            onClick={() => setFilter(value)}
          >
            <strong>
              {counts.data ? (value === 'all' ? counts.data.total : (counts.data.statuses[value] ?? 0)) : '—'}
            </strong>
            <span>
              {value === 'all' ? (
                <>
                  <Layers3 size={14} />
                  全部环境
                </>
              ) : (
                <Status value={value} />
              )}
            </span>
          </button>
        ))}
      </div>
      <ErrorMessage error={counts.error} />
      <div className={styles.listSurface}>
        <div className={styles.toolbar}>
          <span className="section-label">
            <span className="section-symbol">
              <Layers3 size={17} />
            </span>
            环境列表
          </span>
          <TextInput
            aria-label="搜索环境"
            placeholder="搜索环境"
            leftSection={<Search size={16} />}
            value={query}
            onChange={(event) => setQuery(event.currentTarget.value)}
          />
        </div>
        <ErrorMessage error={environments.error} />
        {environments.isPending ? (
          <Loading />
        ) : items.length ? (
          <div className={styles.tableScroll}>
            <table className={`data-table ${styles.table}`}>
              <thead>
                <tr>
                  <th>环境</th>
                  <th>状态</th>
                  <th>资产</th>
                  <th>网段</th>
                  <th>最近更新</th>
                  <th aria-label="操作" />
                </tr>
              </thead>
              <tbody>
                {items.map((environment) => (
                  <tr key={environment.id}>
                    <td className={styles.environment}>
                      <Link className="object-link" to={`/environments/${environment.id}`}>
                        <span className={styles.symbol}>
                          <Layers3 size={20} />
                        </span>
                        <span>
                          <strong>{environment.name}</strong>
                        </span>
                      </Link>
                    </td>
                    <td className={styles.state}>
                      <Status value={environment.status} />
                    </td>
                    <td className={styles.assetCount}>
                      <span className={styles.measure}>
                        <Boxes size={15} />
                        <strong>{environment.assetCount}</strong>
                        <span>资产</span>
                      </span>
                    </td>
                    <td className={styles.networkCount}>
                      <span className={styles.measure}>
                        <Network size={15} />
                        <strong>{environment.networkCount}</strong>
                        <span>网段</span>
                      </span>
                    </td>
                    <td className={styles.updated}>{dateTime(environment.updatedAt)}</td>
                    <td className={styles.actions}>
                      <ActionIcon
                        component={Link}
                        to={`/environments/${environment.id}`}
                        aria-label="进入拓扑"
                        title="进入拓扑"
                        variant="subtle"
                        color="gray"
                      >
                        <GitBranch size={17} />
                      </ActionIcon>
                      <ActionIcon
                        component={Link}
                        to={`/environments/${environment.id}?view=assets`}
                        aria-label="查看资产"
                        title="查看资产"
                        variant="subtle"
                        color="gray"
                      >
                        <List size={17} />
                      </ActionIcon>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : (
          !environments.error && (
            <Empty
              icon={<Layers3 size={30} />}
              title={query || filter !== 'all' ? '没有匹配的环境' : '暂无环境'}
              action={canCreate && !query && filter === 'all' ? '新建环境' : undefined}
              onAction={() => setCreating(true)}
            />
          )
        )}
        <LoadMore list={environments} />
      </div>
      {creating && <CreateEnvironmentDialog onClose={() => setCreating(false)} />}
    </main>
  )
}
