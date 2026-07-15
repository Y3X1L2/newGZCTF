import { Search, Trophy, Users } from 'lucide-react'
import { ChangeEvent, useEffect, useMemo, useState } from 'react'
import { useSearchParams } from 'react-router'
import api, { ScoreboardItem } from '@Api'
import { DataState, PageHeading, StatusPill } from '../../../shared/Primitives'
import { useGameWorkspace } from '../workspace/GameWorkspaceShell'
import styles from './ScoreboardPage.module.css'

const divisionToneClasses = [styles.divisionGreen, styles.divisionBlue, styles.divisionOrange, styles.divisionNeutral]

function formatTime(value?: number) {
  if (!value) return '暂无提交'
  return new Intl.DateTimeFormat('zh-CN', {
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    hour12: false,
  }).format(value)
}

function initials(value?: string | null) {
  return value?.trim().slice(0, 2).toUpperCase() || 'YY'
}

function TeamAvatar({ item }: { item: ScoreboardItem }) {
  const [failed, setFailed] = useState(false)

  useEffect(() => setFailed(false), [item.avatar])

  return (
    <span className={styles.avatar}>
      {item.avatar && !failed ? <img alt="" onError={() => setFailed(true)} src={item.avatar} /> : initials(item.name)}
    </span>
  )
}

export function ScoreboardPage() {
  const { gameId, game, revision } = useGameWorkspace()
  const ongoing = Date.now() >= (game.start ?? 0) && Date.now() < (game.end ?? 0)
  const {
    data: scoreboard,
    error,
    mutate,
  } = api.game.useGameScoreboard(gameId, {
    revalidateOnFocus: false,
    refreshInterval: ongoing ? 30_000 : 0,
  })
  const { data: teamInfo } = api.game.useGameChallengesWithTeamInfo(
    gameId,
    { revalidateOnFocus: false, refreshInterval: ongoing ? 30_000 : 0 },
    true
  )
  const [searchParams, setSearchParams] = useSearchParams()
  const [query, setQuery] = useState(searchParams.get('q') ?? '')
  const divisionId = Number(searchParams.get('division')) || null

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

  const divisionMap = useMemo(
    () => new Map((scoreboard?.divisions ?? []).map((division, index) => [division.id, { ...division, index }])),
    [scoreboard?.divisions]
  )

  const visibleItems = useMemo(() => {
    const keyword = (searchParams.get('q') ?? '').trim().toLocaleLowerCase('zh-CN')
    return (scoreboard?.items ?? [])
      .filter((item) => !divisionId || item.divisionId === divisionId)
      .filter((item) => !keyword || `${item.name} ${item.bio ?? ''}`.toLocaleLowerCase('zh-CN').includes(keyword))
      .sort((left, right) => left.rank - right.rank)
  }, [divisionId, scoreboard?.items, searchParams])

  const setDivision = (value: number | null) => {
    const next = new URLSearchParams(searchParams)
    if (value) next.set('division', String(value))
    else next.delete('division')
    setSearchParams(next)
  }

  const onQueryChange = (event: ChangeEvent<HTMLInputElement>) => setQuery(event.currentTarget.value)

  return (
    <div className={styles.page}>
      <PageHeading
        actions={
          scoreboard ? (
            <span className={styles.updatedAt}>更新于 {formatTime(scoreboard.updateTimeUtc)}</span>
          ) : undefined
        }
        description="按比赛总分排序，实时状态变化会在连接恢复后自动校准。"
        eyebrow="LIVE SCOREBOARD"
        title="积分榜"
      />

      {scoreboard ? (
        <section aria-label="积分榜概览" className={styles.metrics}>
          <div>
            <Users size={17} />
            <span>参赛战队</span>
            <strong>{scoreboard.items.length}</strong>
          </div>
          <div>
            <Trophy size={17} />
            <span>当前题目</span>
            <strong>{scoreboard.challengeCount}</strong>
          </div>
          <div>
            <span>我的排名</span>
            <strong>{teamInfo?.rank?.rank ? `#${teamInfo.rank.rank}` : '--'}</strong>
          </div>
          <div>
            <span>我的分数</span>
            <strong>{teamInfo?.rank?.score ?? '--'}</strong>
          </div>
        </section>
      ) : null}

      <section aria-label="积分榜筛选" className={styles.toolbar}>
        <label className={styles.searchBox}>
          <Search aria-hidden="true" size={17} />
          <input
            aria-label="搜索战队"
            onChange={onQueryChange}
            placeholder="搜索战队名称或简介"
            type="search"
            value={query}
          />
        </label>
        <div className={styles.divisionFilters}>
          <button
            className={!divisionId ? styles.filterActive : styles.filter}
            onClick={() => setDivision(null)}
            type="button"
          >
            全部赛区
          </button>
          {(scoreboard?.divisions ?? []).map((division) => (
            <button
              className={divisionId === division.id ? styles.filterActive : styles.filter}
              key={division.id}
              onClick={() => setDivision(division.id)}
              type="button"
            >
              {division.name}
            </button>
          ))}
        </div>
      </section>

      {!scoreboard && !error ? (
        <DataState description="正在读取战队排名和解题状态。" loading title="积分榜加载中" />
      ) : error ? (
        <DataState description="积分榜尚未生成或当前账户无权查看。" title="积分榜加载失败" />
      ) : visibleItems.length ? (
        <div className={styles.tableViewport}>
          <table className={styles.table}>
            <thead>
              <tr>
                <th>排名</th>
                <th>战队</th>
                <th className={styles.divisionColumn}>赛区</th>
                <th>总分</th>
                <th className={styles.ctfColumn}>CTF 分数</th>
                <th>解题数</th>
                <th className={styles.timeColumn}>最后得分</th>
              </tr>
            </thead>
            <tbody>
              {visibleItems.map((item) => {
                const division = item.divisionId ? divisionMap.get(item.divisionId) : undefined
                const isCurrent = item.id === teamInfo?.rank?.id
                return (
                  <tr data-current={isCurrent || undefined} key={item.id}>
                    <td>
                      <span className={item.rank <= 3 ? styles.rankTop : styles.rank}>#{item.rank}</span>
                    </td>
                    <td>
                      <div className={styles.teamCell}>
                        <TeamAvatar item={item} />
                        <span>
                          <strong>{item.name}</strong>
                          <small>{item.bio || (isCurrent ? '当前参赛战队' : '暂无简介')}</small>
                        </span>
                        {isCurrent ? <StatusPill tone="success">我的战队</StatusPill> : null}
                      </div>
                    </td>
                    <td className={styles.divisionColumn}>
                      {division ? (
                        <span
                          className={`${styles.divisionPill} ${divisionToneClasses[division.index % divisionToneClasses.length]}`}
                        >
                          {division.name}
                        </span>
                      ) : (
                        <span className={styles.muted}>默认赛区</span>
                      )}
                    </td>
                    <td>
                      <strong className={styles.score}>{item.score}</strong>
                    </td>
                    <td className={styles.ctfColumn}>{item.ctfScore}</td>
                    <td>{item.solvedCount}</td>
                    <td className={styles.timeColumn}>{formatTime(item.lastSubmissionTime)}</td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        </div>
      ) : (
        <DataState description="调整战队关键词或赛区筛选后重试。" title="没有符合条件的战队" />
      )}
    </div>
  )
}
