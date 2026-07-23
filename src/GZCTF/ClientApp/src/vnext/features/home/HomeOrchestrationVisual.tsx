import { useId } from 'react'
import styles from './HomeOrchestrationVisual.module.css'

const meshPaths = [
  'M122 139 C258 114 294 189 393 232 C500 278 594 251 738 132',
  'M124 146 C250 129 293 198 389 240 C501 289 604 256 752 125',
  'M130 154 C248 144 290 208 385 249 C504 300 619 258 767 116',
  'M165 111 C255 173 320 204 414 224 C520 246 620 218 733 139',
  'M191 106 C268 170 335 198 426 215 C528 235 622 207 721 145',
  'M220 111 C285 166 350 191 438 207 C535 225 617 199 704 154',
  'M250 122 C305 164 366 187 453 200 C542 214 610 192 684 164',
]

const sparkles = [
  { cx: 472, cy: 237, r: 2.5 },
  { cx: 482, cy: 274, r: 2 },
  { cx: 351, cy: 225, r: 2 },
  { cx: 302, cy: 278, r: 2.4 },
  { cx: 590, cy: 343, r: 2.2 },
  { cx: 697, cy: 273, r: 2.4 },
]

export function HomeOrchestrationVisual() {
  const prefix = `home-flow-${useId().replaceAll(':', '')}`
  const ids = {
    ribbonA: `${prefix}-ribbon-a`,
    ribbonB: `${prefix}-ribbon-b`,
    ribbonC: `${prefix}-ribbon-c`,
    mesh: `${prefix}-mesh`,
    glowBlue: `${prefix}-glow-blue`,
    glowGreen: `${prefix}-glow-green`,
    dots: `${prefix}-dots`,
    nodeGlow: `${prefix}-node-glow`,
    ribbonClip: `${prefix}-ribbon-clip`,
    ribbonFade: `${prefix}-ribbon-fade`,
    ribbonFadeGradient: `${prefix}-ribbon-fade-gradient`,
  }

  return (
    <div aria-hidden="true" className={styles.root} data-testid="home-orchestration-visual">
      <svg className={styles.art} focusable="false" viewBox="0 0 760 420">
        <defs>
          <linearGradient id={ids.ribbonA} gradientUnits="userSpaceOnUse" x1="116" x2="672" y1="100" y2="316">
            <stop className={styles.bluePale} offset="0" stopOpacity="0.18" />
            <stop className={styles.blueSoft} offset="0.32" stopOpacity="0.8" />
            <stop className={styles.blueMid} offset="0.68" stopOpacity="0.9" />
            <stop className={styles.blue} offset="1" stopOpacity="0.3" />
          </linearGradient>
          <linearGradient id={ids.ribbonB} gradientUnits="userSpaceOnUse" x1="184" x2="664" y1="120" y2="352">
            <stop className={styles.greenPale} offset="0" stopOpacity="0.24" />
            <stop className={styles.greenSoft} offset="0.3" stopOpacity="0.82" />
            <stop className={styles.greenMid} offset="0.65" stopOpacity="0.9" />
            <stop className={styles.green} offset="1" stopOpacity="0.3" />
          </linearGradient>
          <linearGradient id={ids.ribbonC} gradientUnits="userSpaceOnUse" x1="272" x2="748" y1="324" y2="150">
            <stop className={styles.bluePale} offset="0" stopOpacity="0.14" />
            <stop className={styles.blueSoft} offset="0.38" stopOpacity="0.72" />
            <stop className={styles.blueMid} offset="0.76" stopOpacity="0.84" />
            <stop className={styles.blue} offset="1" stopOpacity="0.26" />
          </linearGradient>
          <linearGradient id={ids.mesh} gradientUnits="userSpaceOnUse" x1="250" x2="670" y1="130" y2="328">
            <stop className={styles.meshPale} offset="0" stopOpacity="0.18" />
            <stop className={styles.meshBright} offset="0.48" stopOpacity="0.82" />
            <stop className={styles.meshBlue} offset="1" stopOpacity="0.12" />
          </linearGradient>
          <radialGradient id={ids.glowBlue}>
            <stop className={styles.glowBlue} offset="0" stopOpacity="0.62" />
            <stop className={styles.glowBlue} offset="1" stopOpacity="0" />
          </radialGradient>
          <radialGradient id={ids.glowGreen}>
            <stop className={styles.glowGreen} offset="0" stopOpacity="0.58" />
            <stop className={styles.glowGreen} offset="1" stopOpacity="0" />
          </radialGradient>
          <pattern id={ids.dots} height="21" patternUnits="userSpaceOnUse" width="21">
            <circle className={styles.dot} cx="3" cy="3" r="2.1" />
          </pattern>
          <filter id={ids.nodeGlow} height="600%" width="600%" x="-250%" y="-250%">
            <feGaussianBlur result="blur" stdDeviation="4" />
            <feMerge>
              <feMergeNode in="blur" />
              <feMergeNode in="SourceGraphic" />
            </feMerge>
          </filter>
          <clipPath id={ids.ribbonClip}>
            <path d="M128 127 C228 95 280 150 368 200 C456 251 536 262 646 193 C692 164 732 124 780 80 L780 286 C694 318 625 337 544 326 C424 309 353 220 263 180 C211 157 167 149 128 151 Z" />
          </clipPath>
          <linearGradient id={ids.ribbonFadeGradient} gradientUnits="userSpaceOnUse" x1="64" x2="230" y1="0" y2="0">
            <stop offset="0" stopColor="black" />
            <stop offset="0.34" stopColor="black" />
            <stop offset="1" stopColor="white" />
          </linearGradient>
          <mask height="420" id={ids.ribbonFade} maskUnits="userSpaceOnUse" width="840" x="0" y="0">
            <rect fill={`url(#${ids.ribbonFadeGradient})`} height="420" width="840" />
          </mask>
        </defs>

        <g className={styles.glows}>
          <ellipse cx="500" cy="233" fill={`url(#${ids.glowBlue})`} rx="270" ry="154" />
          <ellipse cx="470" cy="260" fill={`url(#${ids.glowGreen})`} rx="200" ry="124" />
        </g>

        <g className={styles.dotFields}>
          <rect fill={`url(#${ids.dots})`} height="104" opacity="0.26" width="118" x="86" y="84" />
          <rect fill={`url(#${ids.dots})`} height="106" opacity="0.18" width="126" x="192" y="271" />
          <rect fill={`url(#${ids.dots})`} height="106" opacity="0.17" width="112" x="350" y="165" />
        </g>

        <g className={styles.ribbons} mask={`url(#${ids.ribbonFade})`}>
          <path
            className={`${styles.ribbon} ${styles.ribbonA}`}
            d="M91 72 C215 72 274 119 362 174 C464 238 539 258 642 202 C697 172 742 121 790 62 L790 242 C700 270 633 284 555 275 C431 260 363 191 281 142 C208 98 155 89 91 97 Z"
            fill={`url(#${ids.ribbonA})`}
          />
          <path
            className={`${styles.ribbon} ${styles.ribbonB}`}
            d="M128 127 C228 95 280 150 368 200 C456 251 536 262 646 193 C692 164 732 124 780 80 L780 286 C694 318 625 337 544 326 C424 309 353 220 263 180 C211 157 167 149 128 151 Z"
            fill={`url(#${ids.ribbonB})`}
          />
          <path
            className={`${styles.ribbon} ${styles.ribbonC}`}
            d="M178 355 C292 312 349 236 448 207 C536 181 615 210 700 194 C731 188 758 176 790 156 L790 337 C704 354 620 353 536 330 C427 300 356 286 277 323 C236 342 207 354 178 367 Z"
            fill={`url(#${ids.ribbonC})`}
          />
          <path
            className={styles.ribbonEdge}
            d="M93 74 C215 73 275 119 362 174 C464 238 539 258 642 202 C697 172 742 121 790 62"
            pathLength="1"
          />
          <path
            className={`${styles.ribbonEdge} ${styles.ribbonEdgeGreen}`}
            d="M129 128 C228 96 280 150 368 200 C456 251 536 262 646 193 C692 164 732 124 780 80"
            pathLength="1"
          />
          <g className={styles.mesh} clipPath={`url(#${ids.ribbonClip})`}>
            {meshPaths.map((path) => (
              <path d={path} key={path} pathLength="1" stroke={`url(#${ids.mesh})`} />
            ))}
          </g>
        </g>

        <g className={styles.orbits}>
          <ellipse
            className={`${styles.orbit} ${styles.orbitOne}`}
            cx="365"
            cy="226"
            pathLength="1"
            rx="282"
            ry="126"
            transform="rotate(8 365 226)"
          />
          <ellipse
            className={`${styles.orbit} ${styles.orbitTwo}`}
            cx="466"
            cy="218"
            pathLength="1"
            rx="247"
            ry="128"
            transform="rotate(-33 466 218)"
          />
          <ellipse
            className={`${styles.orbit} ${styles.orbitThree}`}
            cx="526"
            cy="212"
            pathLength="1"
            rx="235"
            ry="116"
            transform="rotate(57 526 212)"
          />
        </g>

        <g className={styles.nodes} filter={`url(#${ids.nodeGlow})`}>
          <circle className={`${styles.node} ${styles.nodeBlueA}`} cx="143" cy="203" r="6" />
          <circle className={`${styles.node} ${styles.nodeGreenA}`} cx="211" cy="331" r="6" />
          <circle className={`${styles.node} ${styles.nodeBlueB}`} cx="562" cy="158" r="7" />
          <circle className={`${styles.node} ${styles.nodeGreenB}`} cx="609" cy="95" r="6" />
          <circle className={`${styles.node} ${styles.nodePale}`} cx="324" cy="57" r="3.5" />
        </g>

        <g className={styles.sparkles}>
          {sparkles.map((sparkle) => (
            <circle {...sparkle} key={`${sparkle.cx}-${sparkle.cy}`} />
          ))}
          <path d="M735 219 l4 7 7 4-7 4-4 7-4-7-7-4 7-4z" />
        </g>
      </svg>
    </div>
  )
}
