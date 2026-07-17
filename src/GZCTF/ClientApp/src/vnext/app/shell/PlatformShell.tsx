import { Activity, ChevronRight, Grid3X3, LogIn, LogOut, Moon, PlayCircle, Settings, Sun, UserRound } from 'lucide-react'
import { useEffect, useMemo, useRef, useState } from 'react'
import { Link, NavLink, Outlet, useLocation, useNavigate } from 'react-router'
import { getPlatformName, PLATFORM_TYPE } from '@Utils/Brand'
import { useConfig } from '@Hooks/useConfig'
import { roleLabel, useAccountLogout, useCurrentAccount } from '../../features/account/useCurrentAccount'
import { useAccountSummary } from '../../features/profile/useUserProfileController'
import { DrawerRequestClose, VNextConfirmDialog, VNextDrawer } from '../../shared/Interaction'
import { useVNextTheme } from '../VNextThemeProvider'
import styles from './PlatformShell.module.css'
import { currentModule, isModuleActive, platformModules, primaryModules } from './moduleRegistry'

function initials(name?: string | null) {
  const normalized = name?.trim()
  if (!normalized) return 'YY'
  return normalized.slice(0, 2).toUpperCase()
}

interface DrawerProps {
  open: boolean
  onClose: () => void
}

function ModuleDrawer({ open, onClose }: DrawerProps) {
  const location = useLocation()
  const navigate = useNavigate()
  const { isAdmin } = useCurrentAccount()
  const visibleModules = useMemo(() => platformModules.filter((module) => !module.adminOnly || isAdmin), [isAdmin])
  const groups = [...new Set(visibleModules.map((module) => module.group))]

  return (
    <VNextDrawer
      bodyPadding="none"
      eyebrow="MODULE INDEX"
      onClose={onClose}
      open={open}
      side="left"
      size="medium"
      title="全部模块"
    >
      {(requestClose) => (
        <div className={styles.moduleDrawerBody}>
          {groups.map((group) => (
            <section className={styles.moduleGroup} key={group}>
              <h3>{group}</h3>
              <div className={styles.moduleList}>
                {visibleModules
                  .filter((module) => module.group === group)
                  .map((module) => {
                    const Icon = module.icon
                    const active = isModuleActive(location.pathname, module.route)
                    return (
                      <Link
                        className={active ? styles.moduleLinkActive : styles.moduleLink}
                        key={module.id}
                        onClick={(event) => {
                          event.preventDefault()
                          requestClose(() => navigate(module.route))
                        }}
                        to={module.route}
                      >
                        <span className={styles.moduleIcon}>
                          <Icon size={18} />
                        </span>
                        <span className={styles.moduleCopy}>
                          <strong>{module.label}</strong>
                          <small>{module.description}</small>
                        </span>
                        <span className={module.implemented ? styles.readyState : styles.pendingState}>
                          {module.implemented ? '已重构' : '待建设'}
                        </span>
                      </Link>
                    )
                  })}
              </div>
            </section>
          ))}
        </div>
      )}
    </VNextDrawer>
  )
}

function AccountDrawer({ open, onClose }: DrawerProps) {
  const navigate = useNavigate()
  const { user, isAuthenticated, isAdmin } = useCurrentAccount()
  const logout = useAccountLogout()
  const summaryRequest = useAccountSummary(open && isAuthenticated)
  const summary = summaryRequest.data
  const displayName = summary?.userName || user?.userName || '当前用户'
  const [logoutConfirmOpen, setLogoutConfirmOpen] = useState(false)
  const logoutDrawerCloseRef = useRef<DrawerRequestClose | null>(null)

  const onLogout = (requestClose: DrawerRequestClose) => {
    logoutDrawerCloseRef.current = requestClose
    setLogoutConfirmOpen(true)
  }

  return (
    <>
      <VNextDrawer
        bodyPadding="none"
        eyebrow="ACCOUNT"
        footer={
          isAuthenticated
            ? (requestClose) => (
                <button className={styles.accountLogout} onClick={() => onLogout(requestClose)} type="button">
                  <LogOut size={17} />
                  退出登录
                </button>
              )
            : undefined
        }
        onClose={onClose}
        open={open}
        size="narrow"
        title="个人中心"
      >
        {(requestClose) => (
          <div className={styles.accountDrawerBody}>
            {isAuthenticated ? (
              <section className={styles.identity}>
                <span className={styles.avatarLarge}>
                  {user?.avatar ? <img alt="" src={user.avatar} /> : initials(displayName)}
                </span>
                <div>
                  <strong>{displayName}</strong>
                  <span>{roleLabel(summary?.role ?? user?.role)}</span>
                  <p>{summary?.bio || user?.bio || '还没有填写公开简介。'}</p>
                </div>
              </section>
            ) : (
              <section className={styles.signedOut}>
                <span className={styles.avatarLarge}>YY</span>
                <div>
                  <strong>尚未登录</strong>
                  <p>登录后可访问参赛状态、培训进度和个人设置。</p>
                </div>
              </section>
            )}

            {isAuthenticated && summary ? (
              <section className={styles.accountSummary}>
                <div>
                  <strong>{summary.solved}</strong>
                  <span>个人解题</span>
                </div>
                <div>
                  <strong>{summary.activeDays}</strong>
                  <span>活跃天数</span>
                </div>
                <div>
                  <strong>{summary.runningInstances}</strong>
                  <span>运行实例</span>
                </div>
                {summary.pendingReviews ? (
                  <div>
                    <strong>{summary.pendingReviews}</strong>
                    <span>待审核</span>
                  </div>
                ) : null}
              </section>
            ) : null}

            {isAuthenticated && summary?.continueItems.length ? (
              <section className={styles.continueSection}>
                <header>
                  <Activity size={15} />
                  <span>继续进行</span>
                </header>
                <div>
                  {summary.continueItems.map((item) => (
                    <Link
                      key={item.id}
                      onClick={(event) => {
                        event.preventDefault()
                        requestClose(() => navigate(item.route))
                      }}
                      to={item.route}
                    >
                      <PlayCircle size={17} />
                      <span>
                        <strong>{item.title}</strong>
                        <small>{item.subtitle}</small>
                      </span>
                      <ChevronRight size={15} />
                    </Link>
                  ))}
                </div>
              </section>
            ) : null}

            <nav className={styles.accountLinks}>
              {isAuthenticated ? (
                <>
                  <Link
                    onClick={(event) => {
                      event.preventDefault()
                      requestClose(() => navigate('/users/me'))
                    }}
                    to="/users/me"
                  >
                    <UserRound size={17} />
                    个人主页
                    <ChevronRight size={16} />
                  </Link>
                  <Link
                    onClick={(event) => {
                      event.preventDefault()
                      requestClose(() => navigate('/settings/security'))
                    }}
                    to="/settings/security"
                  >
                    <Settings size={17} />
                    账户设置
                    <ChevronRight size={16} />
                  </Link>
                  {isAdmin ? (
                    <Link
                      onClick={(event) => {
                        event.preventDefault()
                        requestClose(() => navigate('/admin'))
                      }}
                      to="/admin"
                    >
                      <Grid3X3 size={17} />
                      管理工作台
                      <ChevronRight size={16} />
                    </Link>
                  ) : null}
                </>
              ) : (
                <Link
                  onClick={(event) => {
                    event.preventDefault()
                    requestClose(() => navigate('/account/login'))
                  }}
                  to="/account/login"
                >
                  <LogIn size={17} />
                  登录平台
                  <ChevronRight size={16} />
                </Link>
              )}
            </nav>
          </div>
        )}
      </VNextDrawer>
      <VNextConfirmDialog
        confirmLabel="退出登录"
        message="当前会话和本地缓存将被清除，需要重新登录才能继续访问受限功能。"
        onClose={() => setLogoutConfirmOpen(false)}
        onConfirm={() => {
          const requestClose = logoutDrawerCloseRef.current
          logoutDrawerCloseRef.current = null
          if (requestClose) requestClose(() => void logout())
          else void logout()
        }}
        open={logoutConfirmOpen}
        title="确认退出当前账户？"
      />
    </>
  )
}

