import { ChevronLeft, ChevronRight, Clock3, Search, Users } from 'lucide-react'
import { motion } from 'motion/react'
import { ChangeEvent, useEffect, useMemo, useState } from 'react'
import { Link, useSearchParams } from 'react-router'
import { DataState, GeometricPoster, PageHeading, StatusPill } from '../../shared/Primitives'
import { useVNextPageTitle } from '../../shared/useVNextPageTitle'
import styles from './GamesPage.module.css'
import {
  formatGameRange,
  GameListStatus,
  gameStatusLabel,
  gameStatusTone,
  participationLabel,
  useGameCatalog,
} from './gameCatalog'

const PAGE_SIZE = 10
const validStatuses = new Set<GameListStatus>(['ongoing', 'upcoming', 'ended'])

function parsePage(value: string | null) {
  const page = Number(value)
  return Number.isInteger(page) && page > 0 ? page : 1
}

export function GamesPage() {
  const catalog = useGameCatalog()
  const [searchParams, setSearchParams] = useSearchParams()
  const statusValue = searchParams.get('status')
  const status = validStatuses.has(statusValue as GameListStatus) ? (statusValue as GameListStatus) : null
  const page = parsePage(searchParams.get('page'))
  const [query, setQuery] = useState(searchParams.get('q') ?? '')

  useVNextPageTitle('赛事中心')

  useEffect(() => {
    setQuery(searchParams.get('q') ?? '')
  }, [searchParams])

  useEffect(() => {
    const current = searchParams.get('q') ?? ''
    if (query.trim() === current) return undefined

    const timer = window.setTimeout(() => {
      const next = new URLSearchParams(searchParams)
      const normalized = query.trim()
      if (normalized) next.set('q', normalized)
      else next.delete('q')
      next.delete('page')
      setSearchParams(next, { replace: true })
    }, 250)

    return () => window.clearTimeout(timer)
  }, [query, searchParams, setSearchParams])

  const counts = useMemo(() => {
    const result = { all: 0, ongoing: 0, upcoming: 0, ended: 0 }
    for (const game of catalog.games ?? []) {
      result.all += 1
      result[game.status] += 1
    }
    return result
  }, [catalog.games])

  const filteredGames = useMemo(() => {
    const normalizedQuery = (searchParams.get('q') ?? '').trim().toLocaleLowerCase('zh-CN')
    return (catalog.games ?? []).filter((game) => {
      if (status && game.status !== status) return false
      if (!normalizedQuery) return true
      return `${game.title ?? ''} ${game.summary ?? ''}`.toLocaleLowerCase('zh-CN').includes(normalizedQuery)
    })
  }, [catalog.games, searchParams, status])

  const pageCount = Math.max(1, Math.ceil(filteredGames.length / PAGE_SIZE))
  const safePage = Math.min(page, pageCount)
  const visibleGames = filteredGames.slice((safePage - 1) * PAGE_SIZE, safePage * PAGE_SIZE)

  useEffect(() => {
    if (page <= pageCount) return
    const next = new URLSearchParams(searchParams)
    if (pageCount <= 1) next.delete('page')
    else next.set('page', String(pageCount))
    setSearchParams(next, { replace: true })
  }, [page, pageCount, searchParams, setSearchParams])

  const setStatus = (nextStatus: GameListStatus | null) => {
    const next = new URLSearchParams(searchParams)
    if (nextStatus) next.set('status', nextStatus)
    else next.delete('status')
    next.delete('page')
    setSearchParams(next)
  }

  const setPage = (nextPage: number) => {
    const next = new URLSearchParams(searchParams)
    if (nextPage <= 1) next.delete('page')
    else next.set('page', String(nextPage))
    setSearchParams(next)
    window.scrollTo({ top: 0, behavior: 'smooth' })
  }

  const onQueryChange = (event: ChangeEvent<HTMLInputElement>) => setQuery(event.currentTarget.value)

  return (
    <div className={styles.page}>
      <PageHeading
        description="按时间状态浏览公开赛事，进入详情后完成报名并访问对应赛制工作区。"
        eyebrow="EXERCISE INDEX"
        title="赛事中心"
        actions={<span className={styles.resultCount}>{filteredGames.length} 场赛事</span>}
      />

      <section className={styles.toolbar} aria-label="赛事筛选">
        <label className={styles.searchBox}>
          <Search aria-hidden="true" size={17} />
          <input
            aria-label="搜索赛事"
            onChange={onQueryChange}
            placeholder="搜索赛事名称或简介"
            type="search"
            value={query}
          />
        </label>
        <div className={styles.statusFilters}>
          <button
            className={!status ? styles.filterActive : styles.filter}
            onClick={() => setStatus(null)}
            type="button"
          >
            全部 <span>{counts.all}</span>
          </button>
          <button
            className={status === 'ongoing' ? styles.filterActive : styles.filter}
            onClick={() => setStatus('ongoing')}
            type="button"
          >
            进行中 <span>{counts.ongoing}</span>
          </button>
          <button
            className={status === 'upcoming' ? styles.filterActive : styles.filter}
            onClick={() => setStatus('upcoming')}
            type="button"
          >
            即将开始 <span>{counts.upcoming}</span>
          </button>
          <button
            className={status === 'ended' ? styles.filterActive : styles.filter}
            onClick={() => setStatus('ended')}
            type="button"
          >
            已结束 <span>{counts.ended}</span>
          </button>
        </div>
      </section>

      {catalog.isLoading ? (
        <DataState description="正在读取全部公开赛事并计算时间状态。" loading title="赛事加载中" />
      ) : catalog.error ? (
        <DataState description="赛事接口暂时不可用，请稍后刷新。" title="赛事加载失败" />
      ) : visibleGames.length > 0 ? (
        <motion.section className={styles.gameList} layout>
          {visibleGames.map((game, index) => (
            <motion.article key={game.id} layout="position">
              <Link className={styles.gameRow} to={`/games/${game.id}`}>
                <span className={styles.rowIndex}>
                  {String((safePage - 1) * PAGE_SIZE + index + 1).padStart(2, '0')}
                </span>
                <span className={styles.poster}>
                  <GeometricPoster alt={`${game.title || '赛事'}海报`} src={game.poster} />
                </span>
                <span className={styles.gameMain}>
                  <span className={styles.gameTitleLine}>
                    <h2>{game.title || `赛事 ${game.id}`}</h2>
                    <StatusPill tone={gameStatusTone(game.status)}>{gameStatusLabel(game.status)}</StatusPill>
                  </span>
                  <p>{game.summary || '赛事介绍尚未填写。'}</p>
                </span>
                <span className={styles.gameMeta}>
                  <span>
                    <Users size={15} />
                    {participationLabel(game.limit)}
                  </span>
                  <span>
                    <Clock3 size={15} />
                    {formatGameRange(game)}
                  </span>
                </span>
                <ChevronRight aria-hidden="true" className={styles.rowArrow} size={18} />
              </Link>
            </motion.article>
          ))}
        </motion.section>
      ) : (
        <DataState description="调整关键词或状态筛选后重试。" title="没有符合条件的赛事" />
      )}

      {filteredGames.length > PAGE_SIZE ? (
        <nav aria-label="赛事分页" className={styles.pagination}>
          <button aria-label="上一页" disabled={safePage <= 1} onClick={() => setPage(safePage - 1)} type="button">
            <ChevronLeft size={17} />
          </button>
          <span>
            第 {safePage} / {pageCount} 页
          </span>
          <button
            aria-label="下一页"
            disabled={safePage >= pageCount}
            onClick={() => setPage(safePage + 1)}
            type="button"
          >
            <ChevronRight size={17} />
          </button>
        </nav>
      ) : null}
    </div>
  )
}
