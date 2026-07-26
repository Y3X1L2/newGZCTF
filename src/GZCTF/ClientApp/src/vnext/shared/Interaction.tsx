import { X } from 'lucide-react'
import { ReactNode, useEffect, useId, useLayoutEffect, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import styles from './Interaction.module.css'

export type ActionTone = 'primary' | 'secondary' | 'danger' | 'ghost'
export type DrawerSide = 'left' | 'right'
export type DrawerSize = 'narrow' | 'medium' | 'wide'
export type DrawerRequestClose = (afterClose?: () => void) => void
type DrawerContent = ReactNode | ((requestClose: DrawerRequestClose) => ReactNode)

const drawerCloseFallbackMs = 1000
let bodyScrollLockCount = 0
let bodyOverflowBeforeLock = ''

function acquireBodyScrollLock() {
  if (bodyScrollLockCount === 0) {
    bodyOverflowBeforeLock = document.body.style.overflow
    document.body.style.overflow = 'hidden'
  }
  bodyScrollLockCount += 1
  return () => {
    bodyScrollLockCount = Math.max(0, bodyScrollLockCount - 1)
    if (bodyScrollLockCount === 0) document.body.style.overflow = bodyOverflowBeforeLock
  }
}

export function ActionButton({
  children,
  tone = 'secondary',
  icon,
  className,
  ...props
}: React.ButtonHTMLAttributes<HTMLButtonElement> & {
  tone?: ActionTone
  icon?: ReactNode
}) {
  return (
    <button className={`${styles.actionButton} ${styles[`actionButton_${tone}`]} ${className ?? ''}`} {...props}>
      {icon}
      <span>{children}</span>
    </button>
  )
}

export function InlineFeedback({
  tone = 'neutral',
  children,
}: {
  tone?: 'success' | 'danger' | 'neutral'
  children: ReactNode
}) {
  return <div className={`${styles.feedback} ${styles[`feedback_${tone}`]}`}>{children}</div>
}

export function VNextDialog({
  open,
  eyebrow,
  title,
  description,
  onClose,
  children,
  footer,
  wide = false,
}: {
  open: boolean
  eyebrow: string
  title: string
  description?: string
  onClose: () => void
  children: ReactNode
  footer?: ReactNode
  wide?: boolean
}) {
  const ref = useRef<HTMLDialogElement>(null)
  const titleId = useId()
  const descriptionId = useId()

  useEffect(() => {
    const dialog = ref.current
    if (!dialog) return
    if (open && !dialog.open) dialog.showModal()
    if (!open && dialog.open) dialog.close()
  }, [open])

  useEffect(() => {
    if (!open) return undefined
    return acquireBodyScrollLock()
  }, [open])

  return createPortal(
    <dialog
      aria-describedby={description ? descriptionId : undefined}
      aria-labelledby={titleId}
      className={`${styles.dialog} ${wide ? styles.dialogWide : ''}`}
      onCancel={(event) => {
        event.preventDefault()
        onClose()
      }}
      onClick={(event) => {
        if (event.currentTarget === event.target) onClose()
      }}
      ref={ref}
    >
      <div className={styles.dialogPanel}>
        <header className={styles.dialogHeader}>
          <div>
            <span>{eyebrow}</span>
            <h2 id={titleId}>{title}</h2>
            {description ? <p id={descriptionId}>{description}</p> : null}
          </div>
          <button aria-label="关闭" onClick={onClose} type="button">
            <X size={19} />
          </button>
        </header>
        <div className={styles.dialogBody}>{children}</div>
        {footer ? <footer className={styles.dialogFooter}>{footer}</footer> : null}
      </div>
    </dialog>,
    document.body
  )
}

export function VNextConfirmDialog({
  open,
  title,
  description,
  message,
  confirmLabel = '确认',
  confirmationText,
  onClose,
  onConfirm,
  tone = 'danger',
}: {
  open: boolean
  title: string
  description?: string
  message: ReactNode
  confirmLabel?: string
  confirmationText?: string
  onClose: () => void
  onConfirm: () => boolean | void | Promise<boolean | void>
  tone?: Extract<ActionTone, 'primary' | 'danger'>
}) {
  const [input, setInput] = useState('')
  const [confirming, setConfirming] = useState(false)
  const accepted = !confirmationText || input === confirmationText

  useEffect(() => {
    if (!open) setInput('')
  }, [open])

  const confirm = async () => {
    if (!accepted || confirming) return
    setConfirming(true)
    try {
      const result = await onConfirm()
      if (result !== false) onClose()
    } finally {
      setConfirming(false)
    }
  }

  return (
    <VNextDialog
      description={description}
      eyebrow="CONFIRM ACTION"
      footer={
        <>
          <ActionButton disabled={confirming} onClick={onClose} type="button">
            取消
          </ActionButton>
          <ActionButton disabled={!accepted || confirming} onClick={() => void confirm()} tone={tone} type="button">
            {confirming ? '正在处理' : confirmLabel}
          </ActionButton>
        </>
      }
      onClose={() => {
        if (!confirming) onClose()
      }}
      open={open}
      title={title}
    >
      <div className={styles.confirmContent}>
        <p>{message}</p>
        {confirmationText ? (
          <label className={styles.confirmField}>
            <span>请输入“{confirmationText}”确认</span>
            <input autoComplete="off" onChange={(event) => setInput(event.currentTarget.value)} value={input} />
          </label>
        ) : null}
      </div>
    </VNextDialog>
  )
}

export function VNextDrawer({
  open,
  eyebrow,
  title,
  description,
  onClose,
  children,
  footer,
  side = 'right',
  size = 'wide',
  bodyPadding = 'default',
}: {
  open: boolean
  eyebrow: string
  title: string
  description?: string
  onClose: () => void
  children: DrawerContent
  footer?: DrawerContent
  side?: DrawerSide
  size?: DrawerSize
  bodyPadding?: 'default' | 'none'
}) {
  const ref = useRef<HTMLDialogElement>(null)
  const returnFocusRef = useRef<HTMLElement | null>(null)
  const closeTimeoutRef = useRef<number | null>(null)
  const closeRequestedRef = useRef(false)
  const afterCloseRef = useRef<(() => void) | null>(null)
  const titleId = useId()
  const descriptionId = useId()
  const [drawerState, setDrawerState] = useState<'closed' | 'opening' | 'open' | 'closing'>('closed')

  const finishClose = () => {
    if (!closeRequestedRef.current) return
    closeRequestedRef.current = false
    if (closeTimeoutRef.current !== null) {
      window.clearTimeout(closeTimeoutRef.current)
      closeTimeoutRef.current = null
    }
    const afterClose = afterCloseRef.current
    afterCloseRef.current = null
    onClose()
    afterClose?.()
  }

  const requestClose: DrawerRequestClose = (afterClose) => {
    if (drawerState === 'closing') return
    afterCloseRef.current = afterClose ?? null
    if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
      onClose()
      afterCloseRef.current = null
      afterClose?.()
      return
    }
    closeRequestedRef.current = true
    if (closeTimeoutRef.current !== null) window.clearTimeout(closeTimeoutRef.current)
    setDrawerState('closing')
    closeTimeoutRef.current = window.setTimeout(() => {
      closeTimeoutRef.current = null
      finishClose()
    }, drawerCloseFallbackMs)
  }

  useLayoutEffect(() => {
    const drawer = ref.current
    if (!drawer) return
    if (open) {
      closeRequestedRef.current = false
      if (closeTimeoutRef.current !== null) {
        window.clearTimeout(closeTimeoutRef.current)
        closeTimeoutRef.current = null
      }
      if (!drawer.open) {
        returnFocusRef.current = document.activeElement instanceof HTMLElement ? document.activeElement : null
        drawer.showModal()
      }
      setDrawerState(window.matchMedia('(prefers-reduced-motion: reduce)').matches ? 'open' : 'opening')
      return
    }
    if (!open && drawer.open) {
      closeRequestedRef.current = false
      afterCloseRef.current = null
      if (closeTimeoutRef.current !== null) {
        window.clearTimeout(closeTimeoutRef.current)
        closeTimeoutRef.current = null
      }
      drawer.close()
      setDrawerState('closed')
      requestAnimationFrame(() => returnFocusRef.current?.focus())
    }
  }, [open])

  useEffect(
    () => () => {
      if (closeTimeoutRef.current !== null) window.clearTimeout(closeTimeoutRef.current)
    },
    []
  )

  useEffect(() => {
    if (!open) return undefined
    return acquireBodyScrollLock()
  }, [open])

  return (
    <dialog
      aria-describedby={description ? descriptionId : undefined}
      aria-labelledby={titleId}
      className={`${styles.drawer} ${styles[`drawer_${side}`]} ${styles[`drawer_${size}`]} ${
        drawerState === 'opening'
          ? styles.drawerOpening
          : drawerState === 'open'
            ? styles.drawerOpen
            : drawerState === 'closing'
              ? styles.drawerClosing
              : ''
      }`}
      onCancel={(event) => {
        event.preventDefault()
        requestClose()
      }}
      onClick={(event) => {
        if (event.currentTarget === event.target) requestClose()
      }}
      ref={ref}
    >
      <div
        className={`${styles.drawerPanel} ${
          drawerState === 'opening'
            ? styles.drawerPanelOpening
            : drawerState === 'closing'
              ? styles.drawerPanelClosing
              : ''
        }`}
        onAnimationEnd={(event) => {
          if (event.currentTarget !== event.target) return
          if (closeRequestedRef.current) {
            finishClose()
            return
          }
          if (drawerState === 'opening') setDrawerState('open')
        }}
      >
        <header className={styles.dialogHeader}>
          <div>
            <span>{eyebrow}</span>
            <h2 id={titleId}>{title}</h2>
            {description ? <p id={descriptionId}>{description}</p> : null}
          </div>
          <button aria-label="关闭" onClick={() => requestClose()} type="button">
            <X size={19} />
          </button>
        </header>
        <div className={`${styles.drawerBody} ${bodyPadding === 'none' ? styles.drawerBodyFlush : ''}`}>
          {typeof children === 'function' ? children(requestClose) : children}
        </div>
        {footer ? (
          <footer className={styles.dialogFooter}>
            {typeof footer === 'function' ? footer(requestClose) : footer}
          </footer>
        ) : null}
      </div>
    </dialog>
  )
}
