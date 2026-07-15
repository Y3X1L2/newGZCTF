import { ChevronLeft, ChevronRight, Pin, Search } from 'lucide-react'
import { ChangeEvent, useEffect, useMemo, useState } from 'react'
import { Link, useSearchParams } from 'react-router'
import api from '@Api'
import { DataState, PageHeading, StatusPill } from '../../shared/Primitives'
import { useVNextPageTitle } from '../../shared/useVNextPageTitle'
import styles from './PostsPage.module.css'

const PAGE_SIZE = 12

function parsePage(value: string | null) {
  const page = Number(value)
  return Number.isInteger(page) && page > 0 ? page : 1
}

function formatDate(value: number) {
  return new Intl.DateTimeFormat('zh-CN', {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    hour12: false,
  }).format(value)
}

export function PostsPage() {
  const { data: posts, error } = api.info.useInfoGetPosts({ revalidateOnFocus: false })
  const [searchParams, setSearchParams] = useSearchParams()
  const [query, setQuery] = useState(searchParams.get('q') ?? '')
  const pinnedOnly = searchParams.get('pinned') === '1'
  const page = parsePage(searchParams.get('page'))

  useVNextPageTitle('平台公告')

  useEffect(() => setQuery(searchParams.get('q') ?? ''), [searchParams])

  useEffect(() => {
    const current = searchParams.get('q') ?? ''
    if (query.trim() === current) return undefined
    const timer = window.setTimeout(() => {
      const next = new URLSearchParams(searchParams)
      if (query.trim()) next.set('q', query.trim())
      else next.delete('q')
      next.delete('page')
      setSearchParams(next, { replace: true })
    }, 250)
    return () => window.clearTimeout(timer)
  }, [query, searchParams, setSearchParams])

  const filtered = useMemo(() => {
    const keyword = (searchParams.get('q') ?? '').trim().toLocaleLowerCase('zh-CN')
    return [...(posts ?? [])]
      .filter((post) => !pinnedOnly || post.isPinned)
      .filter(
        (post) =>
          !keyword ||
          `${post.title} ${post.summary} ${(post.tags ?? []).join(' ')}`.toLocaleLowerCase('zh-CN').includes(keyword)
      )
      .sort((left, right) => Number(right.isPinned) - Number(left.isPinned) || right.time - left.time)
  }, [pinnedOnly, posts, searchParams])

  const pageCount = Math.max(1, Math.ceil(filtered.length / PAGE_SIZE))
  const safePage = Math.min(page, pageCount)
  const visible = filtered.slice((safePage - 1) * PAGE_SIZE, safePage * PAGE_SIZE)

  useEffect(() => {
    if (page <= pageCount) return
    const next = new URLSearchParams(searchParams)
    if (pageCount === 1) next.delete('page')
    else next.set('page', String(pageCount))
    setSearchParams(next, { replace: true })
  }, [page, pageCount, searchParams, setSearchParams])

  const togglePinned = () => {
    const next = new URLSearchParams(searchParams)
    if (pinnedOnly) next.delete('pinned')
    else next.set('pinned', '1')
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
        actions={<span className={styles.resultCount}>{filtered.length} 条公告</span>}
        description="平台规则、维护信息和赛事通知统一归档，可通过链接直接分享。"
        eyebrow="NOTICE ARCHIVE"
        title="平台公告"
      />

      <section aria-label="公告筛选" className={styles.toolbar}>
        <label className={styles.searchBox}>
          <Search aria-hidden="true" size={17} />
          <input
            aria-label="搜索公告"
            onChange={onQueryChange}
            placeholder="搜索标题、摘要或标签"
            type="search"
            value={query}
          />
        </label>
        <button className={pinnedOnly ? styles.pinFilterActive : styles.pinFilter} onClick={togglePinned} type="button">
          <Pin size={16} />
          只看置顶
        </button>
      </section>

      {!posts && !error ? (
        <DataState description="正在读取公告归档。" loading title="公告加载中" />
      ) : error ? (
        <DataState description="公告接口暂时不可用，请稍后刷新。" title="公告加载失败" />
      ) : visible.length ? (
        <section className={styles.postList}>
          {visible.map((post) => (
            <article key={post.id}>
              <Link className={styles.postRow} to={`/posts/${post.id}`}>
                <div className={styles.postTitleLine}>
                  <h2>{post.title}</h2>
                  {post.isPinned ? <StatusPill tone="success">置顶</StatusPill> : null}
                </div>
                <p>{post.summary || '暂无摘要。'}</p>
                <div className={styles.postMeta}>
                  <span>{post.authorName || '平台管理员'}</span>
                  <time dateTime={new Date(post.time).toISOString()}>{formatDate(post.time)}</time>
                  {(post.tags ?? []).slice(0, 3).map((tag) => (
                    <span className={styles.tag} key={tag}>
                      {tag}
                    </span>
                  ))}
                </div>
                <ChevronRight aria-hidden="true" className={styles.rowArrow} size={18} />
              </Link>
            </article>
          ))}
        </section>
      ) : (
        <DataState description="调整关键词或置顶筛选后重试。" title="没有符合条件的公告" />
      )}

      {filtered.length > PAGE_SIZE ? (
        <nav aria-label="公告分页" className={styles.pagination}>
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
