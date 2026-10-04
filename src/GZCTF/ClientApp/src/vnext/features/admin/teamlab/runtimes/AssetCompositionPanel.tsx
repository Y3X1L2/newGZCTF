import { PackagePlus, Send, Trash2 } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import { ActionButton, InlineFeedback } from '../../../../shared/Interaction'
import { errorMessage } from '../../../../shared/errors'
import { listTeamLabImageOptions, teamLabResourcesApi, teamLabRuntimeApi } from '../api'
import type { TeamLabDevicePackage, TeamLabImageOption, TeamLabRuntime, TeamLabTopologyAsset } from '../api'
import { DeviceParametersEditor } from '../editor/inspector/DeviceParametersEditor'
import styles from './RuntimeComposition.module.css'

type Change = { action: 'add' | 'replace' | 'remove'; key: string; name: string; asset?: TeamLabTopologyAsset }

const ipv4Number = (address: string) => address.split('.').reduce((value, part) => value * 256 + Number(part), 0)

function nextHostOffset(runtime: TeamLabRuntime, networkKeys: readonly string[], changes: readonly Change[]) {
  const used = new Set(changes.flatMap(change => change.asset?.interfaces.filter(item => networkKeys.includes(item.networkKey)).map(item => item.hostOffset) ?? []))
  for (const network of runtime.networks.filter(item => networkKeys.includes(item.key))) {
    const base = ipv4Number(network.cidr.split('/')[0])
    const size = 2 ** (32 - Number(network.cidr.split('/')[1]))
    for (const asset of runtime.assets) {
      if (!asset.primaryIp) continue
      const offset = ipv4Number(asset.primaryIp) - base
      if (offset >= 0 && offset < size) used.add(offset)
    }
  }
  let offset = 3
  while (used.has(offset)) offset++
  return offset
}

