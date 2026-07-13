import { AdminPage } from '@Components/admin/AdminPage'
import { DeploymentQueueView } from '@Components/admin/observability/DeploymentQueueView'

export default function QueuePage() {
  return <AdminPage><DeploymentQueueView /></AdminPage>
}
