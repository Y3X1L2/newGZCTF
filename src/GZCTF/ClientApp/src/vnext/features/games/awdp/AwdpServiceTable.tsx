import { ExternalLink, RotateCcw, Search, Undo2 } from 'lucide-react'
import { useMemo, useState } from 'react'
import { ActionButton } from '../../../shared/Interaction'
import { AwdpInstance, checkerMeta } from '../../awdp/awdpDomain'
import styles from './AwdpWorkspace.module.css'

export function AwdpServiceTable({
  instances,
  myTeamId,
  operation,
  onAction,
}: {
  instances: AwdpInstance[]
  myTeamId: number | null
  operation: string | null
  onAction: (kind: 'recover' | 'reset', instance: AwdpInstance) => void
}) {
  const [query, setQuery] = useState('')
  const visible = useMemo(() => {
    const keyword = query.trim().toLocaleLowerCase('zh-CN')
    return instances.filter(
      (item) =>
        !keyword ||
        `${item.serviceName} ${item.teamName} ${item.ipAddress ?? ''}`.toLocaleLowerCase('zh-CN').includes(keyword)
    )
  }, [instances, query])

  return (
    <section className={styles.serviceSection}>
      <header className={styles.sectionHeader}>
        <div>
          <span>SERVICE MAP</span>
          <h2>可攻击服务</h2>
          <p>展示所有队伍服务入口；只有标记为本队的实例可以管理。</p>
        </div>
        <label className={styles.searchBox}>
          <Search size={16} />
          <input
            aria-label="搜索 AWDP 服务"
            onChange={(event) => setQuery(event.currentTarget.value)}
            placeholder="服务、战队或地址"
            type="search"
            value={query}
          />
        </label>
      </header>
      <div className={styles.tableViewport}>
        <table className={styles.serviceTable}>
          <thead>
            <tr>
              <th>服务</th>
              <th>战队</th>
              <th>访问入口</th>
              <th>运行</th>
              <th>Checker</th>
              <th>可用次数</th>
              <th>操作</th>
            </tr>
          </thead>
          <tbody>
            {visible.map((item) => {
              const checker = checkerMeta(item.checkerStatus)
              const isMine = item.teamId === myTeamId
              return (
                <tr data-mine={isMine || undefined} key={item.instanceId || `${item.serviceId}:${item.teamId}`}>
                  <td>
                    <strong>{item.serviceName}</strong>
                  </td>
                  <td>
                    <span className={styles.teamName}>
                      {item.teamName}
                      {isMine ? <small>本队</small> : null}
                    </span>
                  </td>
                  <td>
                    {item.endpoint ? (
                      <a className={styles.endpoint} href={item.endpoint} rel="noreferrer" target="_blank">
                        {item.ipAddress}:{item.port}
                        <ExternalLink size={14} />
                      </a>
                    ) : (
                      <span className={styles.muted}>未分配</span>
                    )}
                  </td>
                  <td>
                    <span className={styles.statusMark} data-tone={item.running ? 'success' : 'danger'}>
                      {item.running ? '运行中' : '已停止'}
                    </span>
                  </td>
                  <td>
                    <span className={styles.statusMark} data-tone={checker.tone}>
                      {checker.label}
                    </span>
                  </td>
                  <td>
                    <span className={styles.quota}>
                      重置 {item.remainingResetCount} · 恢复 {item.remainingRecoveryCount}
                    </span>
                  </td>
                  <td>
                    {item.canManage ? (
                      <span className={styles.rowActions}>
                        <ActionButton
                          aria-label={`重置 ${item.serviceName}`}
                          disabled={operation !== null || item.remainingResetCount <= 0}
                          icon={<RotateCcw size={15} />}
                          onClick={() => onAction('reset', item)}
                          tone="ghost"
                          type="button"
                        >
                          重置
                        </ActionButton>
                        <ActionButton
                          aria-label={`恢复 ${item.serviceName}`}
                          disabled={operation !== null || item.remainingRecoveryCount <= 0}
                          icon={<Undo2 size={15} />}
                          onClick={() => onAction('recover', item)}
                          tone="ghost"
                          type="button"
                        >
                          恢复
                        </ActionButton>
                      </span>
                    ) : (
                      <span className={styles.muted}>仅访问</span>
                    )}
                  </td>
                </tr>
              )
            })}
          </tbody>
        </table>
      </div>
      {!visible.length ? <p className={styles.empty}>当前筛选下没有服务。</p> : null}
    </section>
  )
}
