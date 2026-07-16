import { ArrowLeft, Save, Trash2 } from 'lucide-react'
import { FormEvent, useEffect, useMemo, useState } from 'react'
import { useNavigate, useOutletContext, useParams } from 'react-router'
import { ChallengeCategory, ContainerStatus, EnvironmentType, NetworkMode } from '@Api'
import { SelectField, TextAreaField, TextField, ToggleField } from '../../../../shared/FormControls'
import { ActionButton, InlineFeedback, VNextConfirmDialog } from '../../../../shared/Interaction'
import { MarkdownContent } from '../../../../shared/MarkdownContent'
import { DataState } from '../../../../shared/Primitives'
import { errorMessage } from '../../../../shared/errors'
import { useVNextPageTitle } from '../../../../shared/useVNextPageTitle'
import { gameAdminApi } from '../../api'
import { useAdminImages } from '../../images/useAdminImages'
import { AdminEditorActionBar, AdminEditorSection, AdminPageHeader, StatusBadge } from '../../shared/AdminWorkbench'
import type { GameAdminOutletContext } from '../GameAdminShell'
import {
  challengeConfigurationIssues,
  challengeTypeLabel,
  isContainerChallenge,
  templateAvailableForEnvironment,
} from '../gamePresentation'
import { useAdminGameChallenge } from '../useAdminGames'
import styles from './AdminChallengeEditorPage.module.css'
import { AdminChallengeRuntimePanel, RuntimeOperation } from './AdminChallengeRuntimePanel'
import { ChallengeAttachmentPanel } from './ChallengeAttachmentPanel'
import { ChallengeFlagPanel, type ChallengeEditorFeedback } from './ChallengeFlagPanel'
import {
  challengeEditorDraft,
  challengeUpdatePayload,
  type ChallengeEditorDraft,
  validateChallengeEditorDraft,
} from './challengeEditorModel'

