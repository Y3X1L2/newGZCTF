import { ChevronRight } from 'lucide-react'
import { useMemo, useState } from 'react'
import { NavLink, Outlet, useLocation, useNavigate } from 'react-router'
import { VNextDrawer } from '../../../shared/Interaction'
import { DataState } from '../../../shared/Primitives'
import { useCurrentAccount } from '../../account/useCurrentAccount'
import { MobileAdminMenuButton } from '../shared/AdminWorkbench'
import styles from './AdminShell.module.css'
import { adminNavigation } from './adminNavigation'

function AdminNavigation({ onNavigate }: { onNavigate?: (route: string) => void }) {
  return (
    <nav aria-label="管理工作台导航" className={styles.navigation}>
      {adminNavigation.map((group) => (
        <section key={group.label}>
          <h2>{group.label}</h2>
          <div>
            {group.items.map((item) => {
              const Icon = item.icon
              if (!item.implemented) {
                return (
                  <span className={styles.pendingLink} key={item.id}>
                    <Icon aria-hidden="true" size={17} />
                    <span>{item.label}</span>
                    <small>待建设</small>
                  </span>
                )
              }
              return (
                <NavLink
                  className={({ isActive }) => (isActive ? styles.activeLink : styles.navLink)}
                  key={item.id}
                  onClick={(event) => {
                    if (!onNavigate) return
                    event.preventDefault()
                    onNavigate(item.route)
                  }}
                  to={item.route}
                >
                  <Icon aria-hidden="true" size={17} />
                  <span>{item.label}</span>
                  <ChevronRight aria-hidden="true" size={15} />
                </NavLink>
              )
            })}
          </div>
        </section>
      ))}
    </nav>
  )
}

export function AdminShell() {
  const account = useCurrentAccount()
  const location = useLocation()
  const navigate = useNavigate()
  const [navigationOpen, setNavigationOpen] = useState(false)
  const activeLabel = useMemo(
    () =>
      adminNavigation
        .flatMap((group) => group.items)
        .find((item) => location.pathname === item.route || location.pathname.startsWith(`${item.route}/`))?.label ??
      '管理工作台',
    [location.pathname]
  )

  if (!account.user && !account.error) {
    return <DataState description="正在确认管理权限与账户状态。" loading title="管理工作台加载中" />
  }

  if (!account.isAdmin) {
    return <DataState description="只有管理员和超级管理员可以访问平台运维数据。" title="没有管理权限" />
  }

  return (
    <div className={styles.shell}>
      <aside className={styles.sidebar}>
        <header>
          <span>OPERATIONS</span>
          <strong>管理工作台</strong>
        </header>
        <AdminNavigation />
      </aside>

      <div className={styles.workspace}>
        <div className={styles.mobileContext}>
          <MobileAdminMenuButton onClick={() => setNavigationOpen(true)} />
          <strong>{activeLabel}</strong>
        </div>
        <Outlet />
      </div>

      <VNextDrawer
        bodyPadding="none"
        eyebrow="OPERATIONS"
        onClose={() => setNavigationOpen(false)}
        open={navigationOpen}
        side="left"
        size="medium"
        title="管理工作台"
      >
        {(requestClose) => <AdminNavigation onNavigate={(route) => requestClose(() => navigate(route))} />}
      </VNextDrawer>
    </div>
  )
}
