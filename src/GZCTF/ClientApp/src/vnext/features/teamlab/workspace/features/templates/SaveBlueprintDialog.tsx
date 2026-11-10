import { Button, Modal, Select, TextInput } from '@mantine/core'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { CheckCircle2 } from 'lucide-react'
import { useState } from 'react'
import { useWorkspaceApi, type EnvironmentSpec } from '../../api/client'
import { ErrorMessage } from '../../foundation/Feedback'
import { LoadMore } from '../../foundation/LoadMore'
import { useCursorList } from '../../foundation/useCursorList'
import { Link } from './../../routing'

export function SaveBlueprintDialog({
  environmentId,
  environmentName,
  expectedRevision,
  spec,
  onClose,
}: {
  environmentId: string
  environmentName: string
  expectedRevision: number
  spec: EnvironmentSpec
  onClose: () => void
}) {
  const api = useWorkspaceApi()
  const [mode, setMode] = useState('new')
  const [name, setName] = useState(environmentName)
  const [blueprintId, setBlueprintId] = useState<string | null>(null)
  const blueprints = useCursorList(['blueprints'], api.blueprints, {
    enabled: mode === 'version',
  })
  const client = useQueryClient()
  const save = useMutation({
    mutationFn: async () => {
      if (mode === 'new') {
        const blueprint = await api.saveBlueprint(environmentId, {
          name,
          expectedRevision,
          spec,
        })
        return {
          name: blueprint.name,
          version: blueprint.latestVersion,
          blueprintId: blueprint.id,
        }
      }
      const version = await api.saveBlueprintVersion(blueprintId!, {
        environmentId,
        expectedRevision,
        spec,
      })
      return {
        name: blueprints.data!.find((item) => item.id === blueprintId)!.name,
        version: version.version,
        blueprintId: version.blueprintId,
      }
    },
    onSuccess: (result) => {
      void client.invalidateQueries({ queryKey: ['blueprints'] })
      void client.invalidateQueries({
        queryKey: ['blueprint-versions', result.blueprintId],
      })
    },
  })
  return (
    <Modal opened onClose={onClose} title="保存为环境模板" centered size="sm">
      {save.data ? (
        <>
          <div className="blueprint-saved">
            <CheckCircle2 size={32} />
            <h3>{save.data.name}</h3>
            <span>v{save.data.version} 已保存</span>
          </div>
          <div className="dialog-actions">
            <Button variant="default" component={Link} to="/templates?tab=environments">
              查看模板
            </Button>
            <Button onClick={onClose}>完成</Button>
          </div>
        </>
      ) : (
        <form
          className="form-stack"
          onSubmit={(event) => {
            event.preventDefault()
            save.mutate()
          }}
        >
          <div className="filter-tabs" role="group" aria-label="模板保存方式">
            {[
              ['new', '新模板'],
              ['version', '现有模板新版本'],
            ].map(([value, label]) => (
              <button
                key={value}
                type="button"
                className={mode === value ? 'selected' : ''}
                aria-pressed={mode === value}
                onClick={() => {
                  setMode(value)
                  save.reset()
                }}
              >
                {label}
              </button>
            ))}
          </div>
          {mode === 'new' ? (
            <TextInput
              label="模板名称"
              required
              autoFocus
              value={name}
              onChange={(event) => setName(event.currentTarget.value)}
            />
          ) : (
            <Select
              label="环境模板"
              required
              searchable
              allowDeselect={false}
              value={blueprintId}
              data={(blueprints.data ?? []).map((item) => ({
                value: item.id,
                label: `${item.name} · v${item.latestVersion}`,
              }))}
              onChange={setBlueprintId}
            />
          )}
          {mode === 'version' && <LoadMore list={blueprints} />}
          <div className="blueprint-scale">
            <span>{spec.assets.length} 个资产</span>
            <span>{spec.networks.length} 个网段</span>
            {Boolean(spec.routers?.length) && <span>{spec.routers?.length} 个路由器</span>}
          </div>
          <ErrorMessage error={blueprints.error ?? save.error} />
          <div className="dialog-actions">
            <Button variant="default" onClick={onClose}>
              取消
            </Button>
            <Button type="submit" loading={save.isPending} disabled={mode === 'version' && !blueprintId}>
              保存模板
            </Button>
          </div>
        </form>
      )}
    </Modal>
  )
}
