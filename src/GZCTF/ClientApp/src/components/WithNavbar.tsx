import { AppShell, Box, LoadingOverlay, Stack } from '@mantine/core'
import React, { FC } from 'react'
import { AppHeader } from '@Components/AppHeader'
import { AppNavbar } from '@Components/AppNavbar'
import { IconHeader } from '@Components/IconHeader'
import { WithWiderScreen } from '@Components/WithWiderScreen'
import { DEFAULT_LOADING_OVERLAY } from '@Utils/Shared'
import { useIsMobile } from '@Utils/ThemeOverride'
import classes from '@Styles/AppNavbar.module.css'

interface WithNavBarProps extends React.PropsWithChildren {
  width?: string
  minWidth?: number
  isLoading?: boolean
  withFooter?: boolean
  withHeader?: boolean
  stickyHeader?: boolean
}

export type AppControlProps = Record<string, never>

export const WithNavBar: FC<WithNavBarProps> = ({
  children,
  width,
  isLoading,
  minWidth,
  withHeader,
  stickyHeader = false,
}) => {
  const isMobile = useIsMobile()

  return (
    <WithWiderScreen minWidth={minWidth}>
      <AppShell
        p={0}
        className="yy-app-frame"
        header={{ height: 60, collapsed: !isMobile }}
        navbar={{
          width: 78,
          breakpoint: 'sm',
          collapsed: {
            mobile: true,
          },
        }}
      >
        <AppHeader />
        <AppNavbar />
        <AppShell.Main w="100%" className={classes.shellMain}>
          <Stack
            data-mobile={isMobile || undefined}
            className={classes.main}
          >
            <LoadingOverlay visible={isLoading ?? false} overlayProps={DEFAULT_LOADING_OVERLAY} />
            {withHeader && <IconHeader px={isMobile ? '2%' : '10%'} sticky={stickyHeader} />}
            <Box
              w={width ?? (isMobile ? '96%' : '80%')}
              className={classes.content}
              style={{
                zIndex: 20,
              }}
            >
              {children}
            </Box>
          </Stack>
        </AppShell.Main>
      </AppShell>
    </WithWiderScreen>
  )
}
