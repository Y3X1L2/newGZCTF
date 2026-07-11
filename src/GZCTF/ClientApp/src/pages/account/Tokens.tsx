import { Box } from '@mantine/core'
import { ApiTokenManager } from '@Components/account/ApiTokenManager'
import { WithNavBar } from '@Components/WithNavbar'
import { WithRole } from '@Components/WithRole'
import { Role } from '@Api'

export default function AccountTokens() {
  return (
    <WithNavBar width="var(--container)" minWidth={0}>
      <WithRole requiredRole={Role.Teacher}>
        <Box p="md" className="panel-card admin-panel">
          <ApiTokenManager />
        </Box>
      </WithRole>
    </WithNavBar>
  )
}
