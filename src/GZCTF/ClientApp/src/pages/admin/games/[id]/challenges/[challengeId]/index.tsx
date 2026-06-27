import {
  Anchor,
  Alert,
  Badge,
  Button,
  FileButton,
  Group,
  NumberInput,
  Select,
  Stack,
  Switch,
  Text,
  Textarea,
  TextInput,
} from '@mantine/core'
import { notifications } from '@mantine/notifications'
import {
  mdiDatabasePlusOutline,
  mdiFileUploadOutline,
  mdiLinkVariantPlus,
  mdiPaperclip,
  mdiTrashCanOutline,
} from '@mdi/js'
import { Icon } from '@mdi/react'
import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useNavigate, useParams } from 'react-router'
import { AdminPage } from '@Components/admin/AdminPage'
import { AttachmentRemoteEditModal } from '@Components/admin/AttachmentRemoteEditModal'
import { AttachmentUploadModal } from '@Components/admin/AttachmentUploadModal'
import { YinyuPanel } from '@Components/yinyu/YinyuUI'
import { HunamizeSize, showErrorMsg } from '@Utils/Shared'
import api, { ChallengeType, FileType } from '@Api'

interface ImageTemplate {
  id?: number
  Id?: number
  name?: string
  Name?: string
  description?: string | null
  Description?: string | null
  imagePath?: string
  registryUrl?: string | null
  RegistryUrl?: string | null
  osType: string | number
  OSType?: string | number
  imageType?: string | number
  ImageType?: string | number
  status?: string | number
  Status?: string | number
}

interface ChallengeEditData {
  id: number
  title: string
  content: string
  category: string
  type: string
  environment: string
  imageTemplateId: number | null
  containerImage: string
  memoryLimit: number
  cpuCount: number
  storageLimit: number
  exposePort: number
  originalScore: number
  minScoreRate: number
  difficulty: number
  isEnabled: boolean
  enableTrafficCapture: boolean
  disableBloodBonus: boolean
  submissionLimit: number
  flagTemplate: string | null
  hints: string[]
  acceptedCount: number
  flags: { id: number; flag: string; orderIndex?: number; scoreMode?: string }[]
  attachment?: ChallengeAttachment | null
}

interface ChallengeAttachment {
  id?: number
  type?: FileType | string
  url?: string | null
  fileSize?: number | null
}

const ENV_NONE = 'None'
const ENV_DOCKER = 'Docker'
const ENV_WINDOWS_VM = 'WindowsVM'

const envOptions = [
  { value: ENV_NONE, label: '无环境（附件题）' },
  { value: ENV_DOCKER, label: 'Linux Docker 容器' },
  { value: ENV_WINDOWS_VM, label: 'Windows 虚拟机 (RDP)' },
]

function templateKey(value: string | number | undefined | null) {
  return String(value ?? '').toLowerCase()
}

function isReadyTemplate(template: ImageTemplate) {
  const status = templateKey(template.status ?? template.Status)
  return status === '0' || status === 'ready' || status === ''
}

function isDockerTemplate(template: ImageTemplate) {
  const imageType = templateKey(template.imageType ?? template.ImageType)
  return imageType === '0' || imageType === 'docker'
}

function templateId(template: ImageTemplate) {
  return template.id ?? template.Id
}

function templateName(template: ImageTemplate) {
  return template.name ?? template.Name ?? `模板 ${templateId(template) ?? ''}`
}

function templateDescription(template: ImageTemplate) {
  return template.description ?? template.Description
}

function templateRegistryUrl(template: ImageTemplate) {
  return template.registryUrl ?? template.RegistryUrl
}

