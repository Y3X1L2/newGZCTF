import cx from 'clsx'
import {
  CSSProperties,
  ComponentPropsWithoutRef,
  ElementType,
  PointerEvent,
  ReactNode,
  createElement,
  memo,
  useCallback,
  useEffect,
  useRef,
} from 'react'
import { BloomEffect, ChromaticAberrationEffect, EffectComposer, EffectPass, RenderPass } from 'postprocessing'
import * as THREE from 'three'
import ReactBitsColorBends from '@Components/reactbits-original/ColorBends'
import ReactBitsDotField from '@Components/reactbits-original/DotField'
import ReactBitsGradientText from '@Components/reactbits-original/GradientText'
import { GridScan as ReactBitsGridScan } from '@Components/reactbits-original/GridScan'
import ReactBitsScrambledText from '@Components/reactbits-original/ScrambledText'
import ReactBitsTextType from '@Components/reactbits-original/TextType'
import { YinyuStatusTone } from './YinyuUI'

type GradientTone =
  | 'brand'
  | 'signal'
  | 'ongoing'
  | 'coming'
  | 'ended'
  | 'danger'
  | 'silver'
  | 'single'
  | 'multiple'
  | 'judge'
  | 'score'

type GradientTextProps<T extends ElementType = 'span'> = {
  as?: T
  tone?: GradientTone
  className?: string
  children: ReactNode
} & Omit<ComponentPropsWithoutRef<T>, 'as' | 'children' | 'className'>

export function YinyuGradientText<T extends ElementType = 'span'>({
  as,
  tone = 'brand',
  className,
  children,
  ...props
}: GradientTextProps<T>) {
  const Component = as ?? 'span'
  const palette = gradientPalettes[tone] ?? gradientPalettes.brand

  return createElement(
    Component,
    {
      ...props,
      className: cx('yy-react-gradient-text', `yy-gradient-${tone}`, className),
    },
    <ReactBitsGradientText
      className="yy-reactbits-gradient-original"
      colors={palette}
      animationSpeed={tone === 'brand' ? 5.6 : tone === 'ongoing' ? 4.8 : 6.4}
      direction={tone === 'brand' ? 'diagonal' : 'horizontal'}
      pauseOnHover={false}
      yoyo
    >
      {children}
    </ReactBitsGradientText>
  )
}

const gradientPalettes: Record<GradientTone, string[]> = {
  brand: ['#F8FFF9', '#79F8BE', '#E2F2EA', '#8E7BE6', '#FFFFFF'],
  signal: ['#D9FFF0', '#69F6B7', '#F7FFFB', '#8DFFD0'],
  ongoing: ['#F4FFF8', '#6EFFB8', '#22F0A0', '#C9FFE7'],
  coming: ['#FFF7C7', '#FFD166', '#FF8F3D', '#FFF0B3'],
  ended: ['#F8FAFC', '#D8DEE9', '#9AA7B7', '#FFFFFF'],
  danger: ['#FFE8EA', '#FF7A90', '#FF3D68', '#FFD1D8'],
  silver: ['#FFFFFF', '#DCE7E1', '#93A39B', '#F7FFFB'],
  single: ['#DDFBFF', '#38BDF8', '#9BEAFE', '#F8FEFF'],
  multiple: ['#F7F0FF', '#9C88E8', '#D9CEFF', '#FFFFFF'],
  judge: ['#FFF6CC', '#FACC15', '#FB923C', '#FFFFFF'],
  score: ['#E9FFF4', '#6EFFB8', '#A7F3D0', '#FFFFFF'],
}

export function YinyuStatusText({
  children,
  tone = 'neutral',
  className,
}: {
  children: ReactNode
  tone?: YinyuStatusTone
  className?: string
}) {
  const gradientTone: GradientTone =
    tone === 'success' ? 'ongoing' : tone === 'warm' ? 'coming' : tone === 'danger' ? 'danger' : 'ended'

  return (
    <YinyuGradientText as="span" tone={gradientTone} className={cx('yy-react-status-text', className)}>
      {children}
    </YinyuGradientText>
  )
}

export function YinyuTextType(props: ComponentPropsWithoutRef<typeof ReactBitsTextType>) {
  const textKey = Array.isArray(props.text) ? props.text.join('\u0000') : props.text

  return <ReactBitsTextType key={textKey} {...props} />
}

export function YinyuScrambledText(props: ComponentPropsWithoutRef<typeof ReactBitsScrambledText>) {
  return <ReactBitsScrambledText {...props} />
}

const fullScreenVertex = `
varying vec2 vUv;
void main() {
  vUv = uv;
  gl_Position = vec4(position.xy, 0.0, 1.0);
}
`

const darkVeilFragment = `
precision highp float;
uniform vec2 uResolution;
uniform float uTime;
uniform float uOpacity;
varying vec2 vUv;

float hash(vec2 p) {
  return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453123);
}

float noise(vec2 p) {
  vec2 i = floor(p);
  vec2 f = fract(p);
  vec2 u = f * f * (3.0 - 2.0 * f);
  return mix(
    mix(hash(i), hash(i + vec2(1.0, 0.0)), u.x),
    mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), u.x),
    u.y
  );
}

float fbm(vec2 p) {
  float value = 0.0;
  float amplitude = 0.5;
  mat2 rot = mat2(0.86, -0.5, 0.5, 0.86);
  for (int i = 0; i < 5; i++) {
    value += amplitude * noise(p);
    p = rot * p * 2.05 + 0.37;
    amplitude *= 0.5;
  }
  return value;
}

void main() {
  vec2 uv = vUv;
  vec2 p = (uv - 0.5) * vec2(uResolution.x / max(uResolution.y, 1.0), 1.0);
  float t = uTime * 0.18;

  float veilA = fbm(p * 2.2 + vec2(t, -t * 0.72));
  float veilB = fbm(p * 4.6 + vec2(-t * 0.58, t * 0.44));
  float ribbon = smoothstep(0.34, 0.92, veilA + veilB * 0.42);
  float scan = pow(0.5 + 0.5 * sin((p.x * 1.8 + p.y * 2.4 + uTime * 0.32) * 3.14159), 3.0);
  float vignette = smoothstep(1.34, 0.18, length(p));

  vec3 ink = vec3(0.012, 0.021, 0.019);
  vec3 deepGreen = vec3(0.018, 0.135, 0.092);
  vec3 mint = vec3(0.395, 0.930, 0.660);
  vec3 silver = vec3(0.800, 0.875, 0.835);

  vec3 color = ink;
  color += deepGreen * (0.52 * ribbon + 0.18 * veilB);
  color += mint * (0.13 * scan * ribbon + 0.08 * veilA);
  color += silver * (0.035 * pow(veilB, 4.0));
  color *= vignette;

  gl_FragColor = vec4(color, uOpacity * vignette);
}
`

