import type { UserActivityPoint } from './api/userProfileApi'
import { buildHeatmapCells, type ProfileWindow } from './profileDomain'
import styles from './UserProfilePage.module.css'

function activityLabel(date: string, point: UserActivityPoint | null) {
  const formatted = new Intl.DateTimeFormat('zh-CN', { month: 'long', day: 'numeric' }).format(
    new Date(`${date}T00:00:00Z`)
  )
  if (!point?.total) return `${formatted}，无公开活动`
  return `${formatted}，共 ${point.total} 项：CTF ${point.ctf}，培训 ${point.training}，理论 ${point.theory}，AWDP ${point.awdp}，渗透 ${point.penetration}`
}

export function ProfileActivityHeatmap({
  points,
  from,
  to,
  window,
  loading,
  failed,
}: {
  points?: UserActivityPoint[]
  from: string
  to: string
  window: ProfileWindow
  loading: boolean
  failed: boolean
}) {
  const cells = buildHeatmapCells(points ?? [], from, to)
  const activeDays = points?.filter((point) => point.total > 0).length ?? 0
  const totalEvents = points?.reduce((sum, point) => sum + point.total, 0) ?? 0

  return (
    <section className={styles.profilePanel}>
      <header className={styles.panelHeading}>
        <div>
          <span className={styles.panelEyebrow}>ACTIVITY</span>
          <h2>学习与解题活跃度</h2>
        </div>
        <span>{window === '365d' ? '最近 52 周' : '最近 90 天'}</span>
      </header>
      {loading ? (
        <div className={styles.panelLoading}>正在汇总公开活动...</div>
      ) : failed ? (
        <div className={styles.panelLoading}>活动记录暂时无法读取。</div>
      ) : (
        <>
          <div
            aria-label={`${window === '365d' ? '最近 52 周' : '最近 90 天'}活动热力图`}
            className={window === '365d' ? styles.heatmapYear : styles.heatmapQuarter}
            role="img"
          >
            {cells.map((cell) =>
              cell.date ? (
                <span
                  aria-label={activityLabel(cell.date, cell.point)}
                  className={styles.heatCell}
                  data-level={cell.level}
                  key={cell.key}
                  tabIndex={cell.point?.total ? 0 : -1}
                  title={activityLabel(cell.date, cell.point)}
                />
              ) : (
                <span className={styles.heatCellOutside} key={cell.key} />
              )
            )}
          </div>
          <footer className={styles.heatmapFooter}>
            <div>
              <strong>{activeDays}</strong>
              <span>活跃天</span>
              <strong>{totalEvents}</strong>
              <span>有效事件</span>
            </div>
            <div className={styles.heatLegend} aria-label="活跃度由低到高">
              <span>低</span>
              {[0, 1, 2, 3, 4].map((level) => (
                <i data-level={level} key={level} />
              ))}
              <span>高</span>
            </div>
          </footer>
        </>
      )}
    </section>
  )
}
