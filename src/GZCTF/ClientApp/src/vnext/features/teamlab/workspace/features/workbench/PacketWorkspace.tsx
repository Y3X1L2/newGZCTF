import { ChevronDown, ChevronRight, ShieldAlert } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import type { Schema } from '../../api/client'
import { dateTime } from '../../foundation/format'
import styles from './PacketWorkspace.module.css'
import { riskLevel } from './TrafficGraph'
import { packetAlerts } from './trafficModel'

export type Field = Schema<'PacketField'>
type Packet = Schema<'CapturePacket'>
const time = (value: string) => {
  const date = new Date(value)
  return date.toLocaleTimeString('zh-CN', { hour12: false }) + '.' + String(date.getMilliseconds()).padStart(3, '0')
}
const socket = (address: string, port: number) =>
  port ? `${address.includes(':') ? `[${address}]` : address}:${port}` : address

export function PacketList({
  packets,
  selectedId,
  onSelect,
  onOpen,
  alerts,
  loading,
  onOlder,
  fetching,
  live,
}: {
  packets: Packet[]
  selectedId?: string
  onSelect: (id: string) => void
  onOpen?: (id: string) => void
  alerts: Schema<'SecurityAlert'>[]
  loading: boolean
  onOlder?: () => void
  fetching: boolean
  live: boolean
}) {
  const host = useRef<HTMLDivElement>(null)
  useEffect(() => {
    if (live && selectedId === packets.at(-1)?.id) host.current?.scrollTo({ top: host.current.scrollHeight })
  }, [packets, live, selectedId])
  return (
    <div className={styles.list} ref={host}>
      {onOlder && (
        <button className={styles.older} onClick={onOlder} disabled={fetching}>
          {fetching ? '读取中…' : '更早报文'}
        </button>
      )}
      <table
        aria-label="报文列表"
        role="grid"
        onKeyDown={(event) => {
          if (!['ArrowDown', 'ArrowUp', 'Home', 'End'].includes(event.key)) return
          event.preventDefault()
          const current = packets.findIndex((packet) => packet.id === selectedId)
          const index =
            event.key === 'Home'
              ? 0
              : event.key === 'End'
                ? packets.length - 1
                : Math.max(0, Math.min(packets.length - 1, current + (event.key === 'ArrowDown' ? 1 : -1)))
          if (packets[index]) {
            onSelect(packets[index].id!)
            host.current?.querySelector<HTMLTableRowElement>(`tbody tr:nth-child(${index + 1})`)?.focus()
          }
        }}
      >
        <thead>
          <tr>
            <th>No.</th>
            <th>时间</th>
            <th>来源</th>
            <th>目标</th>
            <th>协议</th>
            <th>长度</th>
            <th>摘要</th>
            <th aria-label="关联风险">
              <ShieldAlert size={13} />
            </th>
          </tr>
        </thead>
        <tbody>
          {packets.map((packet, index) => {
            const related = packetAlerts(packet, alerts).sort((a, b) => a.severity - b.severity),
              risk = related[0] && riskLevel(related[0].severity)
            return (
              <tr
                key={packet.id}
                aria-selected={selectedId === packet.id}
                data-risk={risk?.value}
                data-protocol={packet.protocol.toLowerCase()}
                tabIndex={selectedId === packet.id || (!selectedId && index === 0) ? 0 : -1}
                onClick={() => {
                  onSelect(packet.id!)
                  onOpen?.(packet.id!)
                }}
                onKeyDown={(event) => {
                  if (event.key === 'Enter' || event.key === ' ') {
                    event.preventDefault()
                    onSelect(packet.id!)
                    onOpen?.(packet.id!)
                  }
                }}
              >
                <td>
                  <button tabIndex={-1} aria-label={`查看第 ${packet.number} 帧`}>
                    {packet.number}
                  </button>
                </td>
                <td>{time(packet.time)}</td>
                <td title={packet.source}>{socket(packet.source, packet.sourcePort)}</td>
                <td title={packet.destination}>{socket(packet.destination, packet.destinationPort)}</td>
                <td>
                  <span className={styles.protocolTag}>{packet.protocol}</span>
                </td>
                <td>{packet.length}</td>
                <td title={packet.summary}>{packet.summary}</td>
                <td>
                  {risk && (
                    <span
                      className={styles.risk}
                      style={{ color: risk.color }}
                      title={related.map((alert) => alert.signature).join('\n')}
                      aria-label={`关联${risk.label}`}
                    >
                      <ShieldAlert size={13} />
                    </span>
                  )}
                </td>
              </tr>
            )
          })}
        </tbody>
      </table>
      {!packets.length && <div className={styles.empty}>{loading ? '读取报文…' : '没有匹配报文'}</div>}
    </div>
  )
}

