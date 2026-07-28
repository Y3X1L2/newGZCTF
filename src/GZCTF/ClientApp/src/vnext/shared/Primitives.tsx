import { ArrowRight, ImageOff } from 'lucide-react'
import { ReactNode, useState } from 'react'
import { Link } from 'react-router'
import styles from './Primitives.module.css'

export function PageHeading({
  eyebrow,
  title,
  description,
  actions,
}: {
  eyebrow: string
  title: string
  description?: string
  actions?: ReactNode
}) {
  return (
    <header className={styles.pageHeading}>
      <div>
        <span>{eyebrow}</span>
        <h1>{title}</h1>
        {description ? <p>{description}</p> : null}
      </div>
      {actions ? <div className={styles.pageActions}>{actions}</div> : null}
    </header>
  )
}

export function SectionHeading({
  eyebrow,
  title,
  route,
  routeLabel,
}: {
  eyebrow: string
  title: string
  route?: string
  routeLabel?: string
}) {
  return (
    <header className={styles.sectionHeading}>
      <div>
        <span>{eyebrow}</span>
        <h2>{title}</h2>
      </div>
      {route && routeLabel ? (
        <Link to={route}>
          {routeLabel}
          <ArrowRight size={16} />
        </Link>
      ) : null}
    </header>
  )
}

export type StatusTone = 'success' | 'info' | 'warning' | 'neutral'

export function StatusPill({ children, tone = 'neutral' }: { children: ReactNode; tone?: StatusTone }) {
  return <span className={`${styles.statusPill} ${styles[`statusPill_${tone}`]}`}>{children}</span>
}

export function DataState<T>({
  title,
  description,
  loading = false,
  data,
  error,
  children,
}: {
  title?: string
  description?: string
  loading?: boolean
  data?: T | null
  error?: unknown
  children?: ReactNode
}) {
  if (data) return <>{children}</>
  const stateTitle = title ?? (error ? '加载失败' : loading ? '正在加载' : '暂无数据')
  const stateDescription = description ?? (error ? '数据暂时无法读取，请稍后重试。' : '当前没有可显示的内容。')
  return (
    <div className={styles.dataState} role={loading ? 'status' : undefined}>
      {loading ? (
        <span className={styles.loadingMark} aria-hidden="true">
          <i />
          <i />
          <i />
        </span>
      ) : (
        <span className={styles.emptyMark} aria-hidden="true" />
      )}
      <strong>{stateTitle}</strong>
      <p>{stateDescription}</p>
    </div>
  )
}

type PosterTone = 'green' | 'blue' | 'orange' | 'neutral'

const posterToneClasses: Record<PosterTone, string> = {
  green: styles.posterFallback_green,
  blue: styles.posterFallback_blue,
  orange: styles.posterFallback_orange,
  neutral: styles.posterFallback_neutral,
}

export function GeometricPoster({ src, alt, tone = 'green' }: { src?: string | null; alt: string; tone?: PosterTone }) {
  const [failed, setFailed] = useState(false)

  if (src && !failed) {
    return <img alt={alt} className={styles.posterImage} loading="lazy" onError={() => setFailed(true)} src={src} />
  }

  return (
    <div className={`${styles.posterFallback} ${posterToneClasses[tone]}`}>
      <span className={styles.posterPlaneA} />
      <span className={styles.posterPlaneB} />
      <span className={styles.posterRoute} />
      <ImageOff aria-hidden="true" size={18} />
    </div>
  )
}
