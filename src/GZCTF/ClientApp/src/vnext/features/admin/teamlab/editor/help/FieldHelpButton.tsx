import { CircleHelp } from 'lucide-react'
import { useCallback, useEffect, useId, useLayoutEffect, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import { teamLabFieldHelpOf } from './teamLabFieldHelp'
import styles from './FieldHelpButton.module.css'

interface HelpPosition {
  left: number
  top: number
  placement: 'above' | 'below'
}

const POPOVER_WIDTH = 320
const VIEWPORT_GUTTER = 16
const POPOVER_GAP = 8
const INITIAL_POPOVER_HEIGHT = 176

function positionFor(anchor: HTMLElement, popoverHeight = INITIAL_POPOVER_HEIGHT): HelpPosition {
  const rect = anchor.getBoundingClientRect()
  const width = Math.min(POPOVER_WIDTH, window.innerWidth - VIEWPORT_GUTTER * 2)
  const left = Math.max(VIEWPORT_GUTTER, Math.min(rect.right - width, window.innerWidth - width - VIEWPORT_GUTTER))
  const placement = rect.bottom + POPOVER_GAP + popoverHeight > window.innerHeight && rect.top > popoverHeight
    ? 'above'
    : 'below'
  return {
    left,
    top: placement === 'above'
      ? Math.max(VIEWPORT_GUTTER + popoverHeight, rect.top - POPOVER_GAP)
      : Math.min(rect.bottom + POPOVER_GAP, window.innerHeight - VIEWPORT_GUTTER - popoverHeight),
    placement,
  }
}

export function FieldHelpButton({ fieldKey }: { fieldKey: string }) {
  const [open, setOpen] = useState(false)
  const [position, setPosition] = useState<HelpPosition | null>(null)
  const anchor = useRef<HTMLButtonElement>(null)
  const popover = useRef<HTMLSpanElement>(null)
  const descriptionId = useId()
  const help = teamLabFieldHelpOf(fieldKey)

  const refreshPosition = useCallback(() => {
    if (!anchor.current) return
    const popoverHeight = popover.current?.getBoundingClientRect().height ?? INITIAL_POPOVER_HEIGHT
    const next = positionFor(anchor.current, popoverHeight)
    setPosition((current) => current &&
      current.left === next.left && current.top === next.top && current.placement === next.placement
      ? current
      : next)
  }, [])

  useLayoutEffect(() => {
    if (!open) return
    refreshPosition()
    window.addEventListener('resize', refreshPosition)
    window.addEventListener('scroll', refreshPosition, true)
    return () => {
      window.removeEventListener('resize', refreshPosition)
      window.removeEventListener('scroll', refreshPosition, true)
    }
  }, [open, position, refreshPosition])

  useLayoutEffect(() => {
    if (!popover.current || !position) return
    popover.current.style.setProperty('--teamlab-help-left', `${position.left}px`)
    popover.current.style.setProperty('--teamlab-help-top', `${position.top}px`)
  }, [position])

  useEffect(() => {
    if (!open) setPosition(null)
  }, [open])

  if (!help) return null

  const tooltip = open && position ? createPortal(
    <span
      className={styles.popover}
      data-placement={position.placement}
      id={descriptionId}
      ref={popover}
      role="tooltip"
    >
      <strong>{help.title}</strong>
      <span>{help.description}</span>
    </span>,
    globalThis.document.body
  ) : null

  return (
    <span className={styles.help}>
      <button
        aria-describedby={open ? descriptionId : undefined}
        aria-expanded={open}
        aria-label={`关于${help.title}`}
        onBlur={() => setOpen(false)}
        onClick={(event) => {
          event.preventDefault()
          event.stopPropagation()
          setOpen(true)
        }}
        onKeyDown={(event) => {
          if (event.key !== 'Escape') return
          event.preventDefault()
          event.stopPropagation()
          setOpen(false)
          anchor.current?.blur()
        }}
        onFocus={() => setOpen(true)}
        onMouseEnter={() => setOpen(true)}
        onMouseLeave={() => setOpen(false)}
        onPointerDown={(event) => event.stopPropagation()}
        ref={anchor}
        title={help.title}
        type="button"
      >
        <CircleHelp aria-hidden="true" size={14} />
      </button>
      {tooltip}
    </span>
  )
}
