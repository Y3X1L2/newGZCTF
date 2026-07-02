import {
  ActionIcon,
  Alert,
  Badge,
  Box,
  Button,
  FileInput,
  FileButton,
  Group,
  Modal,
  NumberInput,
  Pagination,
  Progress,
  Select,
  SimpleGrid,
  Stack,
  Switch,
  Table,
  Tabs,
  Text,
  TextInput,
  Textarea,
  Title,
} from '@mantine/core'
import { showNotification } from '@mantine/notifications'
import {
  mdiArchiveArrowUpOutline,
  mdiArchiveOutline,
  mdiArrowLeft,
  mdiBookOpenPageVariantOutline,
  mdiCheck,
  mdiClose,
  mdiContentSaveOutline,
  mdiCubeOutline,
  mdiDownloadOutline,
  mdiDocker,
  mdiFileImportOutline,
  mdiMagnify,
  mdiOpenInNew,
  mdiPencilOutline,
  mdiPlus,
  mdiPublish,
  mdiRefresh,
  mdiTrashCanOutline,
} from '@mdi/js'
import { Icon } from '@mdi/react'
import React, { FC, useEffect, useMemo, useState } from 'react'
import { Link, useLocation, useNavigate, useParams } from 'react-router'
import { Markdown } from '@Components/MarkdownRenderer'
import { WithNavBar } from '@Components/WithNavbar'
import { CourseTheoryBankPanel } from '@Components/training/CourseTheoryBankPanel'
import {
  TrainingStatusText,
  TrainingTagLine,
  trainingCourseProgress,
  trainingCourseStatus,
  trainingTags,
  trainingTeacherNames,
} from '@Components/training/TrainingCourseUI'
import { YinyuGameBendsBackground } from '@Components/yinyu/YinyuReactBits'
import { YinyuPanel } from '@Components/yinyu/YinyuUI'
import api, { ChallengeCategory, ChallengeType, EnvironmentType, FileType, NetworkMode } from '@Api'
import { showErrorMsg } from '@Utils/Shared'
import { useTranslation } from 'react-i18next'
import {
  TrainingCourseChallengeCreateModel,
  TrainingCourseChallengeEditDetailModel,
  TrainingCourseDockerRegistryModel,
  TrainingCourseDockerRegisterModel,
  TrainingCourseEditModel,
  TrainingCourseEnrollmentPolicy,
  TrainingCourseEnrollmentStatus,
  TrainingCourseModel,
  TrainingCourseLocalImageImportModel,
  TrainingCourseResourceEditModel,
  TrainingCourseResourceType,
  TrainingCourseImageTemplateModel,
  TrainingCourseStatus,
  trainingCourseAdminApi,
  trainingCourseApi,
} from '@Utils/TrainingApi'

const emptyCourseDraft = (): TrainingCourseEditModel => ({
  title: '',
  slug: '',
  summary: '',
  description: '',
  coverFileHash: null,
  tags: [],
  enrollmentPolicy: TrainingCourseEnrollmentPolicy.TeacherApproval,
})

const emptyResourceDraft = (): TrainingCourseResourceEditModel => ({
  title: '',
  description: '',
  type: TrainingCourseResourceType.File,
  externalUrl: null,
  localFileHash: null,
  order: 1,
  isVisible: true,
})

const emptyChallengeDraft = (): TrainingCourseChallengeCreateModel => ({
  title: '',
  content: '',
  category: ChallengeCategory.Web,
  type: ChallengeType.StaticAttachment,
  environment: EnvironmentType.None,
  imageTemplateId: null,
  containerImage: '',
  memoryLimit: 128,
  cpuCount: 1,
  storageLimit: 256,
  exposePort: 80,
  networkMode: NetworkMode.Open,
  flagTemplate: 'flag{[TEAM_HASH]}',
  staticFlag: '',
  submissionLimit: 0,
  chapterId: null,
  order: 1,
  isRequired: true,
  displayTitle: null,
  attachmentType: FileType.None,
  attachmentFileHash: null,
  attachmentRemoteUrl: null,
})

const emptyDockerRegisterDraft = (): TrainingCourseDockerRegisterModel => ({
  name: '',
  registryUrl: '',
  osType: '0',
  registryAuth: null,
})

const emptyLocalImportDraft = (): TrainingCourseLocalImageImportModel => ({
  localPath: '',
  displayName: null,
})

const normalizeKey = (value: string | number | undefined | null) => String(value ?? '').toLowerCase()

const imageTypeText = (value: string | number | undefined | null) => {
  const key = normalizeKey(value)
  if (key === '0' || key === 'docker') return 'Docker'
  if (key === '1' || key === 'qcow2') return 'QCOW2'
  if (key === '2' || key === 'ova') return 'OVA'
  if (key === '3' || key === 'vmdk') return 'VMDK'
  return String(value ?? '-')
}

const osTypeText = (value: string | number | undefined | null) => {
  const key = normalizeKey(value)
  if (key === '0' || key === 'linux') return 'Linux'
  if (key === '1' || key === 'windows') return 'Windows'
  return String(value ?? '-')
}

const statusInfo = (value: string | number | undefined | null) => {
  const key = normalizeKey(value)
  if (key === '0' || key === 'ready') return { label: '就绪', color: 'green' }
  if (key === '1' || key === 'importing') return { label: '导入中', color: 'violet' }
  if (key === '2' || key === 'error') return { label: '异常', color: 'red' }
  return { label: String(value ?? '未知'), color: 'gray' }
}

const enrollmentStatusInfo = (status: TrainingCourseEnrollmentStatus) => {
  if (status === TrainingCourseEnrollmentStatus.Approved) return { label: '已加入', color: 'green' }
  if (status === TrainingCourseEnrollmentStatus.Pending) return { label: '待审核', color: 'yellow' }
  if (status === TrainingCourseEnrollmentStatus.Rejected) return { label: '已拒绝', color: 'red' }
  return { label: '已取消', color: 'gray' }
}

const isDockerTemplate = (template: TrainingCourseImageTemplateModel) => imageTypeText(template.imageType) === 'Docker'
const isWindowsTemplate = (template: TrainingCourseImageTemplateModel) => osTypeText(template.osType) === 'Windows'
const isReadyTemplate = (template: TrainingCourseImageTemplateModel) => normalizeKey(template.status) === '0' || normalizeKey(template.status) === 'ready'

const challengeCategoryOptions = Object.values(ChallengeCategory).map((value) => ({ value, label: value }))
const challengeTypeOptions = Object.values(ChallengeType).map((value) => ({ value, label: value }))
const environmentOptions = Object.values(EnvironmentType).map((value) => ({ value, label: value }))
const networkModeOptions = Object.values(NetworkMode).map((value) => ({ value, label: value }))
const courseTabValues = ['intro', 'chapters', 'resources', 'students', 'environments', 'challenges', 'theory-bank', 'homework']

const formatSize = (bytes: number) => {
  if (!Number.isFinite(bytes) || bytes <= 0) return '-'
  const units = ['B', 'KB', 'MB', 'GB']
  let value = bytes
  let index = 0
  while (value >= 1024 && index < units.length - 1) {
    value /= 1024
    index += 1
  }
  return `${value.toFixed(index === 0 ? 0 : 1)} ${units[index]}`
}

