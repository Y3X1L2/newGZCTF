import { useSearchParams } from 'react-router'
import { AwdpAttackLog, AwdpPatchState, AwdpScore, defenseMeta, formatAwdpTime, patchMeta } from '../../awdp/awdpDomain'
import styles from './AwdpWorkspace.module.css'

type View = 'attacks' | 'defense' | 'scoreboard'
const views: Array<{ id: View; label: string }> = [
  { id: 'scoreboard', label: 'AWDP 榜单' },
  { id: 'attacks', label: '攻击日志' },
  { id: 'defense', label: '防守状态' },
]

export function AwdpActivityPanels({
  scoreboard,
  attackLogs,
  patchStatus,
  myTeamId,
}: {
  scoreboard: AwdpScore[]
  attackLogs: AwdpAttackLog[]
  patchStatus: AwdpPatchState[]
  myTeamId: number | null
}) {
  const [params, setParams] = useSearchParams()
  const requested = params.get('view') as View | null
  const view: View = views.some((item) => item.id === requested) ? requested! : 'scoreboard'
  const setView = (nextView: View) => {
    const next = new URLSearchParams(params)
    next.set('view', nextView)
    setParams(next, { replace: true })
  }
  return (
    <section className={styles.activitySection}>
      <nav aria-label="AWDP 活动视图" className={styles.activityTabs}>
        {views.map((item) => (
          <button
            className={view === item.id ? styles.activityTabActive : styles.activityTab}
            key={item.id}
            onClick={() => setView(item.id)}
            type="button"
          >
            {item.label}
          </button>
        ))}
      </nav>
      {view === 'scoreboard' ? (
        <div className={styles.tableViewport}>
          <table className={styles.activityTable}>
            <thead>
              <tr>
                <th>排名</th>
                <th>战队</th>
                <th>攻击</th>
                <th>SLA</th>
                <th>修补</th>
                <th>扣分</th>
                <th>AWDP 总分</th>
              </tr>
            </thead>
            <tbody>
              {scoreboard.map((item) => (
                <tr data-mine={item.teamId === myTeamId || undefined} key={item.teamId}>
                  <td>#{item.rank}</td>
                  <td>
                    <strong>{item.teamName}</strong>
                  </td>
                  <td>{item.attackScore}</td>
                  <td>{item.slaScore}</td>
                  <td>{item.patchScore}</td>
                  <td>{item.penaltyScore ? `-${item.penaltyScore}` : 0}</td>
                  <td>
                    <strong>{item.awdpScore}</strong>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : view === 'attacks' ? (
        <div className={styles.tableViewport}>
          <table className={styles.activityTable}>
            <thead>
              <tr>
                <th>时间</th>
                <th>攻击方</th>
                <th>目标方</th>
                <th>服务</th>
                <th>得分</th>
              </tr>
            </thead>
            <tbody>
              {attackLogs.map((item) => (
                <tr key={item.key}>
                  <td>{formatAwdpTime(item.time)}</td>
                  <td>{item.attackerTeam}</td>
                  <td>{item.victimTeam}</td>
                  <td>{item.serviceName}</td>
                  <td>+{item.points}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : (
        <div className={styles.defenseList}>
          {patchStatus.map((item) => {
            const defense = defenseMeta(item.defenseStatus)
            const result = patchMeta(item.lastPatchResult)
            return (
              <article key={item.serviceId}>
                <div>
                  <strong>{item.serviceName}</strong>
                  <small>{formatAwdpTime(item.lastPatchTime)}</small>
                </div>
                <span className={styles.statusMark} data-tone={defense.tone}>
                  {defense.label}
                </span>
                <span className={styles.statusMark} data-tone={result.tone}>
                  {result.label}
                </span>
                <p>{item.message || '尚无补丁验证消息。'}</p>
              </article>
            )
          })}
        </div>
      )}
      {view === 'scoreboard' && !scoreboard.length ? <p className={styles.empty}>AWDP 榜单尚未生成。</p> : null}
      {view === 'attacks' && !attackLogs.length ? <p className={styles.empty}>当前没有攻击得分记录。</p> : null}
      {view === 'defense' && !patchStatus.length ? <p className={styles.empty}>当前没有可修补服务。</p> : null}
    </section>
  )
}
