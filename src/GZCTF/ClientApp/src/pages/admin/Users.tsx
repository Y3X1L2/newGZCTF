import {
  ActionIcon,
  Avatar,
  Badge,
  Button,
  Code,
  Group,
  Modal,
  MultiSelect,
  PasswordInput,
  ScrollArea,
  Select,
  Stack,
  Switch,
  Table,
  Text,
  TextInput,
} from '@mantine/core'
import { useClipboard, useInputState } from '@mantine/hooks'
import { useModals } from '@mantine/modals'
import { showNotification } from '@mantine/notifications'
import {
  mdiAccountOutline,
  mdiAccountPlusOutline,
  mdiArrowLeftBold,
  mdiArrowRightBold,
  mdiCheck,
  mdiDeleteOutline,
  mdiLockReset,
  mdiMagnify,
  mdiPencilOutline,
} from '@mdi/js'
import { Icon } from '@mdi/react'
import React, { FC, useEffect, useRef, useState } from 'react'
import { Trans, useTranslation } from 'react-i18next'
import { ActionIconWithConfirm } from '@Components/ActionIconWithConfirm'
import { AdminPage } from '@Components/admin/AdminPage'
import { UserEditModal, RoleColorMap, roleDisplayName } from '@Components/admin/UserEditModal'
import { YinyuTableShell } from '@Components/yinyu/YinyuUI'
import { showErrorMsg } from '@Utils/Shared'
import { useArrayResponse } from '@Hooks/useArrayResponse'
import { useUser } from '@Hooks/useUser'
import api, { Role, UserInfoModel } from '@Api'
import { StudentGroupBriefModel, studentGroupAdminApi } from '@Utils/StudentGroupApi'
import tableClasses from '@Styles/Table.module.css'

const ITEM_COUNT_PER_PAGE = 30

const emptyCreateDraft = {
  userName: '',
  password: '',
  email: '',
  realName: '',
  stdNumber: '',
  phone: '',
  assignedRole: Role.Student,
  studentGroupIds: [] as string[],
}

