import clsx from 'clsx'
import { SimpleGrid, type SimpleGridProps } from '@mantine/core'
import type { ComponentPropsWithoutRef, ReactNode } from 'react'
import classes from './Foundation.module.css'

export type PageWidth = 'narrow' | 'standard' | 'wide' | 'fluid'
export type SurfaceDensity = 'compact' | 'default' | 'comfortable'
export type SurfaceVariant = 'default' | 'plain' | 'raised'
export type StatusTone = 'success' | 'warning' | 'danger' | 'info' | 'neutral'

export function PageShell({
  width = 'standard',
  className,
  children,
  ...props
}: ComponentPropsWithoutRef<'main'> & { width?: PageWidth }) {
  return (
    <main className={clsx(classes.pageShell, className)} data-width={width} {...props}>
      <div className={classes.pageStack}>{children}</div>
    </main>
  )
}

export function PageHeader({
  eyebrow,
  title,
  description,
  actions,
  className,
}: {
  eyebrow?: ReactNode
  title: ReactNode
  description?: ReactNode
  actions?: ReactNode
  className?: string
}) {
  return (
    <header className={clsx(classes.pageHeader, className)}>
      <div className={classes.pageHeading}>
        {eyebrow ? <span className={classes.eyebrow}>{eyebrow}</span> : null}
        <h1 className={classes.title}>{title}</h1>
        {description ? <p className={classes.description}>{description}</p> : null}
      </div>
      {actions ? <div className={classes.headerActions}>{actions}</div> : null}
    </header>
  )
}

export function PageSection({
  title,
  description,
  actions,
  className,
  children,
  ...props
}: ComponentPropsWithoutRef<'section'> & { title?: ReactNode; description?: ReactNode; actions?: ReactNode }) {
  return (
    <section className={clsx(classes.section, className)} {...props}>
      {title || description || actions ? (
        <div className={classes.sectionHeader}>
          <div>
            {title ? <h2 className={classes.sectionTitle}>{title}</h2> : null}
            {description ? <p className={classes.sectionDescription}>{description}</p> : null}
          </div>
          {actions}
        </div>
      ) : null}
      {children}
    </section>
  )
}

export function Surface({
  density = 'default',
  variant = 'default',
  className,
  ...props
}: ComponentPropsWithoutRef<'div'> & {
  density?: SurfaceDensity
  variant?: SurfaceVariant
}) {
  return <div className={clsx(classes.surface, className)} data-density={density} data-variant={variant} {...props} />
}

export function DataToolbar({
  children,
  actions,
  className,
  ...props
}: ComponentPropsWithoutRef<'div'> & { actions?: ReactNode }) {
  return (
    <div className={clsx(classes.toolbar, className)} {...props}>
      <div className={classes.toolbarPrimary}>{children}</div>
      {actions ? <div className={classes.toolbarActions}>{actions}</div> : null}
    </div>
  )
}

export function ResponsiveTable({
  minWidth = 720,
  label = '可横向滚动的数据表格',
  className,
  children,
}: {
  minWidth?: number
  label?: string
  className?: string
  children: ReactNode
}) {
  return (
    <div
      className={clsx(classes.tableRegion, className)}
      style={{ '--table-min-width': `${minWidth}px` } as React.CSSProperties}
      role="region"
      aria-label={label}
      tabIndex={0}
    >
      {children}
    </div>
  )
}

export function EmptyState({
  icon,
  title,
  description,
  action,
  className,
}: {
  icon?: ReactNode
  title: ReactNode
  description?: ReactNode
  action?: ReactNode
  className?: string
}) {
  return (
    <div className={clsx(classes.emptyState, className)}>
      <div>
        {icon ? <div className={classes.emptyIcon}>{icon}</div> : null}
        <h3 className={classes.emptyTitle}>{title}</h3>
        {description ? <p className={classes.emptyDescription}>{description}</p> : null}
        {action ? <div className={classes.emptyAction}>{action}</div> : null}
      </div>
    </div>
  )
}

export function StatusBadge({
  tone = 'neutral',
  className,
  children,
  ...props
}: ComponentPropsWithoutRef<'span'> & { tone?: StatusTone }) {
  return (
    <span className={clsx(classes.statusBadge, className)} data-tone={tone} {...props}>
      {children}
    </span>
  )
}

export function MetricGrid({ className, ...props }: ComponentPropsWithoutRef<'div'>) {
  return <div className={clsx(classes.metricGrid, className)} {...props} />
}

export function DeferredList({ className, ...props }: ComponentPropsWithoutRef<'div'>) {
  return <div className={clsx(classes.deferredList, className)} {...props} />
}

export function DeferredGrid({ className, ...props }: SimpleGridProps) {
  return <SimpleGrid className={clsx(classes.deferredList, className)} {...props} />
}
