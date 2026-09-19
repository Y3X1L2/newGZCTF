import { PackagePlus, Send, Trash2 } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import { ActionButton, InlineFeedback } from '../../../../shared/Interaction'
import { errorMessage } from '../../../../shared/errors'
import { listTeamLabImageOptions, teamLabResourcesApi, teamLabRuntimeApi } from '../api'
import type { TeamLabDevicePackage, TeamLabImageOption, TeamLabRuntime, TeamLabTopologyAsset } from '../api'
import { DeviceParametersEditor } from '../editor/inspector/DeviceParametersEditor'
import styles from './RuntimeComposition.module.css'

type Change = { action: 'add' | 'replace' | 'remove'; key: string; name: string; asset?: TeamLabTopologyAsset }

export function AssetCompositionPanel({ runtime, onSubmitted }: {
  runtime: TeamLabRuntime
  onSubmitted: () => Promise<unknown>
}) {
  const [images, setImages] = useState<readonly TeamLabImageOption[]>([])
  const [packages, setPackages] = useState<readonly TeamLabDevicePackage[]>([])
  const [changes, setChanges] = useState<Change[]>([])
  const [action, setAction] = useState<Change['action']>('add')
  const [assetKey, setAssetKey] = useState('')
  const [name, setName] = useState('')
  const [templateId, setTemplateId] = useState(0)
  const [devicePackageId, setDevicePackageId] = useState(0)
  const [deviceParameters, setDeviceParameters] = useState('{}')
  const [networkKeys, setNetworkKeys] = useState<string[]>(runtime.networks[0] ? [runtime.networks[0].key] : [])
  const [primaryNetworkKey, setPrimaryNetworkKey] = useState(runtime.networks[0]?.key ?? '')
  const [hostOffset, setHostOffset] = useState(10)
  const [cpuUnits, setCpuUnits] = useState(1)
  const [memoryMiB, setMemoryMiB] = useState(512)
  const [storageMiB, setStorageMiB] = useState(4096)
  const [busy, setBusy] = useState(false)
  const [failure, setFailure] = useState<unknown>(null)

  useEffect(() => {
    let active = true
    void Promise.all([listTeamLabImageOptions(), teamLabResourcesApi.listDevicePackages({ limit: 100 })]).then(([items, packagePage]) => {
      if (!active) return
      setImages(items)
      setPackages(packagePage.items.filter((item) => item.enabled && !item.archived && item.bindingId))
      setTemplateId((current) => current || items[0]?.id || 0)
    }).catch(setFailure)
    return () => { active = false }
  }, [])

  const existing = useMemo(() => runtime.assets.find((item) => item.key === assetKey), [assetKey, runtime.assets])
  const selectedImage = images.find((item) => item.id === templateId)
  const selectedPackage = packages.find((item) => item.bindingId === devicePackageId)
  const digest = (value: string | null | undefined) => value?.replace(/^sha256:/i, '').toLowerCase()
  const packageImage = (item: TeamLabDevicePackage) => images.find((image) => digest(image.digest) === digest(item.digest))

  const selectExisting = (key: string) => {
    const asset = runtime.assets.find((item) => item.key === key)
    setAssetKey(key)
    setName(asset?.name ?? '')
    const networks = asset?.networkKeys.length ? [...asset.networkKeys] : runtime.networks[0] ? [runtime.networks[0].key] : []
    setNetworkKeys(networks)
    setPrimaryNetworkKey(networks[0] ?? '')
  }

  const stage = () => {
    setFailure(null)
    if (action === 'remove') {
      if (!existing) return setFailure(new Error('请选择要移除的资产。'))
      setChanges((items) => [...items.filter((item) => item.key !== existing.key), { action, key: existing.key, name: existing.name }])
      return
    }
    if (!assetKey.trim() || !name.trim() || !selectedImage || networkKeys.length === 0 || !networkKeys.includes(primaryNetworkKey)) {
      return setFailure(new Error('请填写资产名称、模板和接入网段。'))
    }
    if (action === 'add' && runtime.assets.some((item) => item.key === assetKey.trim())) {
      return setFailure(new Error('资产标识已存在，请改用“替换”。'))
    }
    let parameters: unknown = null
    try { parameters = selectedPackage ? JSON.parse(deviceParameters || '{}') : null }
    catch { return setFailure(new Error('设备参数格式不正确。')) }
    const asset: TeamLabTopologyAsset = {
      key: assetKey.trim(), name: name.trim(), kind: selectedImage.deviceType === 'docker' ? 'docker' : 'vm',
      imageTemplateId: selectedImage.id,
      resources: { cpuUnits, memoryMiB, storageMiB },
      interfaces: networkKeys.map((networkKey, index) => ({
        key: `eth${index}`, networkKey, hostOffset, primary: networkKey === primaryNetworkKey, orderIndex: index,
      })),
      exposePort: null, healthCheck: null, orderIndex: existing ? runtime.assets.indexOf(existing) : runtime.assets.length + changes.length,
      devicePackageId: selectedPackage?.bindingId ?? null, deviceParameters: parameters, connectorId: null,
    }
    setChanges((items) => [...items.filter((item) => item.key !== asset.key), { action, key: asset.key, name: asset.name, asset }])
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

  return <section className={styles.panel} aria-labelledby="asset-composition-title">
    <header className={styles.header}><div><span>资产编排</span><h3 id="asset-composition-title">调整当前运行资产</h3></div></header>
    <p className={styles.hint}>新资产接入当前环境已有网段。提交后沿用当前代次，网络骨架和其他资产保持不变。</p>
    <div className={styles.formGrid}>
      <label>变更方式<select value={action} onChange={(event) => { const value = event.currentTarget.value as Change['action']; setAction(value); if (value !== 'add') selectExisting(runtime.assets[0]?.key ?? '') }}><option value="add">新增资产</option><option value="replace">替换资产</option><option value="remove">移除资产</option></select></label>
      {action === 'add' ? <label>资产标识<input maxLength={64} value={assetKey} onChange={(event) => setAssetKey(event.currentTarget.value)} placeholder="例如 web-02" /></label> : <label>现有资产<select value={assetKey} onChange={(event) => selectExisting(event.currentTarget.value)}><option value="">请选择</option>{runtime.assets.map((asset) => <option key={asset.key} value={asset.key}>{asset.name}</option>)}</select></label>}
      {action !== 'remove' ? <>
        <label>资产名称<input maxLength={128} value={name} onChange={(event) => setName(event.currentTarget.value)} /></label>
        <label>镜像模板<select value={templateId} onChange={(event) => { setTemplateId(Number(event.currentTarget.value)); setDevicePackageId(0); setDeviceParameters('{}') }}>{images.map((image) => <option key={image.id} value={image.id}>{image.name} · {image.deviceType === 'docker' ? '容器' : '虚拟机'}</option>)}</select></label>
        <label>设备模板<select value={devicePackageId} onChange={(event) => {
          const packageId = Number(event.currentTarget.value)
          const selected = packages.find((item) => item.bindingId === packageId)
          const image = selected ? packageImage(selected) : undefined
          setDevicePackageId(packageId)
          setDeviceParameters('{}')
          if (selected && image) {
            setTemplateId(image.id)
            setCpuUnits((value) => Math.max(value, Math.ceil(selected.cpuMillis / 1000)))
            setMemoryMiB((value) => Math.max(value, selected.memoryMiB))
            setStorageMiB((value) => Math.max(value, selected.storageGib * 1024))
          }
        }}><option value={0}>无</option>{packages.map((item) => <option disabled={!packageImage(item)} key={item.id} value={item.bindingId}>{item.displayName} · {item.version}{packageImage(item) ? '' : '（缺少镜像）'}</option>)}</select></label>
        {selectedPackage ? <DeviceParametersEditor schema={selectedPackage.parameterSchema} value={deviceParameters} onChange={setDeviceParameters} /> : null}
        <label>接入网段<select multiple value={networkKeys} onChange={(event) => { const values = [...event.currentTarget.selectedOptions].map((item) => item.value); setNetworkKeys(values); if (!values.includes(primaryNetworkKey)) setPrimaryNetworkKey(values[0] ?? '') }}>{runtime.networks.map((network) => <option key={network.key} value={network.key}>{network.name}</option>)}</select></label>
        <label>主网段<select value={primaryNetworkKey} onChange={(event) => setPrimaryNetworkKey(event.currentTarget.value)}>{networkKeys.map((key) => <option key={key} value={key}>{runtime.networks.find((network) => network.key === key)?.name ?? key}</option>)}</select></label>
        <label>主机地址序号<input min={3} type="number" value={hostOffset} onChange={(event) => setHostOffset(Number(event.currentTarget.value))} /></label>
        <label>CPU 核数<input min={1} type="number" value={cpuUnits} onChange={(event) => setCpuUnits(Number(event.currentTarget.value))} /></label>
        <label>内存 MiB<input min={128} step={128} type="number" value={memoryMiB} onChange={(event) => setMemoryMiB(Number(event.currentTarget.value))} /></label>
        <label>存储 MiB<input min={1024} step={1024} type="number" value={storageMiB} onChange={(event) => setStorageMiB(Number(event.currentTarget.value))} /></label>
      </> : null}
      <div className={styles.formAction}><ActionButton icon={<PackagePlus size={16} />} onClick={stage} type="button">加入本轮变更</ActionButton></div>
    </div>
    {failure ? <InlineFeedback tone="danger">{errorMessage(failure, '资产变更失败。')}</InlineFeedback> : null}
    {changes.length ? <div className={styles.changeList}>{changes.map((item) => <div key={item.key}><span data-action={item.action}>{item.action === 'add' ? '新增' : item.action === 'replace' ? '替换' : '移除'}</span><strong>{item.name}</strong><code>{item.key}</code><button aria-label={`移除 ${item.name}`} onClick={() => setChanges((rows) => rows.filter((row) => row.key !== item.key))} type="button"><Trash2 size={15} /></button></div>)}<ActionButton disabled={busy} icon={<Send size={16} />} onClick={() => void submit()} tone="primary" type="button">提交 {changes.length} 项变更</ActionButton></div> : null}
  </section>
}
