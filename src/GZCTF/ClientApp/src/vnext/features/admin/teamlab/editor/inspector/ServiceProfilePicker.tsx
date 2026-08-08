import { BookOpen, ExternalLink, LoaderCircle, PackageCheck, Search } from 'lucide-react'
import { useMemo, useState } from 'react'
import useSWR from 'swr'
import type { TeamLabBootstrapReference } from '../../api/teamlabContracts'
import {
  teamLabServiceProfileApi,
  teamLabServiceProfileKeys,
  type TeamLabServiceProfileAssetKind,
  type TeamLabServiceProfileSummary,
} from '../../api/teamlabServiceProfileApi'
import { InlineFeedback } from '../../../../../shared/Interaction'
import { errorMessage } from '../../../../../shared/errors'
import styles from './ServiceProfilePicker.module.css'

const assetKindLabels: Record<TeamLabServiceProfileAssetKind, string> = {
  docker: 'Docker 资产',
  vm: '虚拟机',
}

const phaseLabels: Record<string, string> = {
  install: '安装配置',
  provision: '初始化配置',
  verify: '健康检查',
}

const statusLabels: Record<string, string> = {
  published: '已发布',
}

const parameterTypeLabels: Record<string, string> = {
  Boolean: '布尔值',
  Integer: '整数',
  String: '文本',
}

