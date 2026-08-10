import { Radar } from 'lucide-react'
import type { TeamLabEndpointObservationMode, TeamLabObservationPolicy } from '../../api/teamlabContracts'
import { InspectorSection, SelectInput, ToggleInput } from './InspectorFields'

type ObservationEditorProps =
  | {
      policy: TeamLabObservationPolicy
      onChange: (policy: TeamLabObservationPolicy) => void
      endpointMode?: never
      onEndpointModeChange?: never
      readOnly?: boolean
    }
  | {
      endpointMode: TeamLabEndpointObservationMode
      onEndpointModeChange: (mode: TeamLabEndpointObservationMode) => void
      policy?: never
      onChange?: never
      readOnly?: boolean
    }

const modeOption = (value: TeamLabEndpointObservationMode, label: string) => <option value={value}>{label}</option>

export function ObservationEditor(props: ObservationEditorProps) {
  const mode = props.policy?.endpointObservation ?? props.endpointMode ?? 'optional'
  const setMode = (value: string) => {
    const endpointObservation: TeamLabEndpointObservationMode =
      value === 'disabled' || value === 'required' ? value : 'optional'
    if (props.policy) props.onChange({ ...props.policy, endpointObservation })
    else props.onEndpointModeChange(endpointObservation)
  }
  return (
    <InspectorSection icon={<Radar aria-hidden="true" size={16} />} title="流量观测">
      {props.policy ? (
        <>
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
        </>
      ) : null}
      <SelectInput disabled={props.readOnly} help="endpointObservation" label="端点观测" onChange={setMode} value={mode}>
        {modeOption('disabled', '禁用')}
        {modeOption('optional', '可选')}
        {modeOption('required', '必需')}
      </SelectInput>
    </InspectorSection>
  )
}