export default function ChallengeEdit() {
  const { id: gameId, challengeId } = useParams<{ id: string; challengeId: string }>()
  const navigate = useNavigate()
  const [challenge, setChallenge] = useState<ChallengeEditData | null>(null)
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [newFlag, setNewFlag] = useState('')
  const [addingFlag, setAddingFlag] = useState(false)
  const [imageTemplates, setImageTemplates] = useState<ImageTemplate[]>([])
  const [uploadOpened, setUploadOpened] = useState(false)
  const [remoteOpened, setRemoteOpened] = useState(false)
  const [attachmentSaving, setAttachmentSaving] = useState(false)
  const [remoteAttachmentUrl, setRemoteAttachmentUrl] = useState('')
  const { t } = useTranslation()

  const load = async () => {
    setLoading(true)
    try {
      const res = await fetch(`/api/edit/Games/${gameId}/Challenges/${challengeId}`)
      if (!res.ok) {
        console.error('Challenge load failed:', res.status)
        return
      }

      const c = await res.json()
      if (c) {
        setChallenge({
          ...c,
          environment: c.environment ?? ENV_NONE,
          imageTemplateId: c.imageTemplateId ?? null,
          flags: c.flags ?? [],
          hints: c.hints ?? [],
          attachment: c.attachment ?? null,
          containerImage: c.containerImage ?? '',
          memoryLimit: c.memoryLimit ?? 64,
          cpuCount: c.cpuCount ?? 1,
          storageLimit: c.storageLimit ?? 256,
          exposePort: c.exposePort ?? 80,
          originalScore: c.originalScore ?? 500,
          minScoreRate: c.minScoreRate ?? 0.25,
          difficulty: c.difficulty ?? 3,
          submissionLimit: c.submissionLimit ?? 0,
          acceptedCount: c.acceptedCount ?? 0,
          enableTrafficCapture: c.enableTrafficCapture ?? false,
          disableBloodBonus: c.disableBloodBonus ?? false,
        })
      }
    } catch (err) {
      console.error('Challenge load error:', err)
    } finally {
      setLoading(false)
    }
  }

  const loadTemplates = async () => {
    try {
      const res = await fetch('/api/v1/image-templates?pageSize=100')
      if (res.ok) {
        const data = await res.json()
        setImageTemplates(data.items ?? data ?? [])
      }
    } catch {
      // optional template list
    }
  }

  useEffect(() => {
    load()
    loadTemplates()
  }, [gameId, challengeId])

  const dockerTemplates = useMemo(
    () =>
      imageTemplates.filter(
        (template) => isReadyTemplate(template) && isDockerTemplate(template) && templateRegistryUrl(template)
      ),
    [imageTemplates]
  )

  const windowsTemplates = useMemo(
    () =>
      imageTemplates.filter((template) => {
        const osType = templateKey(template.osType ?? template.OSType)
        return isReadyTemplate(template) && !isDockerTemplate(template) && (osType === '1' || osType === 'windows')
      }),
    [imageTemplates]
  )

  const windowsTemplateOptions = useMemo(() => {
    const options = windowsTemplates
      .flatMap((template) => {
        const id = templateId(template)
        const description = templateDescription(template)
        return id
          ? [{
              value: String(id),
              label: `${templateName(template)}${description ? ` - ${description}` : ''}`,
            }]
          : []
      })
    const selectedId = challenge?.imageTemplateId
    if (selectedId && !options.some((option) => option.value === String(selectedId))) {
      options.unshift({ value: String(selectedId), label: `已选择镜像模板 ID: ${selectedId}` })
    }
    return options
  }, [windowsTemplates, challenge?.imageTemplateId])

  const handleSave = async () => {
    if (!challenge) return

    const isDockerEnv = challenge.environment === ENV_DOCKER

    if (isDockerEnv && !challenge.containerImage.trim()) {
      notifications.show({
        title: '保存失败',
        message: '容器题目必须先绑定 Docker 镜像',
        color: 'red',
      })
      return
    }

    if (challenge.environment === ENV_WINDOWS_VM && !challenge.imageTemplateId) {
      notifications.show({
        title: '保存失败',
        message: 'Windows 虚拟机题目必须先选择 Windows 镜像模板',
        color: 'red',
      })
      return
    }

    setSaving(true)
    try {
      const res = await fetch(`/api/edit/Games/${gameId}/Challenges/${challengeId}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          title: challenge.title,
          content: challenge.content,
          containerImage: isDockerEnv ? challenge.containerImage : null,
          memoryLimit: challenge.memoryLimit,
          cpuCount: challenge.cpuCount,
          storageLimit: isDockerEnv ? challenge.storageLimit : null,
          exposePort: isDockerEnv ? challenge.exposePort : null,
          originalScore: challenge.originalScore,
          minScoreRate: challenge.minScoreRate,
          difficulty: challenge.difficulty,
          submissionLimit: challenge.submissionLimit,
          environment: challenge.environment,
          imageTemplateId: challenge.environment === ENV_WINDOWS_VM ? challenge.imageTemplateId : null,
          enableTrafficCapture: challenge.enableTrafficCapture,
          disableBloodBonus: challenge.disableBloodBonus,
          flagTemplate: challenge.flagTemplate,
        }),
      })

      if (res.ok) {
        notifications.show({ title: '保存成功', message: '题目配置已更新', color: 'green' })
        load()
      } else {
        const err = await res.json().catch(() => null)
        notifications.show({ title: '保存失败', message: err?.message ?? err?.title ?? '请检查输入', color: 'red' })
      }
    } finally {
      setSaving(false)
    }
  }

  const handleAddFlag = async () => {
    if (!newFlag.trim()) return

    setAddingFlag(true)
    try {
      const res = await fetch(`/api/edit/Games/${gameId}/Challenges/${challengeId}/Flags`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify([{ flag: newFlag }]),
      })

      if (res.ok) {
        notifications.show({ title: 'Flag 已添加', message: '新的 Flag 已成功添加', color: 'green' })
        setNewFlag('')
        load()
      }
    } finally {
      setAddingFlag(false)
    }
  }

  const refreshAttachment = async () => {
    await load()
  }

  const attachmentUrl = challenge?.attachment?.url ?? null
  const attachmentName = attachmentUrl ? decodeURIComponent(attachmentUrl.split('/').pop() ?? 'attachment') : null
  const isRemoteAttachment = challenge?.attachment?.type === FileType.Remote

  const handleUploadStaticAttachment = async (file: File | null) => {
    if (!file || !gameId || !challengeId) return

    setAttachmentSaving(true)
    try {
      const data = await api.assets.assetsUpload({ files: [file] })
      const uploaded = data.data?.[0]
      if (!uploaded?.hash) throw new Error('Attachment upload returned no file hash')

      await api.edit.editUpdateAttachment(Number(gameId), Number(challengeId), {
        attachmentType: FileType.Local,
        fileHash: uploaded.hash,
      })

      notifications.show({ title: '附件已绑定', message: file.name, color: 'green' })
      await refreshAttachment()
    } catch (err) {
      showErrorMsg(err, t)
    } finally {
      setAttachmentSaving(false)
    }
  }

  const handleSaveRemoteAttachment = async () => {
    if (!remoteAttachmentUrl.trim() || !gameId || !challengeId) return

    setAttachmentSaving(true)
    try {
      await api.edit.editUpdateAttachment(Number(gameId), Number(challengeId), {
        attachmentType: FileType.Remote,
        remoteUrl: remoteAttachmentUrl.trim(),
      })

      notifications.show({ title: '远程附件已绑定', message: remoteAttachmentUrl.trim(), color: 'green' })
      setRemoteAttachmentUrl('')
      await refreshAttachment()
    } catch (err) {
      showErrorMsg(err, t)
    } finally {
      setAttachmentSaving(false)
    }
  }

  const handleClearAttachment = async () => {
    if (!gameId || !challengeId) return

    setAttachmentSaving(true)
    try {
      await api.edit.editUpdateAttachment(Number(gameId), Number(challengeId), {
        attachmentType: FileType.None,
      })

      notifications.show({ title: '附件已清除', message: '当前题目不再绑定附件', color: 'green' })
      await refreshAttachment()
    } catch (err) {
      showErrorMsg(err, t)
    } finally {
      setAttachmentSaving(false)
    }
  }

  const handleToggle = async (enabled: boolean) => {
    if (!challenge) return

    const isDockerEnv = challenge.environment === ENV_DOCKER

    if (enabled && isDockerEnv && !challenge.containerImage.trim()) {
      notifications.show({
        title: '无法启用',
        message: '容器题目必须先绑定 Docker 镜像',
        color: 'red',
      })
      return
    }

    if (enabled && challenge.environment === ENV_WINDOWS_VM && !challenge.imageTemplateId) {
      notifications.show({
        title: '无法启用',
        message: 'Windows 虚拟机题目必须先选择 Windows 镜像模板',
        color: 'red',
      })
      return
    }

    try {
      const res = await fetch(`/api/edit/Games/${gameId}/Challenges/${challengeId}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ isEnabled: enabled }),
      })

      if (res.ok) {
        setChallenge({ ...challenge, isEnabled: enabled })
        notifications.show({ title: enabled ? '已启用' : '已禁用', message: '题目状态已更新', color: 'green' })
      } else {
        const err = await res.json().catch(() => null)
        notifications.show({
          title: enabled ? '启用失败' : '禁用失败',
          message: err?.message ?? err?.title ?? '请检查题目配置',
          color: 'red',
        })
      }
    } catch (err) {
      showErrorMsg(err, t)
    }
  }

  const updateFlag = async (index: number, field: string, value: string) => {
    if (!challenge) return
    const nextFlags = [...challenge.flags]
    nextFlags[index] = { ...nextFlags[index], [field]: value }
    setChallenge({ ...challenge, flags: nextFlags })

    try {
      await fetch(`/api/edit/Games/${gameId}/Challenges/${challengeId}/Flags/${nextFlags[index].id}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ [field]: value }),
      })
    } catch {
      // inline flag edit is best effort
    }
  }

  if (loading) return <AdminPage isLoading />
  if (!challenge) {
    return (
      <AdminPage>
        <Alert color="red">题目不存在</Alert>
      </AdminPage>
    )
  }

  const envType = challenge.environment ?? ENV_NONE
  const isDockerEnv = envType === ENV_DOCKER
  const isWindowsVM = envType === ENV_WINDOWS_VM
  const isDynamicAttachment = challenge.type === ChallengeType.DynamicAttachment
  const isStaticAttachment = challenge.type === ChallengeType.StaticAttachment
  const envLabel = envOptions.find((option) => option.value === envType)?.label ?? '未知'

  return (
    <AdminPage
      head={
        <Group>
          <Button variant="default" onClick={() => navigate(`/admin/games/${gameId}/challenges`)}>
            返回题目列表
          </Button>
          <Switch
            label={challenge.isEnabled ? '已启用' : '已禁用'}
            checked={challenge.isEnabled}
            onChange={(e) => handleToggle(e.currentTarget.checked)}
          />
          <Badge color="blue" className="yy-semantic-badge" data-semantic="category">
            {challenge.category}
          </Badge>
          <Badge color="grape" className="yy-semantic-badge" data-semantic="type">
            {challenge.type}
          </Badge>
          <Badge
            color={isWindowsVM ? 'blue' : isDockerEnv ? 'teal' : 'gray'}
            className="yy-semantic-badge"
            data-semantic={isWindowsVM ? 'windows' : isDockerEnv ? 'docker' : 'neutral'}
          >
            {envLabel}
          </Badge>
          <Badge color="green" className="yy-semantic-badge" data-semantic="success">
            Accepted: {challenge.acceptedCount}
          </Badge>
        </Group>
      }
    >
      <Stack gap="md" w="100%">
        <YinyuPanel p="md">
          <Text fw={700} mb="sm">
            基本信息
          </Text>
          <TextInput
            label="题目标题"
            value={challenge.title}
            onChange={(e) => setChallenge({ ...challenge, title: e.currentTarget.value })}
          />
          <Textarea
            label="题目内容 (Markdown)"
            mt="sm"
            minRows={4}
            value={challenge.content}
            onChange={(e) => setChallenge({ ...challenge, content: e.currentTarget.value })}
          />
        </YinyuPanel>

        <YinyuPanel p="md">
          <Text fw={700} mb="sm">
            环境配置
          </Text>
          <Select
            label="环境类型"
            data={envOptions}
            value={envType}
            onChange={(value) => {
              const newEnv = value || ENV_NONE
              setChallenge({
                ...challenge,
                environment: newEnv,
                imageTemplateId: newEnv === ENV_WINDOWS_VM ? challenge.imageTemplateId : null,
                containerImage: newEnv === ENV_DOCKER ? challenge.containerImage : '',
                exposePort: newEnv === ENV_DOCKER ? challenge.exposePort : 80,
              })
            }}
          />

          {isDockerEnv && (
            <Stack gap="sm" mt="md">
              <Text size="sm" fw={600} c="cyan">
                容器配置
              </Text>
              <Select
                label="已注册 Docker 镜像"
                placeholder={
                  dockerTemplates.length === 0 ? '暂无就绪 Docker 镜像，请先到环境模板上传或注册' : '选择 Docker 镜像'
                }
                data={dockerTemplates.map((template) => ({
                  value: templateRegistryUrl(template) ?? '',
                  label: `${templateName(template)} - ${templateRegistryUrl(template)}`,
                }))}
                value={
                  dockerTemplates.some((template) => templateRegistryUrl(template) === challenge.containerImage)
                    ? challenge.containerImage
                    : null
                }
                onChange={(value) => setChallenge({ ...challenge, containerImage: value ?? '' })}
                searchable
                clearable
              />
              <TextInput
                label="容器镜像地址"
                value={challenge.containerImage}
                onChange={(e) => setChallenge({ ...challenge, containerImage: e.currentTarget.value })}
                placeholder="registry.example.internal/ctf/web:tag"
                description="建议从已注册镜像中选择；这里保存的是 Docker 可直接拉取的完整镜像引用。"
              />
              <Group>
                <NumberInput
                  label="内存 (MB)"
                  value={challenge.memoryLimit}
                  min={32}
                  max={4096}
                  onChange={(v) => setChallenge({ ...challenge, memoryLimit: Number(v) || 64 })}
                />
                <NumberInput
                  label="CPU"
                  value={challenge.cpuCount}
                  min={1}
                  max={8}
                  onChange={(v) => setChallenge({ ...challenge, cpuCount: Number(v) || 1 })}
                />
                <NumberInput
                  label="存储 (MB)"
                  value={challenge.storageLimit}
                  min={64}
                  max={10240}
                  onChange={(v) => setChallenge({ ...challenge, storageLimit: Number(v) || 256 })}
                />
                <NumberInput
                  label="端口"
                  value={challenge.exposePort}
                  min={1}
                  max={65535}
                  onChange={(v) => setChallenge({ ...challenge, exposePort: Number(v) || 80 })}
                />
              </Group>
            </Stack>
          )}

          {isWindowsVM && (
            <Stack gap="sm" mt="md">
              <Text size="sm" fw={600} c="orange">
                Windows 虚拟机配置
              </Text>
              <Select
                label="镜像模板"
                placeholder="选择 Windows 镜像模板..."
                data={windowsTemplateOptions}
                value={challenge.imageTemplateId ? String(challenge.imageTemplateId) : null}
                onChange={(value) => setChallenge({ ...challenge, imageTemplateId: value ? Number(value) : null })}
                searchable
                clearable
                required
              />
              {challenge.imageTemplateId && (
                <Alert color="blue" variant="light">
                  已选择镜像模板 ID: {challenge.imageTemplateId}
                  {windowsTemplates.find((template) => templateId(template) === challenge.imageTemplateId) && (
                    <Text size="xs" mt={4}>
                      模板:{' '}
                      {templateName(
                        windowsTemplates.find((template) => templateId(template) === challenge.imageTemplateId)!
                      )}
                    </Text>
                  )}
                </Alert>
              )}
              <Group>
                <NumberInput
                  label="内存 (MB)"
                  value={challenge.memoryLimit}
                  min={512}
                  max={16384}
                  onChange={(v) => setChallenge({ ...challenge, memoryLimit: Number(v) || 4096 })}
                />
                <NumberInput
                  label="CPU 核数"
                  value={challenge.cpuCount}
                  min={1}
                  max={8}
                  onChange={(v) => setChallenge({ ...challenge, cpuCount: Number(v) || 2 })}
                />
              </Group>
              <Alert color="orange" variant="light" mt="xs">
                Windows 虚拟机将通过 Guacamole RDP 代理提供远程桌面访问。每个队伍启动后获得独立 VM 实例。
              </Alert>
            </Stack>
          )}

          {!isDockerEnv && !isWindowsVM && (
            <Alert color="gray" variant="light" mt="md">
              附件题模式：无需环境配置，仅通过附件和 Flag 评判。
            </Alert>
          )}
        </YinyuPanel>

        <YinyuPanel p="md">
          <Text fw={700} mb="sm">
            {isDynamicAttachment ? '动态附件池' : '题目附件'}
          </Text>

          {isDynamicAttachment ? (
            <Stack gap="sm">
              <Text size="sm" className="yy-readable-text">
                动态附件会按队伍分发，适合每队获得不同附件和不同 Flag 的题目。批量上传时文件名会作为对应 Flag。
              </Text>
              <Group>
                <Button
                  variant="default"
                  leftSection={<Icon path={mdiDatabasePlusOutline} size={1} />}
                  onClick={() => setUploadOpened(true)}
                >
                  批量上传本地附件
                </Button>
                <Button
                  variant="default"
                  leftSection={<Icon path={mdiLinkVariantPlus} size={1} />}
                  onClick={() => setRemoteOpened(true)}
                >
                  批量添加远程附件
                </Button>
                <Badge color="cyan" className="yy-semantic-badge" data-semantic="neutral">
                  已配置 {challenge.flags?.length ?? 0} 个附件 Flag
                </Badge>
              </Group>
            </Stack>
          ) : (
            <Stack gap="sm">
              {attachmentUrl ? (
                <Alert color="green" variant="light">
                  <Group justify="space-between" align="center" wrap="wrap">
                    <Stack gap={2}>
                      <Text fw={700}>{isRemoteAttachment ? '远程附件' : '本地附件'}</Text>
                      <Anchor href={attachmentUrl} target="_blank" rel="noopener noreferrer">
                        {attachmentName ?? attachmentUrl}
                      </Anchor>
                      {challenge.attachment?.fileSize ? (
                        <Text size="xs" className="yy-readable-text">
                          {HunamizeSize(challenge.attachment.fileSize)}
                        </Text>
                      ) : null}
                    </Stack>
                    <Button
                      color="red"
                      variant="light"
                      loading={attachmentSaving}
                      leftSection={<Icon path={mdiTrashCanOutline} size={1} />}
                      onClick={handleClearAttachment}
                    >
                      清除附件
                    </Button>
                  </Group>
                </Alert>
              ) : (
                <Alert color={isStaticAttachment ? 'orange' : 'gray'} variant="light">
                  {isStaticAttachment
                    ? '当前是纯附件题，建议在发布前绑定题目附件。'
                    : '当前未绑定题目附件。容器题可按需上传源码包、说明文档或工具包。'}
                </Alert>
              )}

              <Group align="flex-end" wrap="wrap">
                <FileButton onChange={handleUploadStaticAttachment}>
                  {(props) => (
                    <Button
                      {...props}
                      loading={attachmentSaving}
                      leftSection={<Icon path={mdiFileUploadOutline} size={1} />}
                    >
                      上传并绑定本地附件
                    </Button>
                  )}
                </FileButton>
                <TextInput
                  label="远程附件 URL"
                  placeholder="https://example.com/attachment.zip"
                  value={remoteAttachmentUrl}
                  onChange={(e) => setRemoteAttachmentUrl(e.currentTarget.value)}
                  style={{ flex: 1, minWidth: '18rem' }}
                />
                <Button
                  variant="default"
                  loading={attachmentSaving}
                  disabled={!remoteAttachmentUrl.trim()}
                  leftSection={<Icon path={mdiPaperclip} size={1} />}
                  onClick={handleSaveRemoteAttachment}
                >
                  绑定远程附件
                </Button>
              </Group>
            </Stack>
          )}
        </YinyuPanel>

        <YinyuPanel p="md">
          <Text fw={700} mb="sm">
            评分配置
          </Text>
          <Group>
            <NumberInput
              label="原始分数"
              value={challenge.originalScore}
              min={100}
              max={5000}
              onChange={(v) => setChallenge({ ...challenge, originalScore: Number(v) || 1000 })}
            />
            <NumberInput
              label="最低得分率"
              value={challenge.minScoreRate}
              min={0}
              max={1}
              step={0.05}
              onChange={(v) => setChallenge({ ...challenge, minScoreRate: Number(v) || 0.25 })}
            />
            <NumberInput
              label="难度系数"
              value={challenge.difficulty}
              min={1}
              max={20}
              step={0.5}
              onChange={(v) => setChallenge({ ...challenge, difficulty: Number(v) || 5 })}
            />
            <NumberInput
              label="提交限制 (0=无限制)"
              value={challenge.submissionLimit}
              min={0}
              onChange={(v) => setChallenge({ ...challenge, submissionLimit: Number(v) || 0 })}
            />
          </Group>
        </YinyuPanel>

        <YinyuPanel p="md">
          <Text fw={700} mb="sm">
            动态 Flag
          </Text>
          <TextInput
            label="Flag 模板 (动态容器使用)"
            value={challenge.flagTemplate ?? ''}
            onChange={(e) => setChallenge({ ...challenge, flagTemplate: e.currentTarget.value || null })}
          />
        </YinyuPanel>

        <YinyuPanel p="md">
          <Text fw={700} mb="sm">
            Flag 管理
          </Text>
          <Group>
            <TextInput
              placeholder="输入新 Flag..."
              value={newFlag}
              onChange={(e) => setNewFlag(e.currentTarget.value)}
              style={{ flex: 1 }}
            />
            <Button loading={addingFlag} onClick={handleAddFlag}>
              添加 Flag
            </Button>
          </Group>
          {challenge.flags?.map((flag, index) => (
            <Alert key={flag.id ?? index} color="green" mt="xs" py="xs">
              <Group wrap="nowrap" align="flex-start">
                <Text size="sm" ff="monospace" style={{ flex: 1 }}>
                  {flag.flag}
                </Text>
                <Select
                  label="计分模式"
                  data={[
                    { value: 'InheritDecay', label: '跟随衰减' },
                    { value: 'FixedScore', label: '固定分值' },
                  ]}
                  value={flag.scoreMode ?? 'InheritDecay'}
                  onChange={(value) => updateFlag(index, 'scoreMode', value!)}
                  size="xs"
                  maw={140}
                />
              </Group>
            </Alert>
          ))}
        </YinyuPanel>

        <Group justify="flex-end">
          <Button loading={saving} onClick={handleSave} size="lg">
            保存配置
          </Button>
        </Group>
      </Stack>
      <AttachmentUploadModal
        title="上传附件"
        size="38rem"
        opened={uploadOpened}
        onClose={() => setUploadOpened(false)}
        onUploaded={load}
      />
      <AttachmentRemoteEditModal
        title="远程附件"
        size="38rem"
        opened={remoteOpened}
        onClose={() => setRemoteOpened(false)}
        onUploaded={load}
      />
    </AdminPage>
  )
}
