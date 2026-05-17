import { Badge, Stack, Text, Title } from '@mantine/core'
import { mdiClockOutline } from '@mdi/js'
import { Icon } from '@mdi/react'
import dayjs from 'dayjs'
import { FC, useEffect, useRef, useState } from 'react'
import type { Submission } from '@Api'
import classes from '@Styles/LogsView.module.css'

interface LogEntry {
  id: string
  time: number | undefined
  team: string
  challenge: string
  status: string
  tone: 'accent' | 'success' | 'warning' | 'neutral'
}

interface LogsViewProps {
  game?: { title?: string; start?: number; end?: number }
  now: number
  statusLabel: string
  submissionFeed: Submission[]
  logEntries: LogEntry[]
  submissionSummary: { total: number; accepted: number; solveRate: number }
}

const LogsView: FC<LogsViewProps> = (props) => {
  const { game, now, statusLabel, submissionFeed, logEntries, submissionSummary } = props

  const [containerHeight, setContainerHeight] = useState(0)
  const [paused, setPaused] = useState(false)
  const containerRef = useRef<HTMLDivElement>(null)
  const logContainerRef = useRef<HTMLDivElement>(null)
  const autoScrollRef = useRef(true)

  useEffect(() => {
    const observer = new ResizeObserver((entries) => {
      for (const entry of entries) {
        setContainerHeight(entry.contentRect.height)
      }
    })
    const el = containerRef.current
    if (el) observer.observe(el)
    return () => {
      if (el) observer.unobserve(el)
    }
  }, [])

  const entryHeight = 64
  const maxVisible = Math.max(3, Math.floor(containerHeight / entryHeight))

  const visibleLogs = logEntries.slice(0, maxVisible)

  useEffect(() => {
    if (autoScrollRef.current && !paused && logContainerRef.current) {
      logContainerRef.current.scrollTop = 0
    }
  }, [visibleLogs.length, paused])

  useEffect(() => {
    autoScrollRef.current = !paused
  }, [paused])

  return (
    <div className={classes.root}>
      <div className={classes.backgroundGrid} />

      <header className={classes.header}>
        <div className={classes.headerLeft}>
          <div className={classes.headerMetaValue}>
            <Icon path={mdiClockOutline} size={0.9} />
            <span>{dayjs(now).format('HH:mm:ss')}</span>
          </div>
          <Text className={classes.headerCountdownLabel}>{statusLabel}</Text>
        </div>

        <div className={classes.headerCenter}>
          <Title order={1} className={classes.headerTitle}>
            {game?.title ?? '攻防实时指挥大屏'}
          </Title>
          <div className={classes.headerAccent} />
        </div>

        <div className={classes.headerRight}>
          <div className={classes.statsRow}>
            <span>提交 {submissionSummary.total}</span>
            <span className={classes.statsHighlight}>成功率 {submissionSummary.solveRate}%</span>
          </div>
        </div>
      </header>

      <main
        className={classes.board}
        ref={containerRef}
        onMouseEnter={() => setPaused(true)}
        onMouseLeave={() => setPaused(false)}
      >
        <section className={classes.logPanel}>
          <div className={classes.panelHead}>
            <Text className={classes.panelTitle}>实时日志流</Text>
            <Badge variant="light" color={paused ? 'orange' : 'cyan'}>
              {paused ? '已暂停' : '滚动中'}
            </Badge>
          </div>
          <div className={classes.logContainer} ref={logContainerRef}>
            {visibleLogs.length > 0 ? (
              <Stack gap="sm">
                {visibleLogs.map((entry) => (
                  <div key={entry.id} className={classes.logItem} data-tone={entry.tone}>
                    <div className={classes.logHead}>
                      <Text className={classes.logTeam}>{entry.team}</Text>
                      <Text className={classes.logTime}>{dayjs(entry.time).format('HH:mm:ss')}</Text>
                    </div>
                    <Text className={classes.logChallenge}>{entry.challenge}</Text>
                    <Text className={classes.logStatus}>{entry.status}</Text>
                  </div>
                ))}
              </Stack>
            ) : (
              <div className={classes.emptyPanel}>当前暂无实时日志</div>
            )}
          </div>
        </section>
      </main>
    </div>
  )
}

export default LogsView
