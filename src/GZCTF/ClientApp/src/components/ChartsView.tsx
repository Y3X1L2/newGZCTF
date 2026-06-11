import { Badge, Stack, Text, Title } from '@mantine/core'
import { mdiClockOutline } from '@mdi/js'
import { Icon } from '@mdi/react'
import dayjs from 'dayjs'
import type { EChartsOption } from 'echarts'
import { FC, useEffect, useRef, useState } from 'react'
import { EchartsContainer } from '@Components/charts/EchartsContainer'
import type { ScoreboardItem } from '@Api'
import classes from '@Styles/ChartsView.module.css'

const RANK_ENTRY_HEIGHT = 72
const PROGRESS_ENTRY_HEIGHT = 68
const MIN_VISIBLE = 3

interface CategoryProgress {
  key: string
  name: string
  total: number
  cracked: number
  percent: number
}

interface ChartsViewProps {
  game?: { title?: string }
  now: number
  statusLabel: string
  countdownLabel: string
  countdownValue: string
  rankedTeams: ScoreboardItem[]
  challengeCount: number
  submissionSummary: { coverage: number }
  categoryProgress: CategoryProgress[]
  radarOption: EChartsOption
  trendOption: EChartsOption
}

const ChartsView: FC<ChartsViewProps> = (props) => {
  const {
    game,
    now,
    statusLabel,
    countdownLabel,
    countdownValue,
    rankedTeams,
    challengeCount,
    submissionSummary,
    categoryProgress,
    radarOption,
    trendOption,
  } = props

  const rankPanelRef = useRef<HTMLDivElement>(null)
  const progressPanelRef = useRef<HTMLDivElement>(null)
  const [rankPanelHeight, setRankPanelHeight] = useState(0)
  const [progressPanelHeight, setProgressPanelHeight] = useState(0)

  useEffect(() => {
    const ro = new ResizeObserver((entries) => {
      for (const entry of entries) {
        if (entry.target === rankPanelRef.current) {
          setRankPanelHeight(entry.contentRect.height)
        } else if (entry.target === progressPanelRef.current) {
          setProgressPanelHeight(entry.contentRect.height)
        }
      }
    })
    const rankEl = rankPanelRef.current
    const progressEl = progressPanelRef.current
    if (rankEl) ro.observe(rankEl)
    if (progressEl) ro.observe(progressEl)
    return () => {
      if (rankEl) ro.unobserve(rankEl)
      if (progressEl) ro.unobserve(progressEl)
    }
  }, [])

  const PANEL_HEAD_HEIGHT = 32
  const rankAvailable = Math.max(0, rankPanelHeight - PANEL_HEAD_HEIGHT)
  const progressAvailable = Math.max(0, progressPanelHeight - PANEL_HEAD_HEIGHT)
  const maxRankItems = Math.max(MIN_VISIBLE, Math.floor(rankAvailable / RANK_ENTRY_HEIGHT))
  const maxProgressItems = Math.max(MIN_VISIBLE, Math.floor(progressAvailable / PROGRESS_ENTRY_HEIGHT))

  const rankItems = rankedTeams.slice(0, maxRankItems)
  const rankMaxScore = Math.max(1, ...rankItems.map((item) => item.score), 1)
  const progressItems = categoryProgress.slice(0, maxProgressItems)

  return (
    <div className={classes.root}>
      <div className={classes.backgroundGrid} />

      <header className={classes.header}>
        <div className={classes.headerLeft}>
          <div className={classes.headerMetaValue}>
            <Icon path={mdiClockOutline} size={0.9} />
            <span>{countdownValue}</span>
          </div>
          <Text className={classes.headerCountdownLabel}>{countdownLabel}</Text>
        </div>

        <div className={classes.headerCenter}>
          <Title order={1} className={classes.headerTitle}>
            {game?.title ?? '攻防实时指挥大屏'}
          </Title>
          <div className={classes.headerAccent} />
        </div>

        <div className={classes.headerRight}>
          <Text className={classes.currentTime}>{dayjs(now).format('YYYY-MM-DD HH:mm:ss')}</Text>
          <Badge variant="light" color="cyan">
            {statusLabel}
          </Badge>
        </div>
      </header>

      <main className={classes.board}>
        <section className={`${classes.panel} ${classes.radarPanel}`}>
          <div className={classes.panelHead}>
            <Text className={classes.panelTitle}>攻击流向图</Text>
            <Text className={classes.panelHint}>分类攻防态势</Text>
          </div>
          <div className={classes.radarStage}>
            <EchartsContainer option={radarOption} className={classes.radarChart} />
          </div>
        </section>

        <section className={`${classes.panel} ${classes.trendPanel}`}>
          <div className={classes.panelHead}>
            <Text className={classes.panelTitle}>得分趋势</Text>
            <Text className={classes.panelHint}>实时分数变化</Text>
          </div>
          <EchartsContainer option={trendOption} className={classes.trendChart} />
        </section>

        <section className={`${classes.panel} ${classes.rankPanel}`} ref={rankPanelRef}>
          <div className={classes.panelHead}>
            <Text className={classes.panelTitle}>排行榜</Text>
            <Text className={classes.panelHint}>TOP {rankItems.length}</Text>
          </div>
          {rankItems.length > 0 ? (
            <div className={classes.rankList}>
              {rankItems.map((team, index) => {
                const solveRatio =
                  challengeCount > 0 ? Math.round((team.solvedCount / Math.max(challengeCount, 1)) * 100) : 0

                return (
                  <div key={team.id} className={classes.rankItem} data-top={index < 3 || undefined}>
                    <div className={classes.rankIndex}>{team.rank}</div>
                    <div className={classes.rankBody}>
                      <div className={classes.rankLine}>
                        <Text className={classes.rankName}>{team.name}</Text>
                        <Text className={classes.rankScore}>{team.score}</Text>
                      </div>
                      <div className={classes.rankBarTrack}>
                        <div
                          className={classes.rankBarFill}
                          style={{ width: `${(team.score / rankMaxScore) * 100}%` }}
                        />
                      </div>
                      <div className={classes.rankMeta}>
                        <span>解题 {team.solvedCount}</span>
                        <span>覆盖率 {solveRatio}%</span>
                      </div>
                    </div>
                  </div>
                )
              })}
            </div>
          ) : (
            <div className={classes.emptyPanel}>比赛开始后将显示实时排名</div>
          )}
        </section>

        <section className={`${classes.panel} ${classes.progressPanel}`} ref={progressPanelRef}>
          <div className={classes.panelHead}>
            <Text className={classes.panelTitle}>各方向解题进度</Text>
            <Text className={classes.panelHint}>服务状态</Text>
          </div>
          <div className={classes.progressTop}>
            <span>题目总数 {challengeCount}</span>
            <span>覆盖率 {submissionSummary.coverage}%</span>
          </div>
          {progressItems.length > 0 ? (
            <Stack gap="md">
              {progressItems.map((item) => (
                <div key={item.key} className={classes.progressItem}>
                  <div className={classes.progressHeader}>
                    <Text className={classes.progressName}>{item.name}</Text>
                    <Text className={classes.progressPercent}>{item.percent}%</Text>
                  </div>
                  <div className={classes.progressTrack}>
                    <div className={classes.progressFill} style={{ width: `${item.percent}%` }} />
                  </div>
                  <div className={classes.progressMeta}>
                    <span>已攻破 {item.cracked} 道</span>
                    <span>共 {item.total} 道</span>
                  </div>
                </div>
              ))}
            </Stack>
          ) : (
            <div className={classes.emptyPanel}>首批解题出现后将在此展示分类进度</div>
          )}
        </section>
      </main>
    </div>
  )
}

export default ChartsView
