import { ReactNode } from 'react'
import { Link } from 'react-router'
import styles from './CourseManagementPanelHeader.module.css'

export function CourseManagementPanelHeader({
  eyebrow,
  title,
  description,
  actions,
}: {
  eyebrow: string
  title: string
  description: ReactNode
  actions?: ReactNode
}) {
  return (
    <header className={styles.header}>
      <div>
        <span>{eyebrow}</span>
        <h2>{title}</h2>
        <p>{description}</p>
      </div>
      {actions ? <div className={styles.actions}>{actions}</div> : null}
    </header>
  )
}

export function CourseManagementActionLink({
  to,
  icon,
  children,
}: {
  to: string
  icon?: ReactNode
  children: ReactNode
}) {
  return (
    <Link className={styles.actionLink} to={to}>
      {icon}
      <span>{children}</span>
    </Link>
  )
}
