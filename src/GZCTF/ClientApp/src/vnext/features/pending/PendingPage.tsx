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
        <p className={styles.eyebrow}>VERTICAL SLICE PENDING</p>
        <h1>{module.label}尚未进入本轮重构</h1>
        <p>
          当前新前端只开放首页和赛事列表。本模块会在对应业务流程、真实 API
          和验收矩阵准备完成后独立实现，不回退到旧页面。
        </p>
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