const covers = (field: Field, offset: number) =>
  field.length > 0 && offset >= field.offset && offset < field.offset + field.length
export function fieldAt(fields: Field[], offset: number): Field | undefined {
  let found: Field | undefined
  for (const field of fields) {
    const candidate = fieldAt(field.children, offset) ?? (covers(field, offset) ? field : undefined)
    if (candidate && (!found || candidate.length < found.length)) found = candidate
  }
  return found
}
function ProtocolField({
  field,
  selected,
  onSelect,
  top = false,
}: {
  field: Field
  selected?: Field
  onSelect: (field: Field) => void
  top?: boolean
}) {
  const [open, setOpen] = useState(top)
  const button = useRef<HTMLButtonElement>(null)
  const active = selected === field
  useEffect(() => {
    if (selected && selected !== field && covers(field, selected.offset)) setOpen(true)
  }, [selected, field])
  useEffect(() => {
    if (active) button.current?.scrollIntoView({ block: 'nearest' })
  }, [active])
  return (
    <div className={styles.fieldGroup}>
      <button
        ref={button}
        className={styles.field}
        aria-expanded={field.children.length ? open : undefined}
        data-selected={active}
        title={field.name}
        onClick={() => {
          onSelect(field)
          if (field.children.length) setOpen(!open)
        }}
        onKeyDown={(event) => {
          if (event.key === 'ArrowRight') {
            event.preventDefault()
            setOpen(true)
          } else if (event.key === 'ArrowLeft') {
            event.preventDefault()
            setOpen(false)
          }
        }}
      >
        {field.children.length ? (
          open ? (
            <ChevronDown size={12} />
          ) : (
            <ChevronRight size={12} />
          )
        ) : (
          <span className={styles.spacer} />
        )}
        <span>{field.label}</span>
      </button>
      {open && field.children.length > 0 && (
        <div className={styles.children}>
          {field.children.map((child, index) => (
            <ProtocolField key={child.name + index} field={child} selected={selected} onSelect={onSelect} />
          ))}
        </div>
      )}
    </div>
  )
}
export function PacketProtocols({
  detail,
  field,
  onSelect,
  alerts,
  loading,
  alert,
}: {
  detail?: Schema<'CapturePacketDetail'>
  field?: Field
  onSelect: (field: Field) => void
  alerts: Schema<'SecurityAlert'>[]
  loading: boolean
  alert?: Schema<'SecurityAlert'>
}) {
  const related = detail ? packetAlerts(detail.packet, alerts) : []
  if (alert && !related.some((item) => item.id === alert.id)) related.unshift(alert)
  return (
    <div className={styles.protocols} aria-label="报文协议层">
      {!detail && <div className={styles.empty}>{loading ? '解析报文…' : '选择报文'}</div>}
      {related.map((alert) => (
        <details key={alert.id} className={styles.alert}>
          <summary style={{ color: riskLevel(alert.severity).color }}>
            <ShieldAlert size={13} /> {alert.signature}
          </summary>
          <div>{alert.category}</div>
          <div>
            {alert.source}:{alert.sourcePort} → {alert.destination}:{alert.destinationPort} · {alert.protocol}
          </div>
          <div>
            {dateTime(alert.firstSeen)} – {dateTime(alert.lastSeen)} · {alert.count} 次
          </div>
          <div>
            规则 {alert.generatorId}:{alert.signatureId}:{alert.revision}
          </div>
        </details>
      ))}
      {detail?.layers.map((layer, index) => (
        <ProtocolField key={layer.name + index} field={layer} selected={field} onSelect={onSelect} top />
      ))}
    </div>
  )
}

