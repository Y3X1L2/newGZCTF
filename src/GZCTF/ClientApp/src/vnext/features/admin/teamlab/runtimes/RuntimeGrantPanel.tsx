import { Plus, Trash2 } from 'lucide-react'
import { useEffect, useState } from 'react'
import { ActionButton, InlineFeedback } from '../../../../shared/Interaction'
import { DataState } from '../../../../shared/Primitives'
import { errorMessage } from '../../../../shared/errors'
import { commonAdminApi } from '../../api'
import { teamLabRuntimeApi } from '../api'
import type { TeamLabGrantSubjectOption, TeamLabRuntime, TeamLabRuntimeGrant, TeamLabRuntimePermission } from '../api'
import styles from './RuntimeComposition.module.css'

const permissionOptions: readonly { value: TeamLabRuntimePermission; label: string }[] = [
  { value: 'StateRead', label: '查看状态' }, { value: 'MetadataRead', label: '查看完整信息' },
  { value: 'RemoteSessionOperate', label: '远程会话' }, { value: 'FileTransfer', label: '文件管理' },
  { value: 'AssetOperate', label: '资产启停' }, { value: 'AssetCompose', label: '资产编排' },
  { value: 'ServiceAccessManage', label: '服务开放' }, { value: 'RuntimeManage', label: '环境管理' },
]

export function RuntimeGrantPanel({ runtime }: { runtime: TeamLabRuntime }) {
  const [grants, setGrants] = useState<readonly TeamLabRuntimeGrant[] | null>(null)
  const [subjectType, setSubjectType] = useState<TeamLabRuntimeGrant['subjectType']>('user')
  const [subjectId, setSubjectId] = useState('')
  const [users, setUsers] = useState<readonly TeamLabGrantSubjectOption[]>([])
  const [tokens, setTokens] = useState<readonly TeamLabGrantSubjectOption[]>([])
  const [assetKey, setAssetKey] = useState('')
  const [permissions, setPermissions] = useState<TeamLabRuntimePermission[]>(['StateRead'])
  const [busy, setBusy] = useState(false)
  const [failure, setFailure] = useState<unknown>(null)

  const load = async () => {
    try {
      const [runtimeGrants, userPage, tokenOptions] = await Promise.all([
        teamLabRuntimeApi.listGrants(runtime.id),
        commonAdminApi.users({ page: 1, pageSize: 100 }),
        teamLabRuntimeApi.listGrantTokens(),
      ])
      setGrants(runtimeGrants)
      setUsers(userPage.items.flatMap((user) => user.id ? [{ id: user.id, name: user.userName || user.email || user.id }] : []))
      setTokens(tokenOptions)
      setFailure(null)
    }
    catch (error) { setFailure(error) }
  }
  useEffect(() => { void load() }, [runtime.id])

  const save = async (next: readonly TeamLabRuntimeGrant[]) => {
    setBusy(true)
    setFailure(null)
    try {
      setGrants(await teamLabRuntimeApi.replaceGrants(runtime.id, { grants: next.map((grant) => ({
        subjectType: grant.subjectType, subjectId: grant.subjectId, assetKey: grant.assetKey, permissions: grant.permissions,
      })) }))
    } catch (error) { setFailure(error) }
    finally { setBusy(false) }
  }

  const add = async () => {
    if (!grants || !subjectId.trim() || permissions.length === 0) return
    const subject = (subjectType === 'user' ? users : tokens).find((item) => item.id === subjectId)
    if (!subject) return
    await save([...grants, {
      id: 0, subjectType, subjectId, subjectName: subject.name, assetKey: assetKey || null,
      permissions, updatedAt: Date.now(),
    }])
    setSubjectId('')
  }

  return <section className={styles.panel} aria-labelledby="runtime-grants-title">
    <header className={styles.header}><div><span>访问权限</span><h3 id="runtime-grants-title">运行环境授权</h3></div></header>
    <p className={styles.hint}>授权可覆盖整个运行环境，也可只限定到一个资产。文件管理与远程会话分别控制。</p>
    {failure ? <InlineFeedback tone="danger">{errorMessage(failure, '运行环境授权加载失败。')}</InlineFeedback> : null}
    {grants === null && !failure ? <DataState description="正在读取用户和 Token 授权。" loading title="授权加载中" /> : null}
    {grants?.length ? <div className={styles.grantList}>{grants.map((grant, index) => <div key={`${grant.subjectType}:${grant.subjectId}:${grant.assetKey ?? '*'}:${index}`}><span>{grant.subjectType === 'user' ? '用户' : 'API Token'}</span><strong>{grant.subjectName}</strong><small>{grant.assetKey ? runtime.assets.find((asset) => asset.key === grant.assetKey)?.name ?? grant.assetKey : '整个运行环境'}</small><p>{grant.permissions.map((permission) => permissionOptions.find((item) => item.value === permission)?.label ?? permission).join('、')}</p><button aria-label={`删除 ${grant.subjectName} 的授权`} disabled={busy} onClick={() => void save(grants.filter((_, row) => row !== index))} type="button"><Trash2 size={15} /></button></div>)}</div> : grants ? <DataState description="可在下方为用户或 API Token 添加权限。" title="当前没有额外授权" /> : null}
    <div className={styles.grantForm}>
      <label>授权对象<select value={subjectType} onChange={(event) => { setSubjectType(event.currentTarget.value as TeamLabRuntimeGrant['subjectType']); setSubjectId('') }}><option value="user">用户</option><option value="apiToken">API Token</option></select></label>
      <label>{subjectType === 'user' ? '用户' : 'API Token'}<select value={subjectId} onChange={(event) => setSubjectId(event.currentTarget.value)}><option value="">请选择</option>{(subjectType === 'user' ? users : tokens).map((item) => <option key={item.id} value={item.id}>{item.name}</option>)}</select></label>
      <label>资产范围<select value={assetKey} onChange={(event) => setAssetKey(event.currentTarget.value)}><option value="">整个运行环境</option>{runtime.assets.map((asset) => <option key={asset.key} value={asset.key}>{asset.name}</option>)}</select></label>
      <fieldset><legend>允许操作</legend>{permissionOptions.map((item) => <label key={item.value}><input checked={permissions.includes(item.value)} onChange={(event) => setPermissions((values) => event.currentTarget.checked ? [...values, item.value] : values.filter((value) => value !== item.value))} type="checkbox" />{item.label}</label>)}</fieldset>
      <ActionButton disabled={busy || !subjectId.trim() || permissions.length === 0} icon={<Plus size={16} />} onClick={() => void add()} tone="primary" type="button">添加授权</ActionButton>
    </div>
  </section>
}
