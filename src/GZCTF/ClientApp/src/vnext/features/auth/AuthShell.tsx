import { ArrowLeft, Moon, Sun } from 'lucide-react'
import { ReactNode } from 'react'
import { Link, Outlet } from 'react-router'
import { useConfig } from '@Hooks/useConfig'
import { getPlatformName } from '@Utils/Brand'
import { useVNextTheme } from '../../app/VNextThemeProvider'
import styles from './AuthShell.module.css'

export function AuthShell() {
  const { config } = useConfig()
  const { theme, toggleTheme } = useVNextTheme()
  const platformName = getPlatformName(config.title)
  const slogan = config.slogan?.split(/\r?\n/).find(Boolean) ?? '安全综合演练平台'

  return (
    <main className={styles.shell}>
      <header className={styles.header}>
        <Link aria-label="返回平台首页" className={styles.brand} to="/">
          <img alt="" src={config.logoUrl || '/yinyu-icon.png'} />
          <span>
            <strong>{platformName}</strong>
            <small>{slogan}</small>
          </span>
        </Link>
        <button aria-label={theme === 'dark' ? '切换到日间主题' : '切换到夜间主题'} onClick={toggleTheme} type="button">
          {theme === 'dark' ? <Sun aria-hidden="true" size={18} /> : <Moon aria-hidden="true" size={18} />}
        </button>
      </header>

      <section className={styles.brandPlane} aria-hidden="true">
        <span className={styles.planeA} />
        <span className={styles.planeB} />
        <span className={styles.routeA} />
        <span className={styles.routeB} />
        <div>
          <small>IDENTITY GATEWAY</small>
          <strong>{platformName}</strong>
          <p>{config.description || '赛事、培训与攻防演练的统一身份入口。'}</p>
        </div>
      </section>

      <section className={styles.formRegion}>
        <Link className={styles.backHome} to="/">
          <ArrowLeft aria-hidden="true" size={16} />
          返回首页
        </Link>
        <Outlet />
      </section>
    </main>
  )
}

export function AuthPanel({
  eyebrow,
  title,
  description,
  children,
  footer,
}: {
  eyebrow: string
  title: string
  description: string
  children: ReactNode
  footer?: ReactNode
}) {
  return (
    <section className={styles.panel}>
      <header>
        <span>{eyebrow}</span>
        <h1>{title}</h1>
        <p>{description}</p>
      </header>
      {children}
      {footer ? <footer>{footer}</footer> : null}
    </section>
  )
}

export function AuthMessage({ children, tone = 'error' }: { children: ReactNode; tone?: 'error' | 'info' | 'success' }) {
  return (
    <div className={styles.message} data-tone={tone} role={tone === 'error' ? 'alert' : 'status'}>
      {children}
    </div>
  )
}
