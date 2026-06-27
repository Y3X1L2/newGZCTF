import {
  Avatar,
  Button,
  Center,
  Grid,
  Group,
  Modal,
  ModalProps,
  MultiSelect,
  Radio,
  SimpleGrid,
  Stack,
  Text,
  Textarea,
  TextInput,
} from '@mantine/core'
import { showNotification } from '@mantine/notifications'
import { mdiCheck } from '@mdi/js'
import { Icon } from '@mdi/react'
import dayjs from 'dayjs'
import { FC, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { YinyuModalBody } from '@Components/yinyu/YinyuUI'
import { isValidPhoneNumber, showErrorMsg } from '@Utils/Shared'
import { useUser } from '@Hooks/useUser'
import api, { AdminUserInfoModel, Role, UserInfoModel } from '@Api'
import { StudentGroupBriefModel, trainingAdminApi } from '@Utils/TrainingApi'

export const RoleColorMap = new Map<Role, string>([
  [Role.SuperAdmin, 'grape'],
  [Role.Admin, 'violet'],
  [Role.Teacher, 'cyan'],
  [Role.Student, 'teal'],
  [Role.User, 'teal'],
  [Role.Monitor, 'cyan'],
  [Role.Banned, 'red'],
])

const getAssignableRoles = (role?: Role | null) => {
  switch (role) {
    case Role.SuperAdmin:
      return [Role.Student, Role.Teacher, Role.Admin, Role.SuperAdmin, Role.Banned]
    case Role.Admin:
      return [Role.Student, Role.Teacher, Role.Banned]
    case Role.Teacher:
    case Role.Monitor:
      return [Role.Student]
    default:
      return []
  }
}

export const roleDisplayName = (role?: Role | null) => {
  switch (role) {
    case Role.SuperAdmin:
      return '超级管理员'
    case Role.Admin:
      return '管理员'
    case Role.Teacher:
    case Role.Monitor:
      return '老师'
    case Role.Student:
    case Role.User:
      return '学生'
    case Role.Banned:
      return '禁用'
    default:
      return '未知'
  }
}

interface UserEditModalProps extends ModalProps {
  user: UserInfoModel
  mutateUser: (user: UserInfoModel) => void
}

export const UserEditModal: FC<UserEditModalProps> = (props) => {
  const { user, mutateUser, ...modalProps } = props
  const { user: self } = useUser()

  const [disabled, setDisabled] = useState(false)
  const [profile, setProfile] = useState<AdminUserInfoModel>({})
  const [groups, setGroups] = useState<StudentGroupBriefModel[]>([])

  const { t } = useTranslation()
  const isSelf = self?.userId === user.id
  const roleOptions = getAssignableRoles(self?.role)
  const editingStudent = (profile.role ?? user.role) === Role.Student || (profile.role ?? user.role) === Role.User

  useEffect(() => {
    setProfile({ ...user, studentGroupIds: user.studentGroups?.map((group) => group.id!).filter(Boolean) ?? [] })
  }, [user])

  useEffect(() => {
    const loadGroups = async () => {
      try {
        const res = await trainingAdminApi.groups()
        setGroups(res.data)
      } catch (e) {
        showErrorMsg(e, t)
      }
    }

    if (modalProps.opened) void loadGroups()
  }, [modalProps.opened])

  const onChangeProfile = async () => {
    if (!user.id) return
    if (!isValidPhoneNumber(profile.phone)) {
      showNotification({ color: 'red', message: t('common.error.check_input') })
      return
    }

    setDisabled(true)

    try {
      await api.admin.adminUpdateUserInfo(user.id, profile)
      showNotification({
        color: 'teal',
        message: t('admin.notification.users.updated'),
        icon: <Icon path={mdiCheck} size={1} />,
      })
      mutateUser({ ...user, ...profile })
      modalProps.onClose()
    } catch (e) {
      showErrorMsg(e, t)
    } finally {
      setDisabled(false)
    }
  }

  return (
    <Modal {...modalProps}>
      <YinyuModalBody>
        <Stack gap="md" m="auto">
          <Grid grow>
            <Grid.Col span={8}>
              <TextInput
                label={t('account.label.username')}
                type="text"
                w="100%"
                value={profile.userName ?? 'ctfer'}
                disabled={disabled}
                onChange={(event) => setProfile({ ...profile, userName: event.target.value })}
              />
            </Grid.Col>
            <Grid.Col span={4}>
              <Center>
                <Avatar alt="avatar" radius="xl" size={70} src={user.avatar}>
                  {user.userName?.slice(0, 1) ?? 'U'}
                </Avatar>
              </Center>
            </Grid.Col>
          </Grid>
          <Radio.Group
            label={t('admin.label.users.role')}
            value={profile.role as Role | undefined}
            onChange={(value) => {
              setProfile({ ...profile, role: value as Role })
            }}
          >
            <Group grow mt="xs">
              {roleOptions.map((role) => (
                <Radio
                  key={role}
                  value={role}
                  label={
                    <Text size="sm" fw="bold">
                      {roleDisplayName(role)}
                    </Text>
                  }
                  color={RoleColorMap.get(role)}
                  disabled={disabled || isSelf}
                />
              ))}
            </Group>
          </Radio.Group>
          {editingStudent ? (
            <MultiSelect
              label="培训分组"
              placeholder="选择学生所属分组"
              searchable
              clearable
              data={groups.map((group) => ({ value: group.id.toString(), label: group.name }))}
              value={(profile.studentGroupIds ?? []).map(String)}
              onChange={(values) =>
                setProfile({
                  ...profile,
                  studentGroupIds: values.map(Number).filter((id) => Number.isInteger(id) && id > 0),
                })
              }
              disabled={disabled}
            />
          ) : null}
          <SimpleGrid cols={2}>
            <TextInput
              label={t('account.label.email')}
              type="email"
              w="100%"
              value={profile.email ?? 'ctfer@gzti.me'}
              disabled={disabled}
              onChange={(event) => setProfile({ ...profile, email: event.target.value })}
            />
            <TextInput
              label={t('account.label.phone')}
              type="tel"
              w="100%"
              value={profile.phone ?? ''}
              disabled={disabled}
              error={!isValidPhoneNumber(profile.phone) ? t('common.error.check_input') : undefined}
              onChange={(event) => setProfile({ ...profile, phone: event.target.value })}
            />
            <TextInput
              label={t('account.label.student_id')}
              type="text"
              w="100%"
              value={profile.stdNumber ?? ''}
              disabled={disabled}
              onChange={(event) => setProfile({ ...profile, stdNumber: event.target.value })}
            />
            <TextInput
              label={t('account.label.real_name')}
              type="text"
              w="100%"
              value={profile.realName ?? ''}
              disabled={disabled}
              onChange={(event) => setProfile({ ...profile, realName: event.target.value })}
            />
          </SimpleGrid>
          <Textarea
            label={t('account.label.bio')}
            value={profile.bio ?? t('account.placeholder.bio')}
            w="100%"
            disabled={disabled}
            autosize
            minRows={2}
            maxRows={4}
            onChange={(event) => setProfile({ ...profile, bio: event.target.value })}
          />

          <Stack gap={2}>
            <Group justify="space-between">
              <Text size="sm" fw={500}>
                {t('common.label.ip')}
              </Text>
              <Text size="sm" span fw={500} ff="monospace">
                {user.ip}
              </Text>
            </Group>
            <Group justify="space-between">
              <Text size="sm" fw={500}>
                {t('admin.label.users.last_visit')}
              </Text>
              <Text size="sm" span fw={500} ff="monospace">
                {dayjs(user.lastVisitedUtc).format('YYYY-MM-DD HH:mm:ss')}
              </Text>
            </Group>
          </Stack>

          <Group grow m="auto" w="100%">
            <Button fullWidth disabled={disabled} onClick={onChangeProfile}>
              {t('admin.button.save')}
            </Button>
          </Group>
        </Stack>
      </YinyuModalBody>
    </Modal>
  )
}