export function RiskRecords({
  alerts,
  onSelect,
  more,
  onMore,
  fetching,
}: {
  alerts: Schema<'SecurityAlert'>[]
  onSelect: (alert: Schema<'SecurityAlert'>) => void
  more: boolean
  onMore: () => void
  fetching: boolean
}) {
  return (
    <div className={styles.list}>
      <table aria-label="告警记录">
        <thead>
          <tr>
            <th>风险</th>
            <th>时间</th>
            <th>来源</th>
            <th>目标</th>
            <th>规则</th>
            <th>次数</th>
          </tr>
        </thead>
        <tbody>
          {alerts.map((alert) => {
            const risk = riskLevel(alert.severity)
            return (
              <tr
                key={alert.id}
                data-risk={risk.value}
                tabIndex={0}
                onClick={() => onSelect(alert)}
                onKeyDown={(event) => {
                  if (event.key === 'Enter' || event.key === ' ') {
                    event.preventDefault()
                    onSelect(alert)
                  }
                }}
              >
                <td style={{ color: risk.color }}>{risk.label}</td>
                <td>{time(alert.lastSeen)}</td>
                <td>{alert.source}</td>
                <td>{alert.destination}</td>
                <td title={alert.signature}>{alert.signature}</td>
                <td>{alert.count}</td>
              </tr>
            )
          })}
        </tbody>
      </table>
      {!alerts.length && <div className={styles.empty}>没有匹配告警</div>}
      {more && (
        <button className={styles.older} onClick={onMore} disabled={fetching}>
          {fetching ? '读取中…' : '更早告警'}
        </button>
      )}
    </div>
  )
}
export function PacketBytes({
  detail,
  field,
  onSelect,
}: {
  detail?: Schema<'CapturePacketDetail'>
  field?: Field
  onSelect: (field: Field) => void
}) {
  const bytes = detail?.hex.match(/.{2}/g) ?? []
  const selected = (index: number) => Boolean(field && covers(field, index))
  const choose = (index: number) => {
    const found = fieldAt(detail?.layers ?? [], index)
    if (found) onSelect(found)
  }
  return (
    <div
      className={styles.hex}
      aria-label="报文字节"
      onKeyDown={(event) => {
        if (event.key !== 'ArrowLeft' && event.key !== 'ArrowRight') return
        const button = (event.target as HTMLElement).closest<HTMLButtonElement>('button[data-offset]')
        if (!button) return
        event.preventDefault()
        const next = Number(button.dataset.offset) + (event.key === 'ArrowRight' ? 1 : -1)
        if (next >= 0 && next < bytes.length) {
          choose(next)
          event.currentTarget.querySelector<HTMLButtonElement>(`[data-offset="${next}"]`)?.focus()
        }
      }}
    >
      {Array.from({ length: Math.ceil(bytes.length / 16) }, (_, row) => (
        <div className={styles.hexRow} key={row}>
          <span className={styles.offset}>{(row * 16).toString(16).padStart(4, '0')}</span>
          <code>
            {bytes.slice(row * 16, row * 16 + 16).map((byte, column) => (
              <button
                key={column}
                data-offset={row * 16 + column}
                tabIndex={field ? (field.offset === row * 16 + column ? 0 : -1) : row === 0 && column === 0 ? 0 : -1}
                data-selected={selected(row * 16 + column)}
                aria-label={`字节 ${row * 16 + column}，${byte}`}
                onClick={() => choose(row * 16 + column)}
              >
                {byte}
              </button>
            ))}
          </code>
          <code className={styles.ascii}>
            {bytes.slice(row * 16, row * 16 + 16).map((byte, column) => {
              const value = parseInt(byte, 16)
              return (
                <button
                  key={column}
                  tabIndex={-1}
                  data-selected={selected(row * 16 + column)}
                  aria-label={`ASCII ${row * 16 + column}`}
                  onClick={() => choose(row * 16 + column)}
                >
                  {value >= 32 && value < 127 ? String.fromCharCode(value) : '.'}
                </button>
              )
            })}
          </code>
        </div>
      ))}
    </div>
  )
}
