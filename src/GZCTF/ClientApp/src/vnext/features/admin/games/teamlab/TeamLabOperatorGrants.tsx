import { Search, ShieldCheck, Trash2, UserPlus } from 'lucide-react'
import { useMemo, useState } from 'react'
import useSWR from 'swr'
import type { UserInfoModel } from '@Api'
import { commonAdminApi } from '../../api'
import { teamLabGameAdminApi, teamLabGameAdminKeys, type TeamLabOperatorGrant } from '../../api/teamlabGameAdminApi'
import { ActionButton, InlineFeedback } from '../../../../shared/Interaction'
import { errorMessage } from '../../../../shared/errors'
import { StatusBadge } from '../../shared/AdminWorkbench'
import styles from './TeamLabGame.module.css'

type GrantLevel = 'view' | 'operate'

function formatIdentity(grant: TeamLabOperatorGrant) {
  return grant.displayName ? `${grant.displayName} (${grant.userName})` : grant.userName
}

export function TeamLabOperatorGrants({ gameId }: { gameId: number }) {
  const grantsRequest = useSWR(teamLabGameAdminKeys.operators(gameId), () => teamLabGameAdminApi.operators(gameId))
  const [query, setQuery] = useState('')
  const [candidates, setCandidates] = useState<UserInfoModel[]>([])
  const [searching, setSearching] = useState(false)
  const [savingId, setSavingId] = useState<string | null>(null)
  const [failure, setFailure] = useState<string | null>(null)
  const grantedIds = useMemo(() => new Set((grantsRequest.data ?? []).map((grant) => grant.userId)), [grantsRequest.data])

  const search = async () => {
    if (query.trim().length < 2 || searching) return
    setSearching(true)
    setFailure(null)
    try {
      const result = await commonAdminApi.users({ keyword: query.trim(), pageSize: 20 })
      setCandidates(result.items.filter((user) => Boolean(user.id && !grantedIds.has(user.id))))
    } catch (error) {
      setFailure(errorMessage(error, '搜索用户失败。'))
    } finally {
      setSearching(false)
    }
  }

  const save = async (userId: string, level: GrantLevel) => {
    if (savingId) return
    setSavingId(userId)
    setFailure(null)
    try {
      await teamLabGameAdminApi.setOperator(gameId, userId, { viewAssets: true, operateAssets: level === 'operate' })
      setCandidates((current) => current.filter((item) => item.id !== userId))
      await grantsRequest.mutate()
    } catch (error) {
      setFailure(errorMessage(error, '保存运维授权失败。'))
    } finally {
      setSavingId(null)
    }
  }

  const remove = async (userId: string) => {
    if (savingId) return
    setSavingId(userId)
    setFailure(null)
    try {
      await teamLabGameAdminApi.deleteOperator(gameId, userId)
      await grantsRequest.mutate()
    } catch (error) {
      setFailure(errorMessage(error, '撤销运维授权失败。'))
    } finally {
      setSavingId(null)
    }
  }

  return (
    <section className={styles.operatorSection}>
      <header className={styles.sectionHeader}>
        <div><span>运维访问</span><h2>资产运维授权</h2></div>
        <ShieldCheck aria-hidden="true" size={18} />
      </header>
      <p className={styles.operatorDescription}>管理员和比赛所有者始终可以访问资产。此处仅为其他人员授予查看资产或进入远程运维的权限。</p>
      <div className={styles.operatorSearch}>
        <label><Search aria-hidden="true" size={16} /><input aria-label="搜索需要授权的用户" onChange={(event) => setQuery(event.currentTarget.value)} onKeyDown={(event) => { if (event.key === 'Enter') void search() }} placeholder="用户名、姓名、邮箱或用户编号" type="search" value={query} /></label>
        <ActionButton disabled={query.trim().length < 2 || searching} onClick={() => void search()} type="button">{searching ? '搜索中' : '搜索'}</ActionButton>
      </div>
      {failure ? <InlineFeedback tone="danger">{failure}</InlineFeedback> : null}
      {grantsRequest.error ? <InlineFeedback tone="danger">{errorMessage(grantsRequest.error, '读取运维授权失败。')}</InlineFeedback> : null}
      {candidates.length ? <ul className={styles.operatorCandidates}>{candidates.map((user) => <li key={user.id}><div><strong>{user.realName || user.userName}</strong><small>{user.userName}{user.email ? ` · ${user.email}` : ''}</small></div><div><ActionButton disabled={savingId === user.id} icon={<UserPlus size={15} />} onClick={() => user.id && void save(user.id, 'view')} type="button">仅查看</ActionButton><ActionButton disabled={savingId === user.id} icon={<ShieldCheck size={15} />} onClick={() => user.id && void save(user.id, 'operate')} tone="primary" type="button">可进入运维</ActionButton></div></li>)}</ul> : null}
      {grantsRequest.data?.length ? <ul className={styles.operatorList}>{grantsRequest.data.map((grant) => <li key={grant.userId}><div><strong>{formatIdentity(grant)}</strong><small>最近更新：{new Date(grant.updatedAt).toLocaleString()}</small></div><div><StatusBadge tone={grant.operateAssets ? 'success' : 'neutral'}>{grant.operateAssets ? '可进入运维' : '仅查看'}</StatusBadge><ActionButton disabled={savingId === grant.userId} icon={<Trash2 size={15} />} onClick={() => void remove(grant.userId)} tone="danger" type="button">撤销</ActionButton></div></li>)}</ul> : <p className={styles.operatorEmpty}>尚未额外授予其他用户访问权限。</p>}
    </section>
  )
}
