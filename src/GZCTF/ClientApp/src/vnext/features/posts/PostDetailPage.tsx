import { ArrowLeft, Pin } from 'lucide-react'
import { Link, useParams } from 'react-router'
import api from '@Api'
import { MarkdownContent, markdownOutline } from '../../shared/MarkdownContent'
import { DataState, StatusPill } from '../../shared/Primitives'
import { useVNextPageTitle } from '../../shared/useVNextPageTitle'
import styles from './PostDetailPage.module.css'

function formatDate(value: number) {
  return new Intl.DateTimeFormat('zh-CN', {
    year: 'numeric',
    month: 'long',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
    hour12: false,
  }).format(value)
}

export function PostDetailPage() {
  const { postId = '' } = useParams()
  const { data: post, error } = api.info.useInfoGetPost(postId, { revalidateOnFocus: false }, Boolean(postId))
  const outline = markdownOutline(post?.content ?? '')

  useVNextPageTitle(post?.title || '公告详情')

  if (!post && !error)
    return (
      <div className={styles.statePage}>
        <DataState description="正在读取公告正文。" loading title="公告加载中" />
      </div>
    )
  if (error || !post)
    return (
      <div className={styles.statePage}>
        <DataState description="公告不存在或暂时无法访问。" title="公告加载失败" />
      </div>
    )

  return (
    <div className={styles.page}>
      <Link className={styles.backLink} to="/posts">
        <ArrowLeft size={16} />
        返回公告列表
      </Link>
      <header className={styles.header}>
        <div className={styles.headerMeta}>
          {post.isPinned ? (
            <StatusPill tone="success">
              <Pin size={13} />
              置顶
            </StatusPill>
          ) : null}
          {(post.tags ?? []).map((tag) => (
            <span className={styles.tag} key={tag}>
              {tag}
            </span>
          ))}
        </div>
        <h1>{post.title}</h1>
        <p>{post.summary}</p>
        <div className={styles.byline}>
          <span>{post.authorName || '平台管理员'}</span>
          <time dateTime={new Date(post.time).toISOString()}>{formatDate(post.time)}</time>
        </div>
      </header>

      <div className={styles.layout}>
        <article className={styles.article}>
          <MarkdownContent source={post.content} />
        </article>
        {outline.length ? (
          <aside className={styles.outline}>
            <span>ON THIS PAGE</span>
            <nav aria-label="公告目录">
              {outline.map((item) => (
                <a className={item.level === 3 ? styles.outlineNested : undefined} href={`#${item.id}`} key={item.id}>
                  {item.label}
                </a>
              ))}
            </nav>
          </aside>
        ) : null}
      </div>
    </div>
  )
}
