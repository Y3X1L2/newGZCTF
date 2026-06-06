import { FC, useEffect, useState } from "react";
import { CTFHeader } from "./CTFHeader";
import { Leaderboard } from "./Leaderboard";
import { ScoreChart } from "./ScoreChart";
import { CategoryStats } from "./CategoryStats";
import { RecentSolves } from "./RecentSolves";
import { HeatmapPanel } from "./HeatmapPanel";
import { useCTFScreenData } from "./useCTFScreenData";
import "../../styles/ctf-screen/index.css";

// ─── Animated Background ────────────────────────────────────────────────────

function BinaryRain() {
  const cols = 20;
  const chars = "01001011001100110101011001010101010011001101010100110011010";
  return (
    <div className="absolute inset-0 overflow-hidden pointer-events-none" style={{ opacity: 0.04 }}>
      {Array.from({ length: cols }).map((_, i) => (
        <div key={i}
          className="absolute top-0 text-xs"
          style={{
            left: `${(i / cols) * 100}%`,
            color: "#00d4ff",
            fontFamily: "'Courier New', monospace",
            fontSize: "10px",
            lineHeight: "1.4",
            animation: `data-stream ${3 + i * 0.3}s linear infinite`,
            animationDelay: `${-i * 0.5}s`,
            whiteSpace: "nowrap",
            writingMode: "horizontal-tb"
          }}>
          {chars.slice(i % 10, i % 10 + 30)}
        </div>
      ))}
    </div>
  );
}

function HexParticles() {
  return (
    <div className="absolute inset-0 overflow-hidden pointer-events-none">
      {Array.from({ length: 8 }).map((_, i) => (
        <div key={i}
          className="absolute"
          style={{
            width: "6px",
            height: "6px",
            background: i % 2 === 0 ? "#00d4ff" : "#00ff88",
            opacity: 0.3 + (i % 3) * 0.1,
            left: `${10 + i * 11}%`,
            top: `${20 + (i % 4) * 15}%`,
            clipPath: "polygon(50% 0%, 100% 25%, 100% 75%, 50% 100%, 0% 75%, 0% 25%)",
            animation: `pulse-glow ${2 + i * 0.4}s ease-in-out infinite`,
            animationDelay: `${i * 0.3}s`
          }}
        />
      ))}
    </div>
  );
}

// ─── Main Screen Component ───────────────────────────────────────────────────────

interface CTFScreenPageProps {
  gameId: number;
}

const SHANGHAI_TIMEZONE = "Asia/Shanghai";

const CTFScreenPage: FC<CTFScreenPageProps> = ({ gameId }) => {
  const data = useCTFScreenData(gameId);
  const [currentTime, setCurrentTime] = useState(() => new Date());

  useEffect(() => {
    const timer = window.setInterval(() => setCurrentTime(new Date()), 1000);
    return () => window.clearInterval(timer);
  }, []);

  const currentTimeText = currentTime.toLocaleTimeString("zh-CN", {
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit",
    hour12: false,
    timeZone: SHANGHAI_TIMEZONE,
  });

  return (
    <div className="ctf-screen scanline-overlay relative w-screen h-screen overflow-hidden flex flex-col"
      style={{ fontFamily: "'Courier New', monospace" }}>

      {/* Ambient background effects */}
      <BinaryRain />
      <HexParticles />

      {/* Gradient overlays for depth */}
      <div className="absolute inset-0 pointer-events-none"
        style={{
          background: "radial-gradient(ellipse at 20% 50%, rgba(0,212,255,0.04) 0%, transparent 50%), radial-gradient(ellipse at 80% 20%, rgba(179,71,255,0.04) 0%, transparent 50%), radial-gradient(ellipse at 50% 100%, rgba(0,255,136,0.03) 0%, transparent 40%)"
        }} />

      {/* Header */}
      <CTFHeader
        eventName={data.eventName}
        totalTeams={data.totalTeams}
        totalSolves={data.totalSolves}
        totalChallenges={data.totalChallenges}
        startTime={data.startTime}
        endTime={data.endTime}
      />

      {/* Main Content */}
      <div className="flex-1 flex gap-2 p-2 min-h-0 overflow-hidden">

        {/* LEFT: Leaderboard */}
        <div className="flex flex-col" style={{ width: "22%" }}>
          <Leaderboard teams={data.teams} />
        </div>

        {/* CENTER */}
        <div className="flex-1 flex flex-col gap-2 min-w-0">
          {/* Score chart - top */}
          <div style={{ flex: "1.2" }}>
            <ScoreChart data={data.scoreHistory} teams={data.top5Teams} />
          </div>
          {/* Category stats + heatmap - bottom */}
          <div className="flex gap-2" style={{ flex: "0.8" }}>
            <div style={{ flex: "1.1" }}>
              <CategoryStats categories={data.categories} />
            </div>
            <div style={{ flex: "1" }}>
              <HeatmapPanel
                data={data.heatmapData}
                totalBlood={data.totalBlood}
                avgScore={data.avgScore}
                activeTeams={data.activeTeams}
              />
            </div>
          </div>
        </div>

        {/* RIGHT: Recent Solves */}
        <div className="flex flex-col" style={{ width: "19%" }}>
          <RecentSolves events={data.solveEvents} />
        </div>
      </div>

      {/* Bottom status bar */}
      <div className="flex items-center justify-between px-4 py-1.5"
        style={{
          borderTop: "1px solid rgba(0,212,255,0.15)",
          background: "rgba(0,10,20,0.9)"
        }}>
        <div className="flex items-center gap-6">
          {[
            { label: "系统状态", value: "NORMAL", color: "#00ff88" },
            { label: "当前时间", value: currentTimeText, color: "#00d4ff" },
          ].map(({ label, value, color }) => (
            <div key={label} className="flex items-center gap-1.5 text-xs">
              <div className="w-1.5 h-1.5 rounded-full" style={{ background: color, boxShadow: `0 0 6px ${color}` }} />
              <span style={{ color: "rgba(255,255,255,0.3)" }}>{label}:</span>
              <span style={{ color, fontFamily: "'Courier New', monospace" }}>{value}</span>
            </div>
          ))}
        </div>

        <div className="flex items-center gap-1 text-xs" style={{ color: "rgba(0,212,255,0.3)", fontFamily: "'Courier New', monospace" }}>
          <span>DEVELOPED BY</span>
          <span>SCU</span>
          <span style={{ color: "#00d4ff" }}>CYBERRANGE</span>
        </div>

        <div className="flex items-center gap-4 text-xs" style={{ fontFamily: "'Courier New', monospace" }}>
          {[
            { label: "STATUS", value: data.statusInfo.status },
          ].map(({ label, value }) => (
            <div key={label} className="flex items-center gap-1">
              <span style={{ color: "rgba(255,255,255,0.2)" }}>{label}:</span>
              <span style={{ color: "rgba(0,212,255,0.6)" }}>{value}</span>
            </div>
          ))}
        </div>
      </div>
    </div>
  );
};

export default CTFScreenPage;
