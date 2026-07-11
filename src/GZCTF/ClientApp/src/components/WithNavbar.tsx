import { AppShell, Box, Stack } from '@mantine/core'
import React, { FC } from 'react'
import { useLocation } from 'react-router'
import { AppHeader } from '@Components/AppHeader'
import { AppNavbar } from '@Components/AppNavbar'
import { IconHeader } from '@Components/IconHeader'
import { PublicAccountEntry } from '@Components/PublicAccountEntry'
import { WithWiderScreen } from '@Components/WithWiderScreen'
import { YinyuRouteTransition } from '@Components/yinyu/YinyuUI'
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
  const location = useLocation()
  const isTeamRoute = location.pathname.startsWith('/teams')
  const normalizedPath = location.pathname.replace(/\/$/, '')
  const isTrainingChapterRoute = /^\/training\/courses\/[^/]+\/chapters\/[^/]+$/.test(normalizedPath)
  const isGameEntryRoute = /^\/games\/[^/]+$/.test(normalizedPath)
  const showPublicAccount =
    location.pathname === '/' ||
    location.pathname.startsWith('/posts') ||
    location.pathname === '/games' ||
    location.pathname.startsWith('/teams') ||
    location.pathname.startsWith('/about')

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
          {showPublicAccount && !isMobile ? <PublicAccountEntry /> : null}
          <Stack
            data-mobile={isMobile || undefined}
            data-team-shell={isTeamRoute || undefined}
            data-training-chapter-shell={isTrainingChapterRoute || undefined}
            data-game-entry-shell={isGameEntryRoute || undefined}
            className={classes.main}
          >
            {isLoading ? (
              <div className="yy-page-loading-overlay" role="status" aria-live="polite">
                <YinyuRouteTransition title="YINYU" description="正在读取页面数据" />
              </div>
            ) : null}
            {withHeader && <IconHeader px={isMobile ? '2%' : '10%'} sticky={stickyHeader} />}
            <Box
              w={isTeamRoute ? '100%' : (width ?? 'var(--container)')}
              maw="100%"
              miw={0}
              data-team-content={isTeamRoute || undefined}
              data-training-chapter-content={isTrainingChapterRoute || undefined}
              data-game-entry-content={isGameEntryRoute || undefined}
              className={classes.content}
            >
              {children}
            </Box>
          </Stack>
        </AppShell.Main>
      </AppShell>
    </WithWiderScreen>
  )
}
