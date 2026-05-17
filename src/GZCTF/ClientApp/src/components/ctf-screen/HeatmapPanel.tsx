import { BarChart, Bar, XAxis, YAxis, ResponsiveContainer, Cell, Tooltip } from "recharts";
import { Activity } from "lucide-react";

interface HeatmapData {
  hour: string;
  count: number;
}

interface HeatmapPanelProps {
  data: HeatmapData[];
  totalBlood: number;
  avgScore: number;
  activeTeams: number;
}

const CustomTooltip = ({ active, payload, label }: any) => {
  if (active && payload && payload.length) {
    return (
      <div style={{
        background: "rgba(4,8,16,0.95)",
        border: "1px solid rgba(0,255,136,0.4)",
        padding: "8px 12px",
        fontFamily: "'Courier New', monospace",
        fontSize: "11px"
      }}>
        <p style={{ color: "rgba(0,212,255,0.7)", marginBottom: "4px" }}>{label}</p>
        <p style={{ color: "#00ff88" }}>解题数: {payload[0].value}</p>
      </div>
    );
  }
  return null;
};

export function HeatmapPanel({ data, totalBlood, avgScore, activeTeams }: HeatmapPanelProps) {
  const max = Math.max(...data.map(d => d.count), 1);

  return (
    <div className="flex flex-col h-full panel-border-green"
      style={{
        background: "linear-gradient(135deg, rgba(0,15,10,0.95) 0%, rgba(4,8,16,0.98) 100%)",
        border: "1px solid rgba(0,255,136,0.2)"
      }}>
      {/* Header */}
      <div className="flex items-center justify-between px-4 py-3"
        style={{ borderBottom: "1px solid rgba(0,255,136,0.12)" }}>
        <div className="flex items-center gap-2">
          <Activity size={16} style={{ color: "#00ff88", filter: "drop-shadow(0 0 6px #00ff88)" }} />
          <span className="text-sm tracking-[0.2em] uppercase font-bold"
            style={{ color: "#00d4ff", fontFamily: "'Courier New', monospace" }}>
            解题热度
          </span>
        </div>
        <div className="flex items-center gap-7 text-xs" style={{ fontFamily: "'Courier New', monospace" }}>
          {[
            { label: "首血", value: totalBlood, color: "#ffd700" },
            { label: "场均分", value: avgScore.toLocaleString(), color: "#00d4ff" },
            { label: "活动队", value: activeTeams, color: "#00ff88" },
          ].map(({ label, value, color }) => (
            <div key={label} className="flex flex-col items-center">
              <span style={{ color: `${color}99`, fontSize: "9px", letterSpacing: "0.1em" }}>{label}</span>
              <span style={{ color, fontWeight: "bold" }}>{value}</span>
            </div>
          ))}
        </div>
      </div>

      {/* Chart */}
      <div className="flex-1 p-3">
        <ResponsiveContainer width="100%" height="100%">
          <BarChart data={data} margin={{ top: 5, right: 5, left: -25, bottom: 0 }} barSize={14}>
            <XAxis
              dataKey="hour"
              tick={{ fill: "rgba(0,255,136,0.4)", fontSize: 9, fontFamily: "'Courier New', monospace" }}
              axisLine={{ stroke: "rgba(0,255,136,0.15)" }}
              tickLine={false}
            />
            <YAxis
              tick={{ fill: "rgba(0,255,136,0.4)", fontSize: 9, fontFamily: "'Courier New', monospace" }}
              axisLine={false}
              tickLine={false}
            />
            <Tooltip content={<CustomTooltip />} cursor={{ fill: "rgba(0,255,136,0.05)" }} />
            <Bar dataKey="count" radius={[2, 2, 0, 0]}>
              {data.map((entry, index) => (
                <Cell
                  key={`cell-${index}`}
                  fill={`rgba(0,255,136,${0.2 + (entry.count / max) * 0.7})`}
                  style={{ filter: entry.count > max * 0.7 ? "drop-shadow(0 0 4px #00ff88)" : "none" }}
                />
              ))}
            </Bar>
          </BarChart>
        </ResponsiveContainer>
      </div>
    </div>
  );
}
