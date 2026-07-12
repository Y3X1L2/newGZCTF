import { Button, Group, Stack, Text, Title } from '@mantine/core'
import { mdiArchiveOutline, mdiPencilOutline, mdiPublish, mdiTrashCanOutline } from '@mdi/js'
import { Icon } from '@mdi/react'
import {
  TrainingStatusText,
  TrainingTagLine,
  trainingCourseProgress,
  trainingCourseStatus,
  trainingTags,
  trainingTeacherNames,
} from '@Components/training/TrainingCourseUI'
import { YinyuPanel } from '@Components/yinyu/YinyuUI'
import { TrainingCourseModel, TrainingCourseStatus } from '@Utils/TrainingApi'
import classes from './CourseHero.module.css'

type Props = {
  course: TrainingCourseModel
  canLearn: boolean
  onEnroll: () => void
  onEdit: () => void
  onPublish: () => void
  onArchive: () => void
  onDelete: () => void
}

export function CourseHero({ course, canLearn, onEnroll, onEdit, onPublish, onArchive, onDelete }: Props) {
  const status = trainingCourseStatus(course)
  const progress = trainingCourseProgress(course)

  return (
    <YinyuPanel p="lg" className="yy-course-detail-hero yy-training-course-detail-hero">
      <div className={`${classes.heroGrid} yy-course-detail-hero-grid`}>
        <div className={`${classes.coverFrame} yy-course-detail-cover`}>
          {course.coverUrl ? <img className={classes.coverImage} src={course.coverUrl} alt="" /> : <span>YINYU TRAINING</span>}
        </div>

        <Stack gap="md" className="yy-course-detail-hero-body">
          <Group justify="space-between" align="flex-start" gap="md">
            <Group gap="md">
              <TrainingStatusText tone={status.tone}>{status.label}</TrainingStatusText>
              <TrainingTagLine tags={trainingTags(course)} max={5} />
            </Group>
            <TrainingStatusText tone="ongoing">{progress}%</TrainingStatusText>
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
            {!canLearn && course.status === TrainingCourseStatus.Published ? <Button onClick={onEnroll}>报名课程</Button> : null}
            {course.canEdit ? (
              <>
                <Button variant="light" leftSection={<Icon path={mdiPencilOutline} size={0.82} />} onClick={onEdit}>
                  编辑课程
                </Button>
                {course.status !== TrainingCourseStatus.Published ? (
                  <>
                    <Button leftSection={<Icon path={mdiPublish} size={0.82} />} onClick={onPublish}>
                      发布
                    </Button>
                    {course.status === TrainingCourseStatus.Draft ? (
                      <Button
                        color="orange"
                        variant="light"
                        leftSection={<Icon path={mdiArchiveOutline} size={0.82} />}
                        onClick={onArchive}
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
                    onClick={onArchive}
                  >
                    归档
                  </Button>
                )}
              </>
            ) : null}
            {course.canDelete ? (
              <Button color="red" variant="light" leftSection={<Icon path={mdiTrashCanOutline} size={0.82} />} onClick={onDelete}>
                删除课程
              </Button>
            ) : null}
          </Group>
        </Stack>
      </div>
    </YinyuPanel>
  )
}
