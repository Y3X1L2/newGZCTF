import { LoaderCircle } from 'lucide-react'
import { ButtonHTMLAttributes, ReactNode } from 'react'
import { Link } from 'react-router'
import styles from './AuthForm.module.css'

export function AuthForm({ children, onSubmit }: { children: ReactNode; onSubmit: () => void | Promise<void> }) {
  return (
    <form
      className={styles.form}
      onSubmit={(event) => {
        event.preventDefault()
        void onSubmit()
      }}
    >
      {children}
    </form>
  )
}

export function AuthSubmitButton({ pending, children, ...props }: ButtonHTMLAttributes<HTMLButtonElement> & { pending?: boolean }) {
  return (
    <button className={styles.primaryButton} disabled={pending || props.disabled} type="submit" {...props}>
      {pending ? <LoaderCircle aria-hidden="true" className={styles.spinner} size={17} /> : null}
      {children}
    </button>
  )
}

export function AuthSecondaryButton({ children, ...props }: ButtonHTMLAttributes<HTMLButtonElement>) {
  return (
    <button className={styles.secondaryButton} type="button" {...props}>
      {children}
    </button>
  )
}

export function AuthTextLink({ children, to }: { children: ReactNode; to: string }) {
  return (
    <Link className={styles.textLink} to={to}>
      {children}
    </Link>
  )
}

export function AuthDivider({ children }: { children: ReactNode }) {
  return (
    <div className={styles.divider}>
      <span>{children}</span>
    </div>
  )
}