const CourseDetail: FC = () => {
  const { courseId } = useParams()
  const location = useLocation()
  const navigate = useNavigate()
  const id = Number(courseId)
  const [course, setCourse] = useState<TrainingCourseModel | null>(null)
  const [enrollments, setEnrollments] = useState<Awaited<ReturnType<typeof trainingCourseAdminApi.enrollments>>['data']>([])
  const [activeTab, setActiveTab] = useState<string | null>('intro')
  const [editOpened, setEditOpened] = useState(false)
  const [resourceOpened, setResourceOpened] = useState(false)
  const [dockerRegisterOpened, setDockerRegisterOpened] = useState(false)
  const [dockerUploadOpened, setDockerUploadOpened] = useState(false)
  const [vmUploadOpened, setVmUploadOpened] = useState(false)
  const [localImportOpened, setLocalImportOpened] = useState(false)
  const [challengeOpened, setChallengeOpened] = useState(false)
  const [editingChallengeId, setEditingChallengeId] = useState<number | null>(null)
  const [editingChallengeDetail, setEditingChallengeDetail] = useState<TrainingCourseChallengeEditDetailModel | null>(null)
  const [challengeAttachmentFile, setChallengeAttachmentFile] = useState<File | null>(null)
  const [courseDraft, setCourseDraft] = useState<TrainingCourseEditModel>(emptyCourseDraft())
  const [resourceDraft, setResourceDraft] = useState<TrainingCourseResourceEditModel>(emptyResourceDraft())
  const [challengeDraft, setChallengeDraft] = useState<TrainingCourseChallengeCreateModel>(emptyChallengeDraft())
  const [dockerRegisterDraft, setDockerRegisterDraft] = useState<TrainingCourseDockerRegisterModel>(emptyDockerRegisterDraft())
  const [localImportDraft, setLocalImportDraft] = useState<TrainingCourseLocalImageImportModel>(emptyLocalImportDraft())
  const [dockerArchiveFile, setDockerArchiveFile] = useState<File | null>(null)
  const [dockerArchiveName, setDockerArchiveName] = useState('')
  const [dockerArchiveRepository, setDockerArchiveRepository] = useState('')
  const [dockerArchiveTag, setDockerArchiveTag] = useState('latest')
  const [dockerArchiveSourceImage, setDockerArchiveSourceImage] = useState('')
  const [dockerArchiveOsType, setDockerArchiveOsType] = useState('0')
  const [vmFile, setVmFile] = useState<File | null>(null)
  const [vmArchiveMode, setVmArchiveMode] = useState(false)
  const [templateQuery, setTemplateQuery] = useState('')
  const [dockerRegistry, setDockerRegistry] = useState<TrainingCourseDockerRegistryModel | null>(null)
  const [courseTemplates, setCourseTemplates] = useState<TrainingCourseImageTemplateModel[]>([])
  const [saving, setSaving] = useState(false)
  const [uploading, setUploading] = useState(false)
  const [studentPage, setStudentPage] = useState(1)
  const { t } = useTranslation()

  const canLearn = course?.canLearn || course?.canEdit
  const orderedChapters = useMemo(
    () => [...(course?.chapters ?? [])].sort((a, b) => a.order - b.order || a.id - b.id),
    [course?.chapters]
  )
  const filteredTemplates = useMemo(() => {
    const keyword = templateQuery.trim().toLowerCase()
    if (!keyword) return courseTemplates

    return courseTemplates.filter((template) =>
      [template.name, template.registryUrl, template.imageHash, template.description, String(template.id)]
        .filter(Boolean)
        .some((value) => String(value).toLowerCase().includes(keyword))
    )
  }, [courseTemplates, templateQuery])
  const readyDockerTemplates = useMemo(
    () => courseTemplates.filter((template) => isDockerTemplate(template) && isReadyTemplate(template)),
    [courseTemplates]
  )
  const readyWindowsTemplates = useMemo(
    () => courseTemplates.filter((template) => isWindowsTemplate(template) && isReadyTemplate(template)),
    [courseTemplates]
  )
  const courseStatus = course ? trainingCourseStatus(course) : null
  const progressPercent = course ? trainingCourseProgress(course) : 0
  const studentPageSize = 6
  const studentPageCount = Math.max(1, Math.ceil(enrollments.length / studentPageSize))
  const visibleEnrollments = useMemo(
    () => enrollments.slice((studentPage - 1) * studentPageSize, studentPage * studentPageSize),
    [enrollments, studentPage]
  )

  const load = async () => {
    if (!Number.isFinite(id)) return
    try {
      const res = await trainingCourseApi.course(id)
      setCourse(res.data)
      setCourseDraft({
        title: res.data.title,
        slug: res.data.slug,
        summary: res.data.summary,
        description: res.data.description,
        coverFileHash: res.data.coverFileHash,
        tags: res.data.tags,
        enrollmentPolicy: res.data.enrollmentPolicy,
      })
      if (res.data.canManageEnrollments) {
        const enrollmentRes = await trainingCourseAdminApi.enrollments(id)
        setEnrollments(enrollmentRes.data)
      }
      if (res.data.canEdit) {
        const [courseTemplateRes, registryRes] = await Promise.all([
          trainingCourseAdminApi.imageTemplates(id),
          trainingCourseAdminApi.dockerRegistry(id),
        ])
        setCourseTemplates(courseTemplateRes.data)
        setDockerRegistry(registryRes.data)
      } else {
        setCourseTemplates([])
        setDockerRegistry(null)
      }
    } catch (e) {
      showErrorMsg(e, t)
    }
  }

  const uploadOne = async (file: File | null) => {
    if (!file) return null
    const res = await api.assets.assetsUpload({ files: [file] }, { filename: file.name })
    return res.data?.[0]?.hash ?? null
  }

  const enroll = async () => {
    if (!course) return
    try {
      await trainingCourseApi.enroll(course.id, { applyReason: '' })
      showNotification({ color: 'teal', message: '报名已提交' })
      await load()
    } catch (e) {
      showErrorMsg(e, t)
    }
  }

  const persistCourse = async (closeModal: boolean) => {
    if (!course) return
    setSaving(true)
    try {
      await trainingCourseAdminApi.updateCourse(course.id, courseDraft)
      if (closeModal) setEditOpened(false)
      showNotification({ color: 'teal', message: '课程信息已保存。' })
      await load()
    } catch (e) {
      showErrorMsg(e, t)
    } finally {
      setSaving(false)
    }
  }

  const saveCourse = async () => persistCourse(true)

  const saveResource = async () => {
    if (!course || !resourceDraft.title.trim()) return
    setSaving(true)
    try {
      await trainingCourseAdminApi.createResource(course.id, resourceDraft)
      setResourceOpened(false)
      setResourceDraft(emptyResourceDraft())
      await load()
    } catch (e) {
      showErrorMsg(e, t)
    } finally {
      setSaving(false)
    }
  }

  const reviewEnrollment = async (userId: string, status: TrainingCourseEnrollmentStatus) => {
    if (!course) return
    try {
      await trainingCourseAdminApi.reviewEnrollment(course.id, userId, { status, reviewComment: '' })
      await load()
    } catch (e) {
      showErrorMsg(e, t)
    }
  }

  const refreshTemplates = async () => {
    if (!course) return
    try {
      const [templateRes, registryRes] = await Promise.all([
        trainingCourseAdminApi.imageTemplates(course.id),
        trainingCourseAdminApi.dockerRegistry(course.id),
      ])
      setCourseTemplates(templateRes.data)
      setDockerRegistry(registryRes.data)
    } catch (e) {
      showErrorMsg(e, t)
    }
  }

  const parseUploadError = async (response: Response, fallback: string) => {
    const data = await response.json().catch(() => ({}))
    return data?.message || data?.title || fallback
  }

  const registerDockerTemplate = async () => {
    if (!course || !dockerRegisterDraft.name.trim() || !dockerRegisterDraft.registryUrl.trim()) return
    setSaving(true)
    try {
      await trainingCourseAdminApi.registerDockerTemplate(course.id, {
        ...dockerRegisterDraft,
        name: dockerRegisterDraft.name.trim(),
        registryUrl: dockerRegisterDraft.registryUrl.trim(),
        registryAuth: dockerRegisterDraft.registryAuth?.trim() || null,
      })
      showNotification({ color: 'teal', message: 'Docker 镜像已注册，后台正在拉取。' })
      setDockerRegisterDraft(emptyDockerRegisterDraft())
      setDockerRegisterOpened(false)
      await refreshTemplates()
    } catch (e) {
      showErrorMsg(e, t)
    } finally {
      setSaving(false)
    }
  }

  const uploadDockerTemplate = async () => {
    if (!course || !dockerArchiveFile || !dockerArchiveName.trim() || !dockerArchiveRepository.trim()) return
    setUploading(true)
    try {
      const formData = new FormData()
      formData.append('file', dockerArchiveFile)
      formData.append('name', dockerArchiveName.trim())
      formData.append('repository', dockerArchiveRepository.trim())
      formData.append('tag', dockerArchiveTag.trim() || 'latest')
      formData.append('sourceImage', dockerArchiveSourceImage.trim())
      formData.append('osType', dockerArchiveOsType)

      const response = await fetch(`/api/admin/training/courses/${course.id}/image-templates/upload-docker`, {
        method: 'POST',
        body: formData,
      })
      if (!response.ok) throw new Error(await parseUploadError(response, 'Docker 镜像包上传失败。'))

      showNotification({ color: 'teal', message: 'Docker 镜像包已导入课程。' })
      setDockerArchiveFile(null)
      setDockerArchiveName('')
      setDockerArchiveRepository('')
      setDockerArchiveTag('latest')
      setDockerArchiveSourceImage('')
      setDockerArchiveOsType('0')
      setDockerUploadOpened(false)
      await refreshTemplates()
    } catch (e) {
      showErrorMsg(e, t)
    } finally {
      setUploading(false)
    }
  }

  const uploadVmTemplate = async () => {
    if (!course || !vmFile) return
    setUploading(true)
    try {
      const formData = new FormData()
      formData.append('file', vmFile)
      const endpoint = vmArchiveMode ? 'upload-vm-archive' : 'upload-vm'
      const response = await fetch(`/api/admin/training/courses/${course.id}/image-templates/${endpoint}`, {
        method: 'POST',
        body: formData,
      })
      if (!response.ok) throw new Error(await parseUploadError(response, 'VM 镜像上传失败。'))

      showNotification({ color: 'teal', message: 'VM 镜像已导入课程。' })
      setVmFile(null)
      setVmArchiveMode(false)
      setVmUploadOpened(false)
      await refreshTemplates()
    } catch (e) {
      showErrorMsg(e, t)
    } finally {
      setUploading(false)
    }
  }

  const importLocalTemplate = async () => {
    if (!course || !localImportDraft.localPath.trim()) return
    setSaving(true)
    try {
      await trainingCourseAdminApi.importLocalTemplate(course.id, {
        localPath: localImportDraft.localPath.trim(),
        displayName: localImportDraft.displayName?.trim() || null,
      })
      showNotification({ color: 'teal', message: '服务器本地镜像已导入课程。' })
      setLocalImportDraft(emptyLocalImportDraft())
      setLocalImportOpened(false)
      await refreshTemplates()
    } catch (e) {
      showErrorMsg(e, t)
    } finally {
      setSaving(false)
    }
  }

  const openCreateCourseChallenge = () => {
    setEditingChallengeId(null)
    setEditingChallengeDetail(null)
    setChallengeAttachmentFile(null)
    setChallengeDraft({
      ...emptyChallengeDraft(),
      order: (course?.challenges?.length ?? 0) + 1,
    })
    setChallengeOpened(true)
  }

  const openEditCourseChallenge = async (exerciseChallengeId: number) => {
    if (!course) return
    setSaving(true)
    try {
      const res = await trainingCourseAdminApi.challengeEditDetail(course.id, exerciseChallengeId)
      setEditingChallengeId(exerciseChallengeId)
      setEditingChallengeDetail(res.data)
      setChallengeAttachmentFile(null)
      setChallengeDraft({
        ...emptyChallengeDraft(),
        ...res.data,
        attachmentType: res.data.attachmentType ?? FileType.None,
        attachmentFileHash: res.data.attachmentFileHash ?? null,
        attachmentRemoteUrl: res.data.attachmentRemoteUrl ?? null,
      })
      setChallengeOpened(true)
    } catch (e) {
      showErrorMsg(e, t)
    } finally {
      setSaving(false)
    }
  }

  const closeChallengeEditor = () => {
    setChallengeOpened(false)
    setEditingChallengeId(null)
    setEditingChallengeDetail(null)
    setChallengeAttachmentFile(null)
    setChallengeDraft(emptyChallengeDraft())
  }

  const saveCourseChallenge = async () => {
    if (!course || !challengeDraft.title.trim()) return
    setSaving(true)
    try {
      const attachmentFileHash =
        challengeDraft.attachmentType === FileType.Local && challengeAttachmentFile
          ? await uploadOne(challengeAttachmentFile)
          : challengeDraft.attachmentFileHash
      const payload: TrainingCourseChallengeCreateModel = {
        ...challengeDraft,
        attachmentType: challengeDraft.attachmentType ?? FileType.None,
        attachmentFileHash: challengeDraft.attachmentType === FileType.Local ? attachmentFileHash : null,
        attachmentRemoteUrl:
          challengeDraft.attachmentType === FileType.Remote ? challengeDraft.attachmentRemoteUrl?.trim() || null : null,
      }

      if (editingChallengeId) {
        await trainingCourseAdminApi.updateChallenge(course.id, editingChallengeId, {
          ...payload,
          title: payload.title.trim(),
          content: payload.content,
          containerImage: payload.containerImage?.trim() || null,
          flagTemplate: payload.flagTemplate?.trim() || null,
          staticFlag: payload.staticFlag?.trim() || null,
          displayTitle: payload.displayTitle?.trim() || null,
          order: payload.order || 1,
        })
        showNotification({ color: 'teal', message: '课程题目已更新。' })
      } else {
        await trainingCourseAdminApi.createChallenge(course.id, {
          ...payload,
          title: payload.title.trim(),
          content: payload.content,
          containerImage: payload.containerImage?.trim() || null,
          flagTemplate: payload.flagTemplate?.trim() || null,
          staticFlag: payload.staticFlag?.trim() || null,
          displayTitle: payload.displayTitle?.trim() || null,
          order: payload.order || (course.challenges?.length ?? 0) + 1,
        })
        showNotification({ color: 'teal', message: '课程题目已创建。' })
      }

      closeChallengeEditor()
      await load()
    } catch (e) {
      showErrorMsg(e, t)
    } finally {
      setSaving(false)
    }
  }

  const handleTabChange = (value: string | null) => {
    const next = value && courseTabValues.includes(value) ? value : 'intro'
    setActiveTab(next)

    const params = new URLSearchParams(location.search)
    if (next === 'intro') {
      params.delete('tab')
    } else {
      params.set('tab', next)
    }

    const search = params.toString()
    navigate(
      {
        pathname: location.pathname,
        search: search ? `?${search}` : '',
      },
      { replace: true }
    )
  }

  useEffect(() => {
    void load()
  }, [courseId])

  useEffect(() => {
    const tab = new URLSearchParams(location.search).get('tab')
    const next = tab && courseTabValues.includes(tab) ? tab : 'intro'
    setActiveTab((current) => (current === next ? current : next))
  }, [location.search])

  useEffect(() => {
    if (studentPage > studentPageCount) setStudentPage(studentPageCount)
  }, [studentPage, studentPageCount])

  if (!course) {
    return (
      <WithNavBar isLoading width="min(112rem, calc(100vw - 4rem))">
        <></>
      </WithNavBar>
    )
  }

  return (
    <WithNavBar width="min(100%, calc(100vw - 7.25rem))" minWidth={0}>
      <Box className="yy-training-page yy-course-detail">
        <YinyuGameBendsBackground className="yy-training-bg" />
        <Button component={Link} to="/training" variant="subtle" leftSection={<Icon path={mdiArrowLeft} size={0.85} />}>
          培训课程
        </Button>

        <YinyuPanel p="lg" className="yy-course-detail-hero yy-training-course-detail-hero">
          <div className="yy-course-detail-hero-grid">
            <div
              className="yy-course-detail-cover"
              style={course.coverUrl ? { backgroundImage: `url(${course.coverUrl})` } : undefined}
            >
              {!course.coverUrl ? <span>YINYU TRAINING</span> : null}
            </div>

            <Stack gap="md" className="yy-course-detail-hero-body">
              <Group justify="space-between" align="flex-start" gap="md">
                <Group gap="md">
                  {courseStatus ? <TrainingStatusText tone={courseStatus.tone}>{courseStatus.label}</TrainingStatusText> : null}
                  <TrainingTagLine tags={trainingTags(course)} max={5} />
                </Group>
                <TrainingStatusText tone="ongoing">{progressPercent}%</TrainingStatusText>
              </Group>

              <Stack gap="xs">
                <Title order={1}>{course.title}</Title>
                <Text c="dimmed" maw="62rem">
                  {course.summary || '暂无课程摘要。'}
                </Text>
              </Stack>

              <Group gap="xl">
                <Text size="sm" c="dimmed">
                  授课：{trainingTeacherNames(course)}
                </Text>
                <Text size="sm" c="dimmed">
                  章节：{course.completedChapterCount}/{course.totalChapterCount || course.chapterCount}
                </Text>
                <Text size="sm" c="dimmed">
                  资源：{course.resourceCount} 份
                </Text>
              </Group>

              <Group gap="xs" mt="auto">
                {!canLearn && course.status === TrainingCourseStatus.Published ? <Button onClick={enroll}>报名课程</Button> : null}
                {course.canEdit ? (
                  <>
                    <Button
                      variant="light"
                      leftSection={<Icon path={mdiPencilOutline} size={0.82} />}
                      onClick={() => setEditOpened(true)}
                    >
                      编辑课程
                    </Button>
                    {course.status !== TrainingCourseStatus.Published ? (
                      <>
                        <Button
                          leftSection={<Icon path={mdiPublish} size={0.82} />}
                          onClick={() => trainingCourseAdminApi.publish(course.id).then(load).catch((e) => showErrorMsg(e, t))}
                        >
                          发布
                        </Button>
                        {course.status === TrainingCourseStatus.Draft ? (
                          <Button
                            color="orange"
                            variant="light"
                            leftSection={<Icon path={mdiArchiveOutline} size={0.82} />}
                            onClick={() => trainingCourseAdminApi.archive(course.id).then(load).catch((e) => showErrorMsg(e, t))}
                          >
                            归档
                          </Button>
                        ) : null}
                      </>
                    ) : (
                      <Button
                        color="orange"
                        variant="light"
                        leftSection={<Icon path={mdiArchiveOutline} size={0.82} />}
                        onClick={() => trainingCourseAdminApi.archive(course.id).then(load).catch((e) => showErrorMsg(e, t))}
                      >
                        归档
                      </Button>
                    )}
                  </>
                ) : null}
              </Group>
            </Stack>
          </div>
        </YinyuPanel>

        <Tabs value={activeTab} onChange={handleTabChange} className="yy-course-tabs yy-training-detail-tabs">
          <div className="yy-training-detail-grid">
            <main className="yy-training-detail-main">
              <Tabs.List>
                <Tabs.Tab value="intro">课程介绍</Tabs.Tab>
                <Tabs.Tab value="chapters">课程列表</Tabs.Tab>
                <Tabs.Tab value="resources">课程资源</Tabs.Tab>
                {course.canManageEnrollments ? <Tabs.Tab value="students">学员管理</Tabs.Tab> : null}
                {course.canEdit ? <Tabs.Tab value="environments">环境模板</Tabs.Tab> : null}
                {course.canEdit ? <Tabs.Tab value="challenges">题目管理</Tabs.Tab> : null}
                {course.canEdit ? <Tabs.Tab value="theory-bank">理论题库</Tabs.Tab> : null}
                {course.canEdit ? <Tabs.Tab value="homework">课后练习</Tabs.Tab> : null}
              </Tabs.List>

          <Tabs.Panel value="intro" pt="md">
            <YinyuPanel p="lg">
              <Markdown source={course.description || course.summary || '暂无课程介绍。'} />
            </YinyuPanel>
          </Tabs.Panel>

          <Tabs.Panel value="chapters" pt="md">
            <YinyuPanel p="lg">
              <Group justify="space-between" mb="md">
                <Title order={3}>课程列表</Title>
                {course.canEdit ? (
                  <Button
                    component={Link}
                    to={`/training/courses/${course.id}/chapters/new`}
                    leftSection={<Icon path={mdiPlus} size={0.82} />}
                  >
                    添加章节
                  </Button>
                ) : null}
              </Group>
              <Stack gap="sm">
                {orderedChapters.map((chapter) => (
                  <YinyuPanel key={chapter.id} p="md" className="yy-course-row-card">
                    <Group justify="space-between" align="center">
                      <Stack gap={4}>
                        <Group gap="xs">
                          <Badge variant="light" color={chapter.isPublished ? 'teal' : 'gray'}>
                            {chapter.isPublished ? '已发布' : '未发布'}
                          </Badge>
                          {chapter.progressStatus ? <Badge color="green">{chapter.progressStatus}</Badge> : null}
                        </Group>
                        <Title order={4}>{chapter.title}</Title>
                        <Text size="sm" c="dimmed">
                          {chapter.summary || '暂无章节摘要'}
                        </Text>
                      </Stack>
                      <Group gap="xs" wrap="nowrap">
                        {course.canEdit ? (
                          <Button
                            component={Link}
                            to={`/training/courses/${course.id}/chapters/${chapter.id}/edit`}
                            variant="light"
                            leftSection={<Icon path={mdiPencilOutline} size={0.82} />}
                          >
                            编辑
                          </Button>
                        ) : null}
                        <Button
                          component={Link}
                          to={`/training/courses/${course.id}/chapters/${chapter.id}`}
                          rightSection={<Icon path={mdiBookOpenPageVariantOutline} size={0.82} />}
                          disabled={!canLearn}
                        >
                          查看章节
                        </Button>
                      </Group>
                    </Group>
                  </YinyuPanel>
                ))}
                {orderedChapters.length === 0 ? <Text c="dimmed">暂无章节</Text> : null}
              </Stack>
            </YinyuPanel>
          </Tabs.Panel>

          <Tabs.Panel value="resources" pt="md">
            <YinyuPanel p="lg">
              <Group justify="space-between" mb="md">
                <Title order={3}>课程资源</Title>
                {course.canEdit ? (
                  <Button leftSection={<Icon path={mdiPlus} size={0.82} />} onClick={() => setResourceOpened(true)}>
                    添加资源
                  </Button>
                ) : null}
              </Group>
              <Table.ScrollContainer minWidth={760}>
                <Table verticalSpacing="sm">
                  <Table.Thead>
                    <Table.Tr>
                      <Table.Th>资源</Table.Th>
                      <Table.Th>类型</Table.Th>
                      <Table.Th>大小</Table.Th>
                      <Table.Th>操作</Table.Th>
                    </Table.Tr>
                  </Table.Thead>
                  <Table.Tbody>
                    {course.resources.map((resource) => {
                      const isExternal = resource.type === TrainingCourseResourceType.Link || resource.type === TrainingCourseResourceType.Video

                      return (
                        <Table.Tr key={resource.id}>
                          <Table.Td>
                            <Text fw={800}>{resource.title}</Text>
                            <Text size="xs" c="dimmed">
                              {resource.description || resource.fileName || resource.externalUrl || '-'}
                            </Text>
                          </Table.Td>
                          <Table.Td>{resource.type}</Table.Td>
                          <Table.Td>{resource.fileSize ? `${Math.round(resource.fileSize / 1024)} KB` : '-'}</Table.Td>
                          <Table.Td>
                            {resource.downloadUrl ? (
                              <Button
                                component="a"
                                href={`/api/training/courses/${course.id}/resources/${resource.id}/download`}
                                target="_blank"
                                rel="noopener noreferrer"
                                variant="light"
                                leftSection={<Icon path={isExternal ? mdiOpenInNew : mdiDownloadOutline} size={0.82} />}
                              >
                                {isExternal ? '打开' : '下载'}
                              </Button>
                            ) : (
                              <Text size="sm" c="dimmed">
                                未开放
                              </Text>
                            )}
                          </Table.Td>
                        </Table.Tr>
                      )
                    })}
                  </Table.Tbody>
                </Table>
              </Table.ScrollContainer>
            </YinyuPanel>
          </Tabs.Panel>

          <Tabs.Panel value="students" pt="md">
            <YinyuPanel p="lg">
              <Title order={3} mb="md">
                学员管理
              </Title>
              <Table.ScrollContainer minWidth={820}>
                <Table verticalSpacing="sm">
                  <Table.Thead>
                    <Table.Tr>
                      <Table.Th>学员</Table.Th>
                      <Table.Th>学号</Table.Th>
                      <Table.Th>状态</Table.Th>
                      <Table.Th>操作</Table.Th>
                    </Table.Tr>
                  </Table.Thead>
                  <Table.Tbody>
                    {enrollments.map((enrollment) => (
                      <Table.Tr key={enrollment.userId}>
                        <Table.Td>
                          <Text fw={800}>{enrollment.realName || enrollment.userName}</Text>
                          <Text size="xs" c="dimmed">
                            {enrollment.userName}
                          </Text>
                        </Table.Td>
                        <Table.Td>{enrollment.stdNumber || '-'}</Table.Td>
                        <Table.Td>{enrollment.status}</Table.Td>
                        <Table.Td>
                          <Group gap="xs">
                            <ActionIcon
                              color="green"
                              onClick={() => reviewEnrollment(enrollment.userId, TrainingCourseEnrollmentStatus.Approved)}
                            >
                              <Icon path={mdiCheck} size={0.86} />
                            </ActionIcon>
                            <ActionIcon
                              color="red"
                              onClick={() => reviewEnrollment(enrollment.userId, TrainingCourseEnrollmentStatus.Rejected)}
                            >
                              <Icon path={mdiClose} size={0.86} />
                            </ActionIcon>
                          </Group>
                        </Table.Td>
                      </Table.Tr>
                    ))}
                  </Table.Tbody>
                </Table>
              </Table.ScrollContainer>
            </YinyuPanel>
          </Tabs.Panel>

          <Tabs.Panel value="environments" pt="md">
            <YinyuPanel p="lg">
              <Group justify="space-between" mb="md">
                <Title order={3}>环境模板</Title>
                <Group gap="xs">
                  <Button variant="light" leftSection={<Icon path={mdiRefresh} size={0.82} />} onClick={refreshTemplates}>
                    刷新
                  </Button>
                  <Button leftSection={<Icon path={mdiDocker} size={0.82} />} onClick={() => setDockerRegisterOpened(true)}>
                    注册 Docker
                  </Button>
                  <Button
                    variant="light"
                    leftSection={<Icon path={mdiArchiveArrowUpOutline} size={0.82} />}
                    onClick={() => setDockerUploadOpened(true)}
                  >
                    上传 Docker 包
                  </Button>
                  <Button variant="light" leftSection={<Icon path={mdiCubeOutline} size={0.82} />} onClick={() => setVmUploadOpened(true)}>
                    上传 VM 镜像
                  </Button>
                  <Button
                    variant="light"
                    leftSection={<Icon path={mdiFileImportOutline} size={0.82} />}
                    onClick={() => setLocalImportOpened(true)}
                  >
                    本地导入
                  </Button>
                </Group>
              </Group>
              <Alert color="blue" variant="light" mb="md">
                {dockerRegistry?.enabled
                  ? `内网 Registry：${dockerRegistry.address}${dockerRegistry.namespace ? `/${dockerRegistry.namespace}` : ''}`
                  : '内网 Docker Registry 未配置。'}
              </Alert>
              <TextInput
                mb="md"
                leftSection={<Icon path={mdiMagnify} size={0.82} />}
                placeholder="搜索模板名称、Registry、Hash 或说明"
                value={templateQuery}
                onChange={(event) => setTemplateQuery(event.currentTarget.value)}
              />
              <Table.ScrollContainer minWidth={900}>
                <Table verticalSpacing="sm">
                  <Table.Thead>
                    <Table.Tr>
                      <Table.Th>模板</Table.Th>
                      <Table.Th>类型</Table.Th>
                      <Table.Th>状态</Table.Th>
                      <Table.Th>大小</Table.Th>
                      <Table.Th>来源</Table.Th>
                      <Table.Th>操作</Table.Th>
                    </Table.Tr>
                  </Table.Thead>
                  <Table.Tbody>
                    {filteredTemplates.map((template) => {
                      const status = statusInfo(template.status)
                      return (
                        <Table.Tr key={template.id}>
                          <Table.Td>
                            <Text fw={800}>{template.name}</Text>
                            <Text size="xs" c="dimmed">
                              #{template.id}
                            </Text>
                          </Table.Td>
                          <Table.Td>
                            <Group gap="xs">
                              <Icon path={isDockerTemplate(template) ? mdiDocker : mdiCubeOutline} size={0.86} />
                              <Text size="sm">
                                {imageTypeText(template.imageType)} / {osTypeText(template.osType)}
                              </Text>
                            </Group>
                          </Table.Td>
                          <Table.Td>
                            <Badge color={status.color} variant="light">
                              {status.label}
                            </Badge>
                            {template.errorMessage ? (
                              <Text size="xs" c="red.3" mt={4} lineClamp={2} title={template.errorMessage}>
                                {template.errorMessage}
                              </Text>
                            ) : null}
                          </Table.Td>
                          <Table.Td>{formatSize(template.fileSize)}</Table.Td>
                          <Table.Td>
                            <Text size="xs" c="dimmed" maw={420} truncate>
                              {template.registryUrl || template.imageHash || template.description || '-'}
                            </Text>
                          </Table.Td>
                          <Table.Td>
                            <Button
                              color="red"
                              variant="subtle"
                              onClick={() =>
                                trainingCourseAdminApi
                                  .detachImageTemplate(course.id, template.id)
                                  .then(refreshTemplates)
                                  .catch((e) => showErrorMsg(e, t))
                              }
                            >
                              移除
                            </Button>
                          </Table.Td>
                        </Table.Tr>
                      )
                    })}
                  </Table.Tbody>
                </Table>
              </Table.ScrollContainer>
              {filteredTemplates.length === 0 ? (
                <Text c="dimmed" ta="center" py="lg">
                  当前课程暂无环境模板
                </Text>
              ) : null}
            </YinyuPanel>
          </Tabs.Panel>

          <Tabs.Panel value="challenges" pt="md">
            <YinyuPanel p="lg">
              <Group justify="space-between" mb="md">
                <Title order={3}>题目管理</Title>
                <Button leftSection={<Icon path={mdiPlus} size={0.82} />} onClick={openCreateCourseChallenge}>
                  创建课程题目
                </Button>
              </Group>
              <SimpleGrid cols={{ base: 1, md: 2 }} spacing="sm">
                {course.challenges.map((challenge) => (
                  <YinyuPanel key={challenge.exerciseChallengeId} p="md">
                    <Group justify="space-between">
                      <Stack gap={4}>
                        <Text fw={900}>{challenge.displayTitle || challenge.title}</Text>
                        <Text size="xs" c="dimmed">
                          #{challenge.exerciseChallengeId} / {challenge.category} / {challenge.type}
                        </Text>
                        {challenge.hasAttachment ? (
                          <Badge variant="light" color="indigo">
                            附件：{challenge.attachmentFileName || '已绑定'}
                          </Badge>
                        ) : null}
                      </Stack>
                      <Group gap={6} wrap="nowrap">
                        <ActionIcon variant="light" onClick={() => openEditCourseChallenge(challenge.exerciseChallengeId)}>
                          <Icon path={mdiPencilOutline} size={0.86} />
                        </ActionIcon>
                        <ActionIcon
                          color="red"
                          onClick={() =>
                            trainingCourseAdminApi
                              .removeChallenge(course.id, challenge.exerciseChallengeId)
                              .then(load)
                              .catch((e) => showErrorMsg(e, t))
                          }
                        >
                          <Icon path={mdiTrashCanOutline} size={0.86} />
                        </ActionIcon>
                      </Group>
                    </Group>
                  </YinyuPanel>
                ))}
              </SimpleGrid>
            </YinyuPanel>
          </Tabs.Panel>

          <Tabs.Panel value="theory-bank" pt="md">
            <CourseTheoryBankPanel courseId={course.id} />
          </Tabs.Panel>

          <Tabs.Panel value="homework" pt="md">
            <YinyuPanel p="lg">
              <Group justify="space-between" mb="md">
                <Stack gap={2}>
                  <Title order={3}>课后练习</Title>
                  <Text size="sm" c="dimmed">
                    每个章节最多绑定一套理论测试，题目来源于当前课程题库。
                  </Text>
                </Stack>
              </Group>
              <Stack gap="sm">
                {orderedChapters.map((chapter) => {
                  const theory = chapter.theoryPaper
                  return (
                    <YinyuPanel key={chapter.id} p="md" className="yy-course-row-card">
                      <Group justify="space-between" align="center">
                        <Stack gap={4}>
                          <Group gap="xs">
                            <Badge variant="light" color={chapter.isPublished ? 'teal' : 'gray'}>
                              {chapter.isPublished ? '已发布章节' : '未发布章节'}
                            </Badge>
                            {theory ? (
                              <Badge variant="light" color={theory.isPublished ? 'green' : 'yellow'}>
                                {theory.isPublished ? '测试已发放' : '测试草稿'}
                              </Badge>
                            ) : (
                              <Badge variant="light" color="gray">
                                未配置测试
                              </Badge>
                            )}
                          </Group>
                          <Text fw={900}>{chapter.title}</Text>
                          <Text size="sm" c="dimmed">
                            {theory
                              ? `${theory.questionCount} 题 / ${theory.totalScore} 分 / 及格线 ${theory.passRate}%`
                              : '可从课程题库中指定题目或随机抽题生成课后测试。'}
                          </Text>
                        </Stack>
                        <Button
                          component={Link}
                          to={`/training/courses/${course.id}/chapters/${chapter.id}/theory-edit`}
                          variant="light"
                          leftSection={<Icon path={mdiPencilOutline} size={0.82} />}
                        >
                          配置测试
                        </Button>
                      </Group>
                    </YinyuPanel>
                  )
                })}
                {orderedChapters.length === 0 ? (
                  <Text c="dimmed" ta="center" py="lg">
                    还没有章节，先创建章节后再配置课后测试。
                  </Text>
                ) : null}
              </Stack>
            </YinyuPanel>
          </Tabs.Panel>
            </main>

            <aside className="yy-training-detail-aside">
              <YinyuPanel p="lg">
                <Stack gap="md">
                  <Stack gap={2}>
                    <Text className="yy-section-kicker">Progress</Text>
                    <Title order={3}>学习状态</Title>
                  </Stack>
                  {course.canManageEnrollments ? (
                    <>
                      <Stack gap="xs" className="yy-training-student-progress-list">
                        {visibleEnrollments.map((enrollment) => {
                          const status = enrollmentStatusInfo(enrollment.status)
                          const total = enrollment.totalChapterCount || course.totalChapterCount || course.chapterCount || 0
                          const completed = enrollment.completedChapterCount ?? 0
                          const percent = total > 0 ? Math.max(0, Math.min(100, Math.round((completed / total) * 100))) : 0

                          return (
                            <div key={enrollment.userId} className="yy-training-student-progress-row">
                              <Group justify="space-between" align="flex-start" gap="xs" wrap="nowrap">
                                <Stack gap={1} miw={0}>
                                  <Text fw={900} lineClamp={1}>
                                    {enrollment.realName || enrollment.userName}
                                  </Text>
                                  <Text size="xs" c="dimmed" lineClamp={1}>
                                    {enrollment.stdNumber || enrollment.userName}
                                  </Text>
                                </Stack>
                                <Badge color={status.color} variant="light">
                                  {status.label}
                                </Badge>
                              </Group>
                              <Box className="yy-training-course-progress">
                                <Group justify="space-between" mb={5}>
                                  <Text size="xs" c="dimmed" fw={800}>
                                    进度
                                  </Text>
                                  <Text size="xs" fw={950}>
                                    {completed}/{total}
                                  </Text>
                                </Group>
                                <Progress value={percent} radius="xl" size="sm" color="teal" />
                              </Box>
                            </div>
                          )
                        })}
                        {enrollments.length === 0 ? (
                          <Text size="sm" c="dimmed">
                            暂无学员报名。
                          </Text>
                        ) : null}
                      </Stack>
                      {studentPageCount > 1 ? (
                        <Pagination total={studentPageCount} value={studentPage} onChange={setStudentPage} size="sm" />
                      ) : null}
                    </>
                  ) : (
                    <>
                      <Box className="yy-training-course-progress">
                        <Group justify="space-between" mb={5}>
                          <Text size="xs" c="dimmed" fw={800}>我的课程进度</Text>
                          <Text size="xs" fw={950}>{course.completedChapterCount}/{course.totalChapterCount || course.chapterCount}</Text>
                        </Group>
                        <Progress value={progressPercent} radius="xl" size="sm" color="teal" />
                      </Box>
                    </>
                  )}
                </Stack>
              </YinyuPanel>
            </aside>
          </div>
        </Tabs>
      </Box>

      <Modal opened={editOpened} onClose={() => setEditOpened(false)} title="编辑课程" size="lg">
        <Stack>
          <TextInput label="课程名称" value={courseDraft.title} onChange={(e) => setCourseDraft({ ...courseDraft, title: e.currentTarget.value })} />
          <TextInput
            label="标签"
            value={courseDraft.tags.join('，')}
            onChange={(e) =>
              setCourseDraft({
                ...courseDraft,
                tags: e.currentTarget.value
                  .split(/[，,]/)
                  .map((tag) => tag.trim())
                  .filter(Boolean),
              })
            }
          />
          <Textarea label="摘要" minRows={3} value={courseDraft.summary} onChange={(e) => setCourseDraft({ ...courseDraft, summary: e.currentTarget.value })} />
          <Textarea
            label="介绍 Markdown"
            minRows={8}
            value={courseDraft.description}
            onChange={(e) => setCourseDraft({ ...courseDraft, description: e.currentTarget.value })}
          />
          <Group justify="space-between">
            <FileButton
              onChange={(file) =>
                uploadOne(file).then((hash) => {
                  if (hash) setCourseDraft((current) => ({ ...current, coverFileHash: hash }))
                })
              }
              accept="image/png,image/jpeg,image/webp"
            >
              {(props) => <Button {...props}>上传海报</Button>}
            </FileButton>
            <Button loading={saving} leftSection={<Icon path={mdiContentSaveOutline} size={0.82} />} onClick={saveCourse}>
              保存
            </Button>
          </Group>
        </Stack>
      </Modal>

      <Modal opened={resourceOpened} onClose={() => setResourceOpened(false)} title="添加资源" size="lg">
        <Stack>
          <TextInput label="资源名称" value={resourceDraft.title} onChange={(e) => setResourceDraft({ ...resourceDraft, title: e.currentTarget.value })} />
          <Textarea label="说明" minRows={2} value={resourceDraft.description} onChange={(e) => setResourceDraft({ ...resourceDraft, description: e.currentTarget.value })} />
          <Select
            label="资源类型"
            value={resourceDraft.type}
            data={[
              { value: TrainingCourseResourceType.File, label: '本地文件' },
              { value: TrainingCourseResourceType.Link, label: '外链' },
              { value: TrainingCourseResourceType.Video, label: '视频链接' },
            ]}
            onChange={(value) => setResourceDraft({ ...resourceDraft, type: (value as TrainingCourseResourceType) ?? TrainingCourseResourceType.File })}
          />
          {resourceDraft.type === TrainingCourseResourceType.File ? (
            <FileButton
              onChange={(file) =>
                uploadOne(file).then((hash) => {
                  if (hash) setResourceDraft((current) => ({ ...current, localFileHash: hash }))
                })
              }
            >
              {(props) => <Button {...props}>上传文件</Button>}
            </FileButton>
          ) : (
            <TextInput label="资源外链" value={resourceDraft.externalUrl ?? ''} onChange={(e) => setResourceDraft({ ...resourceDraft, externalUrl: e.currentTarget.value })} />
          )}
          <Group grow>
            <NumberInput label="排序" value={resourceDraft.order} onChange={(value) => setResourceDraft({ ...resourceDraft, order: Number(value) || 1 })} />
            <Switch label="显示资源" checked={resourceDraft.isVisible} onChange={(e) => setResourceDraft({ ...resourceDraft, isVisible: e.currentTarget.checked })} />
          </Group>
          <Group justify="flex-end">
            <Button loading={saving} onClick={saveResource}>
              保存
            </Button>
          </Group>
        </Stack>
      </Modal>

      <Modal opened={dockerRegisterOpened} onClose={() => setDockerRegisterOpened(false)} title="注册 Docker 镜像" size="lg">
        <Stack>
          <TextInput
            label="模板名称"
            required
            value={dockerRegisterDraft.name}
            onChange={(e) => setDockerRegisterDraft({ ...dockerRegisterDraft, name: e.currentTarget.value })}
            placeholder="web-ssti-demo"
          />
          <TextInput
            label="Docker 镜像地址"
            required
            value={dockerRegisterDraft.registryUrl}
            onChange={(e) => setDockerRegisterDraft({ ...dockerRegisterDraft, registryUrl: e.currentTarget.value })}
            placeholder="docker.io/library/nginx:latest"
          />
          <Group grow>
            <Select
              label="操作系统"
              value={String(dockerRegisterDraft.osType)}
              data={[
                { value: '0', label: 'Linux' },
                { value: '1', label: 'Windows' },
              ]}
              onChange={(value) => setDockerRegisterDraft({ ...dockerRegisterDraft, osType: value ?? '0' })}
            />
            <TextInput
              label="Registry Auth"
              type="password"
              value={dockerRegisterDraft.registryAuth ?? ''}
              onChange={(e) => setDockerRegisterDraft({ ...dockerRegisterDraft, registryAuth: e.currentTarget.value || null })}
              placeholder="公开仓库可留空"
            />
          </Group>
          <Group justify="flex-end">
            <Button loading={saving} leftSection={<Icon path={mdiDocker} size={0.82} />} onClick={registerDockerTemplate}>
              注册并拉取
            </Button>
          </Group>
        </Stack>
      </Modal>

      <Modal opened={dockerUploadOpened} onClose={() => setDockerUploadOpened(false)} title="上传 Docker 镜像包" size="lg">
        <Stack>
          {dockerRegistry?.enabled ? (
            <Alert color="blue" variant="light">
              当前内网 Registry：{dockerRegistry.address}
              {dockerRegistry.namespace ? `/${dockerRegistry.namespace}` : ''}
            </Alert>
          ) : (
            <Alert color="yellow" variant="light">
              内网 Docker Registry 未配置。
            </Alert>
          )}
          <FileInput
            label="Docker 镜像包"
            required
            value={dockerArchiveFile}
            onChange={setDockerArchiveFile}
            accept=".tar,.tar.gz,.tgz"
            placeholder="选择 docker save 导出的 .tar 或 .tgz"
          />
          <TextInput
            label="模板名称"
            required
            value={dockerArchiveName}
            onChange={(e) => setDockerArchiveName(e.currentTarget.value)}
            placeholder="web-flag-demo"
          />
          <Group grow>
            <TextInput
              label="仓库路径"
              required
              value={dockerArchiveRepository}
              onChange={(e) => setDockerArchiveRepository(e.currentTarget.value)}
              placeholder="training/course-web-demo"
            />
            <TextInput label="Tag" value={dockerArchiveTag} onChange={(e) => setDockerArchiveTag(e.currentTarget.value)} placeholder="latest" />
          </Group>
          <TextInput
            label="源镜像名"
            value={dockerArchiveSourceImage}
            onChange={(e) => setDockerArchiveSourceImage(e.currentTarget.value)}
            placeholder="镜像包没有 tag 时填写，例如 local/web:dev"
          />
          <Select
            label="操作系统"
            value={dockerArchiveOsType}
            data={[
              { value: '0', label: 'Linux' },
              { value: '1', label: 'Windows' },
            ]}
            onChange={(value) => setDockerArchiveOsType(value ?? '0')}
          />
          {uploading ? <Progress value={100} striped animated /> : null}
          <Group justify="flex-end">
            <Button
              loading={uploading}
              disabled={!dockerRegistry?.enabled}
              leftSection={<Icon path={mdiArchiveArrowUpOutline} size={0.82} />}
              onClick={uploadDockerTemplate}
            >
              上传并导入
            </Button>
          </Group>
        </Stack>
      </Modal>

      <Modal opened={vmUploadOpened} onClose={() => setVmUploadOpened(false)} title="上传 VM 镜像" size="lg">
        <Stack>
          <Switch
            label="上传压缩归档"
            description="开启后支持 zip、tar.gz、tgz、tar.xz、txz；关闭时直接上传 qcow2、ova、vmdk 等镜像文件。"
            checked={vmArchiveMode}
            onChange={(e) => setVmArchiveMode(e.currentTarget.checked)}
          />
          <FileInput
            label={vmArchiveMode ? 'VM 镜像归档' : 'VM 镜像文件'}
            required
            value={vmFile}
            onChange={setVmFile}
            accept={vmArchiveMode ? '.zip,.tar.gz,.tgz,.tar.xz,.txz' : '.qcow2,.ova,.vmdk'}
            placeholder="选择文件"
          />
          {uploading ? <Progress value={100} striped animated /> : null}
          <Group justify="flex-end">
            <Button loading={uploading} leftSection={<Icon path={mdiCubeOutline} size={0.82} />} onClick={uploadVmTemplate}>
              上传并导入
            </Button>
          </Group>
        </Stack>
      </Modal>

      <Modal opened={localImportOpened} onClose={() => setLocalImportOpened(false)} title="从服务器本地导入镜像" size="lg">
        <Stack>
          <TextInput
            label="服务器本地路径"
            required
            value={localImportDraft.localPath}
            onChange={(e) => setLocalImportDraft({ ...localImportDraft, localPath: e.currentTarget.value })}
            placeholder="/var/lib/gzctf/images/template.qcow2"
          />
          <TextInput
            label="显示名称"
            value={localImportDraft.displayName ?? ''}
            onChange={(e) => setLocalImportDraft({ ...localImportDraft, displayName: e.currentTarget.value || null })}
            placeholder="可选"
          />
          <Group justify="flex-end">
            <Button loading={saving} leftSection={<Icon path={mdiFileImportOutline} size={0.82} />} onClick={importLocalTemplate}>
              导入课程
            </Button>
          </Group>
        </Stack>
      </Modal>

      <Modal
        opened={challengeOpened}
        onClose={closeChallengeEditor}
        title={editingChallengeId ? '编辑课程题目' : '创建课程题目'}
        size="min(96vw, 1180px)"
        classNames={{
          content: 'yy-course-challenge-modal-content',
          body: 'yy-course-challenge-modal-body',
        }}
      >
        <Stack className="yy-course-challenge-editor" gap="md">
          <SimpleGrid cols={{ base: 1, md: 2 }} spacing="md">
            <TextInput
              label="题目名称"
              required
              value={challengeDraft.title}
              onChange={(e) => setChallengeDraft({ ...challengeDraft, title: e.currentTarget.value })}
            />
            <TextInput
              label="展示标题"
              value={challengeDraft.displayTitle ?? ''}
              onChange={(e) => setChallengeDraft({ ...challengeDraft, displayTitle: e.currentTarget.value || null })}
              placeholder="留空则使用题目名称"
            />
            <Select
              label="分类"
              value={challengeDraft.category}
              data={challengeCategoryOptions}
              onChange={(value) => setChallengeDraft({ ...challengeDraft, category: (value as ChallengeCategory) ?? ChallengeCategory.Web })}
            />
            <Select
              label="环境类型"
              value={challengeDraft.environment}
              data={environmentOptions}
              onChange={(value) => {
                const environment = (value as EnvironmentType) ?? EnvironmentType.None
                setChallengeDraft({
                  ...challengeDraft,
                  environment,
                  type:
                    environment === EnvironmentType.Docker
                      ? ChallengeType.DynamicContainer
                      : environment === EnvironmentType.WindowsVM
                        ? ChallengeType.StaticContainer
                        : ChallengeType.StaticAttachment,
                  imageTemplateId: null,
                  containerImage: '',
                })
              }}
            />
            <Select
              label="题目类型"
              value={challengeDraft.type}
              data={challengeTypeOptions}
              onChange={(value) => setChallengeDraft({ ...challengeDraft, type: (value as ChallengeType) ?? ChallengeType.StaticAttachment })}
            />
            <Select
              label="绑定章节"
              value={challengeDraft.chapterId ? String(challengeDraft.chapterId) : null}
              data={orderedChapters.map((chapter) => ({ value: String(chapter.id), label: chapter.title }))}
              onChange={(value) => setChallengeDraft({ ...challengeDraft, chapterId: value ? Number(value) : null })}
              clearable
            />
          </SimpleGrid>

          <Textarea
            label="题目描述 Markdown"
            minRows={5}
            value={challengeDraft.content}
            onChange={(e) => setChallengeDraft({ ...challengeDraft, content: e.currentTarget.value })}
          />

          <YinyuPanel p="md">
            <Stack gap="sm">
              <Group justify="space-between" align="flex-start">
                <Stack gap={2}>
                  <Text fw={900}>附件</Text>
                </Stack>
                {editingChallengeDetail?.attachmentUrl ? (
                  <Button
                    component="a"
                    href={editingChallengeDetail.attachmentUrl}
                    target="_blank"
                    rel="noopener noreferrer"
                    variant="light"
                    leftSection={<Icon path={mdiDownloadOutline} size={0.82} />}
                  >
                    当前附件
                  </Button>
                ) : null}
              </Group>
              <SimpleGrid cols={{ base: 1, md: 2 }} spacing="md">
                <Select
                  label="附件类型"
                  value={challengeDraft.attachmentType ?? FileType.None}
                  data={[
                    { value: FileType.None, label: '无附件' },
                    { value: FileType.Local, label: '本地上传' },
                    { value: FileType.Remote, label: '外链附件' },
                  ]}
                  onChange={(value) =>
                    setChallengeDraft({
                      ...challengeDraft,
                      attachmentType: (value as FileType) ?? FileType.None,
                      attachmentFileHash: value === FileType.Local ? challengeDraft.attachmentFileHash ?? null : null,
                      attachmentRemoteUrl: value === FileType.Remote ? challengeDraft.attachmentRemoteUrl ?? null : null,
                    })
                  }
                />
                {challengeDraft.attachmentType === FileType.Local ? (
                  <FileInput
                    label="上传附件"
                    value={challengeAttachmentFile}
                    onChange={setChallengeAttachmentFile}
                    placeholder={editingChallengeDetail?.attachmentFileName || '选择附件文件'}
                  />
                ) : challengeDraft.attachmentType === FileType.Remote ? (
                  <TextInput
                    label="附件外链"
                    value={challengeDraft.attachmentRemoteUrl ?? ''}
                    onChange={(event) =>
                      setChallengeDraft({ ...challengeDraft, attachmentRemoteUrl: event.currentTarget.value })
                    }
                    placeholder="https://example.com/attachment.zip"
                  />
                ) : (
                  <TextInput label="附件状态" value="不绑定附件" disabled />
                )}
              </SimpleGrid>
              {challengeDraft.attachmentType === FileType.Local && editingChallengeDetail?.attachmentFileName && !challengeAttachmentFile ? (
                <Text size="xs" c="dimmed">
                  当前本地附件：{editingChallengeDetail.attachmentFileName}
                </Text>
              ) : null}
            </Stack>
          </YinyuPanel>

          {challengeDraft.environment === EnvironmentType.Docker ? (
            <Select
              label="Docker 环境模板"
              placeholder="选择当前课程的就绪 Docker 模板"
              value={challengeDraft.imageTemplateId ? String(challengeDraft.imageTemplateId) : null}
              data={readyDockerTemplates.map((template) => ({
                value: String(template.id),
                label: `#${template.id} ${template.name}`,
              }))}
              onChange={(value) => {
                const template = readyDockerTemplates.find((item) => String(item.id) === value)
                setChallengeDraft({
                  ...challengeDraft,
                  imageTemplateId: template?.id ?? null,
                  containerImage: template?.registryUrl ?? '',
                })
              }}
              searchable
              clearable
            />
          ) : null}

          {challengeDraft.environment === EnvironmentType.WindowsVM ? (
            <Select
              label="Windows VM 模板"
              placeholder="选择当前课程的就绪 Windows 模板"
              value={challengeDraft.imageTemplateId ? String(challengeDraft.imageTemplateId) : null}
              data={readyWindowsTemplates.map((template) => ({
                value: String(template.id),
                label: `#${template.id} ${template.name}`,
              }))}
              onChange={(value) => setChallengeDraft({ ...challengeDraft, imageTemplateId: value ? Number(value) : null })}
              searchable
              clearable
            />
          ) : null}

          {challengeDraft.environment === EnvironmentType.Docker ? (
            <SimpleGrid cols={{ base: 1, md: 4 }} spacing="md">
              <NumberInput label="内存 MB" value={challengeDraft.memoryLimit ?? 128} onChange={(value) => setChallengeDraft({ ...challengeDraft, memoryLimit: Number(value) || 128 })} />
              <NumberInput label="CPU" value={challengeDraft.cpuCount ?? 1} onChange={(value) => setChallengeDraft({ ...challengeDraft, cpuCount: Number(value) || 1 })} />
              <NumberInput label="存储 MB" value={challengeDraft.storageLimit ?? 256} onChange={(value) => setChallengeDraft({ ...challengeDraft, storageLimit: Number(value) || 256 })} />
              <NumberInput label="暴露端口" value={challengeDraft.exposePort ?? 80} onChange={(value) => setChallengeDraft({ ...challengeDraft, exposePort: Number(value) || 80 })} />
            </SimpleGrid>
          ) : null}

          <SimpleGrid cols={{ base: 1, md: 3 }} spacing="md">
            <Select
              label="网络模式"
              value={challengeDraft.networkMode ?? NetworkMode.Open}
              data={networkModeOptions}
              onChange={(value) => setChallengeDraft({ ...challengeDraft, networkMode: (value as NetworkMode) ?? NetworkMode.Open })}
            />
            <NumberInput
              label="提交次数限制"
              value={challengeDraft.submissionLimit}
              onChange={(value) => setChallengeDraft({ ...challengeDraft, submissionLimit: Number(value) || 0 })}
            />
            <NumberInput
              label="排序"
              value={challengeDraft.order}
              onChange={(value) => setChallengeDraft({ ...challengeDraft, order: Number(value) || 1 })}
            />
          </SimpleGrid>

          {challengeDraft.type === ChallengeType.DynamicContainer || challengeDraft.type === ChallengeType.DynamicAttachment ? (
            <TextInput
              label="动态 Flag 模板"
              value={challengeDraft.flagTemplate ?? ''}
              onChange={(e) => setChallengeDraft({ ...challengeDraft, flagTemplate: e.currentTarget.value })}
              placeholder="flag{[TEAM_HASH]}"
            />
          ) : (
            <TextInput
              label="静态 Flag"
              value={challengeDraft.staticFlag ?? ''}
              onChange={(e) => setChallengeDraft({ ...challengeDraft, staticFlag: e.currentTarget.value })}
              placeholder="flag{example}"
            />
          )}

          <Switch
            label="必做题"
            checked={challengeDraft.isRequired}
            onChange={(e) => setChallengeDraft({ ...challengeDraft, isRequired: e.currentTarget.checked })}
          />

          <Group justify="flex-end" className="yy-course-challenge-editor-actions">
            <Button loading={saving} leftSection={<Icon path={mdiContentSaveOutline} size={0.82} />} onClick={saveCourseChallenge}>
              {editingChallengeId ? '保存题目' : '创建题目'}
            </Button>
          </Group>
        </Stack>
      </Modal>
    </WithNavBar>
  )
}

export default CourseDetail