export function AssetCompositionPanel({ runtime, onSubmitted, initialAction = 'add', initialAssetKey = '' }: {
  runtime: TeamLabRuntime
  onSubmitted: () => Promise<unknown>
  initialAction?: Change['action']
  initialAssetKey?: string
}) {
  const [images, setImages] = useState<readonly TeamLabImageOption[]>([])
  const [packages, setPackages] = useState<readonly TeamLabDevicePackage[]>([])
  const [changes, setChanges] = useState<Change[]>([])
  const action = initialAction
  const [assetKey] = useState(initialAction === 'add' ? '' : initialAssetKey)
  const [name, setName] = useState(runtime.assets.find(item => item.key === initialAssetKey)?.name ?? '')
  const [templateId, setTemplateId] = useState(0)
  const [devicePackageId, setDevicePackageId] = useState(0)
  const [deviceParameters, setDeviceParameters] = useState('{}')
  const initialNetworks = runtime.assets.find(item => item.key === initialAssetKey)?.networkKeys ?? []
  const [networkKeys, setNetworkKeys] = useState<string[]>(initialNetworks.length ? [...initialNetworks] : runtime.networks[0] ? [runtime.networks[0].key] : [])
  const [primaryNetworkKey, setPrimaryNetworkKey] = useState(initialNetworks[0] ?? runtime.networks[0]?.key ?? '')
  const [hostOffset, setHostOffset] = useState(() => nextHostOffset(runtime, initialNetworks.length ? initialNetworks : [runtime.networks[0]?.key ?? ''], []))
  const [cpuUnits, setCpuUnits] = useState(1)
  const [memoryMiB, setMemoryMiB] = useState(512)
  const [storageMiB, setStorageMiB] = useState(4096)
  const [busy, setBusy] = useState(false)
  const [failure, setFailure] = useState<unknown>(null)
  const [definition, setDefinition] = useState<TeamLabTopologyAsset | null>(null)

  useEffect(() => {
    let active = true
    const selected = runtime.assets.find(item => item.key === initialAssetKey)
    void Promise.all([listTeamLabImageOptions(), teamLabResourcesApi.listDevicePackages({ limit: 100 }),
      action === 'replace' && selected ? teamLabRuntimeApi.getAssetDefinition(runtime.id, selected.id) : null,
    ]).then(([items, packagePage, current]) => {
      if (!active) return
      setImages(items)
      setPackages(packagePage.items.filter((item) => item.enabled && !item.archived && item.bindingId))
      if (current) {
        setDefinition(current)
        setName(current.name)
        setTemplateId(current.imageTemplateId)
        setDevicePackageId(current.devicePackageId ?? 0)
        setDeviceParameters(JSON.stringify(current.deviceParameters ?? {}))
        setNetworkKeys(current.interfaces.map(item => item.networkKey))
        setPrimaryNetworkKey(current.interfaces.find(item => item.primary)!.networkKey)
        setCpuUnits(current.resources.cpuUnits)
        setMemoryMiB(current.resources.memoryMiB)
        setStorageMiB(current.resources.storageMiB)
      }
    }).catch(error => { if (active) setFailure(error) })
    return () => { active = false }
  }, [action, initialAssetKey, runtime.id])

  const existing = useMemo(() => runtime.assets.find((item) => item.key === assetKey), [assetKey, runtime.assets])
  const selectedImage = images.find((item) => item.id === templateId)
  const selectedPackage = packages.find((item) => item.bindingId === devicePackageId)
  const digest = (value: string | null | undefined) => value?.replace(/^sha256:/i, '').toLowerCase()
  const packageImage = (item: TeamLabDevicePackage) => item.digest ? images.find((image) => digest(image.digest) === digest(item.digest)) : undefined
  const selectImage = (id: number) => {
    const image = images.find(item => item.id === id)
    setTemplateId(id)
    setDevicePackageId(0)
    setDeviceParameters('{}')
    setCpuUnits(image?.deviceType === 'docker' ? 1 : 2)
    setMemoryMiB(image?.deviceType === 'docker' ? 512 : image?.deviceType === 'windows-vm' ? 4096 : 2048)
    setStorageMiB(image?.deviceType === 'docker' ? 4096 : 32768)
  }

  const stage = () => {
    setFailure(null)
    if (action === 'remove') {
      if (!existing) return setFailure(new Error('请选择要移除的资产。'))
      setChanges((items) => [...items.filter((item) => item.key !== existing.key), { action, key: existing.key, name: existing.name }])
      return
    }
    if (!name.trim() || !selectedImage || networkKeys.length === 0 || !networkKeys.includes(primaryNetworkKey)) {
      return setFailure(new Error('请填写资产名称、模板和接入网段。'))
    }
    let parameters: unknown = null
    try { parameters = selectedPackage ? JSON.parse(deviceParameters || '{}') : null }
    catch { return setFailure(new Error('设备参数格式不正确。')) }
    const asset: TeamLabTopologyAsset = {
      ...definition,
      key: existing?.key ?? `asset-${crypto.randomUUID()}`, name: name.trim(), kind: selectedImage.deviceType === 'docker' ? 'docker' : 'vm',
      imageTemplateId: selectedImage.id,
      resources: { cpuUnits, memoryMiB, storageMiB },
      interfaces: definition?.interfaces ?? networkKeys.map((networkKey, index) => ({
        key: `eth${index}`, networkKey, hostOffset, primary: networkKey === primaryNetworkKey, orderIndex: index,
      })),
      exposePort: definition?.exposePort ?? null, healthCheck: definition?.healthCheck ?? null,
      orderIndex: definition?.orderIndex ?? runtime.assets.length + changes.length,
      devicePackageId: selectedPackage?.bindingId ?? null, deviceParameters: parameters, connectorId: definition?.connectorId ?? null,
    }
    setChanges((items) => [...items.filter((item) => item.key !== asset.key), { action, key: asset.key, name: asset.name, asset }])
    if (action === 'add') {
      setHostOffset(nextHostOffset(runtime, networkKeys, [...changes, { action, key: asset.key, name: asset.name, asset }]))
      setName('')
    }
  }

  const submit = async () => {
    if (busy || changes.length === 0) return
    setBusy(true)
    setFailure(null)
    try {
      await teamLabRuntimeApi.changeAssets(runtime.id, {
        expectedPlanRevision: runtime.planRevision ?? 0,
        add: changes.filter((item) => item.action === 'add').flatMap((item) => item.asset ? [item.asset] : []),
        replace: changes.filter((item) => item.action === 'replace').flatMap((item) => item.asset ? [item.asset] : []),
        remove: changes.filter((item) => item.action === 'remove').map((item) => item.key),
        overlays: null,
      })
      setChanges([])
      await onSubmitted()
    } catch (error) {
      setFailure(error)
    } finally {
      setBusy(false)
    }
  }

  return <section className={styles.panel} aria-label="资产编排">
    <div className={styles.formGrid}>
      {action !== 'remove' ? <>
        <label>资产名称<input maxLength={128} value={name} onChange={(event) => setName(event.currentTarget.value)} /></label>
        <label>镜像模板<select value={templateId} onChange={(event) => selectImage(Number(event.currentTarget.value))}><option value={0}>选择镜像</option>{images.map((image) => <option key={image.id} value={image.id}>{image.name} · {image.deviceType === 'docker' ? '容器' : '虚拟机'}</option>)}</select></label>
        <label>设备模板<select value={devicePackageId} onChange={(event) => {
          const packageId = Number(event.currentTarget.value)
          const selected = packages.find((item) => item.bindingId === packageId)
          const image = selected ? packageImage(selected) : undefined
          setDevicePackageId(packageId)
          setDeviceParameters('{}')
          if (selected && image) {
            setTemplateId(image.id)
            setCpuUnits(Math.max(1, Math.ceil(selected.cpuMillis / 1000)))
            setMemoryMiB(selected.memoryMiB)
            setStorageMiB(selected.storageGib * 1024)
          }
        }}><option value={0}>无</option>{packages.map((item) => <option disabled={!packageImage(item)} key={item.id} value={item.bindingId}>{item.displayName} · {item.version}{packageImage(item) ? '' : '（缺少镜像）'}</option>)}</select></label>
        {selectedPackage ? <DeviceParametersEditor schema={selectedPackage.parameterSchema} value={deviceParameters} onChange={setDeviceParameters} /> : null}
        {action === 'add' ? <label>接入网段<select value={primaryNetworkKey} onChange={(event) => { const key = event.currentTarget.value; setPrimaryNetworkKey(key); setNetworkKeys([key]); setHostOffset(nextHostOffset(runtime, [key], changes)) }}>{runtime.networks.map((network) => <option key={network.key} value={network.key}>{network.name}</option>)}</select></label> : null}
        <details className={styles.advanced}><summary>详细配置</summary>
          {action === 'add' ? <><label>更多网段<select multiple value={networkKeys} onChange={(event) => { const values = [...event.currentTarget.selectedOptions].map((item) => item.value); setNetworkKeys(values); if (!values.includes(primaryNetworkKey)) setPrimaryNetworkKey(values[0] ?? '') }}>{runtime.networks.map((network) => <option key={network.key} value={network.key}>{network.name}</option>)}</select></label>
          <label>地址序号<input min={3} type="number" value={hostOffset} onChange={(event) => setHostOffset(Number(event.currentTarget.value))} /></label></> : null}
          <label>CPU 核数<input min={1} type="number" value={cpuUnits} onChange={(event) => setCpuUnits(Number(event.currentTarget.value))} /></label>
          <label>内存 MiB<input min={128} step={128} type="number" value={memoryMiB} onChange={(event) => setMemoryMiB(Number(event.currentTarget.value))} /></label>
          <label>存储 MiB<input min={1024} step={1024} type="number" value={storageMiB} onChange={(event) => setStorageMiB(Number(event.currentTarget.value))} /></label>
        </details>
      </> : null}
      <div className={styles.formAction}><ActionButton disabled={action === 'replace' && !definition} icon={<PackagePlus size={16} />} onClick={stage} type="button">加入本轮变更</ActionButton></div>
    </div>
    {failure ? <InlineFeedback tone="danger">{errorMessage(failure, '资产变更失败。')}</InlineFeedback> : null}
    {changes.length ? <div className={styles.changeList}>{changes.map((item) => <div key={item.key}><span data-action={item.action}>{item.action === 'add' ? '新增' : item.action === 'replace' ? '替换' : '移除'}</span><strong>{item.name}</strong><button aria-label={`移除 ${item.name}`} onClick={() => setChanges((rows) => rows.filter((row) => row.key !== item.key))} type="button"><Trash2 size={15} /></button></div>)}<ActionButton disabled={busy} icon={<Send size={16} />} onClick={() => void submit()} tone="primary" type="button">提交 {changes.length} 项变更</ActionButton></div> : null}
  </section>
}
