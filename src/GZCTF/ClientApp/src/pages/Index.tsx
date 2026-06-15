import { Stack } from '@mantine/core'
import { Activity, Bell } from 'lucide-react'
import { FC, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Empty } from '@Components/Empty'
import { GameCard, compareGamesForDisplay } from '@Components/GameCard'
import { PostCard } from '@Components/PostCard'
import { WithNavBar } from '@Components/WithNavbar'
import { BrandMark } from '@Components/yinyu/BrandMark'
import { YinyuGradientText } from '@Components/yinyu/YinyuReactBits'
import { YinyuHeartbeatIcon, YinyuHexField, YinyuRouteLoader, YinyuStatusPill } from '@Components/yinyu/YinyuUI'
import { PLATFORM_TITLE } from '@Utils/Brand'
import { showErrorMsg } from '@Utils/Shared'
import { usePageTitle } from '@Hooks/usePageTitle'
import api, { PostInfoModel } from '@Api'

const HOME_GAME_COUNT = 5
const HOME_NOTICE_COUNT = 4
const platformType = '安全综合演练平台'
const terminalLines = ['演练赛事在线调度', '平台通知实时归档', '靶场服务安全编排']

const TerminalTyping: FC<{ lines: string[] }> = ({ lines }) => {
  const [lineIndex, setLineIndex] = useState(0)
  const [visibleCount, setVisibleCount] = useState(0)
  const [phase, setPhase] = useState<'typing' | 'hold' | 'deleting'>('typing')

  const currentLine = lines[lineIndex] ?? ''

  useEffect(() => {
    if (!lines.length) return undefined

    const timeout = window.setTimeout(
      () => {
        if (phase === 'typing') {
          if (visibleCount < currentLine.length) {
            setVisibleCount((count) => count + 1)
          } else {
            setPhase('hold')
          }
          return
        }

        if (phase === 'hold') {
          setPhase('deleting')
          return
        }

        if (visibleCount > 0) {
          setVisibleCount((count) => count - 1)
        } else {
          setLineIndex((index) => (index + 1) % lines.length)
          setPhase('typing')
        }
      },
      phase === 'hold' ? 1500 : phase === 'typing' ? 62 : 34
    )

    return () => window.clearTimeout(timeout)
  }, [currentLine.length, lines.length, phase, visibleCount])

  return (
    <span className="yy-terminal-typing" aria-live="polite" aria-label="platform terminal slogans">
      <b>{`> ${currentLine.slice(0, visibleCount)}`}</b>
      <i aria-hidden="true" />
    </span>
  )
}

