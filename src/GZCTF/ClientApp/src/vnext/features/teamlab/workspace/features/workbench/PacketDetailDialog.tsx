import { Modal, SegmentedControl } from '@mantine/core'
import { useState } from 'react'
import type { Schema } from '../../api/client'
import { ErrorMessage } from '../../foundation/Feedback'
import { dateTime } from '../../foundation/format'
import { PacketBytes, PacketProtocols, type Field } from './PacketWorkspace'
import styles from './TrafficWorkspace.module.css'

export function PacketDetailDialog({
  opened,
  onClose,
  detail,
  loading,
  error,
  alerts,
  alert,
  interfaceName,
}: {
  opened: boolean
  onClose: () => void
  detail?: Schema<'CapturePacketDetail'>
  loading: boolean
  error: Error | null
  alerts: Schema<'SecurityAlert'>[]
  alert?: Schema<'SecurityAlert'>
  interfaceName?: string
}) {
  const [field, setField] = useState<Field>()
  const [view, setView] = useState('protocol')
  return (
    <Modal
      opened={opened}
      onClose={onClose}
      title={detail ? `报文 #${detail.packet.number}` : '报文详情'}
      size="min(1200px, 96vw)"
      closeButtonProps={{ 'aria-label': '关闭报文详情' }}
      portalProps={{
        target: document.fullscreenElement instanceof HTMLElement ? document.fullscreenElement : undefined,
      }}
    >
      <ErrorMessage error={error} />
      {detail && (
        <div className={styles.packetSummary}>
          <strong>{detail.packet.protocol}</strong>
          <span>
            {detail.packet.source}:{detail.packet.sourcePort} → {detail.packet.destination}:
            {detail.packet.destinationPort}
          </span>
          <span>{detail.packet.length} 字节</span>
          <span>{dateTime(detail.packet.time)}</span>
          <span>{detail.packet.summary}</span>
          <span>{interfaceName}</span>
        </div>
      )}
      <div className={styles.detailTabs}>
        <SegmentedControl
          aria-label="报文详情内容"
          value={view}
          onChange={setView}
          data={[
            { value: 'protocol', label: '协议层' },
            { value: 'bytes', label: '原始字节' },
          ]}
        />
      </div>
      <div className={styles.detailGrid} data-view={view}>
        <section className={styles.protocolPane}>
          <header className={styles.paneHeading}>
            <strong>协议层</strong>
          </header>
          <PacketProtocols
            detail={detail}
            field={field}
            onSelect={setField}
            alerts={alerts}
            loading={loading}
            alert={alert}
          />
        </section>
        <section className={styles.bytesPane}>
          <header className={styles.paneHeading}>
            <strong>原始字节</strong>
            {field && (
              <span>
                {field.name} · {field.offset}–{field.offset + field.length - 1}
              </span>
            )}
          </header>
          <PacketBytes detail={detail} field={field} onSelect={setField} />
        </section>
      </div>
    </Modal>
  )
}
