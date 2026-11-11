import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Monitor } from 'lucide-react'
import { useState } from 'react'
import { useParams as useRouteParams } from 'react-router'
import { ApiError, useWorkspaceApi, type Asset, type Schema } from '../../api/client'
import { Empty, ErrorMessage, Loading } from '../../foundation/Feedback'
import { useParams } from '../../routing'
import { allows } from '../access/permissions'
import { ConnectionDialog } from './ConnectionDialog'
import ConsoleWorkspace from './ConsoleWorkspace'
import { isConsoleKind } from './consoles'

export default function ConsolePage() {
  const { id = '' } = useParams()
  const { assetId = '', kind } = useRouteParams()
  const api = useWorkspaceApi()
  const environment = useQuery({
    queryKey: ['environment', id],
    queryFn: () => api.environment(id),
    enabled: Boolean(id),
  })
  if (!id || !isConsoleKind(kind)) return <Empty icon={<Monitor />} title="无效的控制台地址" />
  if (environment.isPending) return <Loading />
  if (environment.error) return <ErrorMessage error={environment.error} />
  const asset = (environment.data.appliedSpec ?? environment.data.spec).assets.find((item) => item.id === assetId)
  if (!asset || !allows(environment.data, 'session', assetId))
    return <Empty icon={<Monitor />} title="无法访问该资产的控制台" />
  return <AssetConsole key={`${id}:${assetId}:${kind}`} environmentId={id} asset={asset} kind={kind} />
}

function AssetConsole({
  environmentId,
  asset,
  kind,
}: {
  environmentId: string
  asset: Asset
  kind: Schema<'ConsoleKind'>
}) {
  const api = useWorkspaceApi()
  const client = useQueryClient()
  const configurable = kind === 'ssh' || kind === 'rdp'
  const [settingsOpen, setSettingsOpen] = useState(false)
  const [settingsDismissed, setSettingsDismissed] = useState(false)
  const [revision, setRevision] = useState(0)
  const settings = useQuery({
    queryKey: [kind, environmentId, asset.id],
    enabled: configurable,
    queryFn: async () => {
      try {
        return await (kind === 'ssh' ? api.sshSettings : api.rdpSettings)(environmentId, asset.id)
      } catch (error) {
        if (error instanceof ApiError && error.status === 404) return null
        throw error
      }
    },
  })
  if (configurable && settings.isPending) return <Loading />
  if (configurable && settings.error) return <ErrorMessage error={settings.error} />
  return (
    <>
      {configurable && !settings.data ? (
        <Empty icon={<Monitor />} title="尚未配置连接" action="连接设置" onAction={() => setSettingsOpen(true)} />
      ) : (
        <ConsoleWorkspace
          environmentId={environmentId}
          session={{ id: asset.id, name: asset.name, kind, revision }}
          onSettings={configurable ? () => setSettingsOpen(true) : undefined}
        />
      )}
      {configurable && (settingsOpen || (settings.data === null && !settingsDismissed)) && (
        <ConnectionDialog
          environmentId={environmentId}
          asset={asset}
          kind={kind}
          onClose={() => {
            setSettingsOpen(false)
            setSettingsDismissed(true)
          }}
          onSaved={async () => {
            await client.invalidateQueries({ queryKey: [kind, environmentId, asset.id] })
            setSettingsOpen(false)
            setSettingsDismissed(true)
            setRevision((value) => value + 1)
          }}
        />
      )}
    </>
  )
}