export function AdminChallengeEditorPage() {
  const navigate = useNavigate()
  const { challengeId } = useParams()
  const { game } = useOutletContext<GameAdminOutletContext>()
  const gameId = game.id as number
  const id = Number(challengeId)
  const [runtimeOperation, setRuntimeOperation] = useState<RuntimeOperation | null>(null)
  const challengeRequest = useAdminGameChallenge(gameId, id, Boolean(runtimeOperation))
  const imagesRequest = useAdminImages({})
  const [draft, setDraft] = useState<ChallengeEditorDraft | null>(null)
  const [baseline, setBaseline] = useState<ChallengeEditorDraft | null>(null)
  const [loadedId, setLoadedId] = useState<number | null>(null)
  const [saving, setSaving] = useState(false)
  const [deleting, setDeleting] = useState(false)
  const [deleteOpen, setDeleteOpen] = useState(false)
  const [feedback, setFeedback] = useState<{ tone: 'success' | 'danger' | 'neutral'; message: string } | null>(null)
  const challenge = challengeRequest.challenge

  useVNextPageTitle(challenge ? `编辑 ${challenge.title}` : '题目工作台')

  useEffect(() => {
    if (!challenge || challenge.id === loadedId) return
    const next = challengeEditorDraft(challenge)
    setDraft(next)
    setBaseline(next)
    setLoadedId(challenge.id ?? id)
  }, [challenge, id, loadedId])

  const dirty = Boolean(draft && baseline && JSON.stringify(draft) !== JSON.stringify(baseline))

  useEffect(() => {
    if (!dirty) return undefined
    const warn = (event: BeforeUnloadEvent) => event.preventDefault()
    window.addEventListener('beforeunload', warn)
    return () => window.removeEventListener('beforeunload', warn)
  }, [dirty])

  useEffect(() => {
    if (!runtimeOperation || !challenge) return undefined
    const complete =
      runtimeOperation.kind === 'create'
        ? challenge.testContainer?.status === ContainerStatus.Running
        : !challenge.testContainer || challenge.testContainer.status === ContainerStatus.Destroyed
    if (complete) {
      setRuntimeOperation(null)
      setFeedback({
        tone: 'success',
        message: runtimeOperation.kind === 'create' ? 'Docker 测试实例已就绪。' : 'Docker 测试实例已销毁。',
      })
      return undefined
    }
    const remaining = 120_000 - (Date.now() - runtimeOperation.startedAt)
    if (remaining <= 0) {
      setRuntimeOperation(null)
      setFeedback({ tone: 'neutral', message: '实例任务仍未回写题目详情，请到部署队列检查任务状态。' })
      return undefined
    }
    const timeout = window.setTimeout(() => {
      setRuntimeOperation(null)
      setFeedback({ tone: 'neutral', message: '实例任务仍未回写题目详情，请到部署队列检查任务状态。' })
    }, remaining)
    return () => window.clearTimeout(timeout)
  }, [challenge, runtimeOperation])

  const update = <Key extends keyof ChallengeEditorDraft>(field: Key, value: ChallengeEditorDraft[Key]) => {
    setDraft((current) => (current ? { ...current, [field]: value } : current))
  }

  const setEnvironment = (environment: EnvironmentType) => {
    setDraft((current) =>
      current
        ? {
            ...current,
            environment,
            imageTemplateId: null,
            containerImage: environment === EnvironmentType.Docker ? current.containerImage : '',
          }
        : current
    )
  }

  const availableTemplates = useMemo(
    () =>
      (imagesRequest.images ?? []).filter(
        (template) => draft && templateAvailableForEnvironment(template, draft.environment)
      ),
    [draft, imagesRequest.images]
  )

  const save = async (event?: FormEvent) => {
    event?.preventDefault()
    if (!draft || !challenge) return
    const issues = validateChallengeEditorDraft(draft, challenge.type)
    if (issues.length) {
      setFeedback({ tone: 'danger', message: issues.join(' ') })
      return
    }
    setSaving(true)
    setFeedback(null)
    try {
      const saved = await gameAdminApi.updateChallenge(gameId, id, challengeUpdatePayload(draft, challenge.type))
      const next = challengeEditorDraft(saved)
      setDraft(next)
      setBaseline(next)
      await challengeRequest.mutate()
      setFeedback({ tone: 'success', message: '题目配置已保存。' })
    } catch (requestError) {
      setFeedback({ tone: 'danger', message: errorMessage(requestError, '题目保存失败。') })
    } finally {
      setSaving(false)
    }
  }

  const reportFeedback: ChallengeEditorFeedback = (tone, message) => setFeedback({ tone, message })

  const createTestInstance = async () => {
    if (!challenge?.id || runtimeOperation || dirty) return
    setFeedback(null)
    try {
      const ticket = await gameAdminApi.createTestInstance(gameId, challenge.id)
      setRuntimeOperation({ kind: 'create', startedAt: Date.now(), ticketId: ticket.ticketId })
      await challengeRequest.mutate()
      setFeedback({
        tone: 'neutral',
        message: `测试实例已进入部署队列${ticket.ticketId ? `，任务 ${ticket.ticketId}` : ''}。`,
      })
    } catch (requestError) {
      setFeedback({ tone: 'danger', message: errorMessage(requestError, '测试实例创建失败。') })
    }
  }

  const destroyTestInstance = async () => {
    if (!challenge?.id || runtimeOperation) return
    setFeedback(null)
    try {
      await gameAdminApi.destroyTestInstance(gameId, challenge.id)
      setRuntimeOperation({ kind: 'destroy', startedAt: Date.now() })
      await challengeRequest.mutate()
      setFeedback({ tone: 'neutral', message: '销毁任务已进入部署队列。' })
    } catch (requestError) {
      setFeedback({ tone: 'danger', message: errorMessage(requestError, '测试实例销毁失败。') })
    }
  }

  const remove = async () => {
    if (!challenge?.id) return false
    setDeleting(true)
    try {
      await gameAdminApi.removeChallenge(gameId, challenge.id)
      navigate(`/admin/games/${gameId}/challenges`, { replace: true })
      return true
    } catch (requestError) {
      setFeedback({ tone: 'danger', message: errorMessage(requestError, '题目删除失败。') })
      return false
    } finally {
      setDeleting(false)
    }
  }

  if (!Number.isInteger(id) || id <= 0) return <DataState description="题目编号不是有效数字。" title="题目参数错误" />
  if (!challenge || !draft) {
    return challengeRequest.error ? (
      <DataState description="题目不存在，或当前账户没有管理权限。" title="无法打开题目工作台" />
    ) : (
      <DataState description="正在读取题目、附件、Flag 和环境模板。" loading title="题目工作台加载中" />
    )
  }

  const containerChallenge = isContainerChallenge(challenge.type)
  const configurationIssues = challengeConfigurationIssues(challenge)
  const testContainer = challenge.testContainer

  return (
    <div className={styles.page}>
      <AdminPageHeader
        actions={
          <ActionButton
            icon={<ArrowLeft size={16} />}
            onClick={() => navigate(`/admin/games/${gameId}/challenges`)}
            type="button"
          >
            返回题目列表
          </ActionButton>
        }
        description="题面、计分和环境配置保存为一个事务；附件与 Flag 使用各自的即时操作接口。"
        eyebrow={`CHALLENGE #${challenge.id}`}
        title={challenge.title}
      />
      <div className={styles.statusLine}>
        <StatusBadge tone={challenge.isEnabled ? 'success' : 'neutral'}>
          {challenge.isEnabled ? '已启用' : '未启用'}
        </StatusBadge>
        <StatusBadge tone="info">{challengeTypeLabel(challenge.type)}</StatusBadge>
        <StatusBadge tone={configurationIssues.length ? 'warning' : 'success'}>
          {configurationIssues.length ? `${configurationIssues.length} 项待配置` : '配置完整'}
        </StatusBadge>
        <span>已解出 {challenge.acceptedCount} 队</span>
      </div>
      {feedback ? (
        <InlineFeedback tone={feedback.tone === 'neutral' ? undefined : feedback.tone}>
          {feedback.message}
        </InlineFeedback>
      ) : null}
      {challengeRequest.error ? (
        <InlineFeedback tone="danger">{errorMessage(challengeRequest.error, '题目详情刷新失败。')}</InlineFeedback>
      ) : null}

      <form className={styles.form} onSubmit={(event) => void save(event)}>
        <AdminEditorSection
          description="题目类型创建后不可变更；名称、分类和启用状态会直接影响选手端展示。"
          title="题目身份"
        >
          <div className={styles.stack}>
            <div className={styles.fieldGrid}>
              <TextField
                label="题目名称"
                maxLength={128}
                onValueChange={(value) => update('title', value)}
                required
                value={draft.title}
              />
              <SelectField
                label="题目分类"
                onValueChange={(value) => update('category', value as ChallengeCategory)}
                value={draft.category}
              >
                {Object.values(ChallengeCategory).map((category) => (
                  <option key={category} value={category}>
                    {category}
                  </option>
                ))}
              </SelectField>
              <TextField disabled label="题目类型" value={challengeTypeLabel(challenge.type)} />
              {challenge.type === 'DynamicAttachment' ? (
                <TextField
                  hint="所有队伍下载时使用的统一文件名。"
                  label="动态附件文件名"
                  onValueChange={(value) => update('fileName', value)}
                  value={draft.fileName}
                />
              ) : null}
            </div>
            <ToggleField
              checked={draft.isEnabled}
              description="发布前应先完成 Flag、附件和环境验证。"
              label="启用题目"
              onChange={(checked) => update('isEnabled', checked)}
            />
            {configurationIssues.length ? (
              <InlineFeedback tone="danger">{configurationIssues.join('；')}。</InlineFeedback>
            ) : null}
          </div>
        </AdminEditorSection>

        <AdminEditorSection description="题面与选手端使用同一 Markdown 渲染器；提示每行一条。" title="题面与提示">
          <div className={styles.stack}>
            <div className={styles.markdownGrid}>
              <TextAreaField
                label="题目说明 Markdown"
                onValueChange={(value) => update('content', value)}
                rows={22}
                value={draft.content}
              />
              <article className={styles.preview}>
                <header>实时预览</header>
                <MarkdownContent source={draft.content || '暂无题目说明。'} />
              </article>
            </div>
            <TextAreaField
              hint="空行会在保存时移除。"
              label="题目提示"
              onValueChange={(value) => update('hintsText', value)}
              rows={6}
              value={draft.hintsText}
            />
          </div>
        </AdminEditorSection>

        <AdminEditorSection
          description="题目分值按解出人数衰减至最低得分率；截止时间为空表示跟随比赛。"
          title="计分与限制"
        >
          <div className={styles.stack}>
            <div className={styles.resourceGrid}>
              <TextField
                label="初始分值"
                min={1}
                onValueChange={(value) => update('originalScore', Number(value))}
                type="number"
                value={draft.originalScore}
              />
              <TextField
                label="最低得分率"
                max={1}
                min={0}
                onValueChange={(value) => update('minScoreRate', Number(value))}
                step={0.05}
                type="number"
                value={draft.minScoreRate}
              />
              <TextField
                label="难度系数"
                min={0.1}
                onValueChange={(value) => update('difficulty', Number(value))}
                step={0.5}
                type="number"
                value={draft.difficulty}
              />
              <TextField
                hint="0 表示不限制。"
                label="提交次数限制"
                min={0}
                onValueChange={(value) => update('submissionLimit', Number(value))}
                type="number"
                value={draft.submissionLimit}
              />
            </div>
            <div className={styles.fieldGrid}>
              <TextField
                label="题目截止时间"
                onValueChange={(value) => update('deadline', value)}
                type="datetime-local"
                value={draft.deadline}
              />
              <ToggleField
                checked={draft.disableBloodBonus}
                description="仅影响当前题目的前三血奖励。"
                label="禁用血分奖励"
                onChange={(checked) => update('disableBloodBonus', checked)}
              />
            </div>
            <div className={styles.scorePreview}>
              <span>初始 {draft.originalScore} 分</span>
              <span>最低约 {Math.round(draft.originalScore * draft.minScoreRate)} 分</span>
              <span>难度系数 {draft.difficulty}</span>
            </div>
          </div>
        </AdminEditorSection>

        <AdminEditorSection
          description="容器题必须选择运行环境。模板列表只展示已就绪且类型匹配的全局环境模板。"
          title="运行环境"
        >
          {containerChallenge ? (
            <div className={styles.stack}>
              <div className={styles.fieldGrid}>
                <SelectField
                  label="环境类型"
                  onValueChange={(value) => setEnvironment(value as EnvironmentType)}
                  value={draft.environment}
                >
                  <option value={EnvironmentType.None}>未配置</option>
                  <option value={EnvironmentType.Docker}>Docker</option>
                  <option value={EnvironmentType.WindowsVM}>Windows VM</option>
                </SelectField>
                <SelectField
                  label="环境模板"
                  onValueChange={(value) => {
                    const template = availableTemplates.find((item) => item.id === Number(value))
                    setDraft((current) =>
                      current
                        ? {
                            ...current,
                            imageTemplateId: template?.id ?? null,
                            containerImage:
                              current.environment === EnvironmentType.Docker
                                ? (template?.registryUrl ?? current.containerImage)
                                : '',
                          }
                        : current
                    )
                  }}
                  value={draft.imageTemplateId ?? ''}
                >
                  <option value="">请选择已就绪模板</option>
                  {availableTemplates.map((template) => (
                    <option key={template.id} value={template.id}>
                      #{template.id} {template.name}
                    </option>
                  ))}
                </SelectField>
              </div>
              {draft.environment === EnvironmentType.Docker ? (
                <>
                  <TextField
                    hint="选择 Docker 模板时自动填充，也可使用节点可拉取的完整镜像引用。"
                    label="Docker 镜像引用"
                    onValueChange={(value) => update('containerImage', value)}
                    value={draft.containerImage}
                  />
                  <div className={styles.resourceGrid}>
                    <TextField
                      label="内存 MB"
                      min={32}
                      onValueChange={(value) => update('memoryLimit', Number(value))}
                      type="number"
                      value={draft.memoryLimit}
                    />
                    <TextField
                      hint="单位为 0.1 CPU。"
                      label="CPU 配额"
                      min={1}
                      onValueChange={(value) => update('cpuCount', Number(value))}
                      type="number"
                      value={draft.cpuCount}
                    />
                    <TextField
                      label="存储 MB"
                      min={0}
                      onValueChange={(value) => update('storageLimit', Number(value))}
                      type="number"
                      value={draft.storageLimit}
                    />
                    <TextField
                      label="暴露端口"
                      max={65535}
                      min={1}
                      onValueChange={(value) => update('exposePort', Number(value))}
                      type="number"
                      value={draft.exposePort}
                    />
                  </div>
                </>
              ) : null}
              {draft.environment !== EnvironmentType.None ? (
                <div className={styles.fieldGrid}>
                  <SelectField
                    label="网络模式"
                    onValueChange={(value) => update('networkMode', value as NetworkMode)}
                    value={draft.networkMode}
                  >
                    <option value={NetworkMode.Open}>开放网络</option>
                    <option value={NetworkMode.Isolated}>隔离网络</option>
                    <option value={NetworkMode.Custom}>自定义网络</option>
                  </SelectField>
                  <ToggleField
                    checked={draft.enableTrafficCapture}
                    description="开启后平台记录实例流量供后续分析。"
                    label="记录实例流量"
                    onChange={(checked) => update('enableTrafficCapture', checked)}
                  />
                </div>
              ) : null}
              {challenge.type === 'DynamicContainer' ? (
                <TextField
                  hint="平台根据队伍和题目信息替换占位符。"
                  label="动态 Flag 模板"
                  maxLength={120}
                  onValueChange={(value) => update('flagTemplate', value)}
                  placeholder="flag{[TEAM_HASH]}"
                  value={draft.flagTemplate}
                />
              ) : null}
            </div>
          ) : (
            <div className={styles.passiveEnvironment}>当前为附件题，不创建 Docker 或 Windows 实例。</div>
          )}
        </AdminEditorSection>

        <AdminEditorSection
          description="普通附件绑定在题目上；动态附件按 Flag 独立绑定，避免两种模型混用。"
          title="题目附件"
        >
          <ChallengeAttachmentPanel
            challenge={challenge}
            gameId={gameId}
            onChanged={challengeRequest.mutate}
            onFeedback={reportFeedback}
          />
        </AdminEditorSection>

        <AdminEditorSection
          description="支持多阶段判题、继承动态分值或固定分值，以及文本、文件和自定义答案。"
          title="Flag 管理"
        >
          <ChallengeFlagPanel
            challenge={challenge}
            gameId={gameId}
            onChanged={challengeRequest.mutate}
            onFeedback={reportFeedback}
          />
        </AdminEditorSection>

        <AdminChallengeRuntimePanel
          challenge={challenge}
          containerChallenge={containerChallenge}
          dirty={dirty}
          draftEnvironment={draft.environment}
          onCreate={createTestInstance}
          onDestroy={destroyTestInstance}
          onRefresh={challengeRequest.mutate}
          operation={runtimeOperation}
          refreshing={challengeRequest.isRefreshing}
        />

        <AdminEditorSection
          description="删除会移除题目、附件和 Flag；已有实例或业务事实时服务端可能拒绝。"
          title="危险区"
        >
          <div className={styles.dangerRow}>
            <div>
              <strong>删除题目</strong>
              <p>此操作不可撤销，必须输入完整题目名称确认。</p>
            </div>
            <ActionButton
              disabled={deleting || Boolean(testContainer) || Boolean(runtimeOperation)}
              icon={<Trash2 size={16} />}
              onClick={() => setDeleteOpen(true)}
              tone="danger"
              type="button"
            >
              删除题目
            </ActionButton>
          </div>
        </AdminEditorSection>

        <AdminEditorActionBar
          status={
            dirty ? '有未保存的题目配置。附件与 Flag 已即时保存。' : feedback?.message || '题目配置已与服务器同步。'
          }
        >
          <ActionButton
            disabled={saving || !dirty}
            icon={<Save size={17} />}
            onClick={() => void save()}
            tone="primary"
            type="button"
          >
            {saving ? '正在保存' : '保存题目'}
          </ActionButton>
        </AdminEditorActionBar>
      </form>

      <VNextConfirmDialog
        confirmationText={challenge.title}
        description="删除后无法恢复。"
        message={`将永久删除题目“${challenge.title}”及其附件和 Flag。`}
        onClose={() => setDeleteOpen(false)}
        onConfirm={remove}
        open={deleteOpen}
        title="确认删除题目"
      />
    </div>
  )
}
