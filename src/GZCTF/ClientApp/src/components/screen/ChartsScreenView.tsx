import { Text } from '@mantine/core'
import { FC } from 'react'
import { EchartsContainer } from '@Components/charts/EchartsContainer'
import classes from '@Styles/ScreenDisplay.module.css'
import ScreenDisplayShell from './ScreenDisplayShell'
import { useGameScreenData } from './useScreenData'

interface ChartsScreenViewProps {
  gameId: number
}

const ChartsScreenView: FC<ChartsScreenViewProps> = ({ gameId }) => {
  const data = useGameScreenData(gameId)
  const rankItems = data.rankedTeams.slice(0, 8)
  const progressItems = data.categoryProgress.slice(0, 6)

  return (
    <ScreenDisplayShell
      title={data.game?.title}
      now={data.now}
      countdownLabel={data.countdownLabel}
      countdownValue={data.countdownValue}
      statusLabel={data.phaseLabel}
      subtitle="ANALYTICS SECONDARY PANEL"
      boardClassName={classes.dualBoard}
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
            <span>题目总数</span>
            <strong>{data.scoreboard?.challengeCount ?? 0}</strong>
          </div>
          <div className={classes.statBadge}>
            <span>覆盖率</span>
            <strong>{data.submissionSummary.coverage}%</strong>
          </div>
          <div className={classes.statBadge}>
            <span>热点方向</span>
            <strong>{data.hotCategory?.name ?? '待激活'}</strong>
          </div>
        </div>
      }
    >
      <section className={`${classes.panel} ${classes.dualPanel}`}>
        <div className={classes.panelHead}>
          <Text className={classes.panelTitle}>攻击流向图</Text>
          <Text className={classes.panelHint}>Hot categories radar</Text>
        </div>
        <div className={`${classes.panelBody} ${classes.chartBody}`}>
          <div className={classes.radarHalo} />
          <EchartsContainer option={data.radarOption} className={classes.chart} />
        </div>
      </section>

      <section className={`${classes.panel} ${classes.dualPanel}`}>
        <div className={classes.panelHead}>
          <Text className={classes.panelTitle}>得分趋势</Text>
          <Text className={classes.panelHint}>Score acceleration lanes</Text>
        </div>
        <div className={`${classes.panelBody} ${classes.chartBody}`}>
          <EchartsContainer option={data.trendOption} className={classes.chart} />
        </div>
      </section>

      <section className={`${classes.panel} ${classes.dualPanel}`}>
        <div className={classes.panelHead}>
          <Text className={classes.panelTitle}>战队火力排行</Text>
          <Text className={classes.panelHint}>Bar comparison / TOP {rankItems.length}</Text>
        </div>
        <div className={`${classes.panelBody} ${classes.chartBody}`}>
          <EchartsContainer option={data.rankingOption} className={classes.chart} />
        </div>
        <div className={classes.chartFooter}>
          <span>领跑战队 {data.topTeam?.name ?? '待激活'}</span>
          <strong>领先 {data.leaderGap} 分</strong>
        </div>
      </section>

      <section className={`${classes.panel} ${classes.dualPanel}`}>
        <div className={classes.panelHead}>
          <Text className={classes.panelTitle}>方向渗透率</Text>
          <Text className={classes.panelHint}>Horizontal progress bars</Text>
        </div>
        <div className={`${classes.panelBody} ${classes.chartBody}`}>
          <EchartsContainer option={data.progressChartOption} className={classes.chart} />
        </div>
        <div className={classes.chartFooter}>
          {progressItems.length > 0 ? (
            <>
              <span>热点方向 {data.hotCategory?.name ?? '待激活'}</span>
              <strong>{progressItems[0]?.percent ?? 0}%</strong>
            </>
          ) : (
            <>
              <span>热点方向</span>
              <strong>待激活</strong>
            </>
          )}
        </div>
      </section>
    </ScreenDisplayShell>
  )
}

export default ChartsScreenView
