import { Stack } from '@mantine/core'
import { FC } from 'react'
import { ChallengePanel } from '@Components/ChallengePanel'
import { GameNoticePanel } from '@Components/GameNoticePanel'
import { TeamRank } from '@Components/TeamRank'
import { WithGameTab } from '@Components/WithGameTab'
import { WithNavBar } from '@Components/WithNavbar'
import { WithRole } from '@Components/WithRole'
import { Role } from '@Api'

const Challenges: FC = () => {
  return (
    <WithNavBar width="min(100%, calc(100vw - 7.25rem))">
      <WithRole requiredRole={Role.User}>
        <WithGameTab>
          <div className="challenge-layout game-challenge-layout yy-challenge-workspace">
            <ChallengePanel />
            <Stack gap="sm" className="yy-challenge-side">
              <TeamRank />
              <GameNoticePanel />
            </Stack>
          </div>
        </WithGameTab>
      </WithRole>
    </WithNavBar>
  )
}

export default Challenges
