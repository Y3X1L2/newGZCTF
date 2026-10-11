import { ChevronLeft, ChevronRight } from 'lucide-react'
import { ActionButton } from './Interaction'
import styles from './CatalogPagination.module.css'

export function CatalogPagination({ page, pageSize, total, disabled = false, onPageChange, onPageSizeChange }: {
  page: number
  pageSize: number
  total: number
  disabled?: boolean
  onPageChange: (page: number) => void
  onPageSizeChange: (size: number) => void
}) {
  const pageCount = Math.max(1, Math.ceil(total / pageSize))
  return (
    <nav aria-label="题库分页" className={styles.pagination}>
      <span>共 {total} 道题目</span>
      <label className={styles.pageSize}>每页
        <select aria-label="每页题目数" disabled={disabled} onChange={(event) => onPageSizeChange(Number(event.currentTarget.value))} value={pageSize}>
          {[20, 50, 100].map(size => <option key={size} value={size}>{size}</option>)}
        </select>
      </label>
      <div className={styles.actions}>
        <ActionButton aria-label="上一页" disabled={disabled || page <= 1} icon={<ChevronLeft size={16} />} onClick={() => onPageChange(page - 1)} type="button">上一页</ActionButton>
        <span aria-live="polite">{page} / {pageCount}</span>
        <ActionButton aria-label="下一页" disabled={disabled || page >= pageCount} icon={<ChevronRight size={16} />} onClick={() => onPageChange(page + 1)} type="button">下一页</ActionButton>
      </div>
    </nav>
  )
}
