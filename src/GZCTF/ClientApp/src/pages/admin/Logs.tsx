import { Tabs } from '@mantine/core'
import { useMemo } from 'react'
import { useLocation, useNavigate } from 'react-router'
import { AdminPage } from '@Components/admin/AdminPage'
import { DeploymentQueueView } from '@Components/admin/observability/DeploymentQueueView'
import { OperationalEventsView } from '@Components/admin/observability/OperationalEventsView'
import { SystemLogView } from '@Components/admin/observability/SystemLogView'

type OperationsTab = 'events' | 'queue' | 'system' | 'recovery'
const tabs = new Set<OperationsTab>(['events', 'queue', 'system', 'recovery'])

export default function Logs() {
  const location = useLocation()
  const navigate = useNavigate()
  const params = useMemo(() => new URLSearchParams(location.search), [location.search])
  const requestedTab = params.get('tab') as OperationsTab | null
  const activeTab: OperationsTab = requestedTab && tabs.has(requestedTab) ? requestedTab : 'events'
  const filters = useMemo(() => ({
    correlationId: params.get('correlation') || undefined,
    workerNodeId: params.get('workerNodeId') || undefined,
    imageTemplateId: params.get('imageTemplateId') || undefined,
    deploymentTicketId: params.get('deploymentTicketId') || undefined,
  }), [params])

  const changeTab = (value: string | null) => {
    const next = value && tabs.has(value as OperationsTab) ? value : 'events'
    const query = new URLSearchParams(location.search)
    query.set('tab', next)
    navigate(`${location.pathname}?${query.toString()}`, { replace: true })
  }

  return (
    <AdminPage>
      <Tabs value={activeTab} onChange={changeTab} keepMounted={false} w="100%">
        <Tabs.List mb="md">
          <Tabs.Tab value="events">事件时间线</Tabs.Tab>
          <Tabs.Tab value="queue">部署队列</Tabs.Tab>
          <Tabs.Tab value="system">系统日志</Tabs.Tab>
          <Tabs.Tab value="recovery">恢复漂移</Tabs.Tab>
        </Tabs.List>
        <Tabs.Panel value="events"><OperationalEventsView initialFilters={filters} /></Tabs.Panel>
        <Tabs.Panel value="queue"><DeploymentQueueView showHeader={false} /></Tabs.Panel>
        <Tabs.Panel value="system"><SystemLogView filters={filters} /></Tabs.Panel>
        <Tabs.Panel value="recovery"><OperationalEventsView recoveryOnly initialFilters={filters} /></Tabs.Panel>
      </Tabs>
    </AdminPage>
  )
}
