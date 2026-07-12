import { Avatar, Badge, Button, Divider, Group, Modal, ScrollArea, Select, Stack, Text, TextInput } from '@mantine/core'
import { mdiMagnify } from '@mdi/js'
import { Icon } from '@mdi/react'
import {
  TrainingCourseModel,
  TrainingCourseStudentCandidateModel,
  TrainingCourseTeacherCandidateModel,
  TrainingCourseTeacherRole,
} from '@Utils/TrainingApi'
import { teacherRoleOptions, teacherRoleText } from './courseDetailModel'

export function AddStudentModal({
  opened,
  keyword,
  candidates,
  selectedId,
  saving,
  onClose,
  onKeywordChange,
  onSearch,
  onSelect,
  onAdd,
}: {
  opened: boolean
  keyword: string
  candidates: TrainingCourseStudentCandidateModel[]
  selectedId: string | null
  saving: boolean
  onClose: () => void
  onKeywordChange: (value: string) => void
  onSearch: () => void
  onSelect: (value: string) => void
  onAdd: () => void
}) {
  return (
    <Modal opened={opened} onClose={onClose} title="添加学员" size="lg">
      <Stack>
        <Group align="flex-end">
          <TextInput
            label="搜索学员"
            placeholder="用户名、姓名、学号、邮箱或 ID"
            value={keyword}
            onChange={(event) => onKeywordChange(event.currentTarget.value)}
            onKeyDown={(event) => {
              if (event.key === 'Enter') onSearch()
            }}
            flex={1}
          />
          <Button variant="light" leftSection={<Icon path={mdiMagnify} size={0.82} />} onClick={onSearch}>
            搜索
          </Button>
        </Group>
        <ScrollArea.Autosize mah={320}>
          <Stack gap="xs">
            {candidates.map((candidate) => {
              const selected = selectedId === candidate.userId
              return (
                <Button
                  key={candidate.userId}
                  variant={selected ? 'light' : 'subtle'}
                  color={candidate.alreadyEnrolled ? 'gray' : selected ? 'teal' : undefined}
                  disabled={candidate.alreadyEnrolled}
                  justify="flex-start"
                  h="auto"
                  py="xs"
                  onClick={() => onSelect(candidate.userId)}
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
            {candidates.length === 0 ? (
              <Text size="sm" c="dimmed" ta="center" py="md">
                暂无匹配学员
              </Text>
            ) : null}
          </Stack>
        </ScrollArea.Autosize>
        <Group justify="flex-end">
          <Button variant="subtle" onClick={onClose}>
            取消
          </Button>
          <Button loading={saving} disabled={!selectedId} onClick={onAdd}>
            添加到课程
          </Button>
        </Group>
      </Stack>
    </Modal>
  )
}

export function AddTeacherModal({
  opened,
  keyword,
  candidates,
  teachers,
  selectedId,
  selectedRole,
  saving,
  onClose,
  onKeywordChange,
  onSearch,
  onSelect,
  onRoleChange,
  onAdd,
}: {
  opened: boolean
  keyword: string
  candidates: TrainingCourseTeacherCandidateModel[]
  teachers: TrainingCourseModel['teachers']
  selectedId: string | null
  selectedRole: TrainingCourseTeacherRole
  saving: boolean
  onClose: () => void
  onKeywordChange: (value: string) => void
  onSearch: () => void
  onSelect: (value: string | null) => void
  onRoleChange: (value: TrainingCourseTeacherRole) => void
  onAdd: () => void
}) {
  return (
    <Modal opened={opened} onClose={onClose} title="添加授课教师" size="lg">
      <Stack>
        <Group align="flex-end">
          <TextInput
            label="搜索用户"
            placeholder="用户名、姓名、邮箱或 ID"
            value={keyword}
            onChange={(event) => onKeywordChange(event.currentTarget.value)}
            flex={1}
          />
          <Button variant="light" leftSection={<Icon path={mdiMagnify} size={0.82} />} onClick={onSearch}>
            搜索
          </Button>
        </Group>
        <Select
          label="候选教师"
          placeholder="选择 Teacher/Admin/SuperAdmin 用户"
          searchable
          value={selectedId}
          onChange={onSelect}
          data={candidates.map((candidate) => ({
            value: candidate.userId,
            label: `${candidate.realName || candidate.userName} (${candidate.userName}) · ${candidate.role}${
              candidate.alreadyTeacher ? ' · 已在课程中' : ''
            }`,
            disabled: candidate.alreadyTeacher,
          }))}
        />
        <Select
          label="课程角色"
          value={selectedRole}
          data={teacherRoleOptions}
          onChange={(value) => onRoleChange((value as TrainingCourseTeacherRole) ?? TrainingCourseTeacherRole.Teacher)}
        />
        <Group justify="flex-end">
          <Button loading={saving} disabled={!selectedId} onClick={onAdd}>
            添加到课程
          </Button>
        </Group>
        <Divider />
        <Stack gap="xs">
          <Text fw={900}>当前授课教师</Text>
          {teachers.map((teacher) => (
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
  )
}
