import {
  Button,
  FileButton,
  Group,
  NumberInput,
  Select,
  Stack,
  Switch,
  Text,
  TextInput,
  Textarea,
  Title,
} from '@mantine/core'
import { showNotification } from '@mantine/notifications'
import {
  mdiArrowLeft,
  mdiContentSaveOutline,
  mdiEyeOutline,
  mdiFileVideoOutline,
} from '@mdi/js'
import { Icon } from '@mdi/react'
import React, { FC, useEffect, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router'
import { Markdown } from '@Components/MarkdownRenderer'
import { WithNavBar } from '@Components/WithNavbar'
import { TrainingEmptyState } from '@Components/training/TrainingCourseUI'
import { YinyuGameBendsBackground } from '@Components/yinyu/YinyuReactBits'
import { YinyuPanel } from '@Components/yinyu/YinyuUI'
import api from '@Api'
import { showErrorMsg } from '@Utils/Shared'
import {
  TrainingCourseChapterEditModel,
  TrainingCourseModel,
  TrainingCourseVideoProvider,
  trainingCourseAdminApi,
  trainingCourseApi,
} from '@Utils/TrainingApi'
import { useTranslation } from 'react-i18next'

const emptyChapterDraft = (): TrainingCourseChapterEditModel => ({
  parentId: null,
  title: '',
  summary: '',
  content: '',
  contentType: 'Markdown',
  videoProvider: TrainingCourseVideoProvider.None,
  videoUrl: null,
  videoFileHash: null,
  order: 1,
  isPublished: true,
})

export const TrainingChapterEditor: FC<{ mode: 'create' | 'edit' }> = ({ mode }) => {
  const { courseId, chapterId } = useParams()
  const id = Number(courseId)
  const chapterNumericId = Number(chapterId)
  const [course, setCourse] = useState<TrainingCourseModel | null>(null)
  const [draft, setDraft] = useState<TrainingCourseChapterEditModel>(emptyChapterDraft())
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const navigate = useNavigate()
  const { t } = useTranslation()

  const returnToCourse = () => navigate(`/training/courses/${id}?tab=chapters`)

  const uploadOne = async (file: File | null) => {
    if (!file) return null
    const res = await api.assets.assetsUpload({ files: [file] }, { filename: file.name })
    return res.data?.[0]?.hash ?? null
  }

  const load = async () => {
    if (!Number.isFinite(id)) return
    setLoading(true)
    try {
      let courseData: TrainingCourseModel
      try {
        const courseRes = await trainingCourseAdminApi.course(id)
        courseData = courseRes.data
      } catch {
        const courseRes = await trainingCourseApi.course(id)
        courseData = courseRes.data
      }

      setCourse(courseData)

      if (mode === 'edit' && Number.isFinite(chapterNumericId)) {
        let chapter = courseData.chapters.find((item) => item.id === chapterNumericId)
        if (!chapter) {
          const chapterRes = await trainingCourseApi.chapter(id, chapterNumericId)
          chapter = chapterRes.data
        }
        setDraft({
          parentId: chapter.parentId ?? null,
          title: chapter.title,
          summary: chapter.summary,
          content: chapter.content,
          contentType: chapter.contentType,
          videoProvider: chapter.videoProvider,
          videoUrl: chapter.videoUrl ?? null,
          videoFileHash: null,
          order: chapter.order,
          isPublished: chapter.isPublished,
        })
      } else {
        setDraft({
          ...emptyChapterDraft(),
          order: (courseData.chapters?.length ?? 0) + 1,
        })
      }
    } catch (e) {
      showErrorMsg(e, t)
    } finally {
      setLoading(false)
    }
  }

  const save = async () => {
    if (!course || !draft.title.trim()) return
    setSaving(true)
    try {
      const payload = {
        ...draft,
        title: draft.title.trim(),
        summary: draft.summary.trim(),
        content: draft.content,
        videoUrl: draft.videoUrl?.trim() || null,
      }

      if (mode === 'edit' && Number.isFinite(chapterNumericId)) {
        await trainingCourseAdminApi.updateChapter(course.id, chapterNumericId, payload)
      } else {
        await trainingCourseAdminApi.createChapter(course.id, payload)
      }

      showNotification({ color: 'teal', message: mode === 'edit' ? '章节已保存。' : '章节已创建。' })
      returnToCourse()
    } catch (e) {
      showErrorMsg(e, t)
    } finally {
      setSaving(false)
    }
  }

  useEffect(() => {
    void load()
  }, [courseId, chapterId, mode])

  if (loading) {
    return (
      <WithNavBar isLoading width="min(100%, calc(100vw - 7.25rem))">
        <></>
      </WithNavBar>
    )
  }

  if (!course || !course.canEdit) {
    return (
      <WithNavBar width="min(100%, calc(100vw - 7.25rem))" minWidth={0}>
        <div className="yy-training-page yy-training-chapter-editor-page">
          <YinyuGameBendsBackground className="yy-training-bg" />
          <TrainingEmptyState title="无法编辑章节" description="只有课程创建者、授课老师或管理员可以维护课程章节。" />
        </div>
      </WithNavBar>
    )
  }

  return (
    <WithNavBar width="min(100%, calc(100vw - 7.25rem))" minWidth={0}>
      <div className="yy-training-page yy-training-chapter-editor-page">
        <YinyuGameBendsBackground className="yy-training-bg" />
        <Button
          component={Link}
          to={`/training/courses/${course.id}?tab=chapters`}
          variant="subtle"
          leftSection={<Icon path={mdiArrowLeft} size={0.85} />}
        >
          返回课程列表
        </Button>

        <YinyuPanel p="lg" className="yy-training-chapter-editor-hero">
          <Group justify="space-between" align="flex-end" gap="md">
            <Stack gap={4}>
              <Text className="yy-section-kicker">Chapter Editor</Text>
              <Title order={1}>{mode === 'edit' ? '编辑章节' : '添加章节'}</Title>
              <Text c="dimmed">{course.title}</Text>
            </Stack>
            <Button loading={saving} leftSection={<Icon path={mdiContentSaveOutline} size={0.85} />} onClick={save}>
              保存并返回
            </Button>
          </Group>
        </YinyuPanel>

        <div className="yy-training-chapter-editor-grid">
          <YinyuPanel p="lg" className="yy-training-chapter-editor-form">
            <Stack gap="md">
              <SimpleChapterFields draft={draft} setDraft={setDraft} uploadOne={uploadOne} />
              <Group justify="flex-end">
                <Button loading={saving} leftSection={<Icon path={mdiContentSaveOutline} size={0.85} />} onClick={save}>
                  保存章节
                </Button>
              </Group>
            </Stack>
          </YinyuPanel>

          <YinyuPanel p="lg" className="yy-training-chapter-editor-preview">
            <Stack gap="sm">
              <Group gap="xs">
                <Icon path={mdiEyeOutline} size={0.9} />
                <Title order={3}>实时预览</Title>
              </Group>
              <Markdown source={draft.content || '暂无章节内容。'} />
            </Stack>
          </YinyuPanel>
        </div>
      </div>
    </WithNavBar>
  )
}

const SimpleChapterFields: FC<{
  draft: TrainingCourseChapterEditModel
  setDraft: React.Dispatch<React.SetStateAction<TrainingCourseChapterEditModel>>
  uploadOne: (file: File | null) => Promise<string | null>
}> = ({ draft, setDraft, uploadOne }) => (
  <>
    <TextInput
      label="章节名称"
      required
      value={draft.title}
      onChange={(event) => setDraft((current) => ({ ...current, title: event.currentTarget.value }))}
    />
    <Textarea
      label="章节摘要"
      minRows={2}
      value={draft.summary}
      onChange={(event) => setDraft((current) => ({ ...current, summary: event.currentTarget.value }))}
    />
    <Select
      label="视频类型"
      value={draft.videoProvider}
      data={[
        { value: TrainingCourseVideoProvider.None, label: '无视频' },
        { value: TrainingCourseVideoProvider.ExternalUrl, label: '外链视频' },
        { value: TrainingCourseVideoProvider.LocalFile, label: '本地视频' },
      ]}
      onChange={(value) =>
        setDraft((current) => ({
          ...current,
          videoProvider: (value as TrainingCourseVideoProvider) ?? TrainingCourseVideoProvider.None,
          videoUrl: value === TrainingCourseVideoProvider.ExternalUrl ? current.videoUrl : null,
          videoFileHash: value === TrainingCourseVideoProvider.LocalFile ? current.videoFileHash : null,
        }))
      }
    />
    {draft.videoProvider === TrainingCourseVideoProvider.ExternalUrl ? (
      <TextInput
        label="视频外链 / iframe"
        value={draft.videoUrl ?? ''}
        onChange={(event) => setDraft((current) => ({ ...current, videoUrl: event.currentTarget.value || null }))}
      />
    ) : null}
    {draft.videoProvider === TrainingCourseVideoProvider.LocalFile ? (
      <FileButton
        onChange={(file) =>
          uploadOne(file).then((hash) => {
            if (hash) setDraft((current) => ({ ...current, videoFileHash: hash }))
          })
        }
        accept="video/*"
      >
        {(props) => (
          <Button {...props} variant="light" leftSection={<Icon path={mdiFileVideoOutline} size={0.82} />}>
            上传视频
          </Button>
        )}
      </FileButton>
    ) : null}
    <Textarea
      label="章节正文 Markdown"
      className="yy-training-editor-markdown-input"
      minRows={22}
      value={draft.content}
      onChange={(event) => setDraft((current) => ({ ...current, content: event.currentTarget.value }))}
    />
    <Group grow>
      <NumberInput
        label="排序"
        value={draft.order}
        onChange={(value) => setDraft((current) => ({ ...current, order: Number(value) || 1 }))}
      />
      <Switch
        label="发布章节"
        checked={draft.isPublished}
        onChange={(event) => setDraft((current) => ({ ...current, isPublished: event.currentTarget.checked }))}
      />
    </Group>
  </>
)
