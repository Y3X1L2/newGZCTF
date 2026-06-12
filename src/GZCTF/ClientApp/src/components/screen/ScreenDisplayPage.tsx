import { FC } from 'react'
import { WithRole } from '@Components/WithRole'
import { usePageTitle } from '@Hooks/usePageTitle'
import { Role } from '@Api'
import CTFScreenPage from '@Components/ctf-screen/CTFScreenPage'

interface ScreenDisplayPageProps {
  gameId: number
  demoMode?: boolean
}

const ScreenDisplayPage: FC<ScreenDisplayPageProps> = ({ gameId, demoMode = false }) => {
  usePageTitle(demoMode ? '大屏演示模式' : '赛事态势大屏')

  return (
    <WithRole requiredRole={Role.Admin}>
      <CTFScreenPage gameId={gameId} demoMode={demoMode} />
    </WithRole>
  )
}

export default ScreenDisplayPage
