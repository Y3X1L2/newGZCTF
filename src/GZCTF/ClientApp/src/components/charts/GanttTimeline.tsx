import { Text } from '@mantine/core'
import cx from 'clsx'
import dayjs, { Dayjs } from 'dayjs'
import gsap from 'gsap'
import { CSSProperties, FC, useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { BrandMark } from '@Components/yinyu/BrandMark'
import { YinyuHeartbeatIcon } from '@Components/yinyu/YinyuUI'
import { useLanguage } from '@Utils/I18n'
import classes from '@Styles/GanttTimeline.module.css'

interface GanttTimeLineProps {
  items: GanttItem[]
}

export interface GanttItem {
  id: number
  color?: string
  textTitle: string
  title: React.ReactNode
  start: Dayjs
  end: Dayjs
}

const clamp = (value: number, min = 0, max = 100) => Math.min(Math.max(value, min), max)

const dateWindow = () => {
  const now = dayjs()
  const start = now.startOf('week').subtract(3, 'week')
  const end = start.add(7, 'week')
  const duration = Math.max(end.diff(start, 'second'), 1)

  return { now, start, end, duration }
}

export const GanttTimeLine: FC<GanttTimeLineProps> = ({ items }) => {
  const rootRef = useRef<HTMLElement>(null)
  const [hoveredId, setHoveredId] = useState<number | null>(null)
  const { t } = useTranslation()
  const { locale } = useLanguage()

  const timeline = useMemo(() => {
    const { now, start, end, duration } = dateWindow()
    const nowPercent = clamp((now.diff(start, 'second') / duration) * 100)

    const ticks = Array.from({ length: 8 }).map((_, index) => {
      const tick = start.add(index, 'week').locale(locale)
      return {
        id: tick.valueOf(),
        label: tick.format('MM/DD'),
        longLabel: tick.format('MMM D'),
        left: clamp((tick.diff(start, 'second') / duration) * 100),
      }
    })

    const rows = items.map((item) => {
      const rawLeft = (item.start.diff(start, 'second') / duration) * 100
      const rawRight = (item.end.diff(start, 'second') / duration) * 100
      const visibleLeft = clamp(rawLeft)
      const visibleRight = clamp(rawRight)
      const width = Math.max(visibleRight - visibleLeft, 3.8)
      const active = now.isAfter(item.start) && now.isBefore(item.end)
      const upcoming = now.isBefore(item.start)
      const state = active ? 'active' : upcoming ? 'upcoming' : 'ended'

      return {
        ...item,
        left: visibleLeft,
        width,
        state,
        startsBefore: rawLeft < 0,
        endsAfter: rawRight > 100,
        rawLeft,
        rawRight,
        visibleRight,
        startLabel: item.start.locale(locale).format('MM/DD HH:mm'),
        endLabel: item.end.locale(locale).format('MM/DD HH:mm'),
        fullStartLabel: item.start.locale(locale).format('YYYY/MM/DD HH:mm'),
        fullEndLabel: item.end.locale(locale).format('YYYY/MM/DD HH:mm'),
      }
    })

    return {
      now,
      start,
      end,
      nowPercent,
      ticks,
      rows,
      activeCount: rows.filter((row) => row.state === 'active').length,
      upcomingCount: rows.filter((row) => row.state === 'upcoming').length,
    }
  }, [items, locale])

  const hoveredRow = timeline.rows.find((item) => item.id === hoveredId)

  useEffect(() => {
    const root = rootRef.current
    if (!root || window.matchMedia('(prefers-reduced-motion: reduce)').matches) return undefined

    const ctx = gsap.context(() => {
      const rows = gsap.utils.toArray<HTMLElement>(`.${classes.timelineRow}`)
      const bars = gsap.utils.toArray<HTMLElement>(`.${classes.rowBar}`)
      const nodes = gsap.utils.toArray<HTMLElement>(`.${classes.signalNode}`)

      gsap.from(rows, {
        opacity: 0,
        y: 18,
        duration: 0.58,
        ease: 'power3.out',
        stagger: 0.045,
      })

      gsap.from(bars, {
        scaleX: 0.08,
        transformOrigin: 'left center',
        duration: 0.82,
        ease: 'expo.out',
        stagger: 0.04,
      })

      gsap.to(nodes, {
        opacity: 0.92,
        scale: 1.18,
        duration: 1.65,
        ease: 'sine.inOut',
        stagger: { each: 0.12, repeat: -1, yoyo: true },
      })

      gsap.to(root, {
        '--timeline-breathe': 1,
        duration: 2.8,
        repeat: -1,
        yoyo: true,
        ease: 'sine.inOut',
      })
    }, root)

    return () => ctx.revert()
  }, [items.length])

  if (!items.length) return null

  return (
    <section
      ref={rootRef}
      className={classes.shell}
      aria-label={t('common.content.home.recent_games')}
      style={
        {
          '--timeline-now': `${timeline.nowPercent}%`,
          '--timeline-breathe': 0,
          '--focus-left': `${hoveredRow?.left ?? timeline.nowPercent}%`,
          '--focus-width': `${hoveredRow?.width ?? 0}%`,
        } as CSSProperties
      }
      data-has-focus={hoveredRow ? true : undefined}
    >
      <header className={classes.header}>
        <div className={classes.heading}>
          <BrandMark className={classes.headingMark} />
          <div>
            <span>EVENT SCHEDULE</span>
            <h3>{t('common.content.home.recent_games')}</h3>
          </div>
        </div>
        <div className={classes.summary}>
          <YinyuHeartbeatIcon label="schedule signal" />
          <Text span>{timeline.activeCount} LIVE</Text>
          <Text span>{timeline.upcomingCount} READY</Text>
        </div>
      </header>

      <div className={classes.viewport}>
        <div className={classes.axis} aria-hidden="true">
          {timeline.ticks.map((tick) => (
            <span
              key={tick.id}
              className={classes.tick}
              data-focus={
                hoveredRow && tick.left >= Math.max(0, hoveredRow.rawLeft) && tick.left <= Math.min(100, hoveredRow.rawRight)
                  ? true
                  : undefined
              }
              style={{ '--tick-left': `${tick.left}%` } as CSSProperties}
            >
              <i />
              <b>{tick.label}</b>
            </span>
          ))}
          <span className={classes.focusRange}>
            {hoveredRow ? (
              <b>
                {hoveredRow.fullStartLabel} - {hoveredRow.fullEndLabel}
              </b>
            ) : null}
          </span>
          <span className={classes.nowAxis}>
            <i />
            <b>{timeline.now.locale(locale).format('MM/DD HH:mm')}</b>
          </span>
        </div>

        <div className={classes.rows}>
          {timeline.rows.map((item, index) => (
            <Link
              key={item.id}
              to={`/games/${item.id}`}
              className={cx(
                classes.timelineRow,
                item.state === 'active' && classes.active,
                item.state === 'upcoming' && classes.upcoming,
                item.state === 'ended' && classes.ended
              )}
              data-state={item.state}
              onMouseEnter={() => setHoveredId(item.id)}
              onMouseLeave={() => setHoveredId((current) => (current === item.id ? null : current))}
              onFocus={() => setHoveredId(item.id)}
              onBlur={() => setHoveredId((current) => (current === item.id ? null : current))}
              style={
                {
                  '--row-left': `${item.left}%`,
                  '--row-width': `${item.width}%`,
                  '--row-color': item.color || 'rgba(107, 238, 177, 0.75)',
                  '--row-index': index,
                } as CSSProperties
              }
            >
              <div className={classes.rowMeta}>
                <span className={classes.metaMark}>
                  <BrandMark />
                  <i className={classes.signalNode} />
                </span>
                <span>
                  <strong>{item.textTitle}</strong>
                  <small>
                    {item.startLabel} / {item.endLabel}
                  </small>
                </span>
              </div>
              <div className={classes.track}>
                <span
                  className={classes.rowBar}
                  aria-label={`${item.textTitle}: ${item.startLabel} - ${item.endLabel}`}
                  data-start-overflow={item.startsBefore || undefined}
                  data-end-overflow={item.endsAfter || undefined}
                >
                  <b className={classes.timeTooltip}>
                    {item.fullStartLabel} - {item.fullEndLabel}
                  </b>
                  <i className={classes.barLead} />
                  <i className={classes.barCore} />
                  <i className={classes.barTail} />
                </span>
              </div>
            </Link>
          ))}
        </div>
      </div>
    </section>
  )
}
