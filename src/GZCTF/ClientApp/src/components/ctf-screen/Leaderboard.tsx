import { useEffect, useState } from "react";
import { Trophy, TrendingUp, TrendingDown, Minus, Star } from "lucide-react";

export interface Team {
  id: number;
  rank: number;
  prevRank: number;
  name: string;
  country: string;
  score: number;
  solves: number;
  lastSolve: string;
  color: string;
}

interface LeaderboardProps {
  teams: Team[];
}

export function Leaderboard({ teams }: LeaderboardProps) {
  const [animatedScores, setAnimatedScores] = useState<Record<number, number>>({});
  const [flashIds, setFlashIds] = useState<Set<number>>(new Set());

  useEffect(() => {
    const scores: Record<number, number> = {};
    teams.forEach(t => { scores[t.id] = t.score; });
    setAnimatedScores(scores);
  }, [teams]);

  useEffect(() => {
    const newFlash = new Set<number>();
    teams.forEach(t => {
      if (t.prevRank !== t.rank) newFlash.add(t.id);
    });
    setFlashIds(newFlash);
    const timer = setTimeout(() => setFlashIds(new Set()), 1500);
    return () => clearTimeout(timer);
  }, [teams]);

  const getRankChange = (team: Team) => {
    const diff = team.prevRank - team.rank;
    if (diff > 0) return <TrendingUp size={12} style={{ color: "#00ff88" }} />;
    if (diff < 0) return <TrendingDown size={12} style={{ color: "#ff4466" }} />;
    return <Minus size={12} style={{ color: "rgba(255,255,255,0.3)" }} />;
  };

  const getRankColor = (rank: number) => {
    if (rank === 1) return "#ffd700";
    if (rank === 2) return "#c0c0c0";
    if (rank === 3) return "#cd7f32";
    return "rgba(0,212,255,0.7)";
  };

  const getRankBg = (rank: number) => {
    if (rank === 1) return "rgba(255,215,0,0.08)";
    if (rank === 2) return "rgba(192,192,192,0.06)";
    if (rank === 3) return "rgba(205,127,50,0.06)";
    return "transparent";
  };

  const maxScore = Math.max(...teams.map(t => t.score), 1);

  return (
    <div className="flex flex-col h-full panel-border corner-tl"
      style={{ background: "linear-gradient(135deg, rgba(0,15,30,0.95) 0%, rgba(4,8,16,0.98) 100%)" }}>
      {/* Header */}
      <div className="flex items-center justify-between px-4 py-3"
        style={{ borderBottom: "1px solid rgba(0,212,255,0.15)" }}>
        <div className="flex items-center gap-2">
          <Trophy size={16} style={{ color: "#ffd700", filter: "drop-shadow(0 0 6px #ffd700)" }} />
          <span className="text-sm tracking-[0.2em] uppercase font-bold"
            style={{ color: "#00d4ff", fontFamily: "'Courier New', monospace" }}>
            实时排行榜
          </span>
        </div>
        <div className="flex items-center gap-1.5">
          <div className="w-1.5 h-1.5 rounded-full pulse-dot" style={{ background: "#00ff88" }} />
          <span className="text-xs" style={{ color: "#00ff88", fontFamily: "'Courier New', monospace" }}>LIVE</span>
        </div>
      </div>

      {/* Column headers */}
      <div className="grid px-4 py-2 text-xs tracking-widest"
        style={{
          gridTemplateColumns: "36px 20px 1fr 70px 40px",
          color: "rgba(0,212,255,0.4)",
          fontFamily: "'Courier New', monospace",
          borderBottom: "1px solid rgba(0,212,255,0.08)"
        }}>
        <span>RANK</span>
        <span></span>
        <span>TEAM</span>
        <span className="text-right">SCORE</span>
        <span className="text-right">SLVS</span>
      </div>

      {/* Team rows */}
      <div className="flex-1 overflow-y-auto">
        {teams.map((team, idx) => (
          <div
            key={team.id}
            className="grid items-center px-4 py-3 relative transition-all duration-500"
            style={{
              gridTemplateColumns: "36px 20px 1fr 70px 40px",
              background: flashIds.has(team.id)
                ? `linear-gradient(90deg, rgba(0,255,136,0.12), ${getRankBg(team.rank)})`
                : getRankBg(team.rank),
              borderBottom: "1px solid rgba(0,212,255,0.06)",
              animation: idx < 3 ? `slide-in-left ${0.3 + idx * 0.1}s ease-out` : "none"
            }}>

            {/* Progress bar background */}
            <div className="absolute left-0 top-0 bottom-0 opacity-20 transition-all duration-1000"
              style={{
                width: `${(team.score / maxScore) * 100}%`,
                background: `linear-gradient(90deg, ${team.color}22, transparent)`,
                borderRight: `1px solid ${team.color}44`
              }} />

            {/* Rank */}
            <div className="relative z-10 flex items-center justify-center">
              {team.rank <= 3 ? (
                <span className="text-sm font-bold tabular-nums"
                  style={{
                    color: getRankColor(team.rank),
                    textShadow: `0 0 10px ${getRankColor(team.rank)}`,
                    fontFamily: "'Courier New', monospace"
                  }}>
                  {team.rank === 1 ? "①" : team.rank === 2 ? "②" : "③"}
                </span>
              ) : (
                <span className="text-xs tabular-nums"
                  style={{ color: "rgba(0,212,255,0.5)", fontFamily: "'Courier New', monospace" }}>
                  #{team.rank}
                </span>
              )}
            </div>

            {/* Rank change */}
            <div className="relative z-10 flex items-center justify-center">
              {getRankChange(team)}
            </div>

            {/* Team name */}
            <div className="relative z-10 min-w-0">
              <div className="flex items-center gap-2">
                <div className="w-2 h-2 rounded-full flex-shrink-0"
                  style={{ background: team.color, boxShadow: `0 0 6px ${team.color}`, marginLeft: "-2px" }} />
                <span className="text-sm font-medium truncate"
                  style={{
                    color: team.rank <= 3 ? "#fff" : "rgba(255,255,255,0.85)",
                    fontFamily: "'Courier New', monospace",
                    textShadow: team.rank === 1 ? "0 0 10px rgba(255,215,0,0.3)" : "none",
                    marginLeft: "2px"
                  }}>
                  {team.name}
                </span>
                {team.rank === 1 && <Star size={10} style={{ color: "#ffd700", flexShrink: 0 }} />}
              </div>
              <div className="text-xs mt-0.5" style={{ color: "rgba(255,255,255,0.25)", fontFamily: "'Courier New', monospace" }}>
                {team.country} · {team.lastSolve}
              </div>
            </div>

            {/* Score */}
            <div className="relative z-10 text-right">
              <span className="text-sm font-bold tabular-nums"
                style={{
                  color: team.rank <= 3 ? getRankColor(team.rank) : "#00d4ff",
                  fontFamily: "'Courier New', monospace",
                  textShadow: `0 0 8px ${team.rank <= 3 ? getRankColor(team.rank) : "#00d4ff"}80`
                }}>
                {(animatedScores[team.id] ?? team.score).toLocaleString()}
              </span>
            </div>

            {/* Solves */}
            <div className="relative z-10 text-right">
              <span className="text-xs tabular-nums"
                style={{ color: "#00ff88", fontFamily: "'Courier New', monospace" }}>
                {team.solves}
              </span>
            </div>
          </div>
        ))}
      </div>

      {/* Footer */}
      <div className="px-4 py-2 text-xs"
        style={{
          borderTop: "1px solid rgba(0,212,255,0.1)",
          color: "rgba(0,212,255,0.3)",
          fontFamily: "'Courier New', monospace"
        }}>
        显示前 {teams.length} 支队伍 · 每5秒更新
      </div>
    </div>
  );
}
