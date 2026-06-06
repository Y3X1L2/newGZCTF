import { useEffect, useMemo, useState } from 'react'
import { TrendingUp } from 'lucide-react'
import { CartesianGrid, Legend, Line, LineChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'

interface ScoreData {
  ts: number
  time: string
  [teamName: string]: number | string
}

interface ScoreChartProps {
  data: ScoreData[]
  teams: { name: string; color: string }[]
}

const formatMinute = (timestamp: number) =>
  new Date(timestamp).toLocaleTimeString('zh-CN', { hour: '2-digit', minute: '2-digit', hour12: false })

const getTeamNames = (point?: ScoreData) => {
  if (!point) return []
  return Object.keys(point).filter((key) => key !== 'ts' && key !== 'time').sort()
}

const CustomTooltip = ({ active, payload, label }: any) => {
  if (!active || !payload?.length) return null

  const timeText = typeof label === 'number' ? formatMinute(label) : String(label ?? '--')

  return (
    <div
      style={{
        background: 'rgba(4,8,16,0.95)',
        border: '1px solid rgba(0,212,255,0.4)',
        padding: '10px 14px',
        boxShadow: '0 0 20px rgba(0,212,255,0.2)',
        fontFamily: "'Courier New', monospace"
      }}
    >
      <p style={{ color: 'rgba(0,212,255,0.7)', fontSize: '11px', marginBottom: '6px' }}>TIME: {timeText}</p>
      {payload.map((entry: any) => (
        <p key={entry.name} style={{ color: entry.color, fontSize: '12px', margin: '2px 0' }}>
          {entry.name}: <span style={{ fontWeight: 'bold' }}>{entry.value?.toLocaleString()}</span>
        </p>
      ))}
    </div>
  )
}

export function ScoreChart({ data, teams }: ScoreChartProps) {
  const [renderData, setRenderData] = useState<ScoreData[]>([])
  const teamSetKey = useMemo(() => teams.map((team) => team.name).sort().join('|'), [teams])

  useEffect(() => {
    if (data.length === 0) {
      setRenderData([])
      return
    }

    const incomingTeamSet = getTeamNames(data[0]).join('|')

    setRenderData((current) => {
      if (current.length === 0) return data

      const currentTeamSet = getTeamNames(current[0]).join('|')
      if (currentTeamSet !== incomingTeamSet || (teamSetKey && incomingTeamSet !== teamSetKey)) {
        return data
      }

      const currentFirstTs = current[0]?.ts ?? 0
      const incomingFirstTs = data[0]?.ts ?? 0
      const currentLastTs = current[current.length - 1]?.ts ?? 0

      if (incomingFirstTs !== currentFirstTs || data.length < current.length) {
        return data
      }

      const tail = data.filter((point) => point.ts > currentLastTs)
      if (tail.length > 0) {
        return [...current, ...tail]
      }

      const lastIncoming = data[data.length - 1]
      const lastCurrent = current[current.length - 1]

      if (!lastIncoming || !lastCurrent || lastIncoming.ts !== lastCurrent.ts) {
        return current
      }

      const teamNames = getTeamNames(lastIncoming)
      const changed = teamNames.some((name) => lastIncoming[name] !== lastCurrent[name])
      if (!changed) return current

      return [...current.slice(0, -1), lastIncoming]
    })
  }, [data, teamSetKey])

  return (
    <div
      className="flex flex-col h-full panel-border"
      style={{
        background: 'linear-gradient(135deg, rgba(0,15,30,0.95) 0%, rgba(4,8,16,0.98) 100%)',
        boxShadow: 'inset 0 0 20px rgba(0,212,255,0.05)',
        animation: 'neon-chart-glow 4s ease-in-out infinite'
      }}
    >
      <div className="flex items-center justify-between px-4 py-3" style={{ borderBottom: '1px solid rgba(0,212,255,0.15)' }}>
        <div className="flex items-center gap-2">
          <TrendingUp size={16} style={{ color: '#00ff88', filter: 'drop-shadow(0 0 6px #00ff88)' }} />
          <span className="text-sm tracking-[0.2em] uppercase font-bold" style={{ color: '#00d4ff', fontFamily: "'Courier New', monospace" }}>
            积分走势
          </span>
        </div>
        <span className="text-xs" style={{ color: 'rgba(0,212,255,0.4)', fontFamily: "'Courier New', monospace" }}>
          TOP 5 队伍
        </span>
      </div>

      <div className="flex-1 p-3">
        <ResponsiveContainer width="100%" height="100%">
          <LineChart data={renderData} margin={{ top: 5, right: 10, left: -10, bottom: 0 }}>
            <CartesianGrid strokeDasharray="3 3" stroke="rgba(0,212,255,0.06)" horizontal={true} vertical={true} />
            <XAxis
              dataKey="ts"
              type="number"
              domain={['dataMin', 'dataMax']}
              minTickGap={24}
              tick={{ fill: 'rgba(0,212,255,0.4)', fontSize: 10, fontFamily: "'Courier New', monospace" }}
              axisLine={{ stroke: 'rgba(0,212,255,0.2)' }}
              tickLine={{ stroke: 'rgba(0,212,255,0.2)' }}
              tickFormatter={(value: number) => formatMinute(Number(value))}
            />
            <YAxis
              tick={{ fill: 'rgba(0,212,255,0.4)', fontSize: 10, fontFamily: "'Courier New', monospace" }}
              axisLine={{ stroke: 'rgba(0,212,255,0.2)' }}
              tickLine={{ stroke: 'rgba(0,212,255,0.2)' }}
              tickFormatter={(v: number) => (v >= 1000 ? `${(v / 1000).toFixed(1)}k` : String(v))}
            />
            <Tooltip content={<CustomTooltip />} />
            <Legend
              wrapperStyle={{
                fontSize: '11px',
                fontFamily: "'Courier New', monospace",
                paddingTop: '4px'
              }}
              formatter={(value: string) => <span style={{ color: 'rgba(255,255,255,0.7)' }}>{value}</span>}
            />
            {teams.map((team, i) => (
              <Line
                key={team.name}
                type="monotone"
                dataKey={team.name}
                stroke={team.color}
                strokeWidth={i === 0 ? 2.5 : 1.5}
                dot={false}
                connectNulls={true}
                isAnimationActive={true}
                animationDuration={450}
                animationEasing="ease-out"
                activeDot={{ r: 4, fill: team.color, stroke: '#000', strokeWidth: 1 }}
                style={{ filter: `drop-shadow(0 0 6px ${team.color}90) drop-shadow(0 0 3px ${team.color}60)` }}
              />
            ))}
          </LineChart>
        </ResponsiveContainer>
      </div>
    </div>
  )
}
