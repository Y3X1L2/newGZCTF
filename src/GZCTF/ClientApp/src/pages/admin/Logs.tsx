import { ActionIcon, Badge, Group, ScrollArea, SegmentedControl, Table, Text, useMantineTheme } from '@mantine/core'
import { showNotification } from '@mantine/notifications'
import { mdiArrowLeftBold, mdiArrowRightBold, mdiClose } from '@mdi/js'
import { Icon } from '@mdi/react'
import * as signalR from '@microsoft/signalr'
import cx from 'clsx'
import dayjs from 'dayjs'
import { FC, useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { AdminPage } from '@Components/admin/AdminPage'
import { YinyuTableShell } from '@Components/yinyu/YinyuUI'
import { handleAxiosError } from '@Utils/ApiHelper'
import { useLanguage } from '@Utils/I18n'
import { TaskStatusColorMap } from '@Utils/Shared'
import api, { LogMessageModel, TaskStatus } from '@Api'
import tableClasses from '@Styles/Table.module.css'

const ITEM_COUNT_PER_PAGE = 50

enum LogLevel {
  Info = 'Information',
  Warn = 'Warning',
  Error = 'Error',
  All = 'All',
}

function taskStatusSemantic(status?: TaskStatus | null) {
  switch (status) {
    case TaskStatus.Success:
      return 'success'
    case TaskStatus.Failed:
    case TaskStatus.Denied:
    case TaskStatus.Unhealthy:
      return 'failed'
    case TaskStatus.Pending:
      return 'pending'
    case TaskStatus.Degraded:
      return 'warning'
    case TaskStatus.NotFound:
      return 'not-found'
    case TaskStatus.Duplicate:
      return 'duplicate'
    case TaskStatus.Exit:
      return 'canceled'
    default:
      return 'unknown'
  }
}

const Logs: FC = () => {
  const [level, setLevel] = useState(LogLevel.Info)
  const [cursorStack, setCursorStack] = useState<string[]>([])
  const [nextCursor, setNextCursor] = useState<string | null>()
  const cursor = cursorStack.at(-1)
  const activePage = cursorStack.length + 1
  const theme = useMantineTheme()

  const [, update] = useState(new Date())
  const newLogs = useRef<LogMessageModel[]>([])
  const [logs, setLogs] = useState<LogMessageModel[]>()

  const { t } = useTranslation()
  const { locale } = useLanguage()
  const viewport = useRef<HTMLDivElement>(null)

  useEffect(() => {
    viewport.current?.scrollTo({ top: 0, behavior: 'smooth' })
  }, [activePage, level, viewport])

  useEffect(() => {
    const fetchLogs = async () => {
      try {
        const res = await api.admin.adminLogs({
          level,
          count: ITEM_COUNT_PER_PAGE,
          cursor,
        })
        setLogs(res.data.items)
        setNextCursor(res.data.nextCursor)
      } catch (err) {
        showNotification({
          color: 'red',
          title: t('admin.notification.logs.fetch_failed'),
          message: await handleAxiosError(err),
          icon: <Icon path={mdiClose} size={1} />,
        })
      }
    }

    fetchLogs()

    if (activePage === 1) {
      newLogs.current = []
    }
  }, [activePage, cursor, level])

  useEffect(() => {
    setCursorStack([])
  }, [level])

  useEffect(() => {
    const connection = new signalR.HubConnectionBuilder()
      .withUrl('/hub/admin')
      .withHubProtocol(new signalR.JsonHubProtocol())
      .withAutomaticReconnect()
      .configureLogging(signalR.LogLevel.None)
      .build()

    connection.serverTimeoutInMilliseconds = 60 * 1000 * 60 * 24

    connection.on('ReceivedLog', (message: LogMessageModel) => {
      console.log(message)
      newLogs.current = [message, ...newLogs.current]
      update(new Date(message.time!))
    })

    const startConnection = async () => {
      try {
        await connection.start()
      } catch (err) {
        console.error(err)
      }
    }

    startConnection()

    return () => {
      connection.stop().catch((err) => {
        console.error(err)
      })
    }
  }, [])

  const rows = [...(activePage === 1 ? newLogs.current : []), ...(logs ?? [])]
    .filter((item) => level === 'All' || item.level === level)
    .map((item, i) => (
      <Table.Tr
        key={item.id ?? `${item.time}@${i}`}
        className={cx({
          [tableClasses.fade]:
            i === 0 && activePage === 1 && newLogs.current.length > 0 && newLogs.current[0].level === level,
        })}
      >
        <Table.Td className={tableClasses.time}>
          <Badge size="sm" color="indigo" fullWidth>
            {dayjs(item.time).locale(locale).format('SL HH:mm:ss')}
          </Badge>
        </Table.Td>
        <Table.Td>
          <Text ff="monospace" size="sm" fw={500} className={tableClasses.overflow}>
            {item.ip || ''}
          </Text>
        </Table.Td>
        <Table.Td>
          <Text ff="monospace" size="sm" fw="bold" className={tableClasses.overflow}>
            {item.name || ''}
          </Text>
        </Table.Td>
        <Table.Td>
          <Text size="sm" className={tableClasses.overflow}>
            {item.msg || ''}
          </Text>
        </Table.Td>
        <Table.Td ff="monospace">
          {item.status && (
            <Badge
              size="sm"
              color={TaskStatusColorMap.get(item.status as TaskStatus) ?? 'gray'}
              className="yy-semantic-badge"
              data-semantic={taskStatusSemantic(item.status as TaskStatus)}
            >
              {item.status}
            </Badge>
          )}
        </Table.Td>
      </Table.Tr>
    ))

  return (
    <AdminPage
      isLoading={!logs}
      head={
        <>
          <SegmentedControl
            className="yy-admin-logs-filter"
            color={theme.primaryColor}
            value={level}
            bg="transparent"
            onChange={(value) => setLevel(value as LogLevel)}
            data={Object.entries(LogLevel).map((role) => ({
              value: role[1],
              label: role[0],
            }))}
          />
          <Group justify="right" className="yy-admin-logs-pagination">
            <ActionIcon
              size="lg"
              disabled={activePage <= 1}
              onClick={() => setCursorStack((current) => current.slice(0, -1))}
            >
              <Icon path={mdiArrowLeftBold} size={1} />
            </ActionIcon>
            <Text fw="bold" size="sm">
              {activePage}
            </Text>
            <ActionIcon
              size="lg"
              disabled={!nextCursor}
              onClick={() => nextCursor && setCursorStack((current) => [...current, nextCursor])}
            >
              <Icon path={mdiArrowRightBold} size={1} />
            </ActionIcon>
          </Group>
        </>
      }
    >
      <YinyuTableShell p="md" w="100%" className="yy-admin-logs-page">
        <ScrollArea viewportRef={viewport} offsetScrollbars scrollbarSize={4} h="calc(100vh - 190px)">
          <Table className={cx(tableClasses.table, tableClasses.fixed)}>
            <Table.Thead>
              <Table.Tr>
                <Table.Th w="7rem">{t('common.label.time')}</Table.Th>
                <Table.Th w="9rem">{t('common.label.ip')}</Table.Th>
                <Table.Th w="7rem">{t('common.label.user')}</Table.Th>
                <Table.Th w="100%">{t('admin.label.logs.message')}</Table.Th>
                <Table.Th w="6rem">{t('admin.label.logs.status')}</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>{rows}</Table.Tbody>
          </Table>
        </ScrollArea>
      </YinyuTableShell>
    </AdminPage>
  )
}

export default Logs