const gridScanFragment = `
precision highp float;
uniform vec3 iResolution;
uniform float iTime;
uniform vec2 uSkew;
uniform float uTilt;
uniform float uYaw;
uniform float uLineThickness;
uniform vec3 uLinesColor;
uniform vec3 uScanColor;
uniform float uGridScale;
uniform float uLineStyle;
uniform float uLineJitter;
uniform float uScanOpacity;
uniform float uScanDirection;
uniform float uNoise;
uniform float uBloomOpacity;
uniform float uScanGlow;
uniform float uScanSoftness;
uniform float uPhaseTaper;
uniform float uScanDuration;
uniform float uScanDelay;
uniform float uOpacity;
varying vec2 vUv;

uniform float uScanStarts[8];
uniform float uScanCount;

const int MAX_SCANS = 8;

float smoother01(float a, float b, float x){
  float t = clamp((x - a) / max(1e-5, (b - a)), 0.0, 1.0);
  return t * t * t * (t * (t * 6.0 - 15.0) + 10.0);
}

void mainImage(out vec4 fragColor, in vec2 fragCoord) {
  vec2 p = (2.0 * fragCoord - iResolution.xy) / iResolution.y;

  vec3 ro = vec3(0.0);
  vec3 rd = normalize(vec3(p, 2.0));

  float cR = cos(uTilt), sR = sin(uTilt);
  rd.xy = mat2(cR, -sR, sR, cR) * rd.xy;

  float cY = cos(uYaw), sY = sin(uYaw);
  rd.xz = mat2(cY, -sY, sY, cY) * rd.xz;

  vec2 skew = clamp(uSkew, vec2(-0.7), vec2(0.7));
  rd.xy += skew * rd.z;

  float minT = 1e20;
  float gridScale = max(1e-5, uGridScale);
  vec2 gridUV = vec2(0.0);
  float hitIsY = 1.0;

  for (int i = 0; i < 4; i++) {
    float isY = float(i < 2);
    float pos = mix(-0.2, 0.2, float(i)) * isY + mix(-0.5, 0.5, float(i - 2)) * (1.0 - isY);
    float num = pos - (isY * ro.y + (1.0 - isY) * ro.x);
    float den = isY * rd.y + (1.0 - isY) * rd.x;
    float t = num / den;
    vec3 h = ro + rd * t;
    float depthBoost = smoothstep(0.0, 3.0, h.z);
    h.xy += skew * 0.15 * depthBoost;

    bool use = t > 0.0 && t < minT;
    gridUV = use ? mix(h.zy, h.xz, isY) / gridScale : gridUV;
    minT = use ? t : minT;
    hitIsY = use ? isY : hitIsY;
  }

  vec3 hit = ro + rd * minT;
  float dist = length(hit - ro);
  float fade = exp(-dist * 2.0);

  float jitterAmt = clamp(uLineJitter, 0.0, 1.0);
  if (jitterAmt > 0.0) {
    vec2 j = vec2(
      sin(gridUV.y * 2.7 + iTime * 1.8),
      cos(gridUV.x * 2.3 - iTime * 1.6)
    ) * (0.15 * jitterAmt);
    gridUV += j;
  }

  float fx = fract(gridUV.x);
  float fy = fract(gridUV.y);
  float ax = min(fx, 1.0 - fx);
  float ay = min(fy, 1.0 - fy);
  float wx = fwidth(gridUV.x);
  float wy = fwidth(gridUV.y);
  float halfPx = max(0.0, uLineThickness) * 0.5;
  float tx = halfPx * wx;
  float ty = halfPx * wy;

  float lineX = 1.0 - smoothstep(tx, tx + wx, ax);
  float lineY = 1.0 - smoothstep(ty, ty + wy, ay);
  if (uLineStyle > 0.5) {
    float dashRepeat = 4.0;
    float dashDuty = 0.5;
    float vy = fract(gridUV.y * dashRepeat);
    float vx = fract(gridUV.x * dashRepeat);
    float dashMaskY = step(vy, dashDuty);
    float dashMaskX = step(vx, dashDuty);
    if (uLineStyle < 1.5) {
      lineX *= dashMaskY;
      lineY *= dashMaskX;
    } else {
      float dotRepeat = 6.0;
      float dotWidth = 0.18;
      float cy = abs(fract(gridUV.y * dotRepeat) - 0.5);
      float cx = abs(fract(gridUV.x * dotRepeat) - 0.5);
      float dotMaskY = 1.0 - smoothstep(dotWidth, dotWidth + fwidth(gridUV.y * dotRepeat), cy);
      float dotMaskX = 1.0 - smoothstep(dotWidth, dotWidth + fwidth(gridUV.x * dotRepeat), cx);
      lineX *= dotMaskY;
      lineY *= dotMaskX;
    }
  }
  float primaryMask = max(lineX, lineY);

  vec2 gridUV2 = (hitIsY > 0.5 ? hit.xz : hit.zy) / gridScale;
  if (jitterAmt > 0.0) {
    vec2 j2 = vec2(
      cos(gridUV2.y * 2.1 - iTime * 1.4),
      sin(gridUV2.x * 2.5 + iTime * 1.7)
    ) * (0.15 * jitterAmt);
    gridUV2 += j2;
  }

  float fx2 = fract(gridUV2.x);
  float fy2 = fract(gridUV2.y);
  float ax2 = min(fx2, 1.0 - fx2);
  float ay2 = min(fy2, 1.0 - fy2);
  float wx2 = fwidth(gridUV2.x);
  float wy2 = fwidth(gridUV2.y);
  float lineX2 = 1.0 - smoothstep(halfPx * wx2, halfPx * wx2 + wx2, ax2);
  float lineY2 = 1.0 - smoothstep(halfPx * wy2, halfPx * wy2 + wy2, ay2);
  if (uLineStyle > 0.5) {
    float dashRepeat2 = 4.0;
    float dashDuty2 = 0.5;
    float vy2m = fract(gridUV2.y * dashRepeat2);
    float vx2m = fract(gridUV2.x * dashRepeat2);
    float dashMaskY2 = step(vy2m, dashDuty2);
    float dashMaskX2 = step(vx2m, dashDuty2);
    if (uLineStyle < 1.5) {
      lineX2 *= dashMaskY2;
      lineY2 *= dashMaskX2;
    } else {
      float dotRepeat2 = 6.0;
      float dotWidth2 = 0.18;
      float cy2 = abs(fract(gridUV2.y * dotRepeat2) - 0.5);
      float cx2 = abs(fract(gridUV2.x * dotRepeat2) - 0.5);
      float dotMaskY2 = 1.0 - smoothstep(dotWidth2, dotWidth2 + fwidth(gridUV2.y * dotRepeat2), cy2);
      float dotMaskX2 = 1.0 - smoothstep(dotWidth2, dotWidth2 + fwidth(gridUV2.x * dotRepeat2), cx2);
      lineX2 *= dotMaskY2;
      lineY2 *= dotMaskX2;
    }
  }
  float altMask = max(lineX2, lineY2);

  float edgeDistX = min(abs(hit.x - (-0.5)), abs(hit.x - 0.5));
  float edgeDistY = min(abs(hit.y - (-0.2)), abs(hit.y - 0.2));
  float edgeDist = mix(edgeDistY, edgeDistX, hitIsY);
  float edgeGate = 1.0 - smoothstep(gridScale * 0.5, gridScale * 2.0, edgeDist);
  altMask *= edgeGate;

  float lineMask = max(primaryMask, altMask);

  float dur = max(0.05, uScanDuration);
  float del = max(0.0, uScanDelay);
  float scanZMax = 2.0;
  float widthScale = max(0.1, uScanGlow);
  float sigma = max(0.001, 0.18 * widthScale * uScanSoftness);
  float sigmaA = sigma * 2.0;
  float cycle = dur + del;
  float tCycle = mod(iTime, cycle);
  float scanPhase = clamp((tCycle - del) / dur, 0.0, 1.0);
  float phase = scanPhase;
  if (uScanDirection > 0.5 && uScanDirection < 1.5) {
    phase = 1.0 - phase;
  } else if (uScanDirection > 1.5) {
    float t2 = mod(max(0.0, iTime - del), 2.0 * dur);
    phase = (t2 < dur) ? (t2 / dur) : (1.0 - (t2 - dur) / dur);
  }
  float scanZ = phase * scanZMax;
  float dz = abs(hit.z - scanZ);
  float lineBand = exp(-0.5 * (dz * dz) / (sigma * sigma));
  float taper = clamp(uPhaseTaper, 0.0, 0.49);
  float headFade = smoother01(0.0, taper, phase);
  float tailFade = 1.0 - smoother01(1.0 - taper, 1.0, phase);
  float phaseWindow = headFade * tailFade;
  float combinedPulse = lineBand * phaseWindow * clamp(uScanOpacity, 0.0, 1.0);
  float combinedAura = exp(-0.5 * (dz * dz) / (sigmaA * sigmaA)) * 0.25 * phaseWindow * clamp(uScanOpacity, 0.0, 1.0);

  for (int i = 0; i < MAX_SCANS; i++) {
    if (float(i) >= uScanCount) break;
    float tActiveI = iTime - uScanStarts[i];
    float phaseI = clamp(tActiveI / dur, 0.0, 1.0);
    if (uScanDirection > 0.5 && uScanDirection < 1.5) {
      phaseI = 1.0 - phaseI;
    } else if (uScanDirection > 1.5) {
      phaseI = (phaseI < 0.5) ? (phaseI * 2.0) : (1.0 - (phaseI - 0.5) * 2.0);
    }
    float scanZI = phaseI * scanZMax;
    float dzI = abs(hit.z - scanZI);
    float lineBandI = exp(-0.5 * (dzI * dzI) / (sigma * sigma));
    float headFadeI = smoother01(0.0, taper, phaseI);
    float tailFadeI = 1.0 - smoother01(1.0 - taper, 1.0, phaseI);
    float phaseWindowI = headFadeI * tailFadeI;
    combinedPulse += lineBandI * phaseWindowI * clamp(uScanOpacity, 0.0, 1.0);
    float auraBandI = exp(-0.5 * (dzI * dzI) / (sigmaA * sigmaA));
    combinedAura += (auraBandI * 0.25) * phaseWindowI * clamp(uScanOpacity, 0.0, 1.0);
  }

  vec3 gridCol = uLinesColor * lineMask * fade;
  vec3 scanCol = uScanColor * combinedPulse;
  vec3 scanAura = uScanColor * combinedAura;
  vec3 color = gridCol + scanCol + scanAura;

  float n = fract(sin(dot(gl_FragCoord.xy + vec2(iTime * 123.4), vec2(12.9898,78.233))) * 43758.5453123);
  color += (n - 0.5) * uNoise;
  color = clamp(color, 0.0, 1.0);

  float gx = 1.0 - smoothstep(tx * 2.0, tx * 2.0 + wx * 2.0, ax);
  float gy = 1.0 - smoothstep(ty * 2.0, ty * 2.0 + wy * 2.0, ay);
  float halo = max(gx, gy) * fade;
  float alpha = clamp(max(lineMask, combinedPulse), 0.0, 1.0);
  alpha = max(alpha, halo * clamp(uBloomOpacity, 0.0, 1.0));

  fragColor = vec4(color, alpha * uOpacity);
}

void main() {
  vec4 c;
  mainImage(c, vUv * iResolution.xy);
  gl_FragColor = c;
}
`

