import { BlendFunction, Effect } from 'postprocessing'
import * as THREE from 'three'

const getWindowSize = () =>
  typeof window === 'undefined'
    ? new THREE.Vector2(1280, 720)
    : new THREE.Vector2(window.innerWidth, window.innerHeight)

const PixelationShader = {
  uniforms: {
    inputTexture: new THREE.Uniform(null),
    resolution: new THREE.Uniform(getWindowSize()),
    pixelSize: new THREE.Uniform(5),
    pixelGap: new THREE.Uniform(2),
    threshold: new THREE.Uniform(0.87),
    greenRatio: new THREE.Uniform(0.49),
    colorNoise: new THREE.Uniform(0.5),
    bgColor: new THREE.Uniform([0, 0, 0]),
    greenColor: new THREE.Uniform([0.196, 0.941, 0.549]),
    uTime: new THREE.Uniform(0),
    uMouse: new THREE.Uniform(new THREE.Vector2(0.5, 0.5)),
    uMouseRadius: new THREE.Uniform(0.1),
    uMouseStrength: new THREE.Uniform(1),
    uColor1: new THREE.Uniform(new THREE.Color(0.196, 0.941, 0.549)),
    uColor2: new THREE.Uniform(new THREE.Color(1, 1, 1)),
  },
  fragmentShader: /* glsl */ `
    uniform sampler2D inputTexture;
    uniform vec2 resolution;
    uniform float pixelSize;
    uniform float pixelGap;
    uniform float threshold;
    uniform float greenRatio;
    uniform vec3 bgColor;
    uniform float uTime;
    uniform vec2 uMouse;
    uniform float uMouseRadius;
    uniform float uMouseStrength;
    uniform vec3 uColor1;
    uniform vec3 uColor2;

    float random(vec2 st) {
      return fract(sin(dot(st.xy, vec2(12.9898, 78.233))) * 43758.5453123);
    }

    vec2 hash(vec2 p) {
      p = vec2(dot(p, vec2(127.1, 311.7)), dot(p, vec2(269.5, 183.3)));
      return -1.0 + 2.0 * fract(sin(p) * 43758.5453123);
    }

    float noise(vec2 p) {
      const float K1 = 0.366025404;
      const float K2 = 0.211324865;
      vec2 i = floor(p + (p.x + p.y) * K1);
      vec2 a = p - i + (i.x + i.y) * K2;
      vec2 o = (a.x > a.y) ? vec2(1.0, 0.0) : vec2(0.0, 1.0);
      vec2 b = a - o + K2;
      vec2 c = a - 1.0 + 2.0 * K2;
      vec3 h = max(0.5 - vec3(dot(a,a), dot(b,b), dot(c,c)), 0.0);
      vec3 n = h*h*h*h * vec3(dot(a, hash(i+0.0)), dot(b, hash(i+o)), dot(c, hash(i+1.0)));
      return dot(n, vec3(70.0));
    }

    float fbm(vec2 p) {
      float value = 0.0;
      float amplitude = 0.5;
      float frequency = 1.0;
      for (int i = 0; i < 3; i++) {
        value += amplitude * noise(p * frequency);
        frequency *= 2.0;
        amplitude *= 0.5;
      }
      return value * 0.5 + 0.5;
    }

    vec2 computeFluidFlow(vec2 uv, float time, float speed, float density, float frequency) {
      vec2 q = vec2(
        fbm(uv * density + vec2(0.0, 0.2 * time * speed)),
        fbm(uv * density + vec2(1.2, -0.3 * time * speed))
      );
      vec2 flowVector = vec2(q.x * 0.3, q.y * 0.7) * 2.0;
      flowVector.x *= 0.5;
      flowVector.y *= 1.5;
      return flowVector;
    }

    float sdHexagon(vec2 p, float r) {
      const vec3 k = vec3(-0.866025404, 0.5, 0.577350269);
      p = abs(p);
      p -= 2.0 * min(dot(k.xy, p), 0.0) * k.xy;
      p -= vec2(clamp(p.x, -k.z * r, k.z * r), r);
      return length(p) * sign(p.y);
    }

    void nearestHexCell(vec2 pixelCoord, out vec2 cellId, out vec2 cellCenter) {
      float totalSize = pixelSize + pixelGap;
      float stepX = totalSize * 1.26;
      float stepY = totalSize * 1.08;
      vec2 base = vec2(floor(pixelCoord.x / stepX), floor(pixelCoord.y / stepY));

      float bestDist = 1.0e20;
      vec2 bestId = base;
      vec2 bestCenter = vec2(0.0);

      for (int xi = -1; xi <= 1; xi++) {
        for (int yi = -1; yi <= 1; yi++) {
          vec2 id = base + vec2(float(xi), float(yi));
          float rowShift = mod(id.y, 2.0) * stepX * 0.5;
          vec2 center = vec2((id.x + 0.5) * stepX + rowShift, (id.y + 0.5) * stepY);
          float distToCenter = dot(pixelCoord - center, pixelCoord - center);
          if (distToCenter < bestDist) {
            bestDist = distToCenter;
            bestId = id;
            bestCenter = center;
          }
        }
      }

      cellId = bestId;
      cellCenter = bestCenter;
    }

    void mainImage(const in vec4 inputColor, const in vec2 uv, out vec4 outputColor) {
      float time = uTime;
      float speed = 0.15;
      float density = 1.5;
      float frequency = 2.5;

      vec2 pixelCoord = uv * resolution;
      vec2 blockId;
      vec2 blockCenter;
      nearestHexCell(pixelCoord, blockId, blockCenter);
      vec2 blockCenterUV = blockCenter / resolution;

      vec2 flow = computeFluidFlow(blockCenterUV, time, speed, density, frequency) * 35.0;
      vec2 posInHex = pixelCoord - blockCenter;
      float hexRadius = max(2.0, pixelSize * 0.78);
      float edgeDistance = sdHexagon(posInHex, hexRadius);

      if (edgeDistance > 0.0) {
        outputColor = vec4(bgColor, 1.0);
        return;
      }

      vec2 blockUv = (blockCenter - flow) / resolution;
      vec4 color = texture2D(inputTexture, blockUv);
      float brightness = (color.r + color.g + color.b) / 3.0;
      float rand = random(blockId);
      float dynamicThreshold = threshold - 0.08 * rand;

      float dist = distance(blockCenterUV, uMouse);
      float mouseFactor = (1.0 - smoothstep(0.0, uMouseRadius, dist)) * uMouseStrength;

      if (brightness > dynamicThreshold) {
        if (rand < greenRatio) {
          vec3 exactGreen = vec3(50.0/255.0, 240.0/255.0, 140.0/255.0);
          outputColor = vec4(mouseFactor > 0.0 ? mix(exactGreen, uColor2, mouseFactor) : exactGreen, 1.0);
        } else {
          outputColor = vec4(mouseFactor > 0.0 ? mix(vec3(1.0), uColor1, mouseFactor) : vec3(1.0), 1.0);
        }
      } else {
        outputColor = vec4(bgColor, 1.0);
      }
    }
  `,
}

