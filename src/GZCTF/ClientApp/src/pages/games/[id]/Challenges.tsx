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
    <WithNavBar width="var(--container)">
      <WithRole requiredRole={Role.User}>
        <WithGameTab>
          <div className="challenge-layout game-challenge-layout">
            <ChallengePanel />
            <Stack gap="sm" miw="22rem" maw="22rem">
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
