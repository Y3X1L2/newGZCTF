import { ImageUp, RotateCcw, Save } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import type { AccountPolicy, ContainerPolicy, GlobalConfig } from '@Api'
import { FileField, TextAreaField, TextField, ToggleField } from '../../../shared/FormControls'
import { ActionButton, InlineFeedback, VNextConfirmDialog } from '../../../shared/Interaction'
import { DataState } from '../../../shared/Primitives'
import { errorMessage } from '../../../shared/errors'
import { useVNextPageTitle } from '../../../shared/useVNextPageTitle'
import { commonAdminApi } from '../api'
import { AdminEditorSection, AdminPageHeader, RefreshIndicator } from '../shared/AdminWorkbench'
import styles from './AdminSystemPage.module.css'
import { useAdminSystem } from './useAdminSystem'

type Feedback = { tone: 'danger' | 'success'; message: string } | null

function sameValue(left: unknown, right: unknown) {
  return JSON.stringify(left) === JSON.stringify(right)
}

export function AdminSystemPage() {
  const request = useAdminSystem()
  const [globalDraft, setGlobalDraft] = useState<GlobalConfig>({})
  const [accountDraft, setAccountDraft] = useState<AccountPolicy>({})
  const [containerDraft, setContainerDraft] = useState<ContainerPolicy>({})
  const [logoFile, setLogoFile] = useState<File | null>(null)
  const [saving, setSaving] = useState<string | null>(null)
  const [feedback, setFeedback] = useState<Feedback>(null)
  const [resetLogoOpen, setResetLogoOpen] = useState(false)

  useVNextPageTitle('系统设置')

  useEffect(() => {
    if (!request.config) return
    setGlobalDraft({ ...request.config.globalConfig })
    setAccountDraft({ ...request.config.accountPolicy })
    setContainerDraft({ ...request.config.containerPolicy })
  }, [request.config])

  const dirty = useMemo(() => ({
    global: !sameValue(globalDraft, request.config?.globalConfig ?? {}),
    account: !sameValue(accountDraft, request.config?.accountPolicy ?? {}),
    container: !sameValue(containerDraft, request.config?.containerPolicy ?? {}),
  }), [accountDraft, containerDraft, globalDraft, request.config])

  const saveSection = async (
    section: 'account' | 'container' | 'global',
    run: () => Promise<void>,
    success: string
  ) => {
    if (saving) return
    setSaving(section)
    setFeedback(null)
    try {
      await run()
      await request.mutate()
      setFeedback({ tone: 'success', message: success })
    } catch (requestError) {
      setFeedback({ tone: 'danger', message: errorMessage(requestError, '系统配置保存失败。') })
    } finally {
      setSaving(null)
    }
  }

  const containerValid =
    (containerDraft.maxExerciseContainerCountPerUser ?? 0) >= 1 &&
    (containerDraft.defaultLifetime ?? 0) >= 1 &&
    (containerDraft.defaultLifetime ?? 0) <= 7200 &&
    (containerDraft.extensionDuration ?? 0) >= 1 &&
    (containerDraft.extensionDuration ?? 0) <= 7200 &&
    (containerDraft.renewalWindow ?? 0) >= 1 &&
    (containerDraft.renewalWindow ?? 0) <= 360
  const logoUrl = request.config?.globalConfig?.logoHash
    ? `/assets/${request.config.globalConfig.logoHash}/logo`
    : null

  if (request.isLoading) return <DataState description="正在读取平台、账号和容器策略。" loading title="系统设置加载中" />

  return (
    <div className={styles.page}>
      <AdminPageHeader
        actions={<RefreshIndicator active={request.isRefreshing} label="配置按保存操作回读" />}
        description="分区维护平台身份、账号策略和实例生命周期；每个区段独立写入。"
        eyebrow="PLATFORM SETTINGS"
        title="系统设置"
      />
      {request.error ? <InlineFeedback tone="danger">{errorMessage(request.error, '系统配置加载失败。')}</InlineFeedback> : null}
      {feedback ? <InlineFeedback tone={feedback.tone}>{feedback.message}</InlineFeedback> : null}
      <div className={styles.settingsLayout}>
        <AdminEditorSection description="面向所有用户展示的平台名称、标语与说明。" title="平台身份">
          <div className={styles.sectionBody}>
            <div className={styles.twoColumns}>
              <TextField label="平台标题" onValueChange={(title) => setGlobalDraft({ ...globalDraft, title })} value={globalDraft.title ?? ''} />
              <TextField label="平台标语" onValueChange={(slogan) => setGlobalDraft({ ...globalDraft, slogan })} value={globalDraft.slogan ?? ''} />
            </div>
            <TextAreaField label="站点说明" onValueChange={(description) => setGlobalDraft({ ...globalDraft, description })} rows={4} value={globalDraft.description ?? ''} />
            <TextAreaField label="页脚信息" onValueChange={(footerInfo) => setGlobalDraft({ ...globalDraft, footerInfo })} rows={3} value={globalDraft.footerInfo ?? ''} />
            <div className={styles.sectionActions}>
              <span>{dirty.global ? '存在未保存修改' : '配置已同步'}</span>
              <ActionButton
                disabled={!dirty.global || Boolean(saving)}
                icon={<Save size={16} />}
                onClick={() => void saveSection('global', () => commonAdminApi.updateSystemConfig({ globalConfig: globalDraft }), '平台身份配置已保存并回读。')}
                tone="primary"
                type="button"
              >{saving === 'global' ? '保存中' : '保存平台身份'}</ActionButton>
            </div>
          </div>
        </AdminEditorSection>

        <AdminEditorSection description="Logo 上传会同时生成平台 Logo 和 Favicon。" title="品牌资源">
          <div className={styles.sectionBody}>
            <div className={styles.logoArea}>
              <div className={styles.logoPreview}>{logoUrl ? <img alt="当前平台 Logo" src={logoUrl} /> : <span>Y</span>}</div>
              <div><strong>{logoUrl ? '已使用自定义 Logo' : '正在使用默认标志'}</strong><p>支持常见图片格式，服务器限制文件不超过 3 MB。</p></div>
            </div>
            <FileField accept="image/png,image/jpeg,image/webp,image/svg+xml" hint={logoFile ? `已选择 ${logoFile.name}` : '选择后仍需点击上传。'} label="选择 Logo 文件" onChange={setLogoFile} />
            <div className={styles.sectionActions}>
              <ActionButton disabled={!logoUrl || Boolean(saving)} icon={<RotateCcw size={16} />} onClick={() => setResetLogoOpen(true)} tone="danger" type="button">恢复默认 Logo</ActionButton>
              <ActionButton
                disabled={!logoFile || Boolean(saving)}
                icon={<ImageUp size={16} />}
                onClick={async () => {
                  if (!logoFile || saving) return
                  setSaving('logo')
                  setFeedback(null)
                  try {
                    await commonAdminApi.uploadLogo(logoFile)
                    setLogoFile(null)
                    await request.mutate()
                    setFeedback({ tone: 'success', message: '平台 Logo 与 Favicon 已更新。' })
                  } catch (requestError) {
                    setFeedback({ tone: 'danger', message: errorMessage(requestError, 'Logo 上传失败。') })
                  } finally {
                    setSaving(null)
                  }
                }}
                tone="primary"
                type="button"
              >{saving === 'logo' ? '上传中' : '上传并应用'}</ActionButton>
            </div>
          </div>
        </AdminEditorSection>

        <AdminEditorSection description="控制账号注册、初始状态和邮件验证要求。" title="账号策略">
          <div className={styles.sectionBody}>
            <div className={styles.toggleList}>
              <ToggleField checked={accountDraft.allowRegister ?? false} description="关闭后仅管理员可以创建新用户。" label="允许用户自主注册" onChange={(allowRegister) => setAccountDraft({ ...accountDraft, allowRegister })} />
              <ToggleField checked={accountDraft.activeOnRegister ?? false} description="关闭后，新注册用户需要管理员激活。" label="注册后立即激活" onChange={(activeOnRegister) => setAccountDraft({ ...accountDraft, activeOnRegister })} />
              <ToggleField checked={accountDraft.useCaptcha ?? false} description="登录和注册流程按平台验证码配置执行。" label="启用验证码" onChange={(useCaptcha) => setAccountDraft({ ...accountDraft, useCaptcha })} />
              <ToggleField checked={accountDraft.emailConfirmationRequired ?? false} description="注册、换绑邮箱和密码找回需要邮件确认。" label="要求邮件确认" onChange={(emailConfirmationRequired) => setAccountDraft({ ...accountDraft, emailConfirmationRequired })} />
            </div>
            <TextField hint="使用英文逗号分隔；留空表示不限制邮箱域名。" label="允许的邮箱域名" onValueChange={(emailDomainList) => setAccountDraft({ ...accountDraft, emailDomainList })} value={accountDraft.emailDomainList ?? ''} />
            <div className={styles.sectionActions}>
              <span>{dirty.account ? '存在未保存修改' : '配置已同步'}</span>
              <ActionButton
                disabled={!dirty.account || Boolean(saving)}
                icon={<Save size={16} />}
                onClick={() => void saveSection('account', () => commonAdminApi.updateSystemConfig({ accountPolicy: accountDraft }), '账号策略已保存并回读。')}
                tone="primary"
                type="button"
              >{saving === 'account' ? '保存中' : '保存账号策略'}</ActionButton>
            </div>
          </div>
        </AdminEditorSection>

        <AdminEditorSection description="这些值直接影响用户实例数量、默认时长和延期窗口。" title="容器生命周期">
          <div className={styles.sectionBody}>
            <ToggleField checked={containerDraft.autoDestroyOnLimitReached ?? false} description="达到上限时，创建新实例前自动销毁最早的练习实例。" label="达到上限时自动清理" onChange={(autoDestroyOnLimitReached) => setContainerDraft({ ...containerDraft, autoDestroyOnLimitReached })} />
            <div className={styles.twoColumns}>
              <TextField label="每名用户练习实例上限" min={1} onValueChange={(value) => setContainerDraft({ ...containerDraft, maxExerciseContainerCountPerUser: Number(value) })} type="number" value={containerDraft.maxExerciseContainerCountPerUser ?? 1} />
              <TextField hint="1-7200 分钟" label="默认实例时长（分钟）" max={7200} min={1} onValueChange={(value) => setContainerDraft({ ...containerDraft, defaultLifetime: Number(value) })} type="number" value={containerDraft.defaultLifetime ?? 120} />
              <TextField hint="1-7200 分钟" label="单次延期时长（分钟）" max={7200} min={1} onValueChange={(value) => setContainerDraft({ ...containerDraft, extensionDuration: Number(value) })} type="number" value={containerDraft.extensionDuration ?? 120} />
              <TextField hint="1-360 分钟" label="允许延期窗口（分钟）" max={360} min={1} onValueChange={(value) => setContainerDraft({ ...containerDraft, renewalWindow: Number(value) })} type="number" value={containerDraft.renewalWindow ?? 10} />
            </div>
            {!containerValid ? <InlineFeedback tone="danger">容器策略数值超出允许范围，请修正后保存。</InlineFeedback> : null}
            <div className={styles.sectionActions}>
              <span>{dirty.container ? '存在未保存修改' : '配置已同步'}</span>
              <ActionButton
                disabled={!dirty.container || !containerValid || Boolean(saving)}
                icon={<Save size={16} />}
                onClick={() => void saveSection('container', () => commonAdminApi.updateSystemConfig({ containerPolicy: containerDraft }), '容器生命周期策略已保存并回读。')}
                tone="primary"
                type="button"
              >{saving === 'container' ? '保存中' : '保存容器策略'}</ActionButton>
            </div>
          </div>
        </AdminEditorSection>
      </div>
      <VNextConfirmDialog
        confirmLabel="恢复默认 Logo"
        description="当前自定义 Logo 与 Favicon 文件会从存储中删除。"
        message="确认恢复平台默认 Logo？"
        onClose={() => setResetLogoOpen(false)}
        onConfirm={async () => {
          try {
            await commonAdminApi.resetLogo()
            await request.mutate()
            setFeedback({ tone: 'success', message: '平台 Logo 已恢复默认。' })
            return true
          } catch (requestError) {
            setFeedback({ tone: 'danger', message: errorMessage(requestError, 'Logo 重置失败。') })
            return false
          }
        }}
        open={resetLogoOpen}
        title="恢复默认 Logo"
      />
    </div>
  )
}
