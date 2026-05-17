import { RadarChart, PolarGrid, PolarAngleAxis, Radar, ResponsiveContainer, BarChart, Bar, XAxis, YAxis, Cell } from "recharts";
import { BarChart2 } from "lucide-react";

export interface Category {
  name: string;
  total: number;
  solved: number;
  color: string;
  icon: string;
}

interface CategoryStatsProps {
  categories: Category[];
}

export function CategoryStats({ categories }: CategoryStatsProps) {
  const radarData = categories.map(c => ({
    subject: c.name,
    value: Math.round((c.solved / c.total) * 100),
    fullMark: 100,
  }));

  return (
    <div className="flex flex-col h-full panel-border"
      style={{ background: "linear-gradient(135deg, rgba(0,15,30,0.95) 0%, rgba(4,8,16,0.98) 100%)" }}>
      {/* Header */}
      <div className="flex items-center justify-between px-4 py-3"
        style={{ borderBottom: "1px solid rgba(0,212,255,0.15)" }}>
        <div className="flex items-center gap-2">
          <BarChart2 size={16} style={{ color: "#b347ff", filter: "drop-shadow(0 0 6px #b347ff)" }} />
          <span className="text-sm tracking-[0.2em] uppercase font-bold"
            style={{ color: "#00d4ff", fontFamily: "'Courier New', monospace" }}>
            题目统计
          </span>
        </div>
      </div>

      <div className="flex flex-1 min-h-0">
        {/* Left: category progress bars */}
        <div className="flex flex-col justify-center gap-3 p-4 flex-1 min-w-0">
          {categories.map((cat) => {
            const pct = Math.round((cat.solved / cat.total) * 100);
            return (
              <div key={cat.name} className="group">
                <div className="flex items-center justify-between mb-1">
                  <div className="flex items-center gap-1.5">
                    <span className="text-sm">{cat.icon}</span>
                    <span className="text-xs font-medium tracking-wider"
                      style={{ color: cat.color, fontFamily: "'Courier New', monospace" }}>
                      {cat.name}
                    </span>
                  </div>
                  <div className="flex items-center gap-2">
                    <span className="text-xs tabular-nums"
                      style={{ color: "rgba(255,255,255,0.5)", fontFamily: "'Courier New', monospace" }}>
                      {cat.solved}/{cat.total}
                    </span>
                    <span className="text-xs tabular-nums font-bold"
                      style={{ color: cat.color, fontFamily: "'Courier New', monospace", minWidth: "35px", textAlign: "right" }}>
                      {pct}%
                    </span>
                  </div>
                </div>
                <div className="relative h-2 rounded-sm overflow-hidden"
                  style={{ background: "rgba(255,255,255,0.05)" }}>
                  <div
                    className="h-full rounded-sm transition-all duration-1000"
                    style={{
                      width: `${pct}%`,
                      background: `linear-gradient(90deg, ${cat.color}88, ${cat.color})`,
                      boxShadow: `0 0 8px ${cat.color}80`,
                    }}
                  />
                  {/* Animated shimmer */}
                  <div className="absolute inset-0 opacity-30"
                    style={{
                      background: "linear-gradient(90deg, transparent 0%, rgba(255,255,255,0.4) 50%, transparent 100%)",
                      backgroundSize: "200% 100%",
                      animation: "border-flow 2s linear infinite"
                    }} />
                </div>
              </div>
            );
          })}
        </div>

        {/* Right: Radar chart */}
        <div className="w-44 flex-shrink-0 p-2">
          <ResponsiveContainer width="100%" height="100%">
            <RadarChart data={radarData} margin={{ top: 10, right: 15, bottom: 10, left: 15 }}>
              <PolarGrid stroke="rgba(0,212,255,0.15)" />
              <PolarAngleAxis
                dataKey="subject"
                tick={{ fill: "rgba(0,212,255,0.5)", fontSize: 9, fontFamily: "'Courier New', monospace" }}
              />
              <Radar
                name="完成率"
                dataKey="value"
                stroke="#00d4ff"
                fill="#00d4ff"
                fillOpacity={0.12}
                strokeWidth={1.5}
                style={{ filter: "drop-shadow(0 0 6px #00d4ff)" }}
              />
            </RadarChart>
          </ResponsiveContainer>
        </div>
      </div>
    </div>
  );
}