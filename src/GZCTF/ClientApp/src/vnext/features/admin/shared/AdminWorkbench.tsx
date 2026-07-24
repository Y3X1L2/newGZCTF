import { ChevronLeft, ChevronRight, Menu, RefreshCw } from 'lucide-react'
import { KeyboardEvent, MouseEvent, ReactNode } from 'react'
import { VNextDrawer } from '../../../shared/Interaction'
import { DataState } from '../../../shared/Primitives'
import styles from './AdminWorkbench.module.css'

export type AdminStatusTone = 'danger' | 'info' | 'neutral' | 'success' | 'warning'
export type AdminColumnWidth = 'compact' | 'medium' | 'wide'
export type AdminColumnVisibility = 'always' | 'desktop' | 'wide'

export interface AdminDataColumn<T> {
  id: string
  header: ReactNode
  render: (row: T) => ReactNode
  width?: AdminColumnWidth
  visibility?: AdminColumnVisibility
  align?: 'left' | 'right'
}

export function AdminPageHeader({
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
    <header className={styles.pageHeader}>
      <div>
        <span>{eyebrow}</span>
        <h1>{title}</h1>
        {description ? <p>{description}</p> : null}
      </div>
      {actions ? <div className={styles.pageActions}>{actions}</div> : null}
    </header>
  )
}

export function MetricStrip({ children, density = 'compact' }: { children: ReactNode; density?: 'compact' | 'comfortable' }) {
  return <section className={styles.metricStrip} data-density={density}>{children}</section>
}

export function MetricItem({
  label,
  value,
  detail,
  tone = 'neutral',
}: {
  label: string
  value: ReactNode
  detail?: ReactNode
  tone?: AdminStatusTone
}) {
  return (
    <div className={styles.metricItem} data-tone={tone}>
      <span>{label}</span>
      <strong>{value}</strong>
      {detail ? <small>{detail}</small> : null}
    </div>
  )
}

export function FilterToolbar({ children }: { children: ReactNode }) {
  return <section className={styles.toolbar}>{children}</section>
}

export function ToolbarGroup({ children, grow = false }: { children: ReactNode; grow?: boolean }) {
  return <div className={grow ? styles.toolbarGroupGrow : styles.toolbarGroup}>{children}</div>
}

export function StatusBadge({
  children,
  tone = 'neutral',
  pulse = false,
}: {
  children: ReactNode
  tone?: AdminStatusTone
  pulse?: boolean
}) {
  return (
    <span className={styles.statusBadge} data-pulse={pulse || undefined} data-tone={tone}>
      <i aria-hidden="true" />
      {children}
    </span>
  )
}

export function ResourceMeter({
  label,
  value,
  max,
  detail,
}: {
  label: string
  value: number
  max: number
  detail?: string
}) {
  const ratio = max > 0 ? Math.min(1, Math.max(0, value / max)) : 0
  const level = ratio >= 0.9 ? 'danger' : ratio >= 0.7 ? 'warning' : 'normal'
  return (
    <div className={styles.resourceMeter} data-level={level}>
      <span>
        <strong>{label}</strong>
        <small>{detail ?? `${value} / ${max}`}</small>
      </span>
      <progress aria-label={label} max={Math.max(max, 1)} value={value} />
    </div>
  )
}

function interactiveTarget(target: EventTarget | null) {
  return target instanceof Element && Boolean(target.closest('a, button, input, select, textarea, label'))
}

