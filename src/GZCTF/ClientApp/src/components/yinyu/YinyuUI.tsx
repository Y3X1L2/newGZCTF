import { Box, BoxProps } from '@mantine/core'
import cx from 'clsx'
import { CircleDot } from 'lucide-react'
import { ComponentType, CSSProperties, PropsWithChildren, ReactNode } from 'react'

export type YinyuStatusTone = 'success' | 'warm' | 'danger' | 'neutral'
export type YinyuStatusState = 'running' | 'solved' | 'open' | 'busy' | 'alert' | 'idle'

export function getYinyuStatusState(value: ReactNode, tone?: YinyuStatusTone): YinyuStatusState {
  const text = String(value ?? '').toLowerCase()

  if (
    text.includes('运行') ||
    text.includes('进行') ||
    text.includes('running') ||
    text.includes('live') ||
    text.includes('ongoing')
  ) {
    return 'running'
  }

  if (
    text.includes('已解') ||
    text.includes('成功') ||
    text.includes('solved') ||
    text.includes('accepted') ||
    text.includes('ready') ||
    text.includes('healthy')
  ) {
    return 'solved'
  }

  if (text.includes('开放') || text.includes('open') || text.includes('pending') || text.includes('queued')) {
    return 'open'
  }

  if (
    text.includes('error') ||
    text.includes('failed') ||
    text.includes('stale') ||
    text.includes('异常') ||
    tone === 'danger'
  ) {
    return 'alert'
  }

  if (text.includes('busy') || text.includes('sync') || text.includes('retry') || text.includes('等待') || tone === 'warm') {
    return 'busy'
  }

  return 'idle'
}

export function YinyuHeartbeatIcon({ label = 'signal' }: { label?: string }) {
  return (
    <span className="heartbeat-icon yy-heartbeat-icon" aria-label={label}>
      <svg viewBox="0 0 64 24" aria-hidden="true">
        <path className="heartbeat-shadow yy-heartbeat-shadow" d="M2 13h10l4-7 7 14 7-18 6 11h8l4-5 6 5h8" />
        <path className="heartbeat-line yy-heartbeat-line" d="M2 13h10l4-7 7 14 7-18 6 11h8l4-5 6 5h8" />
      </svg>
    </span>
  )
}

export function YinyuStatusPill({
  children,
  tone = 'neutral',
  state,
  icon: Icon = CircleDot,
  className,
}: PropsWithChildren<{
  tone?: YinyuStatusTone
  state?: YinyuStatusState
  icon?: ComponentType<{ size?: number; label?: string }>
  className?: string
}>) {
  const motionState = state || getYinyuStatusState(children, tone)

  return (
    <span className={cx('status-pill yy-status-pill', `tone-${tone}`, `state-${motionState}`, className)}>
      <span className="status-signal yy-status-signal" aria-hidden="true">
        <i />
        <i />
        <i />
        <i />
      </span>
      <Icon size={14} />
      <span className="status-text yy-status-text">{children}</span>
      <span className="status-tail yy-status-tail" aria-hidden="true" />
    </span>
  )
}

export function YinyuHexField({ cells = 42 }: { cells?: number }) {
  return (
    <span className="hex-field yy-hex-field" aria-hidden="true">
      {Array.from({ length: cells }).map((_, index) => (
        <i
          key={index}
          style={
            {
              '--hex-delay': `${(index % 14) * 0.071}s`,
              '--hex-row': `${Math.floor(index / 14)}`,
              '--hex-x': `${(index % 14) * 7.5 + (Math.floor(index / 14) % 2) * 3.75}%`,
              '--hex-y': `${Math.floor(index / 14) * 18}%`,
            } as CSSProperties
          }
        />
      ))}
    </span>
  )
}

export function YinyuPanel({
  children,
  className,
  cells = 42,
  ...props
}: PropsWithChildren<BoxProps & { cells?: number }>) {
  return (
    <Box className={cx('panel-card yy-panel-card', className)} {...props}>
      <YinyuHexField cells={cells} />
      {children}
    </Box>
  )
}

export function YinyuPageFrame({ children, className, ...props }: PropsWithChildren<BoxProps>) {
  return (
    <Box className={cx('yy-page-frame view-stack', className)} {...props}>
      {children}
    </Box>
  )
}

export function YinyuTableShell({
  children,
  className,
  cells = 28,
  ...props
}: PropsWithChildren<BoxProps & { cells?: number }>) {
  return (
    <Box className={cx('table-shell panel-card yy-table-shell', className)} {...props}>
      <YinyuHexField cells={cells} />
      {children}
    </Box>
  )
}

export function YinyuModalShell({
  children,
  className,
  cells = 32,
  ...props
}: PropsWithChildren<BoxProps & { cells?: number }>) {
  return (
    <Box className={cx('panel-card yy-modal-shell', className)} {...props}>
      <YinyuHexField cells={cells} />
      {children}
    </Box>
  )
}

export function YinyuModalBody({
  children,
  className,
  cells = 32,
  ...props
}: PropsWithChildren<BoxProps & { cells?: number }>) {
  return (
    <Box className={cx('panel-card yy-modal-body', className)} {...props}>
      <YinyuHexField cells={cells} />
      {children}
    </Box>
  )
}

export function YinyuDrawerBody({
  children,
  className,
  cells = 36,
  ...props
}: PropsWithChildren<BoxProps & { cells?: number }>) {
  return (
    <Box className={cx('panel-card yy-drawer-body', className)} {...props}>
      <YinyuHexField cells={cells} />
      {children}
    </Box>
  )
}

