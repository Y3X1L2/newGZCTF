import { Save } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import { ActionButton, InlineFeedback, VNextDrawer } from '../../../shared/Interaction'
import { AwdpService } from '../../awdp/awdpDomain'
import styles from './AdminAwdp.module.css'
import { AwdpServiceDraft, awdpServiceDraft, awdpServiceWarnings, validateAwdpService } from './awdpServiceForm'

type ImageOption = { id: number; name: string; registryUrl: string }

function NumberField({
  label,
  field,
  draft,
  update,
  min = 0,
  max,
}: {
  label: string
  field: keyof AwdpServiceDraft
  draft: AwdpServiceDraft
  update: (field: keyof AwdpServiceDraft, value: string | number) => void
  min?: number
  max?: number
}) {
  return (
    <label className={styles.field}>
      <span>{label}</span>
      <input
        max={max}
        min={min}
        onChange={(event) => update(field, Number(event.currentTarget.value))}
        step="1"
        type="number"
        value={draft[field]}
      />
    </label>
  )
}

export function AwdpServiceDrawer({
  open,
  service,
  images,
  saving,
  onClose,
  onSave,
}: {
  open: boolean
  service: AwdpService | null
  images: ImageOption[]
  saving: boolean
  onClose: () => void
  onSave: (serviceId: number | null, draft: AwdpServiceDraft) => Promise<boolean>
}) {
  const [draft, setDraft] = useState<AwdpServiceDraft>(() => awdpServiceDraft(service))
  const [errors, setErrors] = useState<string[]>([])
  const warnings = useMemo(() => awdpServiceWarnings(draft), [draft])

  useEffect(() => {
    if (!open) return
    setDraft(awdpServiceDraft(service))
    setErrors([])
  }, [open, service])

  const update = (field: keyof AwdpServiceDraft, value: string | number) =>
    setDraft((current) => ({ ...current, [field]: value }))
  const save = async () => {
    const nextErrors = validateAwdpService(draft)
    setErrors(nextErrors)
    if (nextErrors.length) return false
    const success = await onSave(service?.id ?? null, draft)
    if (success) onClose()
    return success
  }

  return (
    <VNextDrawer
      description="保存服务草稿后仍需在启动前补齐 Checker 和 Exp 配置。"
      eyebrow="AWDP SERVICE"
      footer={
        <>
          <ActionButton disabled={saving} onClick={onClose} type="button">
            取消
          </ActionButton>
          <ActionButton
            disabled={saving}
            icon={<Save size={16} />}
            onClick={() => void save()}
            tone="primary"
            type="button"
          >
            {saving ? '正在保存' : '保存服务'}
          </ActionButton>
        </>
      }
      onClose={onClose}
      open={open}
      title={service ? `编辑 ${service.name}` : '创建 AWDP 服务'}
    >
      <div className={styles.drawerStack}>
        {errors.length ? <InlineFeedback tone="danger">{errors.join(' ')}</InlineFeedback> : null}
        {warnings.length ? (
          <InlineFeedback>{warnings.join('；')}。服务可以保存，但启动前必须补齐。</InlineFeedback>
        ) : null}
        <section className={styles.drawerSection}>
          <header>
            <h3>基础环境</h3>
            <p>镜像字段应填写节点可拉取的 Registry 地址。</p>
          </header>
          <div className={styles.fieldGrid}>
            <label className={styles.field}>
              <span>服务名称</span>
              <input onChange={(event) => update('name', event.currentTarget.value)} value={draft.name} />
            </label>
            <label className={styles.field}>
              <span>容器镜像</span>
              <input
                list="awdp-ready-images"
                onChange={(event) => update('imageName', event.currentTarget.value)}
                placeholder="registry.example/namespace/image:tag"
                value={draft.imageName}
              />
              <datalist id="awdp-ready-images">
                {images.map((image) => (
                  <option key={image.id} value={image.registryUrl}>
                    {image.name}
                  </option>
                ))}
              </datalist>
            </label>
            <NumberField draft={draft} field="exposePort" label="服务端口" max={65_535} min={1} update={update} />
            <NumberField draft={draft} field="originalScore" label="原始分数" min={1} update={update} />
          </div>
        </section>
        <section className={styles.drawerSection}>
          <header>
            <h3>可用性与漏洞验证</h3>
            <p>Checker 检查服务可用性，Exp 验证漏洞是否仍可利用。</p>
          </header>
          <label className={styles.field}>
            <span>Checker 入口命令</span>
            <input
              onChange={(event) => update('checkerEntrypoint', event.currentTarget.value)}
              value={draft.checkerEntrypoint}
            />
          </label>
          <label className={styles.field}>
            <span>Checker 脚本</span>
            <textarea
              onChange={(event) => update('checkerScript', event.currentTarget.value)}
              rows={8}
              value={draft.checkerScript}
            />
          </label>
          <label className={styles.field}>
            <span>Exp 入口命令</span>
            <input
              onChange={(event) => update('expEntrypoint', event.currentTarget.value)}
              value={draft.expEntrypoint}
            />
          </label>
          <label className={styles.field}>
            <span>Exp 脚本</span>
            <textarea
              onChange={(event) => update('expScript', event.currentTarget.value)}
              rows={8}
              value={draft.expScript}
            />
          </label>
        </section>
        <section className={styles.drawerSection}>
          <header>
            <h3>计分规则</h3>
            <p>所有分值均由后端在轮次结算时执行。</p>
          </header>
          <div className={styles.fieldGrid}>
            <NumberField draft={draft} field="attackPoints" label="攻击得分" min={1} update={update} />
            <NumberField draft={draft} field="slaPoints" label="SLA 得分" min={1} update={update} />
            <NumberField draft={draft} field="patchPoints" label="修补得分" min={1} update={update} />
            <NumberField draft={draft} field="serviceAbnormalPenalty" label="服务异常扣分" min={1} update={update} />
          </div>
        </section>
        <section className={styles.drawerSection}>
          <header>
            <h3>轮次与操作限制</h3>
            <p>服务级轮次参数将用于 AWDP 当前比赛。</p>
          </header>
          <div className={styles.fieldGrid}>
            <NumberField draft={draft} field="maxAttackPerRound" label="每轮最大攻击次数" min={1} update={update} />
            <NumberField draft={draft} field="totalRounds" label="总轮数" min={1} update={update} />
            <NumberField draft={draft} field="attackPhaseMinutes" label="攻击阶段（分钟）" min={1} update={update} />
            <NumberField draft={draft} field="patchPhaseMinutes" label="修补阶段（分钟）" min={1} update={update} />
            <NumberField draft={draft} field="maxResetCount" label="最大重置次数" update={update} />
            <NumberField draft={draft} field="maxRecoveryCount" label="最大恢复次数" update={update} />
          </div>
        </section>
      </div>
    </VNextDrawer>
  )
}
