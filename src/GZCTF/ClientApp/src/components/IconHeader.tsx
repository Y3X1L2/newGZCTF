import { Box, Group, Text, Title } from '@mantine/core'
import { FC } from 'react'
import { LogoHeader } from '@Components/LogoHeader'
import { PLATFORM_SLOGAN } from '@Utils/Brand'
import { useIsMobile } from '@Utils/ThemeOverride'
import { useConfig } from '@Hooks/useConfig'
import classes from '@Styles/IconHeader.module.css'

interface StickyHeaderProps {
  sticky?: boolean
  px?: string
}

export const IconHeader: FC<StickyHeaderProps> = ({ sticky, px }) => {
  const { config } = useConfig()
  const isMobile = useIsMobile()

  return isMobile ? (
    <Box h={8} />
  ) : (
    <Group
      __vars={{
        '--header-px': px || undefined,
      }}
      data-sticky={sticky || undefined}
      className={classes.group}
    >
      <LogoHeader />
      <Title className={classes.subtitle} order={3}>
        &gt;&nbsp;{config?.slogan ?? PLATFORM_SLOGAN}
        <Text span className={classes.blink}>
          _
        </Text>
      </Title>
    </Group>
  )
}
