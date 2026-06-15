import { Group, Pagination } from '@mantine/core'
import { FC, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Empty } from '@Components/Empty'
import { GameCard, GameStatus, compareGamesForDisplay } from '@Components/GameCard'
import { WithNavBar } from '@Components/WithNavbar'
import { GanttTimeLine } from '@Components/charts/GanttTimeline'
import { YinyuRouteLoader, YinyuSectionHead, YinyuStatusPill } from '@Components/yinyu/YinyuUI'
import { getGameStatus, useRecentGames } from '@Hooks/useGame'
import { usePageTitle } from '@Hooks/usePageTitle'
import api from '@Api'

const ITEM_PER_PAGE = 12

const Games: FC = () => {
  const { t } = useTranslation()
  const { recentGames } = useRecentGames()
  const [activePage, setPage] = useState(1)

  const { data: games } = api.game.useGameGames(
    { count: ITEM_PER_PAGE, skip: (activePage - 1) * ITEM_PER_PAGE },
    {
      refreshInterval: 5 * 60 * 1000,
    }
  )

  usePageTitle(t('game.title.index'))

  const recents =
    recentGames?.map((game) => {
      const { startTime, endTime, status } = getGameStatus(game)
      const colorHex =
        status === GameStatus.OnGoing
          ? 'rgba(107, 238, 177, 0.92)'
          : status === GameStatus.Coming
            ? 'rgba(246, 198, 97, 0.82)'
            : 'rgba(150, 164, 180, 0.64)'

      return {
        id: game.id,
        textTitle: game.title ?? '',
        color: colorHex,
        title: game.title,
        start: startTime,
        end: endTime,
      }
    }) ?? []

  const pageCount = Math.ceil((games?.total ?? 0) / ITEM_PER_PAGE)
  const displayedGames = games?.data ? [...games.data].sort(compareGamesForDisplay) : undefined

  return (
    <WithNavBar width="var(--container)">
      <section className="yy-page-frame view-stack yy-archive-page yy-games-page">
        <YinyuSectionHead eyebrow="EXERCISE INDEX" title={t('game.title.index')}>
          <YinyuStatusPill tone="neutral" state="open">
            {games?.total ?? 0} 场演练
          </YinyuStatusPill>
        </YinyuSectionHead>
        {recents.length > 0 ? <GanttTimeLine items={recents} /> : null}
        {!displayedGames ? (
          <article className="state-card panel-card yy-list-loading">
            <YinyuRouteLoader title={t('game.title.index')} description="正在加载赛事列表" />
          </article>
        ) : displayedGames.length > 0 ? (
          <div className="yy-game-event-list">
            {displayedGames.map((game) => (
              <GameCard key={game.id} game={game} />
            ))}
          </div>
        ) : (
          <article className="state-card panel-card">
            <Empty description="暂无赛事" />
          </article>
        )}
        {pageCount > 0 && (
          <Pagination.Root total={pageCount} siblings={3} value={activePage} onChange={setPage} mb="xl">
            <Group gap={5} justify="flex-end">
              <Pagination.First />
              <Pagination.Previous />
              <Pagination.Items />
              <Pagination.Next />
              <Pagination.Last />
            </Group>
          </Pagination.Root>
        )}
      </section>
    </WithNavBar>
  )
}

export default Games
