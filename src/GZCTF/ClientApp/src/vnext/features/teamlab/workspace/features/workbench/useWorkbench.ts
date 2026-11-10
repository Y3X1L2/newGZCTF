import { useDebouncedValue } from '@mantine/hooks'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useMemo, useState } from 'react'
import { useWorkspaceApi, type EnvironmentSpec, type Schema } from '../../api/client'
import { randomUUID } from '../../foundation/id'
import { useCursorList } from '../../foundation/useCursorList'

export function useWorkbench(id: string) {
  const api = useWorkspaceApi()
  const client = useQueryClient()
  const [editing, setEditing] = useState(false)
  const [observing, setObserving] = useState<string>()
  const [spec, setSpec] = useState<EnvironmentSpec>({
    assets: [],
    networks: [],
  })
  const [templateSearch, setTemplateSearch] = useState('')
  const [search] = useDebouncedValue(templateSearch, 250)
  const environment = useQuery({
    queryKey: ['environment', id],
    queryFn: ({ signal }) => api.environment(id, signal),
  })
  const state = useQuery({
    queryKey: ['state', id],
    queryFn: ({ signal }) => api.state(id, signal),
    enabled: observing === id,
  })
  const services = useQuery({
    queryKey: ['services', id],
    queryFn: ({ signal }) => api.services(id, signal),
    enabled: Boolean(environment.data?.appliedSpec),
  })
  const catalog = useCursorList(['templates', 'picker', { search }], (page) => api.templates({ ...page, search }), {
    enabled: environment.data?.permissions?.includes('compose') === true,
    refetchInterval: (items) => (items.some((template) => template.state === 'importing') ? 2500 : false),
  })
  const currentSpec = editing ? spec : (environment.data?.appliedSpec ?? environment.data?.spec)
  const ids = [...new Set(currentSpec?.assets.map((asset) => asset.templateId) ?? [])].sort()
  const usedTemplates = useCursorList(['templates', 'used', ids], (page) => api.templates({ ...page, ids }), {
    enabled: ids.length > 0,
  })
  useEffect(() => {
    if (usedTemplates.hasNextPage && !usedTemplates.isFetchingNextPage && !usedTemplates.isFetchNextPageError)
      void usedTemplates.fetchNextPage()
  }, [
    usedTemplates.hasNextPage,
    usedTemplates.isFetchingNextPage,
    usedTemplates.isFetchNextPageError,
    usedTemplates.fetchNextPage,
  ])
  const templateData = useMemo(
    () => [
      ...new Map([...(catalog.data ?? []), ...(usedTemplates.data ?? [])].map((item) => [item.id, item])).values(),
    ],
    [catalog.data, usedTemplates.data]
  )
  const templates = {
    ...catalog,
    data: templateData,
    error: usedTemplates.error ?? catalog.error,
  }
  const operations = useCursorList(['operations', id], (page) => api.operations(id, page))
  useEffect(() => {
    const events = api.events(id)
    let scheduled: ReturnType<typeof setTimeout> | undefined
    let operationChanged = false
    let resourcesChanged = false
    const refreshState = (event?: Event) => {
      if (event?.type.startsWith('operation.')) {
        operationChanged = true
        resourcesChanged ||= event.type !== 'operation.progress'
      }
      scheduled ??= setTimeout(() => {
        scheduled = undefined
        void client.invalidateQueries({ queryKey: ['state', id] })
        if (operationChanged) {
          operationChanged = false
          void client.invalidateQueries({ queryKey: ['operations', id] })
        }
        if (resourcesChanged) {
          resourcesChanged = false
          void client.invalidateQueries({ queryKey: ['services', id] })
          void client.invalidateQueries({ queryKey: ['vpn-access', id] })
          void client.invalidateQueries({ queryKey: ['recovery-points', id] })
          void client.invalidateQueries({ queryKey: ['environments'] })
        }
      }, 100)
    }
    events.onopen = () => {
      setObserving(id)
      refreshState()
    }
    events.addEventListener('runtime.changed', refreshState)
    events.addEventListener('security.changed', () => {
      void client.invalidateQueries({ queryKey: ['security', id] })
    })
    for (const kind of ['operation.progress', 'operation.succeeded', 'operation.failed', 'operation.partially_applied'])
      events.addEventListener(kind, refreshState)
    return () => {
      clearTimeout(scheduled)
      events.close()
    }
  }, [client, id])
  const [baseRevision, setBaseRevision] = useState(0)
  const refresh = () => {
    void client.invalidateQueries({ queryKey: ['environment', id] })
    void client.invalidateQueries({ queryKey: ['state', id] })
    void client.invalidateQueries({ queryKey: ['operations', id] })
    void client.invalidateQueries({ queryKey: ['services', id] })
    void client.invalidateQueries({ queryKey: ['vpn-access', id] })
    void client.invalidateQueries({ queryKey: ['environments'] })
  }
  useEffect(() => {
    if (
      state.data &&
      environment.data &&
      (state.data.revision !== environment.data.revision || state.data.status !== environment.data.status)
    ) {
      void client.invalidateQueries({ queryKey: ['environment', id] })
      void client.invalidateQueries({ queryKey: ['services', id] })
      void client.invalidateQueries({ queryKey: ['vpn-access', id] })
    }
  }, [state.data, environment.data, client, id])
  const beginEdit = () => {
    if (environment.data) {
      setSpec(structuredClone(environment.data.draft?.spec ?? environment.data.spec))
      setBaseRevision(environment.data.draft?.baseRevision ?? environment.data.revision)
      setEditing(true)
    }
  }
  const saveDraft = useMutation({
    mutationFn: () => api.saveDraft(id, { baseRevision, spec }),
    onSuccess: () => {
      setEditing(false)
      refresh()
    },
  })
  const discard = useMutation({
    mutationFn: () => api.discardDraft(id),
    onSuccess: () => {
      setEditing(false)
      refresh()
    },
  })
  const preview = useMutation({
    mutationFn: () => api.preview(id, { expectedRevision: baseRevision, spec, apply: false }),
  })
  const apply = useMutation({
    mutationFn: () =>
      api.apply(id, {
        expectedRevision: preview.data!.revision,
        spec,
        apply: true,
        clientRequestId: randomUUID(),
      }),
    onSuccess: () => {
      setEditing(false)
      preview.reset()
      refresh()
    },
  })
  const action = useMutation({
    mutationFn: ({
      action: value,
      assetId,
      resetVolumes,
    }: {
      action: Schema<'ActionRequest'>['action']
      assetId?: string
      resetVolumes?: boolean
    }) => {
      const body = {
        action: value,
        resetVolumes: resetVolumes ?? false,
        expectedRevision: environment.data!.revision,
        clientRequestId: randomUUID(),
      }
      return assetId ? api.assetAction(id, assetId, body) : api.action(id, body)
    },
    onSuccess: refresh,
  })
  const saveView = useMutation({
    scope: { id: `view-${id}` },
    mutationFn: (view: Schema<'CanvasView'>) => api.saveView(id, view),
    onMutate: (view) =>
      client.setQueryData(['environment', id], (value: typeof environment.data) => value && { ...value, view }),
  })
  const retry = useMutation({
    mutationFn: api.retryOperation,
    onSuccess: refresh,
  })
  const exposeService = useMutation({
    mutationFn: ({
      assetId,
      ...body
    }: Omit<Schema<'CreateService'>, 'expectedRevision' | 'clientRequestId'> & {
      assetId: string
    }) =>
      api.exposeService(id, assetId, {
        ...body,
        expectedRevision: environment.data!.revision,
        clientRequestId: randomUUID(),
      }),
    onSuccess: refresh,
  })
  const revokeService = useMutation({
    mutationFn: (serviceId: string) => api.revokeService(id, serviceId, environment.data!.revision, randomUUID()),
    onSuccess: refresh,
  })
  return {
    environment,
    state,
    services,
    templates,
    templateSearch,
    setTemplateSearch,
    operations,
    editing,
    setEditing,
    beginEdit,
    spec,
    setSpec,
    saveDraft,
    discard,
    preview,
    apply,
    action,
    retry,
    exposeService,
    revokeService,
    saveView,
    refresh,
  }
}