const MAX_COLOR_BENDS_COLORS = 8 as const

const colorBendsFragment = `
#define MAX_COLORS ${MAX_COLOR_BENDS_COLORS}
uniform vec2 uCanvas;
uniform float uTime;
uniform float uSpeed;
uniform vec2 uRot;
uniform int uColorCount;
uniform vec3 uColors[MAX_COLORS];
uniform int uTransparent;
uniform float uScale;
uniform float uFrequency;
uniform float uWarpStrength;
uniform vec2 uPointer;
uniform float uMouseInfluence;
uniform float uParallax;
uniform float uNoise;
uniform int uIterations;
uniform float uIntensity;
uniform float uBandWidth;
uniform float uFadeTop;
varying vec2 vUv;

void main() {
  float t = uTime * uSpeed;
  vec2 p = vUv * 2.0 - 1.0;
  p += uPointer * uParallax * 0.1;
  vec2 rp = vec2(p.x * uRot.x - p.y * uRot.y, p.x * uRot.y + p.y * uRot.x);
  vec2 q = vec2(rp.x * (uCanvas.x / max(uCanvas.y, 1.0)), rp.y);
  q /= max(uScale, 0.0001);
  q /= 0.5 + 0.2 * dot(q, q);
  q += 0.2 * cos(t) - 7.56;
  vec2 toward = (uPointer - rp);
  q += toward * uMouseInfluence * 0.2;

  for (int j = 0; j < 5; j++) {
    if (j >= uIterations - 1) break;
    vec2 rr = sin(1.5 * (q.yx * uFrequency) + 2.0 * cos(q * uFrequency));
    q += (rr - q) * 0.15;
  }

  vec3 col = vec3(0.0);
  float a = 1.0;

  if (uColorCount > 0) {
    vec2 s = q;
    vec3 sumCol = vec3(0.0);
    float cover = 0.0;
    for (int i = 0; i < MAX_COLORS; ++i) {
      if (i >= uColorCount) break;
      s -= 0.01;
      vec2 r = sin(1.5 * (s.yx * uFrequency) + 2.0 * cos(s * uFrequency));
      float m0 = length(r + sin(5.0 * r.y * uFrequency - 3.0 * t + float(i)) / 4.0);
      float kBelow = clamp(uWarpStrength, 0.0, 1.0);
      float kMix = pow(kBelow, 0.3);
      float gain = 1.0 + max(uWarpStrength - 1.0, 0.0);
      vec2 disp = (r - s) * kBelow;
      vec2 warped = s + disp * gain;
      float m1 = length(warped + sin(5.0 * warped.y * uFrequency - 3.0 * t + float(i)) / 4.0);
      float m = mix(m0, m1, kMix);
      float w = 1.0 - exp(-uBandWidth / exp(uBandWidth * m));
      sumCol += uColors[i] * w;
      cover = max(cover, w);
    }
    col = clamp(sumCol, 0.0, 1.0);
    a = uTransparent > 0 ? cover : 1.0;
  } else {
    vec2 s = q;
    for (int k = 0; k < 3; ++k) {
      s -= 0.01;
      vec2 r = sin(1.5 * (s.yx * uFrequency) + 2.0 * cos(s * uFrequency));
      float m0 = length(r + sin(5.0 * r.y * uFrequency - 3.0 * t + float(k)) / 4.0);
      float kBelow = clamp(uWarpStrength, 0.0, 1.0);
      float kMix = pow(kBelow, 0.3);
      float gain = 1.0 + max(uWarpStrength - 1.0, 0.0);
      vec2 disp = (r - s) * kBelow;
      vec2 warped = s + disp * gain;
      float m1 = length(warped + sin(5.0 * warped.y * uFrequency - 3.0 * t + float(k)) / 4.0);
      float m = mix(m0, m1, kMix);
      col[k] = 1.0 - exp(-uBandWidth / exp(uBandWidth * m));
    }
    a = uTransparent > 0 ? max(max(col.r, col.g), col.b) : 1.0;
  }

  col *= uIntensity;

  if (uNoise > 0.0001) {
    float n = fract(sin(dot(gl_FragCoord.xy + vec2(uTime), vec2(12.9898, 78.233))) * 43758.5453123);
    col += (n - 0.5) * uNoise;
    col = clamp(col, 0.0, 1.0);
  }

  float topFade = 1.0;
  if (uFadeTop > 0.0001) {
    topFade = 1.0 - smoothstep(max(0.0, 1.0 - uFadeTop), 1.0, vUv.y);
  }

  vec3 rgb = (uTransparent > 0) ? col * a : col;
  gl_FragColor = vec4(rgb, a * topFade);
}
`

