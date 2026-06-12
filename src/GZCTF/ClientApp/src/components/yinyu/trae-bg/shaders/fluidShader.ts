import * as THREE from 'three'

export const FluidShader = {
  uniforms: {
    uTime: { value: 0 },
    uResolution: { value: new THREE.Vector2(window.innerWidth, window.innerHeight) },
    uSpeed: { value: 0.15 },
    uDensity: { value: 0.56 },
    uStrength: { value: 4.3 },
    uFrequency: { value: 8 },
    uColor1: { value: new THREE.Color(0.196, 0.941, 0.549) },
    uColor2: { value: new THREE.Color(1, 1, 1) },
    uMouse: { value: new THREE.Vector2(0.5, 0.5) },
    uMouseRadius: { value: 0.3 },
    uMouseStrength: { value: 1.3 },
  },
  vertexShader: /* glsl */ `
    varying vec2 vUv;
    void main() {
      vUv = uv;
      gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0);
    }
  `,
  fragmentShader: /* glsl */ `
    uniform float uTime;
    uniform float uSpeed;
    uniform float uDensity;
    uniform float uFrequency;
    uniform vec3 uColor1;
    uniform vec3 uColor2;
    varying vec2 vUv;

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

    void main() {
      vec2 st = vUv;
      float t = uTime * uSpeed;
      vec2 q = vec2(
        fbm(st * uDensity + vec2(0.0, 0.2 * t)),
        fbm(st * uDensity + vec2(1.2, -0.3 * t))
      );
      float noiseVal = fbm(st * uDensity + q * uFrequency);
      vec3 color = mix(uColor1, uColor2, noiseVal);
      gl_FragColor = vec4(color, 1.0);
    }
  `,
}