export function ServiceProfilePicker({
  bootstrap,
  assetKind,
  onChange,
  readOnly,
}: {
  bootstrap: TeamLabBootstrapReference
  assetKind?: TeamLabServiceProfileAssetKind
  onChange: (bootstrap: TeamLabBootstrapReference) => void
  readOnly?: boolean
}) {
  const [query, setQuery] = useState('')
  const catalog = useSWR(teamLabServiceProfileKeys.list(), async () => {
    const items: TeamLabServiceProfileSummary[] = []
    let after: string | null = null
    // Bounded paging: a misbehaving nextCursor must not spin the client forever.
    for (let page = 0; page < 50; page += 1) {
      const next = await teamLabServiceProfileApi.list(100, after ?? undefined)
      items.push(...next.items)
      after = next.nextCursor
      if (after === null) break
    }
    return items
  }, { revalidateOnFocus: true })
  const detail = useSWR(
    bootstrap.profileId ? teamLabServiceProfileKeys.detail(bootstrap.profileId, bootstrap.version) : null,
    () => teamLabServiceProfileApi.detail(bootstrap.profileId, bootstrap.version),
    { keepPreviousData: true }
  )

  const profiles = useMemo(() => {
    const compatible = (catalog.data ?? []).filter(
      (profile) => !assetKind || profile.assetKinds.length === 0 || profile.assetKinds.includes(assetKind)
    )
    const keyword = query.trim().toLocaleLowerCase('zh-CN')
    return keyword
      ? compatible.filter(
          (profile) =>
            profile.name.toLocaleLowerCase('zh-CN').includes(keyword) ||
            (profile.description ?? '').toLocaleLowerCase('zh-CN').includes(keyword)
        )
      : compatible
  }, [assetKind, catalog.data, query])

  // The selected profile must stay visible even when the search filter hides it,
  // otherwise the native select shows a blank value that no longer matches.
  const selectedProfile = (catalog.data ?? []).find((item) => item.id === bootstrap.profileId)
  const profileRemoved = Boolean(bootstrap.profileId && !selectedProfile)
  const visibleProfiles =
    selectedProfile && !profiles.some((profile) => profile.id === selectedProfile.id)
      ? [selectedProfile, ...profiles]
      : profiles

  const parameterValues = bootstrap.parameters
  const updateParameter = (key: string, value: string) => {
    const next = value ? { ...parameterValues, [key]: value } : { ...parameterValues }
    if (!value) delete next[key]
    onChange({ ...bootstrap, parameters: next })
  }
  const profileDetail = detail.data
  // Guard against a stale detail being written back while a different profile is
  // already selected (SWR keepPreviousData keeps the previous detail alive briefly).
  const detailMatchesSelection = Boolean(profileDetail && profileDetail.id === bootstrap.profileId)
  const detailLoading = Boolean(bootstrap.profileId) && !detailMatchesSelection
  const availableVersions = selectedProfile?.availableVersions ?? profileDetail?.availableVersions ?? [bootstrap.version]

  return (
    <div className={styles.picker}>
      <div className={styles.searchRow}>
        <Search aria-hidden="true" size={15} />
        <input
          aria-label="搜索服务目录"
          disabled={readOnly}
          onChange={(event) => setQuery(event.target.value)}
          placeholder="按名称或用途搜索服务目录"
          value={query}
        />
      </div>
      {catalog.error ? (
        <InlineFeedback tone="danger">{errorMessage(catalog.error, '服务目录加载失败。')}</InlineFeedback>
      ) : null}
      <label className={styles.field}>
        <span>服务目录条目</span>
        <select
          disabled={readOnly || Boolean(catalog.error) || (catalog.isLoading && !catalog.data)}
          onChange={(event) => {
            const profile = (catalog.data ?? []).find((item) => item.id === event.target.value)
            if (!profile) return
            onChange({ profileId: profile.id, version: profile.version, parameters: {} })
          }}
          value={bootstrap.profileId}
        >
          {catalog.isLoading && !catalog.data ? <option value="">正在加载服务目录...</option> : null}
          {!catalog.isLoading && !bootstrap.profileId && !profileRemoved && visibleProfiles.length > 0 ? <option value="">请选择服务配置</option> : null}
          {visibleProfiles.length === 0 && !catalog.isLoading ? <option value="">目录中暂无匹配的服务</option> : null}
          {profileRemoved ? <option value={bootstrap.profileId}>（已从目录移除）</option> : null}
          {visibleProfiles.map((profile) => (
            <option key={profile.id} value={profile.id}>
              {profile.name} (v{profile.version})
            </option>
          ))}
        </select>
      </label>
      {profileRemoved ? (
        <InlineFeedback tone="danger">当前引用的服务目录条目已移除或已下架，请重新选择。</InlineFeedback>
      ) : null}

      {bootstrap.profileId ? (
        <label className={styles.field}>
          <span>服务版本</span>
          <select
            aria-label="服务版本"
            disabled={readOnly || detailLoading || availableVersions.length === 0}
            onChange={(event) => onChange({ ...bootstrap, version: Number(event.target.value), parameters: {} })}
            value={bootstrap.version}
          >
            {availableVersions.includes(bootstrap.version) ? null : <option value={bootstrap.version}>当前引用的版本 v{bootstrap.version}</option>}
            {availableVersions.map((version) => <option key={version} value={version}>版本 v{version}</option>)}
          </select>
        </label>
      ) : null}

      {detail.error ? (
        <InlineFeedback tone="danger">{errorMessage(detail.error, '服务目录详情加载失败。')}</InlineFeedback>
      ) : detailLoading ? (
        <div className={styles.loading}><LoaderCircle aria-hidden="true" className={styles.spin} size={16} />正在读取服务目录详情...</div>
      ) : profileDetail ? (
        <dl className={styles.detail}>
          <div><dt>用途</dt><dd>{profileDetail.description || '未提供用途说明。'}</dd></div>
          <div>
            <dt>支持的资产类型</dt>
            <dd>{profileDetail.assetKinds.length ? profileDetail.assetKinds.map((kind) => assetKindLabels[kind]).join('、') : '不限制'}</dd>
          </div>
          <div><dt>执行阶段</dt><dd>{phaseLabels[profileDetail.execution.phase] ?? '已配置'} · {profileDetail.execution.steps} 个步骤 · {profileDetail.execution.healthChecks} 项健康检查{profileDetail.execution.maxReboots ? ` · 最多重启 ${profileDetail.execution.maxReboots} 次` : ''}</dd></div>
          <div><dt>发布状态</dt><dd>{statusLabels[profileDetail.status] ?? '状态未知'} · 版本 v{profileDetail.version}</dd></div>
          {profileDetail.documentationUrl ? (
            <div>
              <dt>文档</dt>
              <dd>
                <a href={profileDetail.documentationUrl} rel="noopener noreferrer" target="_blank">
                  <BookOpen size={14} />
                  查看文档
                  <ExternalLink aria-hidden="true" size={12} />
                </a>
              </dd>
            </div>
          ) : null}
        </dl>
      ) : null}

      {profileDetail?.parameters.length && detailMatchesSelection ? (
        <div className={styles.parameters}>
          <div className={styles.inlineHeading}>
            <strong>公开参数</strong>
            <small>敏感值由运行时安全参数提供，不在此处填写</small>
          </div>
          {profileDetail.parameters.map((parameter) => (
            <label className={styles.parameter} key={parameter.key}>
              <span>
                <strong>{parameter.key}</strong>
                <small>
                  <code>{parameterTypeLabels[parameter.type] ?? parameter.type}</code>
                  {parameter.required ? ' · 必填' : ''}
                  {parameter.secret ? ' · 敏感参数' : parameter.defaultValue != null ? ` · 默认 ${parameter.defaultValue}` : ''}
                </small>
              </span>
              {parameter.secret ? (
                <input
                  aria-label={`参数 ${parameter.key}（敏感参数）`}
                  disabled
                  readOnly
                  value="由运行时安全参数提供"
                />
              ) : (
                <input
                  aria-label={`参数 ${parameter.key}`}
                  disabled={readOnly}
                  onChange={(event) => updateParameter(parameter.key, event.target.value)}
                  placeholder={parameter.defaultValue ?? '输入值'}
                  value={parameterValues[parameter.key] ?? ''}
                />
              )}
            </label>
          ))}
        </div>
      ) : null}

      {bootstrap.profileId ? (
        <p className={styles.reference}>
          <PackageCheck aria-hidden="true" size={14} />
          已引用服务配置 <code>{bootstrap.profileId.slice(0, 8)}</code> · 版本 v{bootstrap.version}
        </p>
      ) : null}
    </div>
  )
}
