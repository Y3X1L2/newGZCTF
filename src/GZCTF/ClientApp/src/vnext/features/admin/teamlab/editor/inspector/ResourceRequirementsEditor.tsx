import { Cpu, Database, MemoryStick } from 'lucide-react'
import type { TeamLabAssetResources } from '../../api/teamlabContracts'
import { InspectorSection, NumberInput } from './InspectorFields'
import styles from './TeamLabInspector.module.css'

export function ResourceRequirementsEditor({
  resources,
  onChange,
  readOnly,
}: {
  resources: TeamLabAssetResources
  onChange: (resources: TeamLabAssetResources) => void
  readOnly?: boolean
}) {
  return (
    <InspectorSection icon={<Cpu aria-hidden="true" size={16} />} title="资源需求">
      <div className={styles.resourceGrid}>
        <NumberInput disabled={readOnly} label="CPU 单位" min={1} onChange={(cpuUnits) => onChange({ ...resources, cpuUnits })} value={resources.cpuUnits} />
        <NumberInput disabled={readOnly} label="内存 MiB" min={1} onChange={(memoryMiB) => onChange({ ...resources, memoryMiB })} value={resources.memoryMiB} />
        <NumberInput disabled={readOnly} label="存储 MiB" min={1} onChange={(storageMiB) => onChange({ ...resources, storageMiB })} value={resources.storageMiB} />
      </div>
      <div className={styles.resourceSummary} aria-label="资源摘要">
        <span><Cpu size={14} />{resources.cpuUnits} CPU</span>
        <span><MemoryStick size={14} />{resources.memoryMiB} MiB</span>
        <span><Database size={14} />{resources.storageMiB} MiB</span>
      </div>
    </InspectorSection>
  )
}
