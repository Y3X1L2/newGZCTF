declare namespace Guacamole {
  interface Status {
    code: number
    message: string
  }
  interface InputStream {}
  class OutputStream {
    sendEnd(): void
  }
  class WebSocketTunnel {
    constructor(url: string)
  }
  class Display {
    getElement(): HTMLElement
    getWidth(): number
    getHeight(): number
    scale(value: number): void
    onresize: (() => void) | null
  }
  class Client {
    constructor(tunnel: WebSocketTunnel)
    getDisplay(): Display
    connect(query: string): void
    disconnect(): void
    sendSize(width: number, height: number): void
    sendKeyEvent(pressed: number, keysym: number): void
    sendMouseState(state: Mouse.State, applyDisplayScale?: boolean): void
    createClipboardStream(type: string): OutputStream
    onstatechange: ((state: number) => void) | null
    onerror: ((status: Status) => void) | null
    onclipboard: ((stream: InputStream, type: string) => void) | null
  }
  class Mouse {
    constructor(element: HTMLElement)
    onEach(types: string[], listener: (event: Mouse.Event) => void): void
  }
  namespace Mouse {
    interface Event {
      state: State
      preventDefault(): void
    }
    interface State {
      x: number
      y: number
    }
    class Touchscreen extends Mouse {}
  }
  class Keyboard {
    constructor(element: HTMLElement)
    onkeydown: ((keysym: number) => boolean | void) | null
    onkeyup: ((keysym: number) => void) | null
    reset(): void
  }
  class StringReader {
    constructor(stream: InputStream)
    ontext: ((text: string) => void) | null
    onend: (() => void) | null
  }
  class StringWriter {
    constructor(stream: OutputStream)
    sendText(text: string): void
    sendEnd(): void
  }
}
export default Guacamole