const colorBendsVertex = `
varying vec2 vUv;
void main() {
  vUv = uv;
  gl_Position = vec4(position, 1.0);
}
`

type CanvasEffectProps = {
  className?: string
  opacity?: number
  speed?: number
  resolutionScale?: number
}

type YinyuColorBendsProps = {
  className?: string
  style?: CSSProperties
  rotation?: number
  speed?: number
  color?: string
  colors?: string[]
  transparent?: boolean
  autoRotate?: number
  scale?: number
  frequency?: number
  warpStrength?: number
  mouseInfluence?: number
  parallax?: number
  noise?: number
  iterations?: number
  intensity?: number
  bandWidth?: number
  fadeTop?: number
  resolutionScale?: number
}

type DotRecord = {
  ax: number
  ay: number
  sx: number
  sy: number
  vx: number
  vy: number
  x: number
  y: number
}

type YinyuDotFieldProps = ComponentPropsWithoutRef<'div'> & {
  dotRadius?: number
  dotSpacing?: number
  cursorRadius?: number
  cursorForce?: number
  bulgeOnly?: boolean
  bulgeStrength?: number
  glowRadius?: number
  sparkle?: boolean
  waveAmplitude?: number
  gradientFrom?: string
  gradientTo?: string
  glowColor?: string
}

function ShaderCanvas({
  className,
  fragment,
  opacity = 0.72,
  speed = 1,
  resolutionScale = 0.82,
}: CanvasEffectProps & { fragment: string }) {
  const ref = useRef<HTMLCanvasElement>(null)

  useEffect(() => {
    const canvas = ref.current
    const parent = canvas?.parentElement
    if (!canvas || !parent) return undefined

    const renderer = new THREE.WebGLRenderer({
      alpha: true,
      antialias: false,
      canvas,
      powerPreference: 'high-performance',
    })
    renderer.setClearColor(0x000000, 0)

    const scene = new THREE.Scene()
    const camera = new THREE.Camera()
    const geometry = new THREE.PlaneGeometry(2, 2)
    const uniforms = {
      uResolution: { value: new THREE.Vector2(1, 1) },
      uTime: { value: 0 },
      uOpacity: { value: opacity },
    }
    const material = new THREE.ShaderMaterial({
      vertexShader: fullScreenVertex,
      fragmentShader: fragment,
      uniforms,
      transparent: true,
      depthWrite: false,
      depthTest: false,
    })
    const mesh = new THREE.Mesh(geometry, material)
    scene.add(mesh)

    const resize = () => {
      const rect = parent.getBoundingClientRect()
      const width = Math.max(1, Math.round(rect.width))
      const height = Math.max(1, Math.round(rect.height))
      renderer.setPixelRatio(Math.min(window.devicePixelRatio, 1.65) * resolutionScale)
      renderer.setSize(width, height, false)
      uniforms.uResolution.value.set(width, height)
    }

    const observer = new ResizeObserver(resize)
    observer.observe(parent)
    resize()

    const start = performance.now()
    let frame = 0
    const loop = () => {
      uniforms.uTime.value = ((performance.now() - start) / 1000) * speed
      uniforms.uOpacity.value = opacity
      renderer.render(scene, camera)
      frame = window.requestAnimationFrame(loop)
    }
    loop()

    return () => {
      window.cancelAnimationFrame(frame)
      observer.disconnect()
      geometry.dispose()
      material.dispose()
      renderer.dispose()
    }
  }, [fragment, opacity, resolutionScale, speed])

  return <canvas ref={ref} className={cx('yy-react-bits-canvas', className)} aria-hidden="true" />
}

