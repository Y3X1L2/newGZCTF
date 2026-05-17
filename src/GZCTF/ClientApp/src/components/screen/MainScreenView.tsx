import { Badge, Group, Stack, Text } from '@mantine/core'
import { useElementSize } from '@mantine/hooks'
import { mdiChevronDown, mdiChevronUp } from '@mdi/js'
import { Icon } from '@mdi/react'
import dayjs from 'dayjs'
import { FC } from 'react'
import { EchartsContainer } from '@Components/charts/EchartsContainer'
import classes from '@Styles/ScreenDisplay.module.css'
import ScreenDisplayShell from './ScreenDisplayShell'
import {
  ScreenDynamicEntry,
  ScreenMoment,
  getRankChange,
  getScoreChange,
  useRotatingWindow,
  useVisibleCount,
  useGameScreenData,
} from './useScreenData'

const NOTICE_ENTRY_HEIGHT = 102
const ACTIVITY_ENTRY_HEIGHT = 92
const RANK_ENTRY_HEIGHT = 86
const PROGRESS_ENTRY_HEIGHT = 86

interface MainScreenViewProps {
  gameId: number
}

const toneBadgeColor: Record<ScreenMoment['tone'] | ScreenDynamicEntry['tone'], string> = {
  accent: 'cyan',
  neutral: 'gray',
  success: 'lime',
  warning: 'orange',
}

