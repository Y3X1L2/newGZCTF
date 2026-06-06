import { Badge, Text } from '@mantine/core'
import { useElementSize } from '@mantine/hooks'
import dayjs from 'dayjs'
import { FC } from 'react'
import classes from '@Styles/ScreenDisplay.module.css'
import ScreenDisplayShell from './ScreenDisplayShell'
import { useGameScreenData, useRotatingWindow, useVisibleCount } from './useScreenData'

const LOG_ENTRY_HEIGHT = 96

interface LogsScreenViewProps {
  gameId: number
}

const LogsScreenView: FC<LogsScreenViewProps> = ({ gameId }) => {
  const data = useGameScreenData(gameId)
  const logBody = useElementSize()

  const visibleLogCount = useVisibleCount(logBody.height, LOG_ENTRY_HEIGHT, { min: 3, max: 12 })
  const logItems = useRotatingWindow(data.liveDynamics, visibleLogCount, data.now, 7000)

  return (
    <ScreenDisplayShell
      title={data.game?.title}
      now={data.now}
      countdownLabel={data.countdownLabel}
      countdownValue={data.countdownValue}
      statusLabel={data.phaseLabel}
      subtitle="LIVE INCIDENT STREAM"
      boardClassName={classes.logBoard}
      leftAside={
        <div className={classes.roundBadge}>
          当前回合
          <span>
            {data.roundInfo.current}/{data.roundInfo.total}
          </span>
        </div>
      }
      rightAside={
        <div className={classes.statsBadgeRow}>
          <div className={classes.statBadge}>
            <span>提交数</span>
            <strong>{data.submissionSummary.total}</strong>
          </div>
          <div className={classes.statBadge}>
            <span>成功率</span>
            <strong>{data.submissionSummary.solveRate}%</strong>
          </div>
          <div className={classes.statBadge}>
            <span>有效解题</span>
            <strong>{data.submissionSummary.accepted}</strong>
          </div>
          <div className={classes.statBadge}>
            <span>告警等级</span>
            <strong>{data.threatLevel}</strong>
          </div>
        </div>
      }
    >
      <section className={`${classes.panel} ${classes.logPanel}`}>
        <div className={classes.panelHead}>
          <Text className={classes.panelTitle}>实时日志流</Text>
          <Badge variant="light" color="cyan">
            自动轮播
          </Badge>
        </div>
        <div ref={logBody.ref} className={classes.panelBody}>
          {logItems.length > 0 ? (
            <div className={classes.logList}>
              {logItems.map((entry) => (
                <article key={entry.id} className={classes.logItem} data-tone={entry.tone}>
                  <div className={classes.logHead}>
                    <Text className={classes.itemTitle} title={entry.team}>
                      {entry.team}
                    </Text>
                    <Text className={classes.itemTime}>{dayjs(entry.time).format('HH:mm:ss')}</Text>
                  </div>
                  <Text className={classes.itemTitle} title={entry.challenge}>
                    {entry.challenge}
                  </Text>
                  <Text className={classes.itemDetail}>{entry.status}</Text>
                </article>
              ))}
            </div>
          ) : (
            <div className={classes.emptyPanel}>当前暂无实时日志</div>
          )}
        </div>
      </section>
    </ScreenDisplayShell>
  )
}

export default LogsScreenView