export function YinyuColorBends({
  className,
  style,
  rotation = 90,
  speed = 0.2,
  color = '#62f3b2',
  colors,
  transparent = true,
  autoRotate = 0,
  scale = 1,
  frequency = 1,
  warpStrength = 1,
  mouseInfluence = 1,
  parallax = 0.5,
  noise = 0.15,
  iterations = 1,
  intensity = 1.3,
  bandWidth = 0.14,
  fadeTop = 0.75,
  resolutionScale = 0.88,
}: YinyuColorBendsProps) {
  const containerRef = useRef<HTMLDivElement | null>(null)
  const rendererRef = useRef<THREE.WebGLRenderer | null>(null)
  const rafRef = useRef<number | null>(null)
  const materialRef = useRef<THREE.ShaderMaterial | null>(null)
  const resizeObserverRef = useRef<ResizeObserver | null>(null)
  const rotationRef = useRef(rotation)
  const autoRotateRef = useRef(autoRotate)
  const pointerTargetRef = useRef(new THREE.Vector2(0, 0))
  const pointerCurrentRef = useRef(new THREE.Vector2(0, 0))
  const pointerSmoothRef = useRef(8)

  useEffect(() => {
    const container = containerRef.current
    if (!container) return undefined

    const scene = new THREE.Scene()
    const camera = new THREE.OrthographicCamera(-1, 1, 1, -1, 0, 1)
    const geometry = new THREE.PlaneGeometry(2, 2)
    const uColorsArray = Array.from({ length: MAX_COLOR_BENDS_COLORS }, () => new THREE.Vector3(0, 0, 0))
    const material = new THREE.ShaderMaterial({
      vertexShader: colorBendsVertex,
      fragmentShader: colorBendsFragment,
      uniforms: {
        uCanvas: { value: new THREE.Vector2(1, 1) },
        uTime: { value: 0 },
        uSpeed: { value: speed },
        uRot: { value: new THREE.Vector2(1, 0) },
        uColorCount: { value: 0 },
        uColors: { value: uColorsArray },
        uTransparent: { value: transparent ? 1 : 0 },
        uScale: { value: scale },
        uFrequency: { value: frequency },
        uWarpStrength: { value: warpStrength },
        uPointer: { value: new THREE.Vector2(0, 0) },
        uMouseInfluence: { value: mouseInfluence },
        uParallax: { value: parallax },
        uNoise: { value: noise },
        uIterations: { value: iterations },
        uIntensity: { value: intensity },
        uBandWidth: { value: bandWidth },
        uFadeTop: { value: fadeTop },
      },
      premultipliedAlpha: true,
      transparent: true,
      depthWrite: false,
      depthTest: false,
    })
    materialRef.current = material

    const mesh = new THREE.Mesh(geometry, material)
    scene.add(mesh)

    const renderer = new THREE.WebGLRenderer({
      antialias: false,
      powerPreference: 'high-performance',
      alpha: true,
    })
    rendererRef.current = renderer
    renderer.outputColorSpace = THREE.SRGBColorSpace
    renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2) * resolutionScale)
    renderer.setClearColor(0x000000, transparent ? 0 : 1)
    renderer.domElement.style.width = '100%'
    renderer.domElement.style.height = '100%'
    renderer.domElement.style.display = 'block'
    container.appendChild(renderer.domElement)

    const clock = new THREE.Clock()

    const handleResize = () => {
      const width = Math.max(1, Math.round(container.clientWidth || 1))
      const height = Math.max(1, Math.round(container.clientHeight || 1))
      renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2) * resolutionScale)
      renderer.setSize(width, height, false)
      ;(material.uniforms.uCanvas.value as THREE.Vector2).set(width, height)
    }

    handleResize()

    if (typeof ResizeObserver !== 'undefined') {
      const observer = new ResizeObserver(handleResize)
      observer.observe(container)
      resizeObserverRef.current = observer
    } else {
      window.addEventListener('resize', handleResize)
    }

    const loop = () => {
      const dt = clock.getDelta()
      const elapsed = clock.elapsedTime
      material.uniforms.uTime.value = elapsed

      const degrees = (rotationRef.current % 360) + autoRotateRef.current * elapsed
      const radians = (degrees * Math.PI) / 180
      ;(material.uniforms.uRot.value as THREE.Vector2).set(Math.cos(radians), Math.sin(radians))

      pointerCurrentRef.current.lerp(pointerTargetRef.current, Math.min(1, dt * pointerSmoothRef.current))
      ;(material.uniforms.uPointer.value as THREE.Vector2).copy(pointerCurrentRef.current)
      renderer.render(scene, camera)
      rafRef.current = window.requestAnimationFrame(loop)
    }
    rafRef.current = window.requestAnimationFrame(loop)

    return () => {
      if (rafRef.current !== null) window.cancelAnimationFrame(rafRef.current)
      if (resizeObserverRef.current) resizeObserverRef.current.disconnect()
      else window.removeEventListener('resize', handleResize)
      geometry.dispose()
      material.dispose()
      renderer.dispose()
      renderer.forceContextLoss()
      if (renderer.domElement.parentElement === container) {
        container.removeChild(renderer.domElement)
      }
    }
  }, [])

  useEffect(() => {
    const material = materialRef.current
    const renderer = rendererRef.current
    if (!material) return

    rotationRef.current = rotation
    autoRotateRef.current = autoRotate
    material.uniforms.uSpeed.value = speed
    material.uniforms.uScale.value = scale
    material.uniforms.uFrequency.value = frequency
    material.uniforms.uWarpStrength.value = warpStrength
    material.uniforms.uMouseInfluence.value = mouseInfluence
    material.uniforms.uParallax.value = parallax
    material.uniforms.uNoise.value = noise
    material.uniforms.uIterations.value = iterations
    material.uniforms.uIntensity.value = intensity
    material.uniforms.uBandWidth.value = bandWidth
    material.uniforms.uFadeTop.value = fadeTop

    const palette =
      colors && colors.length > 0
        ? colors
        : [color, '#d9f5e8', '#11392d', '#74f7bd', '#eef7f2'].filter(Boolean)
    const colorVectors = palette.slice(0, MAX_COLOR_BENDS_COLORS).map((hex) => srgbVector(hex))
    for (let i = 0; i < MAX_COLOR_BENDS_COLORS; i += 1) {
      const vec = (material.uniforms.uColors.value as THREE.Vector3[])[i]
      if (i < colorVectors.length) vec.copy(colorVectors[i])
      else vec.set(0, 0, 0)
    }
    material.uniforms.uColorCount.value = colorVectors.length
    material.uniforms.uTransparent.value = transparent ? 1 : 0
    if (renderer) {
      renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2) * resolutionScale)
      renderer.setClearColor(0x000000, transparent ? 0 : 1)
    }
  }, [
    autoRotate,
    bandWidth,
    color,
    colors,
    fadeTop,
    frequency,
    intensity,
    iterations,
    mouseInfluence,
    noise,
    parallax,
    resolutionScale,
    rotation,
    scale,
    speed,
    transparent,
    warpStrength,
  ])

  useEffect(() => {
    const container = containerRef.current
    if (!container) return undefined

    const handlePointerMove = (event: globalThis.PointerEvent) => {
      const rect = container.getBoundingClientRect()
      const x = ((event.clientX - rect.left) / Math.max(rect.width, 1)) * 2 - 1
      const y = -(((event.clientY - rect.top) / Math.max(rect.height, 1)) * 2 - 1)
      pointerTargetRef.current.set(x, y)
    }

    const handlePointerLeave = () => pointerTargetRef.current.set(0, 0)
    container.addEventListener('pointermove', handlePointerMove, { passive: true })
    container.addEventListener('pointerleave', handlePointerLeave, { passive: true })

    return () => {
      container.removeEventListener('pointermove', handlePointerMove)
      container.removeEventListener('pointerleave', handlePointerLeave)
    }
  }, [])

  return <div ref={containerRef} className={cx('yy-react-color-bends', className)} style={style} aria-hidden="true" />
}

const TWO_PI = Math.PI * 2