export function DataTable<T>({
  caption,
  columns,
  rows,
  rowKey,
  onRowClick,
  emptyTitle = '暂无数据',
  emptyDescription = '当前条件下没有可展示的记录。',
  density = 'compact',
}: {
  caption: string
  columns: AdminDataColumn<T>[]
  rows: T[]
  rowKey: (row: T) => string | number
  onRowClick?: (row: T) => void
  emptyTitle?: string
  emptyDescription?: string
  density?: 'compact' | 'comfortable'
}) {
  if (!rows.length) return <DataState description={emptyDescription} title={emptyTitle} />

  const activate = (row: T, event: MouseEvent<HTMLTableRowElement>) => {
    if (!onRowClick || interactiveTarget(event.target)) return
    onRowClick(row)
  }

  const activateWithKeyboard = (row: T, event: KeyboardEvent<HTMLTableRowElement>) => {
    if (!onRowClick || (event.key !== 'Enter' && event.key !== ' ')) return
    event.preventDefault()
    onRowClick(row)
  }

  return (
    <div className={styles.tableFrame}>
      <table className={styles.dataTable} data-density={density}>
        <caption>{caption}</caption>
        <thead>
          <tr>
            {columns.map((column) => (
              <th
                data-align={column.align}
                data-visibility={column.visibility ?? 'always'}
                data-width={column.width ?? 'medium'}
                key={column.id}
                scope="col"
              >
                {column.header}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => (
            <tr
              data-clickable={Boolean(onRowClick) || undefined}
              key={rowKey(row)}
              onClick={(event) => activate(row, event)}
              onKeyDown={(event) => activateWithKeyboard(row, event)}
              tabIndex={onRowClick ? 0 : undefined}
            >
              {columns.map((column) => (
                <td
                  data-align={column.align}
                  data-visibility={column.visibility ?? 'always'}
                  data-width={column.width ?? 'medium'}
                  key={column.id}
                >
                  {column.render(row)}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

export function PaginationBar({
  page,
  pageCount,
  total,
  onPageChange,
}: {
  page: number
  pageCount: number
  total?: number
  onPageChange: (page: number) => void
}) {
  if (pageCount <= 1) return total === undefined ? null : <div className={styles.singlePageCount}>{total} 条记录</div>
  return (
    <nav aria-label="分页" className={styles.pagination}>
      <span>{total === undefined ? null : `共 ${total} 条`}</span>
      <div>
        <button aria-label="上一页" disabled={page <= 1} onClick={() => onPageChange(page - 1)} type="button">
          <ChevronLeft size={17} />
        </button>
        <strong>
          {page} / {pageCount}
        </strong>
        <button aria-label="下一页" disabled={page >= pageCount} onClick={() => onPageChange(page + 1)} type="button">
          <ChevronRight size={17} />
        </button>
      </div>
    </nav>
  )
}

export function CursorPaginationBar({
  page,
  hasNext,
  onPrevious,
  onNext,
  label = '游标分页',
}: {
  page: number
  hasNext: boolean
  onPrevious: () => void
  onNext: () => void
  label?: string
}) {
  if (page <= 1 && !hasNext) return null
  return (
    <nav aria-label={label} className={styles.pagination}>
      <span>{label}</span>
      <div>
        <button aria-label="上一页" disabled={page <= 1} onClick={onPrevious} type="button">
          <ChevronLeft size={17} />
        </button>
        <strong>第 {page} 页</strong>
        <button aria-label="下一页" disabled={!hasNext} onClick={onNext} type="button">
          <ChevronRight size={17} />
        </button>
      </div>
    </nav>
  )
}

export function DetailDrawer({
  open,
  title,
  description,
  onClose,
  children,
  footer,
}: {
  open: boolean
  title: string
  description?: string
  onClose: () => void
  children: ReactNode
  footer?: ReactNode
}) {
  return (
    <VNextDrawer
      description={description}
      eyebrow="ENTITY DETAIL"
      footer={footer}
      onClose={onClose}
      open={open}
      size="wide"
      title={title}
    >
      {children}
    </VNextDrawer>
  )
}

export function RefreshIndicator({ active, label }: { active: boolean; label: string }) {
  return (
    <span className={styles.refreshIndicator} data-active={active || undefined}>
      <RefreshCw aria-hidden="true" size={14} />
      {label}
    </span>
  )
}

export function AdminEditorSection({
  title,
  description,
  children,
}: {
  title: string
  description?: string
  children: ReactNode
}) {
  return (
    <section className={styles.editorSection}>
      <header>
        <h2>{title}</h2>
        {description ? <p>{description}</p> : null}
      </header>
      <div>{children}</div>
    </section>
  )
}

export function AdminEditorActionBar({ children, status }: { children: ReactNode; status?: ReactNode }) {
  return (
    <footer className={styles.editorActionBar}>
      <div>{status}</div>
      <div>{children}</div>
    </footer>
  )
}

export function MobileAdminMenuButton({ onClick }: { onClick: () => void }) {
  return (
    <button aria-label="打开管理导航" className={styles.mobileMenuButton} onClick={onClick} type="button">
      <Menu size={18} />
      管理导航
    </button>
  )
}
