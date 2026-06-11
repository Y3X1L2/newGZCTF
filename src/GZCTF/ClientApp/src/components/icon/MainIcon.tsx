import { StyleProp, rem } from '@mantine/core'
import { FC, SVGProps } from 'react'
import classes from '@Styles/Icon.module.css'
import yinyuIcon from '../../assets/yinyu-icon-transparent.png'

export interface MainIconProps {
  ignoreTheme?: boolean
  size?: StyleProp<React.CSSProperties['width']>
}

export const MainIcon: FC<MainIconProps & SVGProps<SVGSVGElement>> = ({ ignoreTheme, size, ...svgProps }) => {
  return (
    <svg
      width="480"
      height="480"
      viewBox="0 0 4800 4800"
      style={{
        width: rem(size) || 'auto',
        height: 'auto',
        aspectRatio: '1 / 1',
      }}
      {...svgProps}
    >
      <image
        className={ignoreTheme ? undefined : classes.logoImage}
        href={yinyuIcon}
        width="4800"
        height="4800"
        preserveAspectRatio="xMidYMid meet"
      />
    </svg>
  )
}