export const YinyuDotField = memo(function YinyuDotField({
  dotRadius = 1.5,
  dotSpacing = 14,
  cursorRadius = 500,
  cursorForce = 0.1,
  bulgeOnly = true,
  bulgeStrength = 67,
  glowRadius = 160,
  sparkle = false,
  waveAmplitude = 0,
  gradientFrom = 'rgba(130, 255, 200, 0.28)',
  gradientTo = 'rgba(221, 244, 235, 0.18)',
  glowColor = 'rgba(107, 238, 177, 0.42)',
  className,
  ...rest
}: YinyuDotFieldProps) {
  const canvasRef = useRef<HTMLCanvasElement>(null)
  const glowRef = useRef<SVGCircleElement>(null)
  const dotsRef = useRef<DotRecord[]>([])
  const mouseRef = useRef({ x: -9999, y: -9999, prevX: -9999, prevY: -9999, speed: 0 })
  const rafRef = useRef<number | null>(null)
  const sizeRef = useRef({ w: 0, h: 0, offsetX: 0, offsetY: 0 })
  const glowOpacity = useRef(0)
  const engagement = useRef(0)
  const propsRef = useRef({
    dotRadius,
    dotSpacing,
    cursorRadius,
    cursorForce,
    bulgeOnly,
    bulgeStrength,
    sparkle,
    waveAmplitude,
    gradientFrom,
    gradientTo,
  })
  const rebuildRef = useRef<(() => void) | null>(null)
  const glowIdRef = useRef(`yy-dot-field-glow-${Math.random().toString(36).slice(2, 9)}`)

  propsRef.current = {
    dotRadius,
    dotSpacing,
    cursorRadius,
    cursorForce,
    bulgeOnly,
    bulgeStrength,
    sparkle,
    waveAmplitude,
    gradientFrom,
    gradientTo,
  }

  useEffect(() => {
    const canvas = canvasRef.current
    const glowElement = glowRef.current
    const parent = canvas?.parentElement
    if (!canvas || !parent) return undefined

    const ctx = canvas.getContext('2d', { alpha: true })
    if (!ctx) return undefined

    const dpr = Math.min(window.devicePixelRatio || 1, 2)
    let resizeTimer = 0

    const buildDots = (width: number, height: number) => {
      const p = propsRef.current
      const step = p.dotRadius + p.dotSpacing
      const cols = Math.floor(width / step)
      const rows = Math.floor(height / step)
      const padX = (width % step) / 2
      const padY = (height % step) / 2
      const dots: DotRecord[] = new Array(rows * cols)
      let index = 0

      for (let row = 0; row < rows; row += 1) {
        for (let col = 0; col < cols; col += 1) {
          const ax = padX + col * step + step / 2
          const ay = padY + row * step + step / 2
          dots[index] = { ax, ay, sx: ax, sy: ay, vx: 0, vy: 0, x: ax, y: ay }
          index += 1
        }
      }
      dotsRef.current = dots
    }

    const doResize = () => {
      const rect = parent.getBoundingClientRect()
      const width = Math.max(1, rect.width)
      const height = Math.max(1, rect.height)
      canvas.width = Math.round(width * dpr)
      canvas.height = Math.round(height * dpr)
      canvas.style.width = `${width}px`
      canvas.style.height = `${height}px`
      ctx.setTransform(dpr, 0, 0, dpr, 0, 0)
      sizeRef.current = {
        w: width,
        h: height,
        offsetX: rect.left + window.scrollX,
        offsetY: rect.top + window.scrollY,
      }
      buildDots(width, height)
    }

    const resize = () => {
      window.clearTimeout(resizeTimer)
      resizeTimer = window.setTimeout(doResize, 100)
    }

    const onMouseMove = (event: MouseEvent) => {
      const size = sizeRef.current
      mouseRef.current.x = event.pageX - size.offsetX
      mouseRef.current.y = event.pageY - size.offsetY
    }

    const updateMouseSpeed = () => {
      const mouse = mouseRef.current
      const dx = mouse.prevX - mouse.x
      const dy = mouse.prevY - mouse.y
      const distance = Math.sqrt(dx * dx + dy * dy)
      mouse.speed += (distance - mouse.speed) * 0.5
      if (mouse.speed < 0.001) mouse.speed = 0
      mouse.prevX = mouse.x
      mouse.prevY = mouse.y
    }

    const speedInterval = window.setInterval(updateMouseSpeed, 20)
    let frameCount = 0

    const tick = () => {
      frameCount += 1
      const dots = dotsRef.current
      const mouse = mouseRef.current
      const { w, h } = sizeRef.current
      const p = propsRef.current
      const time = frameCount * 0.02

      const targetEngagement = Math.min(mouse.speed / 5, 1)
      engagement.current += (targetEngagement - engagement.current) * 0.06
      if (engagement.current < 0.001) engagement.current = 0
      const engaged = engagement.current

      glowOpacity.current += (engaged - glowOpacity.current) * 0.08

      if (glowElement) {
        glowElement.setAttribute('cx', String(mouse.x))
        glowElement.setAttribute('cy', String(mouse.y))
        glowElement.style.opacity = String(glowOpacity.current)
      }

      ctx.clearRect(0, 0, w, h)
      const gradient = ctx.createLinearGradient(0, 0, w, h)
      gradient.addColorStop(0, p.gradientFrom)
      gradient.addColorStop(1, p.gradientTo)
      ctx.fillStyle = gradient

      const crSq = p.cursorRadius * p.cursorRadius
      const radius = p.dotRadius / 2

      ctx.beginPath()

      for (let i = 0; i < dots.length; i += 1) {
        const dot = dots[i]
        const dx = mouse.x - dot.ax
        const dy = mouse.y - dot.ay
        const distSq = dx * dx + dy * dy

        if (distSq < crSq && engaged > 0.01) {
          const dist = Math.sqrt(distSq)
          if (p.bulgeOnly) {
            const amount = 1 - dist / p.cursorRadius
            const push = amount * amount * p.bulgeStrength * engaged
            const angle = Math.atan2(dy, dx)
            dot.sx += (dot.ax - Math.cos(angle) * push - dot.sx) * 0.15
            dot.sy += (dot.ay - Math.sin(angle) * push - dot.sy) * 0.15
          } else {
            const angle = Math.atan2(dy, dx)
            const move = (500 / Math.max(dist, 1)) * (mouse.speed * p.cursorForce)
            dot.vx += Math.cos(angle) * -move
            dot.vy += Math.sin(angle) * -move
          }
        } else if (p.bulgeOnly) {
          dot.sx += (dot.ax - dot.sx) * 0.1
          dot.sy += (dot.ay - dot.sy) * 0.1
        }

        if (!p.bulgeOnly) {
          dot.vx *= 0.9
          dot.vy *= 0.9
          dot.x = dot.ax + dot.vx
          dot.y = dot.ay + dot.vy
          dot.sx += (dot.x - dot.sx) * 0.1
          dot.sy += (dot.y - dot.sy) * 0.1
        }

        let drawX = dot.sx
        let drawY = dot.sy
        if (p.waveAmplitude > 0) {
          drawY += Math.sin(dot.ax * 0.03 + time) * p.waveAmplitude
          drawX += Math.cos(dot.ay * 0.03 + time * 0.7) * p.waveAmplitude * 0.5
        }

        if (p.sparkle) {
          const hash = ((i * 2654435761) ^ (frameCount >> 3)) >>> 0
          const sparkleRadius = hash % 100 < 3 ? radius * 1.8 : radius
          ctx.moveTo(drawX + sparkleRadius, drawY)
          ctx.arc(drawX, drawY, sparkleRadius, 0, TWO_PI)
        } else {
          ctx.moveTo(drawX + radius, drawY)
          ctx.arc(drawX, drawY, radius, 0, TWO_PI)
        }
      }

      ctx.fill()
      rafRef.current = window.requestAnimationFrame(tick)
    }

    doResize()
    window.addEventListener('resize', resize)
    window.addEventListener('mousemove', onMouseMove, { passive: true })
    rafRef.current = window.requestAnimationFrame(tick)

    rebuildRef.current = () => {
      const { w, h } = sizeRef.current
      if (w > 0 && h > 0) buildDots(w, h)
    }

    return () => {
      if (rafRef.current) window.cancelAnimationFrame(rafRef.current)
      window.clearInterval(speedInterval)
      window.clearTimeout(resizeTimer)
      window.removeEventListener('resize', resize)
      window.removeEventListener('mousemove', onMouseMove)
    }
  }, [])

  useEffect(() => {
    rebuildRef.current?.()
  }, [dotRadius, dotSpacing])

  return (
    <div className={cx('yy-react-dot-field', className)} {...rest}>
      <canvas ref={canvasRef} />
      <svg aria-hidden="true">
        <defs>
          <radialGradient id={glowIdRef.current}>
            <stop offset="0%" stopColor={glowColor} />
            <stop offset="100%" stopColor="transparent" />
          </radialGradient>
        </defs>
        <circle
          ref={glowRef}
          cx="-9999"
          cy="-9999"
          r={glowRadius}
          fill={`url(#${glowIdRef.current})`}
          style={{ opacity: 0, willChange: 'opacity' }}
        />
      </svg>
    </div>
  )
})