export function PlatformShell() {
  const location = useLocation()
  const { config } = useConfig()
  const { theme, toggleTheme } = useVNextTheme()
  const { user, isAuthenticated } = useCurrentAccount()
  const [moduleOpen, setModuleOpen] = useState(false)
  const [accountOpen, setAccountOpen] = useState(false)
  const activeModule = currentModule(location.pathname)
  const routeFrameKey = location.pathname.match(/^\/games\/\d+(?=\/)/)?.[0] ?? location.pathname
  const platformName = getPlatformName(config.title)
  const displayName = user?.userName || '登录'

  useEffect(() => {
    window.scrollTo({ top: 0, left: 0 })
  }, [location.pathname])

  return (
    <div className={styles.shell}>
      <aside className={styles.globalRail}>
        <button
          aria-label="打开全部模块"
          className={styles.brandButton}
          onClick={() => setModuleOpen(true)}
          type="button"
        >
          <img alt="" src={config.logoUrl || '/yinyu-icon.png'} />
        </button>
        <nav aria-label="主要模块" className={styles.primaryNav}>
          {primaryModules.map((module) => {
            const Icon = module.icon
            return (
              <NavLink
                className={({ isActive }) => (isActive ? styles.railLinkActive : styles.railLink)}
                end={module.route === '/'}
                key={module.id}
                to={module.route}
              >
                <Icon aria-hidden="true" size={19} />
                <span>{module.shortLabel}</span>
              </NavLink>
            )
          })}
        </nav>
        <button
          aria-label="全部模块"
          className={styles.allModulesButton}
          onClick={() => setModuleOpen(true)}
          type="button"
        >
          <Grid3X3 size={19} />
          <span>全部</span>
        </button>
      </aside>

      <header className={styles.contextBar}>
        <div className={styles.contextBrand}>
          <strong>{platformName}</strong>
          <span>{PLATFORM_TYPE}</span>
        </div>
        <div className={styles.contextRoute}>
          <span aria-hidden="true" />
          <strong>{activeModule.label}</strong>
          {!activeModule.implemented ? <small>模块重构中</small> : null}
        </div>
        <div className={styles.contextActions}>
          <button
            aria-label={theme === 'light' ? '切换夜间主题' : '切换日间主题'}
            className={styles.themeButton}
            onClick={toggleTheme}
            type="button"
          >
            {theme === 'light' ? <Moon size={18} /> : <Sun size={18} />}
          </button>
          <button className={styles.accountTrigger} onClick={() => setAccountOpen(true)} type="button">
            <span className={styles.avatar}>
              {user?.avatar ? <img alt="" src={user.avatar} /> : initials(isAuthenticated ? displayName : undefined)}
            </span>
            <span className={styles.accountCopy}>
              <strong>{displayName}</strong>
              <small>{isAuthenticated ? roleLabel(user?.role) : '访问个人中心'}</small>
            </span>
            <ChevronRight aria-hidden="true" size={16} />
          </button>
        </div>
      </header>

      <main className={styles.main}>
        <div className={styles.routeFrame} key={routeFrameKey}>
          <Outlet />
        </div>
      </main>

      <ModuleDrawer onClose={() => setModuleOpen(false)} open={moduleOpen} />
      <AccountDrawer onClose={() => setAccountOpen(false)} open={accountOpen} />
    </div>
  )
}
