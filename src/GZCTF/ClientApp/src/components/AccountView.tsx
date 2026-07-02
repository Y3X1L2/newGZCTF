import { Center } from '@mantine/core'
import { FC, PropsWithChildren } from 'react'
import { useNavigate } from 'react-router'
import { LogoHeader } from '@Components/LogoHeader'
import { YinyuHexField } from '@Components/yinyu/YinyuUI'
import { LogoDistortion } from '@Components/yinyu/grid-distortion/LogoDistortion'
import { useConfig } from '@Hooks/useConfig'
import misc from '@Styles/Misc.module.css'

interface AccountViewProps extends PropsWithChildren {
  onSubmit?: (event: React.FormEvent) => Promise<void>
}

export const AccountView: FC<AccountViewProps> = ({ onSubmit, children }) => {
  const navigate = useNavigate()
  const { config } = useConfig()

  return (
    <Center mih="100vh" px="md" py="xl" className="yy-standalone-shell">
      <article className="auth-stage">
        <div className="panel-card auth-form-card">
          <YinyuHexField cells={28} />
          <div className="auth-form-panel">
            <LogoHeader onClick={() => navigate('/')} />
            <form className={misc.accountForm} onSubmit={onSubmit}>
              {children}
            </form>
          </div>
        </div>
        <div className="auth-logo-panel" aria-hidden="true">
          <LogoDistortion className="auth-logo-distortion" src={config.logoUrl} />
        </div>
      </article>
    </Center>
  )
}