export function YinyuGameBendsBackground({ className }: { className?: string }) {
  return (
    <div className={cx('yy-game-bends-bg', className)} aria-hidden="true">
      <ReactBitsColorBends
        className="yy-game-bends-bg__bends"
        colors={['#66F4B4', '#DFFEF0', '#8676D8']}
        speed={0.2}
        frequency={1.0}
        noise={0.15}
        bandWidth={0.14}
        rotation={90}
        iterations={1}
        intensity={1.3}
        warpStrength={1}
        mouseInfluence={0.72}
        parallax={0.35}
      />
      <ReactBitsDotField
        className="yy-game-bends-bg__dots"
        dotRadius={1.5}
        dotSpacing={14}
        cursorRadius={500}
        cursorForce={0.1}
        bulgeOnly
        bulgeStrength={67}
        glowRadius={160}
        sparkle={false}
        waveAmplitude={0}
        gradientFrom="rgba(210, 238, 228, 0.24)"
        gradientTo="rgba(79, 240, 170, 0.18)"
        glowColor="rgba(107, 238, 177, 0.36)"
      />
    </div>
  )
}

export function YinyuGridScan(props: ComponentPropsWithoutRef<typeof ReactBitsGridScan>) {
  return <ReactBitsGridScan {...props} />
}

export function YinyuDarkVeil(props: CanvasEffectProps) {
  return <ShaderCanvas {...props} fragment={darkVeilFragment} className={cx('yy-react-dark-veil', props.className)} />
}

function LegacyYinyuGridScan(props: CanvasEffectProps) {
  const { className, opacity = 0.78, speed = 1, resolutionScale = 0.86 } = props
  const ref = useRef<HTMLCanvasElement>(null)
  const lookTarget = useRef(new THREE.Vector2(0, 0))
  const lookCurrent = useRef(new THREE.Vector2(0, 0))
  const lookVel = useRef(new THREE.Vector2(0, 0))
  const tiltTarget = useRef(0)
  const tiltCurrent = useRef(0)
  const tiltVel = useRef(0)
  const yawTarget = useRef(0)
  const yawCurrent = useRef(0)
  const yawVel = useRef(0)
  const scanStartsRef = useRef<number[]>([])

  useEffect(() => {
    const canvas = ref.current
    const parent = canvas?.parentElement
    if (!canvas || !parent) return undefined

    const maxScans = 8
    const sensitivity = 0.55
    const skewScale = THREE.MathUtils.lerp(0.06, 0.2, sensitivity)
    const tiltScale = THREE.MathUtils.lerp(0.12, 0.3, sensitivity)
    const yawScale = THREE.MathUtils.lerp(0.1, 0.28, sensitivity)
    const smoothTime = THREE.MathUtils.lerp(0.45, 0.12, sensitivity)
    const yBoost = THREE.MathUtils.lerp(1.2, 1.6, sensitivity)
    const maxSpeed = Infinity

    const renderer = new THREE.WebGLRenderer({
      alpha: true,
      antialias: true,
      canvas,
      powerPreference: 'high-performance',
    })
    renderer.setClearColor(0x000000, 0)
    renderer.outputColorSpace = THREE.SRGBColorSpace
    renderer.toneMapping = THREE.NoToneMapping
    renderer.autoClear = false

    const scene = new THREE.Scene()
    const camera = new THREE.OrthographicCamera(-1, 1, 1, -1, 0, 1)
    const geometry = new THREE.PlaneGeometry(2, 2)
    const uniforms = {
      iResolution: { value: new THREE.Vector3(1, 1, 1) },
      iTime: { value: 0 },
      uSkew: { value: new THREE.Vector2(0, 0) },
      uTilt: { value: 0 },
      uYaw: { value: 0 },
      uLineThickness: { value: 1.0 },
      uLinesColor: { value: srgbColor('#2f6b55') },
      uScanColor: { value: srgbColor('#b8ffe4') },
      uGridScale: { value: 0.105 },
      uLineStyle: { value: 0 },
      uLineJitter: { value: 0.075 },
      uScanOpacity: { value: 0.58 },
      uScanDirection: { value: 2 },
      uNoise: { value: 0.008 },
      uBloomOpacity: { value: 0.22 },
      uScanGlow: { value: 0.7 },
      uScanSoftness: { value: 1.85 },
      uPhaseTaper: { value: 0.49 },
      uScanDuration: { value: 2.45 },
      uScanDelay: { value: 1.3 },
      uOpacity: { value: opacity },
      uScanStarts: { value: new Array(maxScans).fill(0) },
      uScanCount: { value: 0 },
    }
    const material = new THREE.ShaderMaterial({
      vertexShader: fullScreenVertex,
      fragmentShader: gridScanFragment,
      uniforms,
      transparent: true,
      depthWrite: false,
      depthTest: false,
    })
    const mesh = new THREE.Mesh(geometry, material)
    scene.add(mesh)

    const composer = new EffectComposer(renderer)
    composer.addPass(new RenderPass(scene, camera))
    const bloom = new BloomEffect({
      intensity: 1,
      luminanceThreshold: 0.08,
      luminanceSmoothing: 0.72,
    })
    bloom.blendMode.opacity.value = 0.2
    const chroma = new ChromaticAberrationEffect({
      offset: new THREE.Vector2(0.00055, 0.00055),
      radialModulation: true,
      modulationOffset: 0,
    })
    const effectPass = new EffectPass(camera, bloom, chroma)
    effectPass.renderToScreen = true
    composer.addPass(effectPass)

    const resize = () => {
      const rect = parent.getBoundingClientRect()
      const width = Math.max(1, Math.round(rect.width))
      const height = Math.max(1, Math.round(rect.height))
      const pixelRatio = Math.min(window.devicePixelRatio || 1, 1.75) * resolutionScale
      renderer.setPixelRatio(pixelRatio)
      renderer.setSize(width, height, false)
      composer.setSize(width, height)
      uniforms.iResolution.value.set(width, height, pixelRatio)
    }

    const pushScan = (time: number) => {
      const scans = scanStartsRef.current.slice()
      if (scans.length >= maxScans) scans.shift()
      scans.push(time)
      scanStartsRef.current = scans
      const buffer = new Array(maxScans).fill(0)
      scans.forEach((scan, index) => {
        buffer[index] = scan
      })
      uniforms.uScanStarts.value = buffer
      uniforms.uScanCount.value = scans.length
    }

    const onPointerMove = (event: MouseEvent) => {
      const rect = parent.getBoundingClientRect()
      const nx = ((event.clientX - rect.left) / Math.max(rect.width, 1)) * 2 - 1
      const ny = -(((event.clientY - rect.top) / Math.max(rect.height, 1)) * 2 - 1)
      lookTarget.current.set(nx, ny)
      tiltTarget.current = ny * 0.18
      yawTarget.current = nx
    }

    const onPointerLeave = () => {
      lookTarget.current.set(0, 0)
      tiltTarget.current = 0
      yawTarget.current = 0
    }
    const onPointerDown = () => pushScan(performance.now() / 1000)
    parent.addEventListener('mousemove', onPointerMove, { passive: true })
    parent.addEventListener('mouseleave', onPointerLeave)
    parent.addEventListener('pointerdown', onPointerDown, { passive: true })

    const observer = new ResizeObserver(resize)
    observer.observe(parent)
    resize()

    const start = performance.now()
    let last = start
    let frame = 0
    const loop = () => {
      const now = performance.now()
      const dt = Math.max(0, Math.min(0.1, (now - last) / 1000))
      last = now
      const elapsed = ((now - start) / 1000) * speed
      lookCurrent.current.copy(
        smoothDampVec2(lookCurrent.current, lookTarget.current, lookVel.current, smoothTime, maxSpeed, dt)
      )
      const tilt = smoothDampFloat(tiltCurrent.current, tiltTarget.current, { v: tiltVel.current }, smoothTime, maxSpeed, dt)
      tiltCurrent.current = tilt.value
      tiltVel.current = tilt.v
      const yaw = smoothDampFloat(yawCurrent.current, yawTarget.current, { v: yawVel.current }, smoothTime, maxSpeed, dt)
      yawCurrent.current = yaw.value
      yawVel.current = yaw.v

      const skew = new THREE.Vector2(lookCurrent.current.x * skewScale, -lookCurrent.current.y * yBoost * skewScale)
      uniforms.iTime.value = elapsed
      uniforms.uSkew.value.set(skew.x, skew.y)
      uniforms.uTilt.value = tiltCurrent.current * tiltScale + Math.sin(elapsed * 0.18) * 0.025
      uniforms.uYaw.value = THREE.MathUtils.clamp(yawCurrent.current * yawScale, -0.6, 0.6) + Math.cos(elapsed * 0.14) * 0.025
      uniforms.uOpacity.value = opacity
      renderer.clear(true, true, true)
      composer.render(dt)
      frame = window.requestAnimationFrame(loop)
    }
    loop()

    return () => {
      window.cancelAnimationFrame(frame)
      observer.disconnect()
      parent.removeEventListener('mousemove', onPointerMove)
      parent.removeEventListener('mouseleave', onPointerLeave)
      parent.removeEventListener('pointerdown', onPointerDown)
      geometry.dispose()
      material.dispose()
      composer.dispose()
      renderer.dispose()
      renderer.forceContextLoss()
    }
  }, [opacity, resolutionScale, speed])

  return <canvas ref={ref} className={cx('yy-react-bits-canvas yy-react-grid-scan', className)} aria-hidden="true" />
}