const Home: FC = () => {
  const { t } = useTranslation()

  const { data: posts, mutate } = api.info.useInfoGetLatestPosts({
    refreshInterval: 5 * 60 * 1000,
  })
  const { data: games } = api.game.useGameGames(
    { count: HOME_GAME_COUNT, skip: 0 },
    {
      refreshInterval: 5 * 60 * 1000,
    }
  )

  const onTogglePinned = async (post: PostInfoModel, setDisabled: (value: boolean) => void) => {
    setDisabled(true)

    try {
      const res = await api.edit.editUpdatePost(post.id, {
        isPinned: !post.isPinned,
      })
      if (post.isPinned) {
        mutate([
          ...(posts?.filter((p) => p.id !== post.id && p.isPinned) ?? []),
          { ...res.data },
          ...(posts?.filter((p) => p.id !== post.id && !p.isPinned) ?? []),
        ])
      } else {
        mutate([
          { ...res.data },
          ...(posts?.filter((p) => p.id !== post.id && p.isPinned) ?? []),
          ...(posts?.filter((p) => p.id !== post.id && !p.isPinned) ?? []),
        ])
      }
      api.info.mutateInfoGetPosts()
    } catch (e) {
      showErrorMsg(e, t)
    } finally {
      setDisabled(false)
    }
  }

  const displayedGames = games?.data ? [...games.data].sort(compareGamesForDisplay) : undefined
  const displayedPosts = posts?.slice(0, HOME_NOTICE_COUNT)

  usePageTitle()

  return (
    <WithNavBar minWidth={0} width="var(--container)">
      <section className="original-home yy-page-frame yy-home-page yy-home-recomposed">
        <div className="home-title-row yy-home-title-row">
          <BrandMark />
          <h1 className="yy-brand-title yy-home-brand-heading">
            <span>
              <YinyuGradientText tone="silver">{PLATFORM_TITLE}</YinyuGradientText>
            </span>
            <em>
              <YinyuGradientText tone="signal">{platformType}</YinyuGradientText>
            </em>
          </h1>
          <TerminalTyping lines={terminalLines} />
        </div>

        <div className="home-layout-draft yy-home-layout yy-home-event-layout">
          <main className="home-feed yy-home-event-board">
            <div className="panel-title yy-panel-title-strong yy-home-panel-heading">
              <Activity size={24} />
              <span>{'\u6f14\u7ec3\u8d5b\u4e8b'}</span>
              <YinyuStatusPill tone="neutral" state="open">
                {games?.total ?? 0} {'\u573a'}
              </YinyuStatusPill>
            </div>
            <Stack gap="md" className="yy-home-event-list">
              {!displayedGames ? (
                <article className="state-card panel-card yy-list-loading">
                  <YinyuRouteLoader title="\u6f14\u7ec3\u8d5b\u4e8b" description="\u6b63\u5728\u52a0\u8f7d\u8d5b\u4e8b\u5217\u8868" />
                </article>
              ) : displayedGames.length > 0 ? (
                displayedGames.map((game) => <GameCard key={game.id} game={game} compact />)
              ) : (
                <article className="post-preview panel-card yy-home-empty">
                  <YinyuHexField cells={42} />
                  <div className="quote-mark">
                    <YinyuHeartbeatIcon label="exercise heartbeat" />
                  </div>
                  <div>
                    <h4>{'\u6682\u65e0\u6f14\u7ec3\u8d5b\u4e8b'}</h4>
                    <p>{'\u65b0\u7684\u5b89\u5168\u6f14\u7ec3\u3001\u7406\u8bba\u8bad\u7ec3\u4e0e\u653b\u9632\u4efb\u52a1\u53d1\u5e03\u540e\u4f1a\u4f18\u5148\u5728\u8fd9\u91cc\u5c55\u793a\u3002'}</p>
                  </div>
                </article>
              )}
            </Stack>
          </main>

          <aside className="recent-games-draft yy-home-notice-rail">
            <div className="panel-title yy-panel-title-strong yy-home-panel-heading">
              <Bell size={22} />
              <span>{'\u5e73\u53f0\u901a\u77e5'}</span>
              <YinyuStatusPill tone="neutral" state="open">
                {posts?.length ?? 0} {'\u6761'}
              </YinyuStatusPill>
            </div>
            <div className="yy-home-notice-divider" aria-hidden="true" />
            {displayedPosts && displayedPosts.length > 0 ? (
              <Stack gap="md" className="yy-home-notice-list">
                {displayedPosts.map((post) => (
                  <PostCard key={post.id} post={post} onTogglePinned={onTogglePinned} />
                ))}
              </Stack>
            ) : (
              <article className="post-preview panel-card yy-home-empty yy-home-notice-empty">
                <YinyuHexField cells={28} />
                <div className="quote-mark">
                  <YinyuHeartbeatIcon label="notice heartbeat" />
                </div>
                <div>
                  <h4>{posts ? '\u6682\u65e0\u5e73\u53f0\u901a\u77e5' : '\u901a\u77e5\u52a0\u8f7d\u4e2d'}</h4>
                  <p>{'\u5e73\u53f0\u7ef4\u62a4\u3001\u8d5b\u4e8b\u5b89\u6392\u3001\u89c4\u5219\u8c03\u6574\u4e0e\u91cd\u8981\u6d88\u606f\u4f1a\u5728\u8fd9\u91cc\u53d1\u5e03\u3002'}</p>
                </div>
              </article>
            )}
          </aside>
        </div>
      </section>
    </WithNavBar>
  )
}

export default Home