interface PixelationEffectOptions {
  pixelSize?: number
  pixelGap?: number
  threshold?: number
  greenRatio?: number
  colorNoise?: number
  bgColor?: number[]
  greenColor?: number[]
  resolution?: THREE.Vector2
  blendFunction?: BlendFunction
  mousePosition?: THREE.Vector2
  mouseRadius?: number
  mouseStrength?: number
  color1?: number[]
  color2?: number[]
}

export class PixelationEffect extends Effect {
  private currentTime = 0
  private frameCounter = 0
  private lastUpdate = 0

  constructor({
    pixelSize = 5,
    pixelGap = 2,
    threshold = 0.87,
    greenRatio = 0.49,
    colorNoise = 0.5,
    bgColor = [0, 0, 0],
    greenColor = [0.196, 0.941, 0.549],
    resolution = getWindowSize(),
    blendFunction = BlendFunction.NORMAL,
    mousePosition = new THREE.Vector2(0.5, 0.5),
    mouseRadius = 0.1,
    mouseStrength = 1,
    color1 = [0.196, 0.941, 0.549],
    color2 = [1, 1, 1],
  }: PixelationEffectOptions = {}) {
    super('PixelationEffect', PixelationShader.fragmentShader, {
      blendFunction,
      uniforms: new Map<string, THREE.Uniform>([
        ['inputTexture', new THREE.Uniform(null)],
        ['resolution', new THREE.Uniform(resolution)],
        ['pixelSize', new THREE.Uniform(pixelSize)],
        ['pixelGap', new THREE.Uniform(pixelGap)],
        ['threshold', new THREE.Uniform(threshold)],
        ['greenRatio', new THREE.Uniform(greenRatio)],
        ['colorNoise', new THREE.Uniform(colorNoise)],
        ['bgColor', new THREE.Uniform(bgColor)],
        ['greenColor', new THREE.Uniform(greenColor)],
        ['uTime', new THREE.Uniform(0)],
        ['uMouse', new THREE.Uniform(mousePosition)],
        ['uMouseRadius', new THREE.Uniform(mouseRadius)],
        ['uMouseStrength', new THREE.Uniform(mouseStrength)],
        ['uColor1', new THREE.Uniform(new THREE.Color(...(color1 as [number, number, number])))],
        ['uColor2', new THREE.Uniform(new THREE.Color(...(color2 as [number, number, number])))],
      ]),
    })
  }

  update(_renderer: THREE.WebGLRenderer, inputBuffer: THREE.WebGLRenderTarget, deltaTime: number) {
    this.frameCounter += 1
    if (this.frameCounter % 2 === 0) {
      this.currentTime += deltaTime
      const timeUniform = this.uniforms.get('uTime')
      if (timeUniform) timeUniform.value = this.currentTime
    }

    const now = Date.now()
    if (now - this.lastUpdate > 200) {
      const tex = this.uniforms.get('inputTexture')
      if (tex) tex.value = inputBuffer.texture
      this.lastUpdate = now
    }
  }

  setMousePosition(x: number, y: number) {
    const u = this.uniforms.get('uMouse')
    if (u) u.value.set(x, y)
  }

  setMouseParams(radius: number, strength: number) {
    const r = this.uniforms.get('uMouseRadius')
    const s = this.uniforms.get('uMouseStrength')
    if (r) r.value = radius
    if (s) s.value = strength
  }

  setColors(c1: number[], c2: number[]) {
    const u1 = this.uniforms.get('uColor1')
    const u2 = this.uniforms.get('uColor2')
    if (u1) u1.value = new THREE.Color(...(c1 as [number, number, number]))
    if (u2) u2.value = new THREE.Color(...(c2 as [number, number, number]))
  }

  setResolution(w: number, h: number) {
    const r = this.uniforms.get('resolution')
    if (r) r.value.set(w, h)
  }
}
