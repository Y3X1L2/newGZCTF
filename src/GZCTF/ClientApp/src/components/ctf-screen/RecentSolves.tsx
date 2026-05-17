import { useEffect, useRef } from "react";
import { Zap, Lock, Globe, Binary, Search, Cpu, FlaskConical } from "lucide-react";

export interface SolveEvent {
  id: string;
  team: string;
  teamColor: string;
  challenge: string;
  category: string;
  points: number;
  time: string;
  isFirst: boolean;
}

interface RecentSolvesProps {
  events: SolveEvent[];
}

const categoryIcons: Record<string, any> = {
  Web: Globe,
  Crypto: Lock,
  Pwn: Zap,
  Reverse: Binary,
  Misc: Search,
  Forensics: FlaskConical,
  Hardware: Cpu,
};

const categoryColors: Record<string, string> = {
  Web: "#00d4ff",
  Crypto: "#b347ff",
  Pwn: "#ff4466",
  Reverse: "#ff6b35",
  Misc: "#00ff88",
  Forensics: "#ffd700",
  Hardware: "#00ffcc",
};

export function RecentSolves({ events }: RecentSolvesProps) {
  const scrollRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (scrollRef.current) {
      scrollRef.current.scrollTop = 0;
    }
  }, [events]);

  return (
    <div className="flex flex-col h-full panel-border-orange corner-tl"
      style={{
        background: "linear-gradient(135deg, rgba(15,8,0,0.95) 0%, rgba(4,8,16,0.98) 100%)",
        border: "1px solid rgba(255,107,53,0.25)"
      }}>
      {/* Header */}
      <div className="flex items-center justify-between px-4 py-3"
        style={{ borderBottom: "1px solid rgba(255,107,53,0.2)" }}>
        <div className="flex items-center gap-2">
          <Zap size={16} style={{ color: "#ff6b35", filter: "drop-shadow(0 0 6px #ff6b35)" }} />
          <span className="text-sm tracking-[0.2em] uppercase font-bold"
            style={{ color: "#ff6b35", fontFamily: "'Courier New', monospace" }}>
            实时解题
          </span>
        </div>
        <div className="flex items-center gap-1.5">
          <div className="w-1.5 h-1.5 rounded-full pulse-dot" style={{ background: "#ff6b35" }} />
          <span className="text-xs" style={{ color: "#ff6b35", fontFamily: "'Courier New', monospace" }}>FEED</span>
        </div>
      </div>

      {/* Events */}
      <div ref={scrollRef} className="flex-1 overflow-y-auto scroll-feed">
        {events.map((event, idx) => {
          const Icon = categoryIcons[event.category] ?? Zap;
          const catColor = categoryColors[event.category] ?? "#00d4ff";
          const isNew = idx === 0;

          return (
            <div
              key={event.id}
              className={`px-3 py-5 relative ${isNew ? "feed-item-new" : ""}`}
              style={{
                borderBottom: "1px solid rgba(255,107,53,0.08)",
                marginBottom: "4px",
                background: isNew
                  ? "linear-gradient(90deg, rgba(255,107,53,0.08) 0%, transparent 70%)"
                  : event.isFirst
                    ? "linear-gradient(90deg, rgba(255,215,0,0.05) 0%, transparent 70%)"
                    : "transparent",
              }}>

              {/* First Blood badge */}
              {event.isFirst && (
                <div className="absolute top-2 right-3 text-xs px-1.5 py-0.5"
                  style={{
                    background: "rgba(255,215,0,0.15)",
                    border: "1px solid rgba(255,215,0,0.4)",
                    color: "#ffd700",
                    fontFamily: "'Courier New', monospace",
                    fontSize: "9px",
                    letterSpacing: "0.1em",
                    boxShadow: "0 0 8px rgba(255,215,0,0.3)"
                  }}>
                  FIRST BLOOD
                </div>
              )}

              <div className="flex items-start gap-4">
                {/* Category icon */}
                <div className="flex-shrink-0 w-7 h-7 flex items-center justify-center rounded"
                  style={{
                    background: `${catColor}15`,
                    border: `1px solid ${catColor}40`,
                    boxShadow: `0 0 8px ${catColor}20`
                  }}>
                  <Icon size={14} style={{ color: catColor }} />
                </div>

                <div className="flex-1 min-w-0">
                  {/* Team name */}
                  <div className="flex items-center gap-1.5 mb-0.5">
                    <div className="w-1.5 h-1.5 rounded-full flex-shrink-0"
                      style={{ background: event.teamColor, boxShadow: `0 0 4px ${event.teamColor}` }} />
                    <span className="text-xs font-bold truncate"
                      style={{ color: event.teamColor, fontFamily: "'Courier New', monospace" }}>
                      {event.team}
                    </span>
                  </div>

                  {/* Challenge name */}
                  <div className="text-xs truncate"
                    style={{ color: "rgba(255,255,255,0.85)", fontFamily: "'Courier New', monospace" }}>
                    {event.challenge}
                  </div>

                  {/* Meta */}
                  <div className="flex items-center gap-2 mt-0.5">
                    <span className="text-xs px-1.5 py-0.5"
                      style={{
                        background: `${catColor}15`,
                        color: catColor,
                        fontFamily: "'Courier New', monospace",
                        fontSize: "9px",
                        letterSpacing: "0.05em"
                      }}>
                      {event.category}
                    </span>
                    <span className="text-xs font-bold"
                      style={{ color: "#00ff88", fontFamily: "'Courier New', monospace" }}>
                      +{event.points}
                    </span>
                    <span className="text-xs ml-auto"
                      style={{ color: "rgba(255,255,255,0.25)", fontFamily: "'Courier New', monospace" }}>
                      {event.time}
                    </span>
                  </div>
                </div>
              </div>
            </div>
          );
        })}
      </div>

      {/* Footer */}
      <div className="px-4 py-2 text-xs"
        style={{
          borderTop: "1px solid rgba(255,107,53,0.15)",
          color: "rgba(255,107,53,0.3)",
          fontFamily: "'Courier New', monospace"
        }}>
        最近 {events.length} 条解题记录
      </div>
    </div>
  );
}
