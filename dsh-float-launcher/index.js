/**
 * dsh-float-launcher —— 宿主半边。
 *
 * 只做一件事：POST /dsh-float/api/launch
 *   - 已有 DshFloat 在跑 → POST /show，把独立置顶窗唤到前台
 *   - 没有在跑 → spawn DshFloat.exe（detached，不阻塞 dsh）
 *
 * 安全边界：
 *   - 只接受本机回环请求
 *   - 只接受 POST + x-dsh-float-launch: 1（跨站表单无法带自定义头）
 *   - 走 dsh 的 connection.requestRejection 信任栅栏，栅栏不可用则 fail-closed
 *   - spawn 不经过 shell（不启用 shell 模式）
 */
import fs from 'node:fs'
import os from 'node:os'
import path from 'node:path'
import http from 'node:http'
import { spawn } from 'node:child_process'
import { fileURLToPath } from 'node:url'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const LAUNCH_PATH = '/dsh-float/api/launch'
const DEFAULT_PORTS = Array.from({ length: 12 }, (_, i) => 3083 + i)
const DEFAULT_EXE = path.resolve(HERE, '..', 'tools', 'dsh-float', 'dist', 'DshFloat.exe')

let inFlight = null

function json(res, code, obj) {
  res.writeHead(code, {
    'Content-Type': 'application/json; charset=utf-8',
    'Cache-Control': 'no-store',
  })
  res.end(JSON.stringify(obj))
}

function isLoopbackReq(req) {
  const remote = String(req.socket?.remoteAddress ?? '').toLowerCase()
  const remoteOk = remote === '127.0.0.1' || remote === '::1' || remote === '::ffff:127.0.0.1'
  const host = String(req.headers?.host ?? '').toLowerCase()
  const hostOk =
    host === '127.0.0.1' || host.startsWith('127.0.0.1:') ||
    host === 'localhost' || host.startsWith('localhost:') ||
    host === '[::1]' || host.startsWith('[::1]:') || host === '::1'
  return remoteOk && hostOk
}

function isLoopbackOrigin(value) {
  if (!value) return true
  try {
    const host = new URL(String(value)).hostname.toLowerCase()
    return host === '127.0.0.1' || host === 'localhost' || host === '[::1]' || host === '::1'
  } catch {
    return false
  }
}

function readConfig() {
  try {
    return JSON.parse(fs.readFileSync(path.join(HERE, 'config.json'), 'utf8')) || {}
  } catch {
    return {}
  }
}

function exeCandidates(cfg) {
  const localAppData = process.env.LOCALAPPDATA || path.join(os.homedir(), 'AppData', 'Local')
  const out = [
    process.env.DSH_FLOAT_EXE,
    cfg.exePath,
    path.join(HERE, 'bin', 'DshFloat.exe'),
    DEFAULT_EXE,
    path.join(localAppData, 'Programs', 'DshFloat', 'DshFloat.exe'),
    path.join(localAppData, 'DshFloat', 'DshFloat.exe'),
    path.join(os.homedir(), 'DshFloat', 'DshFloat.exe'),
  ]
  return out.filter((v, i) => typeof v === 'string' && v.trim() && out.indexOf(v) === i)
}

function resolveExe(cfg = readConfig()) {
  for (const candidate of exeCandidates(cfg)) {
    try {
      if (fs.existsSync(candidate) && fs.statSync(candidate).isFile()) return candidate
    } catch { /* continue */ }
  }
  return null
}

function portsOf(cfg = readConfig()) {
  const raw = Array.isArray(cfg.ports) ? cfg.ports : []
  const list = raw.map(Number).filter((n) => Number.isInteger(n) && n > 0 && n < 65536)
  return Array.from(new Set([...list, ...DEFAULT_PORTS]))
}

/** 极简 HTTP JSON，仅用于本机回环的 3083+ 桥接服务。 */
function requestJson(port, pathname, method = 'GET', timeoutMs = 600, headers = {}) {
  return new Promise((resolve, reject) => {
    const req = http.request({
      host: '127.0.0.1',
      port,
      path: pathname,
      method,
      timeout: timeoutMs,
      headers: { ...headers },
    }, (res) => {
      let text = ''
      res.setEncoding('utf8')
      res.on('data', (chunk) => { text += chunk })
      res.on('end', () => {
        let body = null
        try { body = text ? JSON.parse(text) : null } catch { /* keep null */ }
        resolve({ status: res.statusCode, body, text })
      })
    })
    req.on('timeout', () => req.destroy(new Error('timeout')))
    req.on('error', reject)
    if (method === 'POST') req.end('{}')
    else req.end()
  })
}

