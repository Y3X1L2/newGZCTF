import { Plus } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import { ChallengeCategory, ChallengeType, EnvironmentType } from '@Api'
import { SelectField, TextField } from '../../../../shared/FormControls'
import { ActionButton, InlineFeedback, VNextDialog } from '../../../../shared/Interaction'
import { errorMessage } from '../../../../shared/errors'
import { gameAdminApi, type ImageTemplateSummary } from '../../api'
import {
  challengeEnvironmentLabel,
  challengeTypeLabel,
  isContainerChallenge,
  templateAvailableForEnvironment,
} from '../gamePresentation'
import styles from '../GameDialogs.module.css'

interface ChallengeCreateDraft {
  title: string
  category: ChallengeCategory
  type: ChallengeType
  environment: EnvironmentType
  templateId: number | null
  exposePort: number
}

const emptyDraft = (): ChallengeCreateDraft => ({
  title: '',
  category: ChallengeCategory.Web,
  type: ChallengeType.StaticAttachment,
  environment: EnvironmentType.None,
  templateId: null,
  exposePort: 80,
})

export function ChallengeCreateDialog({
  gameId,
  templates,
  open,
  onClose,
  onCreated,
}: {
  gameId: number
  templates: ImageTemplateSummary[]
  open: boolean
  onClose: () => void
  onCreated: (challengeId: number) => void
}) {
  const [draft, setDraft] = useState<ChallengeCreateDraft>(emptyDraft)
  const [saving, setSaving] = useState(false)
  const [failure, setFailure] = useState<string | null>(null)

  useEffect(() => {
    if (!open) return
    setDraft(emptyDraft())
    setFailure(null)
  }, [open])

  const availableTemplates = useMemo(
    () => templates.filter((template) => templateAvailableForEnvironment(template, draft.environment)),
    [draft.environment, templates]
  )
  const selectedTemplate = templates.find((template) => template.id === draft.templateId)
  const container = isContainerChallenge(draft.type)

  const setType = (type: ChallengeType) => {
    const nextContainer = isContainerChallenge(type)
    setDraft((current) => ({
      ...current,
      type,
      environment: nextContainer ? EnvironmentType.Docker : EnvironmentType.None,
      templateId: null,
    }))
  }

  const create = async () => {
    if (!draft.title.trim()) {
      setFailure('请输入题目名称。')
      return false
    }
    if (container && !selectedTemplate) {
      setFailure('请选择一个已就绪的运行环境模板。')
      return false
    }
    setSaving(true)
    setFailure(null)
    try {
      const challenge = await gameAdminApi.createChallenge(gameId, {
        title: draft.title.trim(),
        category: draft.category,
        type: draft.type,
        environment: container ? draft.environment : EnvironmentType.None,
        imageTemplateId: container ? selectedTemplate?.id : null,
        containerImage: draft.environment === EnvironmentType.Docker ? selectedTemplate?.registryUrl : null,
        exposePort: draft.environment === EnvironmentType.Docker ? Math.max(1, draft.exposePort) : null,
        isEnabled: false,
      })
      if (!challenge.id) throw new Error('题目已创建，但服务器没有返回题目编号。')
      onCreated(challenge.id)
      return true
    } catch (requestError) {
      setFailure(errorMessage(requestError, '题目创建失败。'))
      return false
    } finally {
      setSaving(false)
    }
  }

  return (
    <VNextDialog
      description="先确定题型和运行环境，创建后进入完整题目工作台维护内容、附件和 Flag。"
      eyebrow="CHALLENGE CREATION"
      footer={
        <>
          <ActionButton disabled={saving} onClick={onClose} type="button">取消</ActionButton>
          <ActionButton disabled={saving} icon={<Plus size={16} />} onClick={() => void create()} tone="primary" type="button">{saving ? '正在创建' : '创建并编辑'}</ActionButton>
        </>
      }
      onClose={onClose}
      open={open}
      title="新建 CTF 题目"
      wide
    >
      <div className={styles.stack}>
        {failure ? <InlineFeedback tone="danger">{failure}</InlineFeedback> : null}
        <div className={styles.fieldGrid}>
          <TextField autoFocus label="题目名称" maxLength={128} onValueChange={(value) => setDraft((current) => ({ ...current, title: value }))} required value={draft.title} />
          <SelectField label="题目分类" onValueChange={(value) => setDraft((current) => ({ ...current, category: value as ChallengeCategory }))} value={draft.category}>
            {Object.values(ChallengeCategory).map((category) => <option key={category} value={category}>{category}</option>)}
          </SelectField>
          <SelectField label="题目类型" onValueChange={(value) => setType(value as ChallengeType)} value={draft.type}>
            {Object.values(ChallengeType).map((type) => <option key={type} value={type}>{challengeTypeLabel(type)}</option>)}
          </SelectField>
          {container ? (
            <SelectField label="运行环境" onValueChange={(value) => setDraft((current) => ({ ...current, environment: value as EnvironmentType, templateId: null }))} value={draft.environment}>
              <option value={EnvironmentType.Docker}>{challengeEnvironmentLabel(EnvironmentType.Docker)}</option>
              <option value={EnvironmentType.WindowsVM}>{challengeEnvironmentLabel(EnvironmentType.WindowsVM)}</option>
            </SelectField>
          ) : null}
          {container ? (
            <SelectField hint="只显示已就绪且与运行环境匹配的模板。" label="环境模板" onValueChange={(value) => setDraft((current) => ({ ...current, templateId: value ? Number(value) : null }))} required value={draft.templateId ?? ''}>
              <option value="">请选择模板</option>
              {availableTemplates.map((template) => <option key={template.id} value={template.id}>#{template.id} {template.name}</option>)}
            </SelectField>
          ) : null}
          {draft.environment === EnvironmentType.Docker ? (
            <TextField label="暴露端口" max={65535} min={1} onValueChange={(value) => setDraft((current) => ({ ...current, exposePort: Number(value) }))} type="number" value={draft.exposePort} />
          ) : null}
        </div>
      </div>
    </VNextDialog>
  )
}