const MainScreenView: FC<MainScreenViewProps> = ({ gameId }) => {
  const data = useGameScreenData(gameId)
  const noticeBody = useElementSize()
  const activityBody = useElementSize()
  const rankBody = useElementSize()
  const progressBody = useElementSize()

  const visibleNoticeCount = useVisibleCount(noticeBody.height, NOTICE_ENTRY_HEIGHT, { min: 1, max: 4 })
  const visibleActivityCount = useVisibleCount(activityBody.height, ACTIVITY_ENTRY_HEIGHT, { min: 1, max: 6 })
  const visibleRankCount = useVisibleCount(rankBody.height, RANK_ENTRY_HEIGHT, { min: 3, max: 10 })
  const visibleProgressCount = useVisibleCount(progressBody.height, PROGRESS_ENTRY_HEIGHT, { min: 2, max: 6 })

  const noticeItems = useRotatingWindow(data.moments, visibleNoticeCount, data.now, 12000)
  const activityItems = useRotatingWindow(data.liveDynamics, visibleActivityCount, data.now, 9000)
  const leaderboard = data.rankedTeams.slice(0, visibleRankCount)
  const leaderboardMaxScore = Math.max(1, ...leaderboard.map((item) => item.score), 1)
  const progressItems = data.categoryProgress.slice(0, visibleProgressCount)
  const topTeamSolveRatio =
    data.topTeam && (data.scoreboard?.challengeCount ?? 0) > 0
      ? Math.round((data.topTeam.solvedCount / Math.max(data.scoreboard?.challengeCount ?? 1, 1)) * 100)
      : 0
  const overviewStats = [
    {
      label: '在线战队',
      value: data.activeTeamCount,
      detail: `活跃态势 ${data.activityLevel}`,
    },
    {
      label: '参赛人数',
      value: data.playerCount,
      detail: `近期攻破 ${data.recentAcceptedCount}`,
    },
    {
      label: '有效解题',
      value: data.totalSolveCount,
      detail: `命中率 ${data.submissionSummary.solveRate}%`,
    },
    {
      label: '总提交量',
      value: data.submissionSummary.total,
      detail: `威胁指数 ${data.threatIndex}`,
    },
  ]

  return (
    <ScreenDisplayShell
      title={data.game?.title}
      now={data.now}
      countdownLabel={data.countdownLabel}
      countdownValue={data.countdownValue}
      statusLabel={data.phaseLabel}
      subtitle="PRIMARY TACTICAL OVERVIEW"
      boardClassName={classes.mainBoard}
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
            <span>命中率</span>
            <strong>{data.submissionSummary.solveRate}%</strong>
          </div>
          <div className={classes.statBadge}>
            <span>威胁指数</span>
            <strong>{data.threatIndex}</strong>
          </div>
          <div className={classes.statBadge}>
            <span>态势等级</span>
            <strong>{data.threatLevel}</strong>
          </div>
        </div>
      }
    >
      <section className={`${classes.panel} ${classes.noticePanel}`}>
        <div className={classes.panelHead}>
          <Text className={classes.panelTitle}>赛事公告</Text>
          <Text className={classes.panelHint}>Incident broadcast</Text>
        </div>
        <div ref={noticeBody.ref} className={classes.panelBody}>
          {noticeItems.length > 0 ? (
            <div className={classes.list}>
              {noticeItems.map((moment) => (
                <article key={moment.id} className={classes.noticeItem} data-tone={moment.tone}>
                  <div className={classes.noticeHead}>
                    <Badge variant="light" color={toneBadgeColor[moment.tone]}>
                      {moment.tag}
                    </Badge>
                    <Text className={classes.itemTime}>{dayjs(moment.time).format('MM-DD HH:mm:ss')}</Text>
                  </div>
                  <Text className={classes.itemTitle} title={moment.title}>
                    {moment.title}
                  </Text>
                  <Text className={classes.itemDetail} title={moment.detail}>
                    {moment.detail}
                  </Text>
                </article>
              ))}
            </div>
          ) : (
            <div className={classes.emptyPanel}>当前暂无实时攻击日志</div>
          )}
        </div>
      </section>

      <section className={`${classes.panel} ${classes.statsPanel}`}>
        <div className={classes.commandHero}>
          <div className={classes.commandHeroMain}>
            <Text className={classes.commandHeroLabel}>战况摘要</Text>
            <Text className={classes.commandHeroTitle}>{data.topTeam?.name ?? '等待首个领先战队'}</Text>
            <Text className={classes.commandHeroDetail}>
              当前领先 {data.leaderGap} 分{data.runnerUp ? `，第二名 ${data.runnerUp.name}` : ''}
            </Text>
            <div className={classes.commandHeroTrack}>
              <div className={classes.commandHeroFill} style={{ width: `${data.missionProgress}%` }} />
            </div>
            <div className={classes.commandHeroMeta}>
              <span>任务进度 {data.missionProgress}%</span>
              <span>头部覆盖率 {topTeamSolveRatio}%</span>
            </div>
          </div>
          <div className={classes.commandHeroSide}>
            <div className={classes.heroSignalCard}>
              <span>威胁指数</span>
              <strong>{data.threatIndex}</strong>
              <small>{data.threatLevel}</small>
            </div>
            <div className={classes.heroSignalCard}>
              <span>实时脉冲</span>
              <strong>{data.scorePulse > 0 ? `+${data.scorePulse}` : '+0'}</strong>
              <small>{data.activityLevel}</small>
            </div>
          </div>
        </div>
        <div className={classes.kpiGrid}>
          {overviewStats.map((item, index) => (
            <article key={item.label} className={classes.kpiCard} data-accent={index === 0 || index === 3 || undefined}>
              <Text className={classes.kpiLabel}>{item.label}</Text>
              <Text className={classes.kpiValue}>{item.value}</Text>
              <Text className={classes.kpiDetail}>{item.detail}</Text>
            </article>
          ))}
        </div>
        <div className={classes.telemetryGrid}>
          <article className={classes.telemetryCard}>
            <span>热点题目</span>
            <strong>{data.hotChallenge?.title ?? '待激活'}</strong>
            <small>已攻破 {data.hotChallenge?.solved ?? 0} 队</small>
          </article>
          <article className={classes.telemetryCard}>
            <span>热点方向</span>
            <strong>{data.hotCategory?.name ?? '待激活'}</strong>
            <small>累计命中 {data.hotCategory?.solved ?? 0}</small>
          </article>
          <article className={classes.telemetryCard}>
            <span>榜单刷新</span>
            <strong>{dayjs(data.scoreboardUpdatedAt).format('HH:mm:ss')}</strong>
            <small>覆盖率 {data.submissionSummary.coverage}%</small>
          </article>
        </div>
      </section>

      <section className={`${classes.panel} ${classes.rankPanel}`}>
        <div className={classes.panelHead}>
          <Text className={classes.panelTitle}>排行榜</Text>
          <Text className={classes.panelHint}>{dayjs(data.scoreboardUpdatedAt).format('HH:mm:ss')}</Text>
        </div>
        <div ref={rankBody.ref} className={classes.panelBody}>
          {leaderboard.length > 0 ? (
            <div className={classes.rankList}>
              {leaderboard.map((team, index) => {
                const solveRatio =
                  (data.scoreboard?.challengeCount ?? 0) > 0
                    ? Math.round((team.solvedCount / Math.max(data.scoreboard?.challengeCount ?? 1, 1)) * 100)
                    : 0
                const rankDelta = getRankChange(team, data.rankDeltaMap)
                const scoreDelta = getScoreChange(team, data.scoreDeltaMap)

                return (
                  <article key={team.id} className={classes.rankItem} data-top={index < 3 || undefined}>
                    <div className={classes.rankIndex}>
                      <span className={classes.rankIndexLabel}>RANK</span>
                      <strong>{team.rank}</strong>
                    </div>
                    <div className={classes.rankBody}>
                      <div className={classes.rankLine}>
                        <Text className={classes.rankName} title={team.name}>
                          {team.name}
                        </Text>
                        <Group gap={6} wrap="nowrap">
                          {rankDelta !== 0 && (
                            <Badge
                              variant="light"
                              color={rankDelta > 0 ? 'cyan' : 'red'}
                              className={classes.deltaBadge}
                            >
                              <Icon path={rankDelta > 0 ? mdiChevronUp : mdiChevronDown} size={0.6} />
                              {Math.abs(rankDelta)}
                            </Badge>
                          )}
                          <Text className={classes.rankScore}>
                            {team.score}
                            {scoreDelta > 0 && <span className={classes.scoreDelta}>+{scoreDelta}</span>}
                          </Text>
                        </Group>
                      </div>
                      <div className={classes.rankBarTrack}>
                        <div
                          className={classes.rankBarFill}
                          style={{ width: `${(team.score / leaderboardMaxScore) * 100}%` }}
                        />
                      </div>
                      <div className={classes.rankMeta}>
                        <span>解题 {team.solvedCount}</span>
                        <span>覆盖率 {solveRatio}%</span>
                      </div>
                    </div>
                  </article>
                )
              })}
            </div>
          ) : (
            <div className={classes.emptyPanel}>比赛开始后将显示实时排名</div>
          )}
        </div>
      </section>

      <section className={`${classes.panel} ${classes.progressPanel}`}>
        <div className={classes.panelHead}>
          <Text className={classes.panelTitle}>各方向解题进度</Text>
          <Text className={classes.panelHint}>全局覆盖率 {data.submissionSummary.coverage}%</Text>
        </div>
        <div className={classes.signalStrip}>
          <div className={classes.signalChip}>
            <span>热点方向</span>
            <strong>{data.hotCategory?.name ?? '待激活'}</strong>
          </div>
          <div className={classes.signalChip}>
            <span>异常告警</span>
            <strong>{data.recentAlertCount}</strong>
          </div>
          <div className={classes.signalChip}>
            <span>近期攻破</span>
            <strong>{data.recentAcceptedCount}</strong>
          </div>
        </div>
        <div ref={progressBody.ref} className={classes.panelBody}>
          {progressItems.length > 0 ? (
            <Stack gap="sm" className={classes.fillStack}>
              {progressItems.map((item) => (
                <article key={item.key} className={classes.progressItem}>
                  <div className={classes.progressHeader}>
                    <Text className={classes.progressName} title={item.name}>
                      {item.name}
                    </Text>
                    <div className={classes.progressBadge}>
                      <span>流量 {item.attempts}</span>
                      <Text className={classes.progressPercent}>{item.percent}%</Text>
                    </div>
                  </div>
                  <div className={classes.progressTrack}>
                    <div className={classes.progressFill} style={{ width: `${item.percent}%` }} />
                  </div>
                  <div className={classes.progressMeta}>
                    <span>已攻破 {item.cracked} 道</span>
                    <span>共 {item.total} 道</span>
                    <span>近期流量 {item.attempts}</span>
                  </div>
                </article>
              ))}
            </Stack>
          ) : (
            <div className={classes.emptyPanel}>首批解题出现后将在此展示分类进度</div>
          )}
        </div>
      </section>

      <section className={`${classes.panel} ${classes.radarPanel}`}>
        <div className={classes.panelHead}>
          <Text className={classes.panelTitle}>攻击流向图</Text>
          <Text className={classes.panelHint}>Category combat pressure</Text>
        </div>
        <div className={`${classes.panelBody} ${classes.chartBody}`}>
          <div className={classes.radarHalo} />
          <EchartsContainer option={data.radarOption} className={classes.chart} />
        </div>
      </section>

      <section className={`${classes.panel} ${classes.trendPanel}`}>
        <div className={classes.panelHead}>
          <Text className={classes.panelTitle}>得分趋势</Text>
          <Text className={classes.panelHint}>Live score pulse</Text>
        </div>
        <div className={`${classes.panelBody} ${classes.chartBody}`}>
          <EchartsContainer option={data.trendOption} className={classes.chart} />
        </div>
      </section>

      <section className={`${classes.panel} ${classes.activityPanel}`}>
        <div className={classes.panelHead}>
          <Text className={classes.panelTitle}>消息动态</Text>
          <Text className={classes.panelHint}>WebSocket tactical relay</Text>
        </div>
        <div ref={activityBody.ref} className={classes.panelBody}>
          {activityItems.length > 0 ? (
            <div className={classes.list}>
              {activityItems.map((item) => (
                <article key={item.id} className={classes.activityItem} data-tone={item.tone}>
                  <div className={classes.activityHead}>
                    <Text className={classes.itemTitle} title={item.team}>
                      {item.team}
                    </Text>
                    <Text className={classes.itemTime}>{dayjs(item.time).format('HH:mm:ss')}</Text>
                  </div>
                  <Text className={classes.itemTitle} title={item.challenge}>
                    {item.challenge}
                  </Text>
                  <Text className={classes.itemDetail}>{item.status}</Text>
                </article>
              ))}
            </div>
          ) : (
            <div className={classes.emptyPanel}>当前暂无提交流水</div>
          )}
        </div>
      </section>
    </ScreenDisplayShell>
  )
}

export default MainScreenView