function srgbColor(hex: string) {
  const color = new THREE.Color(hex)
  return color.convertSRGBToLinear()
}

function srgbVector(hex: string) {
  const color = srgbColor(hex)
  return new THREE.Vector3(color.r, color.g, color.b)
}

function smoothDampVec2(
  current: THREE.Vector2,
  target: THREE.Vector2,
  currentVelocity: THREE.Vector2,
  smoothTime: number,
  maxSpeed: number,
  deltaTime: number
) {
  const out = current.clone()
  smoothTime = Math.max(0.0001, smoothTime)
  const omega = 2 / smoothTime
  const x = omega * deltaTime
  const exp = 1 / (1 + x + 0.48 * x * x + 0.235 * x * x * x)

  let change = current.clone().sub(target)
  const originalTo = target.clone()
  const maxChange = maxSpeed * smoothTime
  if (change.length() > maxChange) change.setLength(maxChange)

  target = current.clone().sub(change)
  const temp = currentVelocity.clone().addScaledVector(change, omega).multiplyScalar(deltaTime)
  currentVelocity.sub(temp.clone().multiplyScalar(omega))
  currentVelocity.multiplyScalar(exp)
  out.copy(target.clone().add(change.add(temp).multiplyScalar(exp)))

  const origMinusCurrent = originalTo.clone().sub(current)
  const outMinusOrig = out.clone().sub(originalTo)
  if (origMinusCurrent.dot(outMinusOrig) > 0) {
    out.copy(originalTo)
    currentVelocity.set(0, 0)
  }
  return out
}

function smoothDampFloat(
  current: number,
  target: number,
  velRef: { v: number },
  smoothTime: number,
  maxSpeed: number,
  deltaTime: number
) {
  smoothTime = Math.max(0.0001, smoothTime)
  const omega = 2 / smoothTime
  const x = omega * deltaTime
  const exp = 1 / (1 + x + 0.48 * x * x + 0.235 * x * x * x)

  let change = current - target
  const originalTo = target
  const maxChange = maxSpeed * smoothTime
  change = Math.sign(change) * Math.min(Math.abs(change), maxChange)

  target = current - change
  const temp = (velRef.v + omega * change) * deltaTime
  velRef.v = (velRef.v - omega * temp) * exp
  let out = target + (change + temp) * exp

  const origMinusCurrent = originalTo - current
  const outMinusOrig = out - originalTo
  if (origMinusCurrent * outMinusOrig > 0) {
    out = originalTo
    velRef.v = 0
  }
  return { value: out, v: velRef.v }
}

export function useYinyuMagicBento<T extends HTMLElement>() {
  const ref = useRef<T>(null)
  const frameRef = useRef<number | null>(null)

  const setCenter = useCallback(() => {
    const element = ref.current
    if (!element) return
    element.style.setProperty('--yy-bento-x', '50%')
    element.style.setProperty('--yy-bento-y', '46%')
    element.style.setProperty('--yy-bento-rotate-x', '0deg')
    element.style.setProperty('--yy-bento-rotate-y', '0deg')
    element.style.setProperty('--yy-bento-active', '0')
  }, [])

  const onPointerMove = useCallback((event: PointerEvent<T>) => {
    const element = ref.current
    if (!element) return

    if (frameRef.current !== null) {
      window.cancelAnimationFrame(frameRef.current)
    }

    frameRef.current = window.requestAnimationFrame(() => {
      const rect = element.getBoundingClientRect()
      const x = event.clientX - rect.left
      const y = event.clientY - rect.top
      const px = Math.min(100, Math.max(0, (x / rect.width) * 100))
      const py = Math.min(100, Math.max(0, (y / rect.height) * 100))
      const rotateY = ((px - 50) / 50) * 4.2
      const rotateX = ((50 - py) / 50) * 3.4

      element.style.setProperty('--yy-bento-x', `${px}%`)
      element.style.setProperty('--yy-bento-y', `${py}%`)
      element.style.setProperty('--yy-bento-rotate-x', `${rotateX.toFixed(2)}deg`)
      element.style.setProperty('--yy-bento-rotate-y', `${rotateY.toFixed(2)}deg`)
      element.style.setProperty('--yy-bento-active', '1')
      frameRef.current = null
    })
  }, [])

  const onPointerLeave = useCallback(() => {
    if (frameRef.current !== null) {
      window.cancelAnimationFrame(frameRef.current)
      frameRef.current = null
    }
    setCenter()
  }, [setCenter])

  useEffect(() => {
    setCenter()
    return () => {
      if (frameRef.current !== null) {
        window.cancelAnimationFrame(frameRef.current)
      }
    }
  }, [setCenter])

  return {
    ref,
    onPointerMove,
    onPointerLeave,
    onPointerEnter: onPointerMove,
  }
}
