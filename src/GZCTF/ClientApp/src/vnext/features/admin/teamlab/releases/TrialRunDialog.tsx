import { useEffect, useMemo, useState } from 'react'
import { ActionButton, VNextDialog } from '../../../../shared/Interaction'
import type { TeamLabRelease, TeamLabRuntimeOverlay } from '../api'
import styles from './TrialRunDialog.module.css'

export function createTrialIdempotencyKey(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function')
    return `teamlab-trial-${crypto.randomUUID()}`

  if (typeof crypto !== 'undefined' && typeof crypto.getRandomValues === 'function') {
    const bytes = new Uint8Array(16)
    crypto.getRandomValues(bytes)
    return `teamlab-trial-${Array.from(bytes, (byte) => byte.toString(16).padStart(2, '0')).join('')}`
  }

  return `teamlab-trial-${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}`
}

export function TrialRunDialog({
  release,
  requiredRuntimeSecrets,
  submitting = false,
  open,
  onClose,
  onConfirm,
}: {
  release: TeamLabRelease | null
  requiredRuntimeSecrets: readonly { assetKey: string; assetName: string; parameterKey: string }[]
  submitting?: boolean
  open: boolean
  onClose: () => void
  onConfirm: (overlays: readonly TeamLabRuntimeOverlay[] | null) => Promise<boolean>
}) {
  const [values, setValues] = useState<Record<string, string>>({})
  const requirementKey = useMemo(
    () => requiredRuntimeSecrets.map((item) => `${item.assetKey}:${item.parameterKey}`).join('|'),
    [requiredRuntimeSecrets]
  )
  useEffect(() => {
    if (!open) setValues({})
  }, [open, requirementKey, release?.id])

  const fieldKey = (assetKey: string, parameterKey: string) => `${assetKey}:${parameterKey}`
  const missing = requiredRuntimeSecrets.filter((item) => !values[fieldKey(item.assetKey, item.parameterKey)]?.trim())
  const submit = async () => {
    if (missing.length > 0) return false
    const secretsByAsset = new Map<string, Record<string, string>>()
    for (const requirement of requiredRuntimeSecrets) {
      const secrets = secretsByAsset.get(requirement.assetKey) ?? {}
      secrets[requirement.parameterKey] = values[fieldKey(requirement.assetKey, requirement.parameterKey)]
      secretsByAsset.set(requirement.assetKey, secrets)
    }
    const overlays = [...secretsByAsset.entries()].map(([assetKey, secrets]) => ({
      assetKey,
      environment: null,
      secrets,
    }))
    const succeeded = await onConfirm(overlays.length ? overlays : null)
    if (succeeded) setValues({})
    return succeeded
  }

  return (
    <VNextDialog
      description="平台将按服务端执行计划预留资源、分发镜像并启动该不可变版本。"
      eyebrow="TRIAL RUNTIME"
      footer={
        <>
          <ActionButton disabled={submitting} onClick={onClose} type="button">取消</ActionButton>
          <ActionButton disabled={missing.length > 0 || submitting} onClick={() => void submit()} tone="primary" type="button">
            {submitting ? '正在创建' : '创建试运行'}
          </ActionButton>
        </>
      }
      onClose={onClose}
      open={open && release !== null}
      title="启动 TeamLab 试运行？"
    >
      <div className={styles.content}>
        <p>{release ? `为发布版本 v${release.version} 创建一套独立试运行环境。` : ''}</p>
        {requiredRuntimeSecrets.length ? (
          <section className={styles.requirements} aria-labelledby="trial-runtime-secrets-title">
            <div>
              <strong id="trial-runtime-secrets-title">运行时密钥</strong>
              <p>这些值仅用于本次试运行，提交后不会保存到场景、发布版本或浏览器缓存中。</p>
            </div>
            {requiredRuntimeSecrets.map((requirement) => {
              const key = fieldKey(requirement.assetKey, requirement.parameterKey)
              return (
                <label className={styles.secretField} key={key}>
                  <span>{requirement.assetName} · <code>{requirement.parameterKey}</code>（必填）</span>
                  <input
                    aria-label={`${requirement.assetName} 的运行时密钥 ${requirement.parameterKey}`}
                    autoComplete="new-password"
                    onChange={(event) => setValues((current) => ({ ...current, [key]: event.target.value }))}
                    type="password"
                    value={values[key] ?? ''}
                  />
                </label>
              )
            })}
          </section>
        ) : null}
      </div>
    </VNextDialog>
  )
}
