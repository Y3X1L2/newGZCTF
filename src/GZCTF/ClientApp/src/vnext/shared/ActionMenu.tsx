import { MoreHorizontal } from 'lucide-react'
import { useEffect, useId, useLayoutEffect, useRef, useState, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import styles from './ActionMenu.module.css'

export interface ActionMenuItem {
  label: string
  icon?: ReactNode
  disabled?: boolean
  danger?: boolean
  separator?: boolean
  onSelect: () => void
}

export function ActionMenu({ label, items, disabled, open: controlledOpen, onOpenChange }: {
  label: string
  items: readonly ActionMenuItem[]
  disabled?: boolean
  open?: boolean
  onOpenChange?: (open: boolean) => void
}) {
  const id = useId()
  const trigger = useRef<HTMLButtonElement>(null)
  const menu = useRef<HTMLDivElement>(null)
  const [localOpen, setLocalOpen] = useState(false)
  const open = controlledOpen ?? localOpen
  const change = (next: boolean) => { setLocalOpen(next); onOpenChange?.(next) }

  useLayoutEffect(() => {
    if (!open) return
    const box = trigger.current!.getBoundingClientRect()
    const popup = menu.current!
    const left = Math.max(8, Math.min(box.right - popup.offsetWidth, innerWidth - popup.offsetWidth - 8))
    const top = box.bottom + popup.offsetHeight + 8 < innerHeight ? box.bottom + 4 : Math.max(8, box.top - popup.offsetHeight - 4)
    popup.style.setProperty('--menu-left', `${left}px`)
    popup.style.setProperty('--menu-top', `${top}px`)
    const focusTarget = popup.querySelector<HTMLButtonElement>('button:not(:disabled)') ?? popup
    focusTarget.focus()
  }, [open])

  useEffect(() => {
    if (!open) return
    const dismiss = (event: PointerEvent) => {
      if (!menu.current?.contains(event.target as Node) && !trigger.current?.contains(event.target as Node)) change(false)
    }
    document.addEventListener('pointerdown', dismiss)
    return () => document.removeEventListener('pointerdown', dismiss)
  }, [open, onOpenChange])

  return <>
    <button aria-label={label} title={label} aria-haspopup="menu" aria-expanded={open} aria-controls={open ? id : undefined}
      className={styles.trigger} disabled={disabled} ref={trigger} onClick={() => change(!open)}
      onKeyDown={event => { if (event.key === 'ArrowDown') { event.preventDefault(); change(true) } }} type="button"><MoreHorizontal size={18} /></button>
    {open ? createPortal(<div id={id} role="menu" aria-label={label} tabIndex={-1} className={styles.menu} ref={menu} onKeyDown={event => {
      if (event.key === 'Escape' || event.key === 'Tab') { change(false); trigger.current?.focus(); if (event.key === 'Escape') event.preventDefault(); return }
      const buttons = [...menu.current!.querySelectorAll<HTMLButtonElement>('button:not(:disabled)')]
      const index = buttons.indexOf(document.activeElement as HTMLButtonElement)
      if (['ArrowDown', 'ArrowUp', 'Home', 'End'].includes(event.key)) {
        event.preventDefault()
        buttons[event.key === 'Home' ? 0 : event.key === 'End' ? buttons.length - 1 : (index + (event.key === 'ArrowDown' ? 1 : -1) + buttons.length) % buttons.length]?.focus()
      }
    }}>{items.map(item => <button key={item.label} role="menuitem" disabled={item.disabled} data-danger={item.danger || undefined}
      data-separator={item.separator || undefined} onClick={() => { change(false); trigger.current?.focus(); item.onSelect() }} type="button">
      {item.icon}<span>{item.label}</span>
    </button>)}</div>, document.body) : null}
  </>
}
