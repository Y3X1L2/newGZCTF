import { useId } from 'react'
import styles from './RuntimePanels.module.css'

const commonProtocols = ['TCP', 'UDP', 'ICMP', 'ARP', 'SCTP', 'GRE', 'ESP']

export function TrafficProtocolFilter({
  label,
  value,
  onChange,
}: {
  label: string
  value: string
  onChange: (value: string) => void
}) {
  const listId = useId()
  return (
    <>
      <input
        aria-label={label}
        className={styles.filterSelect}
        list={listId}
        onChange={(event) => onChange(event.target.value.toUpperCase())}
        placeholder="全部协议"
        value={value}
      />
      <datalist id={listId}>
        {commonProtocols.map((protocol) => <option key={protocol} value={protocol} />)}
      </datalist>
    </>
  )
}