export function YinyuConfirmPanel({
  children,
  className,
  cells = 18,
  ...props
}: PropsWithChildren<BoxProps & { cells?: number }>) {
  return (
    <Box className={cx('panel-card yy-confirm-panel', className)} {...props}>
      <YinyuHexField cells={cells} />
      {children}
    </Box>
  )
}

export function YinyuStatePage({
  children,
  className,
  tone = 'neutral',
  cells = 42,
  ...props
}: PropsWithChildren<BoxProps & { tone?: YinyuStatusTone | 'danger'; cells?: number }>) {
  return (
    <Box
      className={cx(
        'state-page panel-card yy-state-page',
        tone === 'danger' && 'state-danger',
        `state-${tone}`,
        className
      )}
      {...props}
    >
      <YinyuHexField cells={cells} />
      {children}
    </Box>
  )
}

export function YinyuAdminToolbar({ children, className, ...props }: PropsWithChildren<BoxProps>) {
  return (
    <Box className={cx('admin-toolbar yy-admin-toolbar', className)} {...props}>
      {children}
    </Box>
  )
}

export function YinyuFormSection({
  children,
  className,
  cells = 28,
  ...props
}: PropsWithChildren<BoxProps & { cells?: number }>) {
  return (
    <Box className={cx('panel-card yy-form-section', className)} {...props}>
      <YinyuHexField cells={cells} />
      {children}
    </Box>
  )
}

export function YinyuReadableText({ children, className, ...props }: PropsWithChildren<BoxProps>) {
  return (
    <Box component="span" className={cx('yy-readable-text', className)} {...props}>
      {children}
    </Box>
  )
}

export function YinyuDataBar({ value, className }: { value?: number | null; className?: string }) {
  const clamped = Math.max(0, Math.min(100, Number(value ?? 0)))

  return (
    <span className={cx('data-bar', className)} aria-label={`${clamped}%`}>
      <i style={{ width: `${clamped}%` }} />
    </span>
  )
}

export function YinyuSectionHead({
  eyebrow,
  title,
  children,
  className,
}: PropsWithChildren<{
  eyebrow: ReactNode
  title: ReactNode
  className?: string
}>) {
  return (
    <div className={cx('section-head view-reveal', className)}>
      <div>
        <span>{eyebrow}</span>
        <h2>{title}</h2>
      </div>
      {children}
    </div>
  )
}

export function YinyuMetricTile({
  label,
  value,
  detail,
  tone = 'neutral',
  icon: Icon,
  className,
}: {
  label: ReactNode
  value: ReactNode
  detail?: ReactNode
  tone?: YinyuStatusTone
  icon?: ComponentType<{ size?: number }>
  className?: string
}) {
  return (
    <article className={cx('metric-tile panel-card', `metric-${tone}`, className)}>
      <YinyuHexField cells={28} />
      {Icon ? <Icon size={19} /> : null}
      <span>{label}</span>
      <strong>{value}</strong>
      {detail ? <small>{detail}</small> : null}
    </article>
  )
}

export function YinyuRouteLoader({
  title = 'YINYU',
  description = '\u9875\u9762\u5185\u5bb9\u52a0\u8f7d\u4e2d',
  className,
}: {
  title?: ReactNode
  description?: ReactNode
  className?: string
}) {
  return (
    <div className={cx('route-loader', className)}>
      <YinyuHeartbeatIcon label="route transition heartbeat" />
      <div>
        <strong>{title}</strong>
        <span>{description}</span>
      </div>
    </div>
  )
}

export function YinyuRouteTransition({
  title = 'YINYU',
  description = '\u6b63\u5728\u540c\u6b65\u9875\u9762\u4fe1\u53f7',
  cells = 18,
  className,
}: {
  title?: ReactNode
  description?: ReactNode
  cells?: number
  className?: string
}) {
  return (
    <div className={cx('route-transition-preview yy-route-transition', className)}>
      <YinyuRouteLoader title={title} description={description} className="yy-route-transition-loader" />
      <div className="route-progress yy-route-progress" aria-hidden="true">
        {Array.from({ length: cells }).map((_, cell) => (
          <i key={cell} style={{ '--route-delay': `${cell * 0.045}s` } as CSSProperties} />
        ))}
      </div>
    </div>
  )
}

export function YinyuRouteProgress({ cells = 18, className }: { cells?: number; className?: string }) {
  return (
    <div className={cx('route-progress yy-route-progress', className)} aria-hidden="true">
      {Array.from({ length: cells }).map((_, cell) => (
        <i key={cell} style={{ '--route-delay': `${cell * 0.045}s` } as CSSProperties} />
      ))}
    </div>
  )
}

export function YinyuLoadingState({
  title = '\u5185\u5bb9\u52a0\u8f7d\u4e2d',
  description = '\u5e73\u53f0\u6b63\u5728\u8bfb\u53d6\u6700\u65b0\u4fe1\u606f',
  className,
  cells = 30,
}: {
  title?: ReactNode
  description?: ReactNode
  className?: string
  cells?: number
}) {
  return (
    <Box className={cx('state-card panel-card yy-loading-state', className)}>
      <YinyuHexField cells={cells} />
      <YinyuRouteLoader title={title} description={description} />
    </Box>
  )
}

export function YinyuToolbarButton({ children, className, ...props }: PropsWithChildren<BoxProps>) {
  return (
    <Box className={cx('yy-toolbar-button', className)} {...props}>
      {children}
    </Box>
  )
}

export function YinyuSignalLoader({ cells = 35 }: { cells?: number }) {
  return (
    <div className="signal-loader" aria-hidden="true">
      {Array.from({ length: cells }).map((_, index) => (
        <i key={index} style={{ '--delay': `${index * 0.035}s` } as CSSProperties} />
      ))}
    </div>
  )
}
