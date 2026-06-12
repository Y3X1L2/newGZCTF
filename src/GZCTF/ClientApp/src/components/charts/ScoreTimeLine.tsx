import { useMantineTheme } from '@mantine/core'
import dayjs from 'dayjs'
import type { EChartsOption, SeriesOption } from 'echarts'
import { FC, useMemo } from 'react'
import { useTranslation } from 'react-i18next'
import { useParams } from 'react-router'
import { EchartsContainer } from '@Components/charts/EchartsContainer'
import { YinyuHexField, YinyuRouteLoader } from '@Components/yinyu/YinyuUI'
import { normalizeLanguage, useLanguage } from '@Utils/I18n'
import { getGameStatus, useGame, useGameScoreboard } from '@Hooks/useGame'
import { TimeLine, TopTimeLine } from '@Api'

interface TimeLineProps {
  divisionId: number | null
}

export const ScoreTimeLine: FC<TimeLineProps> = ({ divisionId }) => {
  const { id } = useParams()
  const numId = parseInt(id ?? '-1')
  const theme = useMantineTheme()

  const { scoreboard } = useGameScoreboard(numId)

  const { game } = useGame(numId)
  const { t } = useTranslation()
  const { language } = useLanguage()
  const locale = normalizeLanguage(language)

  const { startTime, endTime, progress, finished } = getGameStatus(game)

  const totDuration = endTime.diff(startTime, 'd')
  const longGame = totDuration > 14

  const weekProgress = (7 / totDuration) * 100
  const weekStart = progress - weekProgress
  const weekEnd = progress

  const drawStart = longGame && !finished ? weekStart : 0
  const drawEnd = longGame && !finished ? weekEnd : 100

  const divisionTimelineMap = useMemo(() => {
    const map = new Map<number, TopTimeLine[]>()

    if (!scoreboard?.timelines) return map

    scoreboard.timelines.forEach((item) => {
      const key = item.divisionId ?? 0
      map.set(key, item.teams ?? [])
    })

    return map
  }, [scoreboard?.timelines])

  const selectedDivisionId = useMemo(() => (divisionId === null ? 0 : divisionId), [divisionId])

  const activeTeams = useMemo(() => {
    if (divisionTimelineMap.size === 0) return undefined

    const direct = divisionTimelineMap.get(selectedDivisionId)
    if (direct) return direct

    const overall = divisionTimelineMap.get(0)
    if (overall) return overall

    const iterator = divisionTimelineMap.values().next()
    return iterator.done ? undefined : iterator.value
  }, [divisionTimelineMap, selectedDivisionId])

  const chartData: SeriesOption[] = useMemo(() => {
    if (!activeTeams || !game) return []

    const timeLine = activeTeams
    const current = dayjs()
    const last = endTime.diff(current, 's') < 0 ? endTime : current

    return [
      {
        type: 'line',
        step: 'end',
        data: [],
        markLine:
          dayjs(game.end).diff(dayjs(), 's') < 0
            ? undefined
            : {
                symbol: 'none',
                // https://echarts.apache.org/en/option.html#series-line.markLine.data
                data: [
                  {
                    // xAxis?: string | number, but we need to use a Date object
                    xAxis: last.toDate(),
                    lineStyle: {
                      color: theme.colors.dark[3],
                      wight: 2,
                    },
                    label: {
                      textBorderWidth: 0,
                      fontWeight: 500,
                      formatter: (time: any) => dayjs(time.value).format('YYYY-MM-DD HH:mm'),
                    },
                  },
                ],
              },
      } as SeriesOption,
      ...(timeLine?.map(
        (team) =>
          ({
            type: 'line',
            step: 'end',
            name: team.name,
            showSymbol: false,
            symbol: 'circle',
            symbolSize: 6,
            lineStyle: {
              width: 2.4,
              shadowBlur: 10,
              shadowColor: 'rgba(107, 238, 177, 0.16)',
            },
            emphasis: {
              focus: 'series',
              lineStyle: {
                width: 3.2,
              },
            },
            data: [
              [dayjs(game.start).toDate(), 0],
              ...(team.items?.map((timeline: TimeLine) => [timeline.time, timeline.score]) ?? []),
              [last.toDate(), (team.items && team.items[team.items.length - 1]?.score) ?? 0],
            ],
          }) satisfies SeriesOption
      ) ?? []),
    ]
  }, [activeTeams, game, endTime, theme])

  const staticOption: EChartsOption = useMemo(() => {
    const labelColor = 'rgba(244, 245, 245, 0.82)'
    const quietColor = 'rgba(244, 245, 245, 0.54)'
    const lineColor = 'rgba(244, 245, 245, 0.14)'
    const backgroundColor = 'rgba(8, 12, 12, 0.94)'

    return {
      animation: false,
      backgroundColor: 'transparent',
      color: ['#6beeb1', '#d6f75f', '#8ad7ff', '#f5f5f7', '#e2b35e', '#8f7aff', '#ff7a90'],
      toolbox: {
        show: true,
        right: 10,
        top: 8,
        iconStyle: {
          borderColor: quietColor,
        },
        emphasis: {
          iconStyle: {
            borderColor: '#6beeb1',
          },
        },
        feature: {
          dataZoom: {
            yAxisIndex: false,
          },
          restore: {},
          saveAsImage: {},
        },
      },
      xAxis: {
        type: 'time',
        min: dayjs(game?.start).toDate(),
        max: dayjs(game?.end).toDate(),
        splitLine: {
          show: true,
          lineStyle: {
            color: 'rgba(244, 245, 245, 0.055)',
          },
        },
        axisLabel: {
          color: quietColor,
        },
        axisLine: {
          lineStyle: {
            color: lineColor,
          },
        },
        axisTick: {
          lineStyle: {
            color: lineColor,
          },
        },
      },
      yAxis: {
        type: 'value',
        name: t('game.label.score'),
        nameTextStyle: {
          color: labelColor,
          fontWeight: 'normal',
        },
        boundaryGap: [0, '100%'],
        axisLabel: {
          formatter: t('game.label.score_formatter'),
          color: labelColor,
        },
        max: (value: any) => (Math.floor(value.max / 1000) + 1) * 1000,
        splitLine: {
          show: true,
          lineStyle: {
            color: [lineColor],
            type: 'dashed',
          },
        },
        axisLine: {
          lineStyle: {
            color: lineColor,
          },
        },
        axisTick: {
          lineStyle: {
            color: lineColor,
          },
        },
      },
      tooltip: {
        trigger: 'axis',
        confine: true,
        appendToBody: false,
        textStyle: {
          fontSize: 12,
          color: labelColor,
        },
        backgroundColor: backgroundColor,
        borderColor: 'rgba(107, 238, 177, 0.22)',
        borderWidth: 1,
        extraCssText: 'box-shadow: 0 1rem 2.4rem rgba(0,0,0,.36); border-radius: 6px; backdrop-filter: blur(14px);',
      },
      legend: {
        orient: 'horizontal',
        type: 'scroll',
        left: 'center',
        right: 36,
        bottom: 0,
        pageIconColor: '#6beeb1',
        pageIconInactiveColor: 'rgba(244, 245, 245, 0.22)',
        pageTextStyle: {
          color: labelColor,
        },
        textStyle: {
          fontSize: 12,
          color: labelColor,
        },
      },
      grid: {
        top: 50,
        left: 70,
        right: 64,
        bottom: 124,
      },
      dataZoom: [
        {
          type: 'slider',
          start: drawStart,
          end: drawEnd,
          xAxisIndex: 0,
          showDetail: true,
          bottom: 48,
          height: 28,
          brushSelect: false,
          borderColor: 'rgba(107, 238, 177, 0.16)',
          fillerColor: 'rgba(107, 238, 177, 0.18)',
          backgroundColor: 'rgba(255, 255, 255, 0.045)',
          textStyle: {
            color: labelColor,
            fontFamily: 'var(--font-mono)',
            fontSize: 11,
          },
          labelFormatter: (value: number | string) => dayjs(value).format('MM/DD HH:mm'),
          handleSize: '92%',
          dataBackground: {
            lineStyle: { color: 'rgba(107, 238, 177, 0.32)' },
            areaStyle: { color: 'rgba(107, 238, 177, 0.08)' },
          },
        },
        {
          type: 'slider',
          start: 0,
          end: 100,
          yAxisIndex: 0,
          showDetail: false,
          right: 10,
          width: 20,
          brushSelect: false,
          borderColor: 'rgba(107, 238, 177, 0.16)',
          fillerColor: 'rgba(107, 238, 177, 0.18)',
          backgroundColor: 'rgba(255, 255, 255, 0.045)',
        },
      ],
    } satisfies EChartsOption
  }, [t, game?.start, game?.end, drawStart, drawEnd])

  if (!game) {
    return (
      <section className="panel-card yy-score-timeline-panel yy-score-timeline-loading">
        <YinyuHexField cells={46} />
        <YinyuRouteLoader title={t('game.label.scoreboard')} description="Loading timeline coordinates" />
      </section>
    )
  }

  return (
    <section className="panel-card yy-score-timeline-panel">
      <YinyuHexField cells={46} />
      <EchartsContainer
        option={{
          ...staticOption,
          series: chartData,
        }}
        opts={{
          renderer: 'svg',
          locale,
        }}
        style={{
          width: '100%',
          height: '500px',
          display: 'flex',
        }}
      />
    </section>
  )
}
