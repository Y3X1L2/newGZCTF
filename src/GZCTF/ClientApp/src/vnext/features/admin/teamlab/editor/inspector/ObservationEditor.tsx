import { Radar } from 'lucide-react'
import type { TeamLabObservationPolicy } from '../../api/teamlabContracts'
import { InspectorSection, ToggleInput } from './InspectorFields'

interface ObservationEditorProps {
  policy: TeamLabObservationPolicy
  onChange: (policy: TeamLabObservationPolicy) => void
  readOnly?: boolean
}

export function ObservationEditor(props: ObservationEditorProps) {
  return (
    <InspectorSection icon={<Radar aria-hidden="true" size={16} />} title="流量观测">
      <ToggleInput
        checked={props.policy.flowMetadataEnabled}
        disabled={props.readOnly}
        label="流量元数据"
        onChange={(flowMetadataEnabled) => props.onChange({ ...props.policy, flowMetadataEnabled })}
      />
      <ToggleInput
        checked={props.policy.onDemandPcapEnabled}
        disabled={props.readOnly}
        label="按需 PCAP"
        onChange={(onDemandPcapEnabled) => props.onChange({ ...props.policy, onDemandPcapEnabled })}
      />
    </InspectorSection>
  )
}
