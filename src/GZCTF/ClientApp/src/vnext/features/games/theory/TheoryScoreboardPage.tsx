import { Award, Clock3, Trophy, Users } from 'lucide-react'
import { ChangeEvent, useEffect, useMemo, useState } from 'react'
import { useSearchParams } from 'react-router'
import api from '@Api'
import { DataState, PageHeading, StatusPill } from '../../../shared/Primitives'
import { useGameWorkspace } from '../workspace/GameWorkspaceShell'
import styles from './TheoryScoreboardPage.module.css'

function formatTime(value?: number | null) {
  if (!value) return '尚未提交'
  return new Intl.DateTimeFormat('zh-CN', {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
    hour12: false,
  }).format(value)
}

export function TheoryScoreboardPage() {
  const { gameId, game, revision } = useGameWorkspace()
  const ongoing = Date.now() >= (game.start ?? 0) && Date.now() < (game.end ?? 0)
  const {
    data: items,
    error,
    mutate,
  } = api.theoryPlayer.useTheoryPlayerScoreboard(
    gameId,
    { revalidateOnFocus: false, refreshInterval: ongoing ? 30_000 : 0, shouldRetryOnError: false },
    true
  )
  const [searchParams, setSearchParams] = useSearchParams()
  const [query, setQuery] = useState(searchParams.get('q') ?? '')

  useEffect(() => {
    if (revision > 0) void mutate()
  }, [mutate, revision])

  useEffect(() => setQuery(searchParams.get('q') ?? ''), [searchParams])

  useEffect(() => {
    const current = searchParams.get('q') ?? ''
    if (query.trim() === current) return undefined
    const timer = window.setTimeout(() => {
      const next = new URLSearchParams(searchParams)
      if (query.trim()) next.set('q', query.trim())
      else next.delete('q')
      setSearchParams(next, { replace: true })
    }, 250)
    return () => window.clearTimeout(timer)
  }, [query, searchParams, setSearchParams])

  const visibleItems = useMemo(() => {
    const keyword = (searchParams.get('q') ?? '').trim().toLocaleLowerCase('zh-CN')
    return [...(items ?? [])]
      .filter(
        (item) =>
          !keyword || `${item.teamName ?? ''} ${item.userName ?? ''}`.toLocaleLowerCase('zh-CN').includes(keyword)
      )
      .sort((left, right) => (left.rank ?? Number.MAX_SAFE_INTEGER) - (right.rank ?? Number.MAX_SAFE_INTEGER))
  }, [items, searchParams])

  const currentItem = useMemo(
    () => (items ?? []).find((item) => item.teamName && item.teamName === game.teamName),
    [game.teamName, items]
  )
  const maxScore = Math.max(0, ...(items ?? []).map((item) => item.maxScore ?? 0))
  const averageScore = items?.length
    ? Math.round((items.reduce((sum, item) => sum + (item.score ?? 0), 0) / items.length) * 10) / 10
    : 0

  const onQueryChange = (event: ChangeEvent<HTMLInputElement>) => setQuery(event.currentTarget.value)

  return (
    <div className={styles.page}>
      <PageHeading
        actions={currentItem ? <StatusPill tone="success">我的队伍 #{currentItem.rank ?? '--'}</StatusPill> : undefined}
        description="理论考试独立计分，榜单取每支队伍成员的最高成绩。"
        eyebrow="THEORY SCOREBOARD"
        title="理论榜单"
      />

      {items ? (
        <section aria-label="理论榜单概览" className={styles.metrics}>
          <div>
            <Users size={17} />
            <span>已提交队伍</span>
            <strong>{items.length}</strong>
          </div>
          <div>
            <Award size={17} />
            <span>试卷满分</span>
            <strong>{maxScore}</strong>
          </div>
          <div>
            <Trophy size={17} />
            <span>平均分</span>
            <strong>{averageScore}</strong>
          </div>
          <div>
            <Clock3 size={17} />
            <span>我的分数</span>
            <strong>{currentItem?.score ?? '--'}</strong>
          </div>
        </section>
      ) : null}

      <section aria-label="理论榜单筛选" className={styles.toolbar}>
        <label>
          <span>搜索队伍或最高分成员</span>
          <input
            aria-label="搜索理论榜单"
            onChange={onQueryChange}
            placeholder="输入队伍或成员名称"
            type="search"
            value={query}
          />
        </label>
        <span>{visibleItems.length} 条结果</span>
      </section>

      {!items && !error ? (
        <DataState description="正在读取理论考试排名和提交时间。" loading title="理论榜单加载中" />
      ) : error ? (
        <DataState description="比赛尚未发布理论试卷，或当前账户无权查看理论榜单。" title="理论榜单暂不可用" />
      ) : visibleItems.length ? (
        <div className={styles.tableViewport}>
          <table className={styles.table}>
            <thead>
              <tr>
                <th>排名</th>
                <th>队伍</th>
                <th>最高分成员</th>
                <th>成绩</th>
                <th>提交时间</th>
              </tr>
            </thead>
            <tbody>
              {visibleItems.map((item) => {
                const current = Boolean(item.teamName && item.teamName === game.teamName)
                return (
                  <tr data-current={current || undefined} key={`${item.teamId}:${item.rank}`}>
                    <td>
                      <strong className={(item.rank ?? 0) <= 3 ? styles.rankTop : styles.rank}>
                        #{item.rank ?? '--'}
                      </strong>
                    </td>
                    <td>
                      <div className={styles.teamCell}>
                        <strong>{item.teamName || '未命名队伍'}</strong>
                        {current ? <StatusPill tone="success">我的队伍</StatusPill> : null}
                      </div>
                    </td>
                    <td>{item.userName || <span className={styles.muted}>未公开</span>}</td>
                    <td>
                      <strong className={styles.score}>{item.score ?? 0}</strong>
                      <span className={styles.maxScore}> / {item.maxScore ?? maxScore}</span>
                    </td>
                    <td>{formatTime(item.submittedAt)}</td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        </div>
      ) : (
        <DataState
          description={items?.length ? '调整搜索关键词后重试。' : '试卷尚无最终提交记录。'}
          title={items?.length ? '没有符合条件的队伍' : '暂无理论成绩'}
        />
      )}
    </div>
  )
}