const Users: FC = () => {
  const [page, setPage] = useState(1)
  const [update, setUpdate] = useState(new Date())
  const [editModalOpened, setEditModalOpened] = useState(false)
  const [activeUser, setActiveUser] = useState<UserInfoModel>({})
  const { data: users, total, setData: setUsers, updateData: updateUsers } = useArrayResponse<UserInfoModel>()
  const [hint, setHint] = useInputState('')
  const [searching, setSearching] = useState(false)
  const [disabled, setDisabled] = useState(false)
  const [current, setCurrent] = useState(0)
  const [roleFilter, setRoleFilter] = useState<string | null>(null)
  const [groupFilter, setGroupFilter] = useState<string | null>(null)
  const [groups, setGroups] = useState<StudentGroupBriefModel[]>([])
  const [createModalOpened, setCreateModalOpened] = useState(false)
  const [createDraft, setCreateDraft] = useState(emptyCreateDraft)

  const modals = useModals()
  const { user: currentUser } = useUser()
  const clipboard = useClipboard()
  const { t } = useTranslation()
  const viewport = useRef<HTMLDivElement>(null)
  const roleOptions = React.useMemo(() => {
    switch (currentUser?.role) {
      case Role.SuperAdmin:
        return [
          { value: Role.Student, label: '学生' },
          { value: Role.Teacher, label: '老师' },
          { value: Role.Admin, label: '管理员' },
          { value: Role.SuperAdmin, label: '超级管理员' },
          { value: Role.Banned, label: '禁用' },
        ]
      case Role.Admin:
        return [
          { value: Role.Student, label: '学生' },
          { value: Role.Teacher, label: '老师' },
          { value: Role.Banned, label: '禁用' },
        ]
      default:
        return [{ value: Role.Student, label: '学生' }]
    }
  }, [currentUser?.role])

  useEffect(() => {
    viewport.current?.scrollTo({ top: 0, behavior: 'smooth' })
  }, [page, viewport])

  useEffect(() => {
    const fetchData = async () => {
      try {
        const res = await api.admin.adminUsers({
          count: ITEM_COUNT_PER_PAGE,
          skip: (page - 1) * ITEM_COUNT_PER_PAGE,
          keyword: hint || undefined,
          role: (roleFilter as Role | null) ?? undefined,
          groupId: groupFilter ? Number(groupFilter) : undefined,
        })
        setUsers(res.data)
        setCurrent((page - 1) * ITEM_COUNT_PER_PAGE + res.data.length)
      } catch (err) {
        showErrorMsg(err, t)
      }
    }

    fetchData()
  }, [page, update, roleFilter, groupFilter])

  useEffect(() => {
    const fetchGroups = async () => {
      try {
        const res = await studentGroupAdminApi.groups()
        setGroups(res.data)
      } catch (err) {
        showErrorMsg(err, t)
      }
    }

    void fetchGroups()
  }, [])

  const onSearch = async () => {
    try {
      if (!hint) {
        const res = await api.admin.adminUsers({
          count: ITEM_COUNT_PER_PAGE,
          skip: (page - 1) * ITEM_COUNT_PER_PAGE,
          role: (roleFilter as Role | null) ?? undefined,
          groupId: groupFilter ? Number(groupFilter) : undefined,
        })
        setUsers(res.data)
        setCurrent((page - 1) * ITEM_COUNT_PER_PAGE + res.data.length)
      } else {
        const res = await api.admin.adminUsers({
          count: ITEM_COUNT_PER_PAGE,
          skip: 0,
          keyword: hint,
          role: (roleFilter as Role | null) ?? undefined,
          groupId: groupFilter ? Number(groupFilter) : undefined,
        })
        setUsers(res.data)
        setCurrent(res.data.length)
      }
    } catch (e) {
      showErrorMsg(e, t)
    } finally {
      setSearching(false)
    }
  }

  const onToggleActive = async (user: UserInfoModel) => {
    setDisabled(true)

    try {
      await api.admin.adminUpdateUserInfo(user.id!, {
        emailConfirmed: !user.emailConfirmed,
      })
      if (users) {
        updateUsers(
          users.map((u) =>
            u.id === user.id
              ? {
                  ...u,
                  emailConfirmed: !u.emailConfirmed,
                }
              : u
          )
        )
      }
    } catch (e) {
      showErrorMsg(e, t)
    } finally {
      setDisabled(false)
    }
  }

  const onResetPassword = async (user: UserInfoModel) => {
    setDisabled(true)
    try {
      const res = await api.admin.adminResetPassword(user.id!)

      modals.openModal({
        title: t('admin.content.users.reset.title', {
          name: user.userName,
        }),

        children: (
          <Stack>
            <Text>
              <Trans i18nKey="admin.content.users.reset.content" />
            </Text>
            <Text fw="bold" ff="monospace">
              {res.data}
            </Text>
            <Button
              onClick={() => {
                clipboard.copy(res.data)
                showNotification({
                  message: t('admin.notification.users.password_copied'),
                  color: 'teal',
                  icon: <Icon path={mdiCheck} size={1} />,
                })
              }}
            >
              {t('common.button.copy')}
            </Button>
          </Stack>
        ),
      })
    } catch (err: any) {
      showErrorMsg(err, t)
    } finally {
      setDisabled(false)
    }
  }

  const onDelete = async (user: UserInfoModel) => {
    try {
      setDisabled(true)
      if (!user.id) return

      await api.admin.adminDeleteUser(user.id)
      showNotification({
        message: t('admin.notification.users.deleted', {
          name: user.userName,
        }),
        color: 'teal',
        icon: <Icon path={mdiCheck} size={1} />,
      })
      if (users) {
        updateUsers(users.filter((x) => x.id !== user.id))
      }
      setCurrent(current - 1)
      setUpdate(new Date())
    } catch (e: any) {
      showErrorMsg(e, t)
    } finally {
      setDisabled(false)
    }
  }

  const onCreateUser = async () => {
    if (!createDraft.userName.trim() || !createDraft.password || !createDraft.email.trim()) return

    setDisabled(true)
    try {
      await api.admin.adminAddUsers([
        {
          userName: createDraft.userName.trim(),
          password: createDraft.password,
          email: createDraft.email.trim(),
          realName: createDraft.realName.trim() || undefined,
          stdNumber: createDraft.stdNumber.trim() || undefined,
          phone: createDraft.phone.trim() || undefined,
          assignedRole: createDraft.assignedRole,
          studentGroupIds:
            createDraft.assignedRole === Role.Student
              ? createDraft.studentGroupIds.map(Number).filter((id) => Number.isInteger(id) && id > 0)
              : [],
        },
      ])
      setCreateDraft(emptyCreateDraft)
      setCreateModalOpened(false)
      setUpdate(new Date())
      showNotification({
        message: '用户已创建',
        color: 'teal',
        icon: <Icon path={mdiCheck} size={1} />,
      })
    } catch (err) {
      showErrorMsg(err, t)
    } finally {
      setDisabled(false)
    }
  }

  return (
    <AdminPage
      isLoading={searching || !users}
      head={
        <>
          <TextInput
            miw={280}
            maw={460}
            style={{ flex: '1 1 22rem' }}
            leftSection={<Icon path={mdiMagnify} size={1} />}
            placeholder={t('admin.placeholder.users.search')}
            value={hint}
            onChange={setHint}
            onKeyDown={(e) => {
              if (!searching && e.key === 'Enter') onSearch()
            }}
            rightSection={<Icon path={mdiAccountOutline} size={1} />}
          />
          <Select
            w={150}
            clearable
            placeholder="角色筛选"
            data={roleOptions}
            value={roleFilter}
            onChange={(value) => {
              setRoleFilter(value)
              setPage(1)
            }}
          />
          <Select
            w={180}
            searchable
            clearable
            placeholder="分组筛选"
            data={groups.map((group) => ({ value: group.id.toString(), label: group.name }))}
            value={groupFilter}
            onChange={(value) => {
              setGroupFilter(value)
              setPage(1)
            }}
          />
          <Group justify="right" wrap="nowrap" style={{ overflowX: 'auto' }}>
            <Button
              leftSection={<Icon path={mdiAccountPlusOutline} size={0.9} />}
              onClick={() => setCreateModalOpened(true)}
            >
              新建用户
            </Button>
            <Text fw="bold" size="sm">
              <Trans
                i18nKey="admin.content.users.stats"
                values={{
                  current,
                  total,
                }}
              >
                _<Code>_</Code>_
              </Trans>
            </Text>
            <ActionIcon size="lg" disabled={page <= 1} onClick={() => setPage(page - 1)}>
              <Icon path={mdiArrowLeftBold} size={1} />
            </ActionIcon>
            <Text fw="bold" size="sm">
              {page}
            </Text>
            <ActionIcon size="lg" disabled={page * ITEM_COUNT_PER_PAGE >= total} onClick={() => setPage(page + 1)}>
              <Icon path={mdiArrowRightBold} size={1} />
            </ActionIcon>
          </Group>
        </>
      }
    >
      <YinyuTableShell p="xs" w="100%">
        <ScrollArea viewportRef={viewport} offsetScrollbars scrollbarSize={4} h="calc(100vh - 190px)">
          <Table className={tableClasses.table}>
            <Table.Thead>
              <Table.Tr>
                <Table.Th miw="1.8rem">{t('admin.label.users.active')}</Table.Th>
                <Table.Th>{t('common.label.user')}</Table.Th>
                <Table.Th>{t('account.label.email')}</Table.Th>
                <Table.Th>{t('common.label.ip')}</Table.Th>
                <Table.Th>{t('account.label.real_name')}</Table.Th>
                <Table.Th>{t('account.label.student_id')}</Table.Th>
                <Table.Th>培训分组</Table.Th>
                <Table.Th />
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {users &&
                users.map((user) => (
                  <Table.Tr key={user.id}>
                    <Table.Td>
                      <Switch
                        disabled={disabled}
                        checked={user.emailConfirmed ?? false}
                        onChange={() => onToggleActive(user)}
                      />
                    </Table.Td>
                    <Table.Td>
                      <Group wrap="nowrap" justify="space-between" gap="xs">
                        <Group wrap="nowrap" justify="left">
                          <Avatar alt="avatar" src={user.avatar} radius="xl">
                            {user.userName?.slice(0, 1) ?? 'U'}
                          </Avatar>
                          <Text ff="monospace" size="sm" fw="bold" lineClamp={1}>
                            {user.userName}
                          </Text>
                        </Group>
                        <Badge
                          size="sm"
                          color={RoleColorMap.get(user.role ?? Role.Student)}
                          className="yy-semantic-badge"
                          data-semantic={`role-${String(user.role ?? Role.Student).toLowerCase()}`}
                        >
                          {roleDisplayName(user.role)}
                        </Badge>
                      </Group>
                    </Table.Td>
                    <Table.Td>
                      <Text size="sm" lineClamp={1}>
                        {user.email}
                      </Text>
                    </Table.Td>
                    <Table.Td>
                      <Text lineClamp={1} size="sm" ff="monospace">
                        {user.ip}
                      </Text>
                    </Table.Td>
                    <Table.Td>{user.realName ?? t('admin.placeholder.users.real_name')}</Table.Td>
                    <Table.Td>
                      <Text size="sm" ff="monospace">
                        {user.stdNumber ?? t('admin.placeholder.users.student_id')}
                      </Text>
                    </Table.Td>
                    <Table.Td>
                      <Group gap={4} wrap="wrap">
                        {(user.studentGroups ?? []).length > 0 ? (
                          user.studentGroups?.map((group) => (
                            <Badge key={group.id} size="xs" className="yy-gradient-status">
                              {group.name}
                            </Badge>
                          ))
                        ) : (
                          <Text size="xs" c="dimmed">
                            未分组
                          </Text>
                        )}
                      </Group>
                    </Table.Td>
                    <Table.Td align="right">
                      <Group wrap="nowrap" gap="sm" justify="right">
                        <ActionIcon
                          color="blue"
                          onClick={() => {
                            setActiveUser(user)
                            setEditModalOpened(true)
                          }}
                        >
                          <Icon path={mdiPencilOutline} size={1} />
                        </ActionIcon>
                        <ActionIconWithConfirm
                          iconPath={mdiLockReset}
                          color="orange"
                          message={t('admin.content.users.reset.message', {
                            name: user.userName,
                          })}
                          disabled={disabled}
                          onClick={() => onResetPassword(user)}
                        />
                        <ActionIconWithConfirm
                          iconPath={mdiDeleteOutline}
                          color="alert"
                          message={t('admin.content.users.delete', {
                            name: user.userName,
                          })}
                          disabled={disabled || user.id === currentUser?.userId}
                          onClick={() => onDelete(user)}
                        />
                      </Group>
                    </Table.Td>
                  </Table.Tr>
                ))}
            </Table.Tbody>
          </Table>
        </ScrollArea>
        <UserEditModal
          size="35%"
          title={t('admin.button.users.edit')}
          user={activeUser}
          opened={editModalOpened}
          onClose={() => setEditModalOpened(false)}
          mutateUser={(user: UserInfoModel) => {
            updateUsers(
              [user, ...(users?.filter((n) => n.id !== user.id) ?? [])].sort((a, b) => (a.id! < b.id! ? -1 : 1))
            )
          }}
        />
        <Modal
          opened={createModalOpened}
          onClose={() => setCreateModalOpened(false)}
          title="新建用户"
          size="lg"
        >
          <Stack gap="md">
            <Group grow align="start">
              <TextInput
                required
                label="用户名"
                value={createDraft.userName}
                onChange={(event) => setCreateDraft({ ...createDraft, userName: event.currentTarget.value })}
              />
              <PasswordInput
                required
                label="初始密码"
                value={createDraft.password}
                onChange={(event) => setCreateDraft({ ...createDraft, password: event.currentTarget.value })}
              />
            </Group>
            <Group grow align="start">
              <TextInput
                required
                label="邮箱"
                value={createDraft.email}
                onChange={(event) => setCreateDraft({ ...createDraft, email: event.currentTarget.value })}
              />
              <Select
                label="权限角色"
                data={roleOptions}
                value={createDraft.assignedRole}
                onChange={(value) =>
                  setCreateDraft({
                    ...createDraft,
                    assignedRole: (value as Role | null) ?? Role.Student,
                    studentGroupIds: value === Role.Student ? createDraft.studentGroupIds : [],
                  })
                }
              />
            </Group>
            {createDraft.assignedRole === Role.Student ? (
              <MultiSelect
                label="培训分组"
                description="老师创建学生时，如不选择分组，后端会自动放入“我的默认分组”。"
                searchable
                clearable
                data={groups.map((group) => ({ value: group.id.toString(), label: group.name }))}
                value={createDraft.studentGroupIds}
                onChange={(values) => setCreateDraft({ ...createDraft, studentGroupIds: values })}
              />
            ) : null}
            <Group grow align="start">
              <TextInput
                label="真实姓名"
                value={createDraft.realName}
                onChange={(event) => setCreateDraft({ ...createDraft, realName: event.currentTarget.value })}
              />
              <TextInput
                label="学号"
                value={createDraft.stdNumber}
                onChange={(event) => setCreateDraft({ ...createDraft, stdNumber: event.currentTarget.value })}
              />
            </Group>
            <TextInput
              label="手机号"
              value={createDraft.phone}
              onChange={(event) => setCreateDraft({ ...createDraft, phone: event.currentTarget.value })}
            />
            <Group justify="flex-end">
              <Button variant="subtle" onClick={() => setCreateModalOpened(false)}>
                取消
              </Button>
              <Button disabled={disabled} onClick={onCreateUser}>
                创建
              </Button>
            </Group>
          </Stack>
        </Modal>
      </YinyuTableShell>
    </AdminPage>
  )
}

export default Users
