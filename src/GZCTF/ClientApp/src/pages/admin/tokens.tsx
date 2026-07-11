import { ApiTokenManager } from '@Components/account/ApiTokenManager'
import { AdminPage } from '@Components/admin/AdminPage'

export default function AdminTokens() {
  return (
    <AdminPage>
      <ApiTokenManager />
    </AdminPage>
  )
}
