import { FC } from 'react'
import { WithRole } from '@Components/WithRole'
import { usePageTitle } from '@Hooks/usePageTitle'
import { Role } from '@Api'
import CTFScreenPage from '@Components/ctf-screen/CTFScreenPage'

interface ScreenDisplayPageProps {
  gameId: number
}

const ScreenDisplayPage: FC<ScreenDisplayPageProps> = ({ gameId }) => {
  usePageTitle('赛事大屏')

  return (
    <WithRole requiredRole={Role.Admin}>
      <CTFScreenPage gameId={gameId} />
    </WithRole>
  )
}

export default ScreenDisplayPage
