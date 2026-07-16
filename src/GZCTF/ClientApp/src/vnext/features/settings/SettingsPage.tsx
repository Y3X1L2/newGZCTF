import { KeyRound, LockKeyhole, UserRound } from 'lucide-react'
import { Link, NavLink, Navigate, useParams } from 'react-router'
import { PageHeading } from '../../shared/Primitives'
import { useVNextPageTitle } from '../../shared/useVNextPageTitle'
import { ProfileSettings } from './ProfileSettings'
import { SecuritySettings } from './SecuritySettings'
import styles from './SettingsPage.module.css'
import { TokenSettings } from './TokenSettings'

type SettingsSection = 'profile' | 'security' | 'tokens'

const sections: Array<{ id: SettingsSection; label: string; icon: typeof UserRound }> = [
  { id: 'profile', label: '个人资料', icon: UserRound },
  { id: 'security', label: '账户安全', icon: LockKeyhole },
  { id: 'tokens', label: 'API Token', icon: KeyRound },
]

export function SettingsPage() {
  const { section = 'profile' } = useParams()
  const validSection = sections.some((item) => item.id === section) ? (section as SettingsSection) : null
  useVNextPageTitle('账户设置')

  if (!validSection) return <Navigate replace to="/settings/profile" />

  return (
    <div className={styles.page}>
      <PageHeading description="管理个人资料、账户安全和程序化访问凭据。" eyebrow="ACCOUNT CONTROL" title="账户设置" />
      <div className={styles.layout}>
        <nav aria-label="设置分类" className={styles.sideNav}>
          {sections.map((item) => {
            const Icon = item.icon
            return (
              <NavLink
                className={({ isActive }) => (isActive ? styles.sideLinkActive : styles.sideLink)}
                key={item.id}
                to={`/settings/${item.id}`}
              >
                <Icon size={17} />
                {item.label}
              </NavLink>
            )
          })}
          <Link className={styles.backHome} to="/">
            返回平台首页
          </Link>
        </nav>
        <main className={styles.panel}>
          {validSection === 'profile' ? <ProfileSettings /> : null}
          {validSection === 'security' ? <SecuritySettings /> : null}
          {validSection === 'tokens' ? <TokenSettings /> : null}
        </main>
      </div>
    </div>
  )
}
