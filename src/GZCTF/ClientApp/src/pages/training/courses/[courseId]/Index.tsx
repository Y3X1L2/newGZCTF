import {
  Accordion,
  ActionIcon,
  Alert,
  Avatar,
  Badge,
  Box,
  Button,
  Divider,
  Drawer,
  FileInput,
  FileButton,
  Group,
  Modal,
  NumberInput,
  Pagination,
  Progress,
  ScrollArea,
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
import { modals } from '@mantine/modals'
import { useDebouncedValue } from '@mantine/hooks'
import { showNotification } from '@mantine/notifications'
import {
  mdiArchiveArrowUpOutline,
  mdiArchiveOutline,
  mdiAccountMultipleOutline,
  mdiAccountPlusOutline,
  mdiArrowLeft,
  mdiBookOpenPageVariantOutline,
  mdiCheck,
  mdiClose,
  mdiContentSaveOutline,
  mdiCubeOutline,
  mdiDownloadOutline,
  mdiDocker,
  mdiEyeOutline,
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
  TrainingCourseStudentLearningDetailModel,
  TrainingCourseStudentLearningSummaryModel,
  TrainingCourseStudentCandidateModel,
  TrainingCourseTeacherCandidateModel,
  TrainingCourseTeacherRole,
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
const courseTabValues = ['intro', 'chapters', 'resources', 'students', 'teachers', 'environments', 'challenges', 'theory-bank', 'homework']

const teacherRoleOptions = [
  { value: TrainingCourseTeacherRole.Teacher, label: '授课教师' },
  { value: TrainingCourseTeacherRole.Owner, label: '负责人' },
]

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

const percentOf = (done: number, total: number) => (total > 0 ? Math.max(0, Math.min(100, Math.round((done / total) * 100))) : 0)

const formatTime = (value?: number | string | null) => {
  if (!value) return '-'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? '-' : date.toLocaleString()
}

const teacherRoleText = (role: TrainingCourseTeacherRole | string) =>
  role === TrainingCourseTeacherRole.Owner ? '负责人' : '授课教师'

const optionIndexesText = (options: string[], indexes: number[]) =>
  indexes.length
    ? indexes.map((index) => `${index + 1}. ${options[index] ?? `选项 ${index + 1}`}`).join('；')
    : '未作答'

const theoryScoreText = (score: number | null | undefined, totalScore: number) => `${score ?? '--'}/${totalScore}`

const CourseDetail: FC = () => {
  const { courseId } = useParams()
  const location = useLocation()
  const navigate = useNavigate()
  const id = Number(courseId)
  const [course, setCourse] = useState<TrainingCourseModel | null>(null)
  const [enrollments, setEnrollments] = useState<Awaited<ReturnType<typeof trainingCourseAdminApi.enrollments>>['data']>([])
  const [learningSummaries, setLearningSummaries] = useState<TrainingCourseStudentLearningSummaryModel[]>([])
  const [activeTab, setActiveTab] = useState<string | null>('intro')
  const [editOpened, setEditOpened] = useState(false)
  const [resourceOpened, setResourceOpened] = useState(false)
  const [teacherOpened, setTeacherOpened] = useState(false)
  const [studentOpened, setStudentOpened] = useState(false)
  const [studentDetailOpened, setStudentDetailOpened] = useState(false)
  const [dockerRegisterOpened, setDockerRegisterOpened] = useState(false)
  const [dockerUploadOpened, setDockerUploadOpened] = useState(false)
  const [vmUploadOpened, setVmUploadOpened] = useState(false)
  const [localImportOpened, setLocalImportOpened] = useState(false)
  const [challengeOpened, setChallengeOpened] = useState(false)
  const [editingChallengeId, setEditingChallengeId] = useState<number | null>(null)
  const [editingChallengeDetail, setEditingChallengeDetail] = useState<TrainingCourseChallengeEditDetailModel | null>(null)
  const [challengeAttachmentFile, setChallengeAttachmentFile] = useState<File | null>(null)
  const [studentLearningDetail, setStudentLearningDetail] = useState<TrainingCourseStudentLearningDetailModel | null>(null)
  const [studentLearningLoading, setStudentLearningLoading] = useState(false)
  const [studentCandidates, setStudentCandidates] = useState<TrainingCourseStudentCandidateModel[]>([])
  const [studentKeyword, setStudentKeyword] = useState('')
  const [debouncedStudentKeyword] = useDebouncedValue(studentKeyword, 300)
  const [selectedStudentId, setSelectedStudentId] = useState<string | null>(null)
  const [teacherCandidates, setTeacherCandidates] = useState<TrainingCourseTeacherCandidateModel[]>([])
  const [teacherKeyword, setTeacherKeyword] = useState('')
  const [debouncedTeacherKeyword] = useDebouncedValue(teacherKeyword, 300)
  const [selectedTeacherId, setSelectedTeacherId] = useState<string | null>(null)
  const [selectedTeacherRole, setSelectedTeacherRole] = useState<TrainingCourseTeacherRole>(TrainingCourseTeacherRole.Teacher)
  const [courseDraft, setCourseDraft] = useState<TrainingCourseEditModel>(emptyCourseDraft())
  const [resourceDraft, setResourceDraft] = useState<TrainingCourseResourceEditModel>(emptyResourceDraft())
  const [challengeDraft, setChallengeDraft] = useState<TrainingCourseChallengeCreateModel>(emptyChallengeDraft())
  const [dockerRegisterDraft, setDockerRegisterDraft] = useState<TrainingCourseDockerRegisterModel>(emptyDockerRegisterDraft())
  const [localImportDraft, setLocalImportDraft] = useState<TrainingCourseLocalImageImportModel>(emptyLocalImportDraft())
  const [dockerArchiveFile, setDockerArchiveFile] = useState<File | null>(null)
  const [dockerArchiveName, setDockerArchiveName] = useState('')
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
  const studentProgressRows = useMemo(
    () =>
      learningSummaries.length
        ? learningSummaries
        : enrollments.map((enrollment) => ({
            userId: enrollment.userId,
            userName: enrollment.userName,
            realName: enrollment.realName,
            stdNumber: enrollment.stdNumber,
            enrollmentStatus: enrollment.status,
            completedChapterCount: enrollment.completedChapterCount,
            totalChapterCount: enrollment.totalChapterCount,
            challengeSolvedCount: 0,
            challengeTotalCount: 0,
            theorySubmittedCount: 0,
            theoryPassedCount: 0,
            theoryTotalCount: 0,
            theoryScore: 0,
            theoryMaxScore: 0,
            progressStatus: enrollment.progressStatus,
            lastActivityAt: enrollment.progressUpdatedAt,
          })),
    [enrollments, learningSummaries]
  )
  const studentPageCount = Math.max(1, Math.ceil(studentProgressRows.length / studentPageSize))
  const visibleStudentProgressRows = useMemo(
    () => studentProgressRows.slice((studentPage - 1) * studentPageSize, studentPage * studentPageSize),
    [studentProgressRows, studentPage]
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
        const [enrollmentRes, learningRes] = await Promise.all([
          trainingCourseAdminApi.enrollments(id),
          trainingCourseAdminApi.learningSummaries(id),
        ])
        setEnrollments(enrollmentRes.data)
        setLearningSummaries(learningRes.data)
      } else {
        setEnrollments([])
        setLearningSummaries([])
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

  const deleteCourse = async () => {
    if (!course) return
    modals.openConfirmModal({
      title: '删除课程',
      children: (
        <Text size="sm">
          确认删除「{course.title}」？课程内章节、报名、学习记录和课程专属题目会被删除，环境模板只会解绑。
        </Text>
      ),
      labels: { confirm: '删除', cancel: '取消' },
      confirmProps: { color: 'red' },
      onConfirm: async () => {
        try {
          await trainingCourseAdminApi.deleteCourse(course.id)
          showNotification({ color: 'green', message: '课程已删除。' })
          navigate('/training')
        } catch (e) {
          showErrorMsg(e, t)
        }
      },
    })
  }

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

  const searchStudentCandidates = async (keyword = studentKeyword) => {
    if (!course || !course.canManageEnrollments) return
    try {
      const res = await trainingCourseAdminApi.studentCandidates(course.id, keyword.trim() || undefined)
      setStudentCandidates(res.data)
    } catch (e) {
      showErrorMsg(e, t)
    }
  }

  const openStudentModal = () => {
    setStudentKeyword('')
    setSelectedStudentId(null)
    setStudentOpened(true)
  }

  const addCourseStudent = async () => {
    if (!course || !selectedStudentId) return
    setSaving(true)
    try {
      await trainingCourseAdminApi.addEnrollment(course.id, { userId: selectedStudentId })
      showNotification({ color: 'teal', message: '学员已添加到课程。' })
      setSelectedStudentId(null)
      setStudentKeyword('')
      await load()
      await searchStudentCandidates('')
    } catch (e) {
      showErrorMsg(e, t)
    } finally {
      setSaving(false)
    }
  }

  const openStudentDetail = async (userId: string) => {
    if (!course) return
    setStudentDetailOpened(true)
    setStudentLearningLoading(true)
    setStudentLearningDetail(null)
    try {
      const res = await trainingCourseAdminApi.studentLearningDetail(course.id, userId)
      setStudentLearningDetail(res.data)
    } catch (e) {
      showErrorMsg(e, t)
      setStudentDetailOpened(false)
    } finally {
      setStudentLearningLoading(false)
    }
  }

  const searchTeacherCandidates = async (keyword = teacherKeyword) => {
    if (!course || !course.canManageTeachers) return
    try {
      const res = await trainingCourseAdminApi.teacherCandidates(course.id, keyword.trim() || undefined)
      setTeacherCandidates(res.data)
    } catch (e) {
      showErrorMsg(e, t)
    }
  }

  const openTeacherModal = () => {
    setTeacherKeyword('')
    setSelectedTeacherId(null)
    setSelectedTeacherRole(TrainingCourseTeacherRole.Teacher)
    setTeacherOpened(true)
  }

  const addCourseTeacher = async () => {
    if (!course || !selectedTeacherId) return
    setSaving(true)
    try {
      await trainingCourseAdminApi.addTeacher(course.id, {
        teacherId: selectedTeacherId,
        role: selectedTeacherRole,
      })
      showNotification({ color: 'teal', message: '授课教师已更新。' })
      setSelectedTeacherId(null)
      await load()
      await searchTeacherCandidates()
    } catch (e) {
      showErrorMsg(e, t)
    } finally {
      setSaving(false)
    }
  }

  const removeCourseTeacher = async (teacherId: string) => {
    if (!course) return
    setSaving(true)
    try {
      await trainingCourseAdminApi.removeTeacher(course.id, teacherId)
      showNotification({ color: 'teal', message: '授课教师已移除。' })
      await load()
      await searchTeacherCandidates()
    } catch (e) {
      showErrorMsg(e, t)
    } finally {
      setSaving(false)
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
    if (!course || !dockerArchiveFile || !dockerArchiveName.trim()) return
    setUploading(true)
    try {
      const formData = new FormData()
      formData.append('file', dockerArchiveFile)
      formData.append('name', dockerArchiveName.trim())
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

  useEffect(() => {
    if (!studentOpened || !course?.canManageEnrollments) return
    void searchStudentCandidates(debouncedStudentKeyword)
  }, [course?.id, course?.canManageEnrollments, debouncedStudentKeyword, studentOpened])

  useEffect(() => {
    if (!teacherOpened || !course?.canManageTeachers) return
    void searchTeacherCandidates(debouncedTeacherKeyword)
  }, [course?.id, course?.canManageTeachers, debouncedTeacherKeyword, teacherOpened])

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
                {course.canDelete ? (
                  <Button
                    color="red"
                    variant="light"
                    leftSection={<Icon path={mdiTrashCanOutline} size={0.82} />}
                    onClick={deleteCourse}
                  >
                    删除课程
                  </Button>
                ) : null}
              </Group>
            </Stack>
          </div>
        </YinyuPanel>

        <Tabs value={activeTab} onChange={handleTabChange} keepMounted={false} className="yy-course-tabs yy-training-detail-tabs">
          <div className="yy-training-detail-grid">
            <main className="yy-training-detail-main">
              <Tabs.List>
                <Tabs.Tab value="intro">课程介绍</Tabs.Tab>
                <Tabs.Tab value="chapters">课程列表</Tabs.Tab>
                <Tabs.Tab value="resources">课程资源</Tabs.Tab>
                {course.canManageEnrollments ? <Tabs.Tab value="students">学员管理</Tabs.Tab> : null}
                {course.canEdit ? <Tabs.Tab value="teachers">授课教师</Tabs.Tab> : null}
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
              <Group justify="space-between" mb="md">
                <Title order={3}>学员管理</Title>
                {course.canManageEnrollments ? (
                  <Button leftSection={<Icon path={mdiAccountPlusOutline} size={0.82} />} onClick={openStudentModal}>
                    添加学员
                  </Button>
                ) : null}
              </Group>
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
                              aria-label="查看学员学习详情"
                              title="查看学员学习详情"
                              variant="light"
                              onClick={() => openStudentDetail(enrollment.userId)}
                            >
                              <Icon path={mdiEyeOutline} size={0.86} />
                            </ActionIcon>
                            <ActionIcon
                              aria-label="通过报名"
                              title="通过报名"
                              color="green"
                              onClick={() => reviewEnrollment(enrollment.userId, TrainingCourseEnrollmentStatus.Approved)}
                            >
                              <Icon path={mdiCheck} size={0.86} />
                            </ActionIcon>
                            <ActionIcon
                              aria-label="拒绝报名"
                              title="拒绝报名"
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

          <Tabs.Panel value="teachers" pt="md">
            <YinyuPanel p="lg">
              <Group justify="space-between" mb="md">
                <Stack gap={2}>
                  <Title order={3}>授课教师</Title>
                  <Text size="sm" c="dimmed">
                    负责人可以添加其他老师共同维护课程内容、题目和学员数据。
                  </Text>
                </Stack>
                {course.canManageTeachers ? (
                  <Button leftSection={<Icon path={mdiAccountPlusOutline} size={0.82} />} onClick={openTeacherModal}>
                    添加教师
                  </Button>
                ) : null}
              </Group>
              <Table.ScrollContainer minWidth={760}>
                <Table verticalSpacing="sm">
                  <Table.Thead>
                    <Table.Tr>
                      <Table.Th>教师</Table.Th>
                      <Table.Th>角色</Table.Th>
                      <Table.Th>加入时间</Table.Th>
                      <Table.Th>操作</Table.Th>
                    </Table.Tr>
                  </Table.Thead>
                  <Table.Tbody>
                    {course.teachers.map((teacher) => (
                      <Table.Tr key={teacher.teacherId}>
                        <Table.Td>
                          <Text fw={800}>{teacher.realName || teacher.userName}</Text>
                          <Text size="xs" c="dimmed">
                            {teacher.userName}
                          </Text>
                        </Table.Td>
                        <Table.Td>
                          <Badge color={teacher.role === TrainingCourseTeacherRole.Owner ? 'teal' : 'blue'} variant="light">
                            {teacherRoleText(teacher.role)}
                          </Badge>
                        </Table.Td>
                        <Table.Td>{formatTime(teacher.assignedAt)}</Table.Td>
                        <Table.Td>
                          {course.canManageTeachers ? (
                            <ActionIcon
                              aria-label="移除授课教师"
                              title="移除授课教师"
                              color="red"
                              variant="light"
                              onClick={() => removeCourseTeacher(teacher.teacherId)}
                            >
                              <Icon path={mdiTrashCanOutline} size={0.86} />
                            </ActionIcon>
                          ) : (
                            <Text size="sm" c="dimmed">
                              -
                            </Text>
                          )}
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
                        <ActionIcon
                          aria-label="编辑课程题目"
                          title="编辑课程题目"
                          variant="light"
                          onClick={() => openEditCourseChallenge(challenge.exerciseChallengeId)}
                        >
                          <Icon path={mdiPencilOutline} size={0.86} />
                        </ActionIcon>
                        <ActionIcon
                          aria-label="删除课程题目"
                          title="删除课程题目"
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
                        {visibleStudentProgressRows.map((summary) => {
                          const status = enrollmentStatusInfo(summary.enrollmentStatus)
                          const total = summary.totalChapterCount || course.totalChapterCount || course.chapterCount || 0
                          const completed = summary.completedChapterCount ?? 0
                          const percent = percentOf(completed, total)

                          return (
                            <div
                              key={summary.userId}
                              className="yy-training-student-progress-row"
                              role="button"
                              tabIndex={0}
                              style={{ cursor: 'pointer' }}
                              onClick={() => openStudentDetail(summary.userId)}
                              onKeyDown={(event) => {
                                if (event.key === 'Enter' || event.key === ' ') void openStudentDetail(summary.userId)
                              }}
                            >
                              <Group justify="space-between" align="flex-start" gap="xs" wrap="nowrap">
                                <Stack gap={1} miw={0}>
                                  <Text fw={900} lineClamp={1}>
                                    {summary.realName || summary.userName}
                                  </Text>
                                  <Text size="xs" c="dimmed" lineClamp={1}>
                                    {summary.stdNumber || summary.userName}
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
                              <Group gap={6} mt="xs">
                                <Badge size="xs" variant="light">
                                  实验 {summary.challengeSolvedCount}/{summary.challengeTotalCount}
                                </Badge>
                                <Badge size="xs" variant="light">
                                  理论 {summary.theorySubmittedCount}/{summary.theoryTotalCount}
                                </Badge>
                              </Group>
                            </div>
                          )
                        })}
                        {studentProgressRows.length === 0 ? (
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

      <Drawer
        opened={studentDetailOpened}
        onClose={() => setStudentDetailOpened(false)}
        title="学员学习详情"
        position="right"
        size="min(72rem, calc(100vw - 1.5rem))"
        padding="lg"
        scrollAreaComponent={ScrollArea.Autosize}
        classNames={{
          content: 'yy-training-student-detail-drawer-content',
          header: 'yy-training-student-detail-drawer-header',
          title: 'yy-training-student-detail-drawer-title',
          body: 'yy-training-student-detail-drawer-body',
        }}
      >
        {studentLearningLoading ? (
          <Text c="dimmed">正在加载学习详情...</Text>
        ) : studentLearningDetail ? (
          <Stack gap="md">
            <YinyuPanel p="md">
              <Stack gap="sm">
                <Group justify="space-between" align="flex-start">
                  <Stack gap={2}>
                    <Title order={3}>{studentLearningDetail.realName || studentLearningDetail.userName}</Title>
                    <Text size="sm" c="dimmed">
                      {studentLearningDetail.stdNumber || studentLearningDetail.userName}
                    </Text>
                  </Stack>
                  <Badge color={enrollmentStatusInfo(studentLearningDetail.enrollmentStatus).color} variant="light">
                    {enrollmentStatusInfo(studentLearningDetail.enrollmentStatus).label}
                  </Badge>
                </Group>
                <Box className="yy-training-course-progress">
                  <Group justify="space-between" mb={5}>
                    <Text size="xs" c="dimmed" fw={800}>
                      课程进度
                    </Text>
                    <Text size="xs" fw={950}>
                      {studentLearningDetail.completedChapterCount}/{studentLearningDetail.totalChapterCount}
                    </Text>
                  </Group>
                  <Progress
                    value={percentOf(studentLearningDetail.completedChapterCount, studentLearningDetail.totalChapterCount)}
                    radius="xl"
                    size="sm"
                    color="teal"
                  />
                </Box>
                <Group gap="xs">
                  <Badge variant="light">
                    实验 {studentLearningDetail.challengeSolvedCount}/{studentLearningDetail.challengeTotalCount}
                  </Badge>
                  <Badge variant="light">
                    理论 {studentLearningDetail.theorySubmittedCount}/{studentLearningDetail.theoryTotalCount}
                  </Badge>
                  <Badge variant="light">
                    理论得分 {studentLearningDetail.theoryScore}/{studentLearningDetail.theoryMaxScore}
                  </Badge>
                  <Badge variant="light">最后学习 {formatTime(studentLearningDetail.lastActivityAt)}</Badge>
                </Group>
              </Stack>
            </YinyuPanel>

            <Accordion multiple variant="separated">
              {studentLearningDetail.chapters.map((chapter) => (
                <Accordion.Item key={chapter.chapterId} value={String(chapter.chapterId)}>
                  <Accordion.Control>
                    <Group justify="space-between" pr="md">
                      <Stack gap={2}>
                        <Text fw={900}>{chapter.title}</Text>
                        <Text size="xs" c="dimmed">
                          {chapter.summary || '暂无章节摘要'}
                        </Text>
                      </Stack>
                      <Badge color={chapter.completedAt ? 'teal' : 'gray'} variant="light">
                        {chapter.completedAt ? '已完成' : '未完成'}
                      </Badge>
                    </Group>
                  </Accordion.Control>
                  <Accordion.Panel>
                    <Stack gap="md">
                      {chapter.theory ? (
                        <YinyuPanel p="md">
                          <Stack gap="sm">
                            <Group justify="space-between">
                              <Stack gap={2}>
                                <Text fw={900}>{chapter.theory.title}</Text>
                                <Text size="xs" c="dimmed">
                                  {chapter.theory.questionCount} 题 / {chapter.theory.totalScore} 分 / 得分{' '}
                                  {theoryScoreText(chapter.theory.score, chapter.theory.totalScore)} / 及格线 {chapter.theory.passRate}%
                                </Text>
                              </Stack>
                              <Badge color={chapter.theory.passed ? 'teal' : chapter.theory.status ? 'yellow' : 'gray'} variant="light">
                                {chapter.theory.passed
                                  ? `已通过 ${theoryScoreText(chapter.theory.score, chapter.theory.totalScore)}`
                                  : chapter.theory.status
                                    ? theoryScoreText(chapter.theory.score, chapter.theory.totalScore)
                                    : '未提交'}
                              </Badge>
                            </Group>
                            {chapter.theory.answers.length > 0 ? (
                              <Table.ScrollContainer minWidth={640}>
                                <Table verticalSpacing="sm" style={{ tableLayout: 'fixed' }}>
                                  <colgroup>
                                    <col style={{ width: '52%' }} />
                                    <col style={{ width: '18%' }} />
                                    <col style={{ width: '22%' }} />
                                    <col style={{ width: '8%' }} />
                                  </colgroup>
                                  <Table.Thead>
                                    <Table.Tr>
                                      <Table.Th>题目</Table.Th>
                                      <Table.Th>学生答案</Table.Th>
                                      <Table.Th>正确答案</Table.Th>
                                      <Table.Th>得分</Table.Th>
                                    </Table.Tr>
                                  </Table.Thead>
                                  <Table.Tbody>
                                    {chapter.theory.answers.map((answer) => (
                                      <Table.Tr key={answer.questionId}>
                                        <Table.Td>
                                          <Text fw={800} lineClamp={2}>
                                            {answer.title}
                                          </Text>
                                          <Text size="xs" c="dimmed">
                                            {answer.type}
                                          </Text>
                                        </Table.Td>
                                        <Table.Td>
                                          <Text size="sm" c={answer.isCorrect ? 'teal' : 'red'} style={{ overflowWrap: 'anywhere' }}>
                                            {optionIndexesText(answer.options, answer.selectedIndexes)}
                                          </Text>
                                        </Table.Td>
                                        <Table.Td>
                                          <Text size="sm" style={{ overflowWrap: 'anywhere' }}>
                                            {optionIndexesText(answer.options, answer.answerIndexes)}
                                          </Text>
                                        </Table.Td>
                                        <Table.Td>
                                          {answer.score}/{answer.maxScore}
                                        </Table.Td>
                                      </Table.Tr>
                                    ))}
                                  </Table.Tbody>
                                </Table>
                              </Table.ScrollContainer>
                            ) : (
                              <Text size="sm" c="dimmed">
                                学员尚未提交该测试。
                              </Text>
                            )}
                          </Stack>
                        </YinyuPanel>
                      ) : null}

                      {chapter.challenges.length > 0 ? (
                        <YinyuPanel p="md">
                          <Stack gap="sm">
                            <Group gap="xs">
                              <Icon path={mdiAccountMultipleOutline} size={0.9} />
                              <Text fw={900}>章节实验</Text>
                            </Group>
                            <Table.ScrollContainer minWidth={680}>
                              <Table verticalSpacing="sm" style={{ tableLayout: 'fixed' }}>
                                <colgroup>
                                  <col style={{ width: '34%' }} />
                                  <col style={{ width: '14%' }} />
                                  <col style={{ width: '10%' }} />
                                  <col style={{ width: '22%' }} />
                                  <col style={{ width: '20%' }} />
                                </colgroup>
                                <Table.Thead>
                                  <Table.Tr>
                                    <Table.Th>题目</Table.Th>
                                    <Table.Th>状态</Table.Th>
                                    <Table.Th>提交</Table.Th>
                                    <Table.Th>实例入口</Table.Th>
                                    <Table.Th>最后提交</Table.Th>
                                  </Table.Tr>
                                </Table.Thead>
                                <Table.Tbody>
                                  {chapter.challenges.map((challenge) => (
                                    <Table.Tr key={challenge.exerciseChallengeId}>
                                      <Table.Td>
                                        <Text fw={800}>{challenge.displayTitle || challenge.title}</Text>
                                        <Text size="xs" c="dimmed">
                                          {challenge.category} / {challenge.type}
                                        </Text>
                                      </Table.Td>
                                      <Table.Td>
                                        <Badge color={challenge.solved ? 'teal' : 'gray'} variant="light">
                                          {challenge.solved ? '已完成' : '未完成'}
                                        </Badge>
                                      </Table.Td>
                                      <Table.Td>
                                        {challenge.acceptedSubmissionCount}/{challenge.submissionCount}
                                      </Table.Td>
                                      <Table.Td>
                                        {challenge.instanceEntry && (challenge.instanceEntry.startsWith('http') || challenge.instanceEntry.includes(':')) ? (
                                          <Button
                                            component="a"
                                            href={
                                              challenge.instanceEntry.startsWith('http')
                                                ? challenge.instanceEntry
                                                : `http://${challenge.instanceEntry}`
                                            }
                                            target="_blank"
                                            rel="noopener noreferrer"
                                            size="xs"
                                            variant="light"
                                          >
                                            打开
                                          </Button>
                                        ) : challenge.instanceEntry ? (
                                          <Text size="xs" ff="monospace" c="dimmed">
                                            {challenge.instanceEntry}
                                          </Text>
                                        ) : (
                                          <Text size="sm" c="dimmed">
                                            -
                                          </Text>
                                        )}
                                      </Table.Td>
                                      <Table.Td>
                                        <Text size="sm">{challenge.lastStatus ?? '-'}</Text>
                                        <Text size="xs" c="dimmed">
                                          {formatTime(challenge.lastSubmittedAt)}
                                        </Text>
                                      </Table.Td>
                                    </Table.Tr>
                                  ))}
                                </Table.Tbody>
                              </Table>
                            </Table.ScrollContainer>
                          </Stack>
                        </YinyuPanel>
                      ) : (
                        <Text size="sm" c="dimmed">
                          本章节未配置实验题。
                        </Text>
                      )}
                    </Stack>
                  </Accordion.Panel>
                </Accordion.Item>
              ))}
            </Accordion>
          </Stack>
        ) : (
          <Text c="dimmed">请选择学员查看详情。</Text>
        )}
      </Drawer>

      <Modal opened={studentOpened} onClose={() => setStudentOpened(false)} title="添加学员" size="lg">
        <Stack>
          <Group align="flex-end">
            <TextInput
              label="搜索学员"
              placeholder="用户名、姓名、学号、邮箱或 ID"
              value={studentKeyword}
              onChange={(event) => setStudentKeyword(event.currentTarget.value)}
              onKeyDown={(event) => {
                if (event.key === 'Enter') void searchStudentCandidates()
              }}
              style={{ flex: 1 }}
            />
            <Button variant="light" leftSection={<Icon path={mdiMagnify} size={0.82} />} onClick={() => searchStudentCandidates()}>
              搜索
            </Button>
          </Group>
          <ScrollArea.Autosize mah={320}>
            <Stack gap="xs">
              {studentCandidates.map((candidate) => {
                const selected = selectedStudentId === candidate.userId
                return (
                  <Button
                    key={candidate.userId}
                    variant={selected ? 'light' : 'subtle'}
                    color={candidate.alreadyEnrolled ? 'gray' : selected ? 'teal' : undefined}
                    disabled={candidate.alreadyEnrolled}
                    justify="flex-start"
                    h="auto"
                    py="xs"
                    onClick={() => setSelectedStudentId(candidate.userId)}
                  >
                    <Group gap="sm" wrap="nowrap">
                      <Avatar src={candidate.avatar} radius="xl" size={34}>
                        {(candidate.realName || candidate.userName || '?').slice(0, 1)}
                      </Avatar>
                      <Stack gap={0} align="flex-start">
                        <Text fw={800}>{candidate.realName || candidate.userName}</Text>
                        <Text size="xs" c="dimmed">
                          {candidate.userName} {candidate.stdNumber ? ` / ${candidate.stdNumber}` : ''}
                          {candidate.alreadyEnrolled ? ' / 已在课程中' : ''}
                        </Text>
                      </Stack>
                    </Group>
                  </Button>
                )
              })}
              {studentCandidates.length === 0 ? (
                <Text size="sm" c="dimmed" ta="center" py="md">
                  暂无匹配学员
                </Text>
              ) : null}
            </Stack>
          </ScrollArea.Autosize>
          <Group justify="flex-end">
            <Button variant="subtle" onClick={() => setStudentOpened(false)}>
              取消
            </Button>
            <Button loading={saving} disabled={!selectedStudentId} onClick={addCourseStudent}>
              添加到课程
            </Button>
          </Group>
        </Stack>
      </Modal>

      <Modal opened={teacherOpened} onClose={() => setTeacherOpened(false)} title="添加授课教师" size="lg">
        <Stack>
          <Group align="flex-end">
            <TextInput
              label="搜索用户"
              placeholder="用户名、姓名、邮箱或 ID"
              value={teacherKeyword}
              onChange={(event) => setTeacherKeyword(event.currentTarget.value)}
              style={{ flex: 1 }}
            />
            <Button variant="light" leftSection={<Icon path={mdiMagnify} size={0.82} />} onClick={() => searchTeacherCandidates()}>
              搜索
            </Button>
          </Group>
          <Select
            label="候选教师"
            placeholder="选择 Teacher/Admin/SuperAdmin 用户"
            searchable
            value={selectedTeacherId}
            onChange={setSelectedTeacherId}
            data={teacherCandidates.map((candidate) => ({
              value: candidate.userId,
              label: `${candidate.realName || candidate.userName} (${candidate.userName}) · ${candidate.role}${
                candidate.alreadyTeacher ? ' · 已在课程中' : ''
              }`,
              disabled: candidate.alreadyTeacher,
            }))}
          />
          <Select
            label="课程角色"
            value={selectedTeacherRole}
            data={teacherRoleOptions}
            onChange={(value) => setSelectedTeacherRole((value as TrainingCourseTeacherRole) ?? TrainingCourseTeacherRole.Teacher)}
          />
          <Group justify="flex-end">
            <Button loading={saving} disabled={!selectedTeacherId} onClick={addCourseTeacher}>
              添加到课程
            </Button>
          </Group>
          <Divider />
          <Stack gap="xs">
            <Text fw={900}>当前授课教师</Text>
            {course.teachers.map((teacher) => (
              <Group key={teacher.teacherId} justify="space-between">
                <Stack gap={0}>
                  <Text fw={800}>{teacher.realName || teacher.userName}</Text>
                  <Text size="xs" c="dimmed">
                    {teacher.userName}
                  </Text>
                </Stack>
                <Badge variant="light">{teacherRoleText(teacher.role)}</Badge>
              </Group>
            ))}
          </Stack>
        </Stack>
      </Modal>

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
          <Switch
            label="课程报名审核"
            checked={courseDraft.enrollmentPolicy === TrainingCourseEnrollmentPolicy.TeacherApproval}
            onChange={(event) =>
              setCourseDraft({
                ...courseDraft,
                enrollmentPolicy: event.currentTarget.checked
                  ? TrainingCourseEnrollmentPolicy.TeacherApproval
                  : TrainingCourseEnrollmentPolicy.AutoApprove,
              })
            }
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
