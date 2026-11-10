import { ActionIcon, Button, Drawer, Group, Modal, Select, TextInput } from '@mantine/core'
import { useDebouncedValue } from '@mantine/hooks'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ChevronRight, Layers3, Network, Search, Trash2 } from 'lucide-react'
import { useState } from 'react'
import { useWorkspaceApi, type Blueprint } from '../../api/client'
import { Empty, ErrorMessage, Loading } from '../../foundation/Feedback'
import { LoadMore } from '../../foundation/LoadMore'
import { dateTime } from '../../foundation/format'
import { useCursorList } from '../../foundation/useCursorList'
import { allowsProject } from '../access/permissions'
import { CreateEnvironmentDialog } from '../environments/CreateEnvironmentDialog'
import { useNavigate } from './../../routing'
import styles from './Templates.module.css'

export function BlueprintsPanel() {
  const api = useWorkspaceApi()
  const [query, setQuery] = useState('')
  const [search] = useDebouncedValue(query, 250)
  const blueprints = useCursorList(['blueprints', { search }], (page) => api.blueprints({ ...page, search }))
  const [selected, setSelected] = useState<Blueprint>()
  const navigate = useNavigate()
  const items = blueprints.data ?? []
  return (
    <>
      <div className="collection-toolbar">
        <TextInput
          aria-label="搜索环境模板"
          placeholder="搜索模板"
          leftSection={<Search size={16} />}
          value={query}
          onChange={(event) => setQuery(event.currentTarget.value)}
        />
      </div>
      <ErrorMessage error={blueprints.error} />
      {blueprints.isPending ? (
        <Loading />
      ) : items.length ? (
        <div className="table-surface">
          <table className="data-table">
            <thead>
              <tr>
                <th>模板</th>
                <th>最新版本</th>
                <th>规模</th>
                <th>最近更新</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {items.map((item) => (
                <tr key={item.id}>
                  <td>
                    <button className="object-link object-link-button" onClick={() => setSelected(item)}>
                      <span className="object-symbol">
                        <Layers3 size={20} />
                      </span>
                      <strong>{item.name}</strong>
                    </button>
                  </td>
                  <td className={styles.technical}>v{item.latestVersion}</td>
                  <td className="numeric">
                    {item.assetCount} 资产
                    <span className="muted"> · {item.networkCount} 网段</span>
                  </td>
                  <td className={`${styles.technical} muted`}>{dateTime(item.updatedAt)}</td>
                  <td className="table-action">
                    <ActionIcon
                      variant="subtle"
                      color="gray"
                      aria-label={`查看${item.name}`}
                      onClick={() => setSelected(item)}
                    >
                      <ChevronRight size={18} />
                    </ActionIcon>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : (
        !blueprints.error && (
          <Empty
            icon={<Layers3 size={30} />}
            title={query ? '没有匹配的模板' : '暂无环境模板'}
            action={query ? undefined : '打开环境'}
            onAction={() => navigate('/environments')}
          />
        )
      )}
      <LoadMore list={blueprints} />
      {selected && <BlueprintDetails key={selected.id} blueprint={selected} onClose={() => setSelected(undefined)} />}
    </>
  )
}

function BlueprintDetails({ blueprint, onClose }: { blueprint: Blueprint; onClose: () => void }) {
  const api = useWorkspaceApi()
  const identity = useQuery({ queryKey: ['identity'], queryFn: api.identity })
  const canCreate = allowsProject(identity.data, 'compose', blueprint.projectId)
  const [versionId, setVersionId] = useState(blueprint.latestVersionId)
  const [creating, setCreating] = useState(false)
  const [confirming, setConfirming] = useState(false)
  const client = useQueryClient()
  const remove = useMutation({
    mutationFn: () => api.deleteBlueprint(blueprint.id),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: ['blueprints'] })
      onClose()
    },
  })
  const versions = useCursorList(['blueprint-versions', blueprint.id], (page) =>
    api.blueprintVersions(blueprint.id, page)
  )
  const version = useQuery({
    queryKey: ['blueprint-version', versionId],
    queryFn: () => api.blueprintVersion(versionId),
  })
  return (
    <>
      <Drawer opened onClose={onClose} title={blueprint.name} position="right" size={760}>
        <div className="form-stack">
          <Select
            label="版本"
            allowDeselect={false}
            value={versionId}
            data={(versions.data ?? []).map((item) => ({
              value: item.id,
              label: `v${item.version} · ${dateTime(item.createdAt)}`,
            }))}
            onChange={(value) => setVersionId(value!)}
          />
          <LoadMore list={versions} />
          <ErrorMessage error={versions.error ?? version.error} />
          {version.isPending ? (
            <Loading />
          ) : (
            version.data && (
              <>
                <div className="blueprint-scale">
                  <Layers3 size={18} />
                  <span>{version.data.assetCount} 个资产</span>
                  <span>{version.data.networkCount} 个网段</span>
                  {Boolean(version.data.spec.routers?.length) && (
                    <span>{version.data.spec.routers?.length} 个路由器</span>
                  )}
                </div>
                <div className={styles.blueprintGrid}>
                  <section>
                    <h3>网段</h3>
                    {version.data.spec.networks.map((network) => (
                      <div key={network.id} className={styles.blueprintRow}>
                        <strong>
                          <Network size={14} /> {network.name}
                        </strong>
                        <span className="mono">{network.cidr}</span>
                      </div>
                    ))}
                  </section>
                  {Boolean(version.data.spec.routers?.length) && (
                    <section>
                      <h3>路由器</h3>
                      {version.data.spec.routers?.map((router) => (
                        <div key={router.id} className={styles.blueprintRow}>
                          <strong>{router.name}</strong>
                          <span>
                            {router.interfaces
                              .map(
                                (iface) =>
                                  version.data!.spec.networks.find((network) => network.id === iface.networkId)?.name
                              )
                              .join(' · ')}
                          </span>
                        </div>
                      ))}
                    </section>
                  )}
                  <section>
                    <h3>资产</h3>
                    {version.data.spec.assets.map((asset) => (
                      <div key={asset.id} className={styles.blueprintRow}>
                        <strong>{asset.name}</strong>
                        <span>
                          {asset.resources.cpu} 核 · {asset.resources.memoryMiB / 1024} GiB
                        </span>
                      </div>
                    ))}
                  </section>
                </div>
                {(canCreate || blueprint.permissions?.includes('compose')) && (
                  <div className="drawer-footer">
                    <Group justify="space-between">
                      {blueprint.permissions?.includes('compose') && (
                        <Button
                          variant="subtle"
                          color="red"
                          leftSection={<Trash2 size={15} />}
                          onClick={() => setConfirming(true)}
                        >
                          删除模板
                        </Button>
                      )}
                      {canCreate && <Button onClick={() => setCreating(true)}>创建环境</Button>}
                    </Group>
                  </div>
                )}
              </>
            )
          )}
        </div>
      </Drawer>
      <Modal
        opened={confirming}
        onClose={() => setConfirming(false)}
        title={`删除 ${blueprint.name}？`}
        centered
        size="sm"
      >
        <ErrorMessage error={remove.error} />
        <Group justify="flex-end">
          <Button variant="default" onClick={() => setConfirming(false)}>
            取消
          </Button>
          <Button color="red" loading={remove.isPending} onClick={() => remove.mutate()}>
            删除
          </Button>
        </Group>
      </Modal>
      {creating && (
        <CreateEnvironmentDialog blueprint={blueprint} versionId={versionId} onClose={() => setCreating(false)} />
      )}
    </>
  )
}
