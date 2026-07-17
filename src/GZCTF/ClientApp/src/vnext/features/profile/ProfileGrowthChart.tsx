import { KeyboardEvent, PointerEvent, useEffect, useMemo, useState } from 'react'
import type { UserProfileTrendPoint } from './api/userProfileApi'
import { buildTrendGeometry, type ProfileWindow } from './profileDomain'
import styles from './UserProfilePage.module.css'

function formatDate(value: string) {
  return new Intl.DateTimeFormat('zh-CN', { year: 'numeric', month: 'short', day: 'numeric' }).format(
    new Date(`${value}T00:00:00Z`)
  )
}

export function ProfileGrowthChart({ trend, window }: { trend: UserProfileTrendPoint[]; window: ProfileWindow }) {
  const geometry = useMemo(() => buildTrendGeometry(trend), [trend])
  const [activeIndex, setActiveIndex] = useState(Math.max(0, trend.length - 1))
  const active = geometry.points[Math.min(activeIndex, Math.max(0, geometry.points.length - 1))]

  useEffect(() => setActiveIndex(Math.max(0, trend.length - 1)), [trend])

  const moveToPointer = (event: PointerEvent<SVGSVGElement>) => {
    if (!trend.length) return
    const bounds = event.currentTarget.getBoundingClientRect()
    const ratio = Math.min(1, Math.max(0, (event.clientX - bounds.left) / bounds.width))
    const next = Math.round(ratio * (trend.length - 1))
    setActiveIndex((current) => (current === next ? current : next))
  }
  const onKeyDown = (event: KeyboardEvent<SVGSVGElement>) => {
    if (event.key !== 'ArrowLeft' && event.key !== 'ArrowRight') return
    event.preventDefault()
    setActiveIndex((current) =>
      Math.min(trend.length - 1, Math.max(0, current + (event.key === 'ArrowLeft' ? -1 : 1)))
    )
  }

  return (
    <section className={styles.profilePanel}>
      <header className={styles.panelHeading}>
        <div>
          <span className={styles.panelEyebrow}>GROWTH</span>
          <h2>个人解题成长趋势</h2>
        </div>
        <span>{window === '365d' ? '最近一年' : '最近 90 天'}</span>
      </header>
      {active && geometry.line ? (
        <div className={styles.trendLayout}>
          <svg
            aria-label={`累计个人解题趋势，当前 ${formatDate(active.point.date)} 共 ${active.point.cumulativeSolved} 题`}
            className={styles.trendChart}
            onKeyDown={onKeyDown}
            onPointerMove={moveToPointer}
            role="img"
            tabIndex={0}
            viewBox="0 0 720 220"
          >
            {[50, 100, 150, 196].map((y) => (
              <line className={styles.trendGrid} key={y} x1="0" x2="720" y1={y} y2={y} />
            ))}
            <polygon className={styles.trendArea} points={geometry.area} />
            <polyline className={styles.trendLine} points={geometry.line} />
            <line className={styles.trendCursor} x1={active.x} x2={active.x} y1="16" y2="196" />
            <circle className={styles.trendDot} cx={active.x} cy={active.y} r="5" />
          </svg>
          <div className={styles.trendReading} aria-live="polite">
            <span>{formatDate(active.point.date)}</span>
            <strong>{active.point.cumulativeSolved}</strong>
            <small>累计个人解题</small>
            <em>当日新增 {active.point.delta}</em>
          </div>
        </div>
      ) : (
        <div className={styles.panelLoading}>当前时间窗还没有个人解题趋势。</div>
      )}
    </section>
  )
}
