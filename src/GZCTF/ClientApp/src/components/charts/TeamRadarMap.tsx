import { alpha, useMantineTheme } from '@mantine/core'
import type { EChartsOption } from 'echarts'
import { FC, useMemo } from 'react'
import { EchartsContainer } from '@Components/charts/EchartsContainer'

export interface TeamRadarMapProps {
  indicator: { name: string; max: number }[]
  value: number[]
  name: string
}

export const TeamRadarMap: FC<TeamRadarMapProps> = ({ indicator, value, name }) => {
  const theme = useMantineTheme()

  const option: EChartsOption = useMemo(
    () =>
      ({
        animation: true,
        color: theme.colors[theme.primaryColor][5],
        backgroundColor: 'transparent',
        radar: {
          indicator,
          shape: 'circle',
          radius: '75%',
          center: ['50%', '55%'],
          axisName: {
            color: theme.colors.light[1],
            fontWeight: 'bold',
          },
        },
        series: [
          {
            type: 'radar',
            data: [
              {
                value,
                name,
                areaStyle: {
                  color: alpha(theme.colors[theme.primaryColor][4], 0.8),
                },
                lineStyle: {
                  width: 2,
                },
                symbolSize: 4,
              },
            ],
          },
        ],
      }) satisfies EChartsOption,
    [theme, indicator, value, name]
  )

  return (
    <EchartsContainer
      option={option}
      opts={{
        renderer: 'svg',
      }}
      style={{
        width: '100%',
        height: '100%',
        display: 'flex',
      }}
    />
  )
}