async function probeBridge(port) {
  try {
    const res = await requestJson(port, '/health', 'GET', 600)
    if (res.status !== 200 || !res.body || typeof res.body !== 'object') return null
    return { port, health: res.body }
  } catch {
    return null
  }
}

async function findBridge(ports = portsOf()) {
  const hits = await Promise.all(ports.map((port) => probeBridge(port)))
  return hits.find(Boolean) || null
}

async function showBridge(port) {
  const res = await requestJson(port, '/show', 'POST', 1200, {
    'Content-Type': 'application/json; charset=utf-8',
    'x-dsh-float-launch': '1',
  })
  if (res.status !== 200 || !res.body || res.body.ok !== true) {
    const detail = res.body?.error || res.body?.message || `HTTP ${res.status}`
    throw new Error(`DshFloat 在运行，但唤起失败：${detail}`)
  }
  return res.body
}

async function doLaunch() {
  const cfg = readConfig()
  const bridge = await findBridge(portsOf(cfg))
  if (bridge) {
    await showBridge(bridge.port)
    return { action: 'shown', port: bridge.port }
  }

  const exe = resolveExe(cfg)
  if (!exe) throw new Error('找不到 DshFloat.exe；请在插件目录 config.json 里设置 exePath，或设置 DSH_FLOAT_EXE。')

  const child = spawn(exe, [], {
    detached: true,
    stdio: 'ignore',
    windowsHide: false,
  })
  child.once('error', (err) => {
    console.error('[dsh-float-launcher] spawn failed:', err?.message ?? err)
  })
  child.unref()
  return { action: 'launched', exe: path.basename(exe) }
}

function launch() {
  if (!inFlight) {
    inFlight = doLaunch().finally(() => { inFlight = null })
  }
  return inFlight
}

export default {
  name: 'dsh-float-launcher',
  inject: ['webServer', 'connection'],
  apply(ctx) {
    function rejected(req, res) {
      if (!isLoopbackReq(req)) {
        json(res, 403, { ok: false, error: '只接受本机回环访问' })
        return true
      }
      try {
        const conn = ctx.get('connection') || ctx.connection
        if (!conn || typeof conn.requestRejection !== 'function') {
          console.warn('[dsh-float-launcher] connection 信任栅栏缺失，拒绝请求')
          json(res, 503, { ok: false, error: '信任栅栏不可用，拒绝访问' })
          return true
        }
        const code = conn.requestRejection(req)
        if (code === undefined || code === null || code === false) return false
        res.statusCode = typeof code === 'number' ? code : 403
        res.end()
        return true
      } catch {
        json(res, 503, { ok: false, error: '信任栅栏异常，拒绝访问' })
        return true
      }
    }

    const disposers = []
    disposers.push(ctx.webServer.register({
      kind: 'exact',
      path: LAUNCH_PATH,
      handler: async (req, res) => {
        if (rejected(req, res)) return
        if ((req.method ?? 'GET') !== 'POST') {
          json(res, 405, { ok: false, error: 'POST only' })
          return
        }
        if (String(req.headers['x-dsh-float-launch'] ?? '') !== '1') {
          json(res, 400, { ok: false, error: 'missing launch header' })
          return
        }
        if (!isLoopbackOrigin(req.headers.origin)) {
          json(res, 403, { ok: false, error: 'origin not allowed' })
          return
        }
        try {
          const result = await launch()
          json(res, 200, { ok: true, ...result })
        } catch (err) {
          json(res, 502, { ok: false, error: String(err?.message ?? err) })
        }
      },
    }))

    ctx.effect(() => () => {
      for (const dispose of disposers) {
        try { dispose() } catch { /* ignore */ }
      }
    })
  },
}

// 只给回归测试用：不启动 dsh 也能验证路径解析和桥接探测。
export const __test = { resolveExe, portsOf, findBridge, showBridge, launch }
