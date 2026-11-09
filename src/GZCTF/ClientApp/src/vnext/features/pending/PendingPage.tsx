import { ArrowLeft, LayoutTemplate } from 'lucide-react'
import { Link, useLocation } from 'react-router'
import { currentModule } from '../../app/shell/moduleRegistry'
import { useVNextPageTitle } from '../../shared/useVNextPageTitle'
import styles from './PendingPage.module.css'

export function PendingPage() {
  const location = useLocation()
  const module = currentModule(location.pathname)
  useVNextPageTitle(module.label)

  return (
    <div className={styles.page}>
      <section className={styles.state}>
        <div className={styles.geometry} aria-hidden="true">
          <span />
          <span />
          <i />
        </div>
        <span className={styles.icon}>
          <LayoutTemplate size={22} />
        </span>
        <h1>{module.label}暂未开放</h1>
        <div className={styles.actions}>
          <Link className={styles.primaryAction} to="/">
            <ArrowLeft size={17} />
            返回首页
          </Link>
          <Link className={styles.secondaryAction} to="/games">
            查看赛事列表
          </Link>
        </div>
      </section>
    </div>
  )
}
