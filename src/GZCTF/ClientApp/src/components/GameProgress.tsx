import { BoxProps, Center, MantineColor, em, useMantineTheme } from '@mantine/core'
import { FC } from 'react'
import classes from '@Styles/GameProgress.module.css'

export interface GameProgressProps extends BoxProps {
  thickness?: number
  spikeLength?: number
  percentage: number
  color?: MantineColor
}

export const GameProgress: FC<GameProgressProps> = (props: GameProgressProps) => {
  const { thickness = 4, spikeLength: _spikeLength = 250, percentage, color, ...others } = props

  const theme = useMantineTheme()

  const pulsing = percentage < 100
  const resolvedColor = pulsing ? color ?? 'light' : 'gray'
  const palette = theme.colors[resolvedColor] ?? theme.colors.teal
  const spikeColor = color ? palette[5] : 'var(--yy-green)'
  const bgColor = color ? palette[2] : 'rgba(107, 238, 177, 0.2)'
  const safePercentage = Math.max(0, Math.min(100, Number.isFinite(percentage) ? percentage : 0))

  return (
    <Center
      py={0}
      {...others}
      __vars={{
        '--thickness': em(thickness),
        '--percentage': `${safePercentage}%`,
        '--spike-color': spikeColor,
        '--bg-color': bgColor,
        '--pulsing-display': pulsing ? 'block' : 'none',
      }}
    >
      <div className={classes.back}>
        <div className={classes.box}>
          <div className={classes.bar} />
        </div>
      </div>
    </Center>
  )
}
