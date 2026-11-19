// dsh-float-launcher 回归：路径解析、桥接探测、/show 调用、客户端槽位静态检查。
import assert from 'node:assert/strict'
import fs from 'node:fs'
import http from 'node:http'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { __test } from '../index.js'

const here = path.dirname(fileURLToPath(import.meta.url))
const root = path.join(here, '..')

let passed = 0
let failed = 0
async function check(name, fn) {
  try {
    await fn()
    console.log(`PASS  ${name}`)
    passed += 1
  } catch (err) {
    console.error(`FAIL  ${name}`)
    console.error(`      ${err?.message ?? err}`)
    failed += 1
  }
}

function startBridge(port, { showStatus = 200 } = {}) {
  const calls = []
  const server = http.createServer((req, res) => {
    calls.push({ method: req.method, url: req.url, header: req.headers['x-dsh-float-launch'] })
    if (req.url === '/health') {
      res.writeHead(200, { 'Content-Type': 'application/json' })
      res.end(JSON.stringify({ ok: true, port, dsh: 'http://127.0.0.1:3082' }))
      return
    }
    if (req.url === '/show') {
      const ok = showStatus === 200
      res.writeHead(showStatus, { 'Content-Type': 'application/json' })
      res.end(JSON.stringify(ok ? { ok: true, shown: true } : { ok: false, error: 'old version' }))
      return
    }
    res.writeHead(404)
    res.end('not found')
  })
  return new Promise((resolve, reject) => {
    server.once('error', reject)
    server.listen(port, '127.0.0.1', () => resolve({ server, calls }))
  })
}

await check('默认 exe 路径可解析', () => {
  const exe = __test.resolveExe()
  assert.ok(exe, 'resolveExe 返回空')
  assert.ok(fs.existsSync(exe), `exe 不存在: ${exe}`)
  assert.match(exe, /DshFloat\.exe$/i)
})

await check('端口列表包含默认 3083', () => {
  const ports = __test.portsOf({ ports: [3099, 3099] })
  assert.ok(ports.includes(3083))
  assert.ok(ports.includes(3099))
  assert.equal(new Set(ports).size, ports.length)
})

await check('探测桥接 /health', async () => {
  const bridge = await startBridge(32123)
  try {
    const hit = await __test.findBridge([32123, 32124])
    assert.ok(hit, '未找到桥接')
    assert.equal(hit.port, 32123)
    assert.equal(hit.health.ok, true)
    assert.equal(bridge.calls[0].url, '/health')
  } finally {
    bridge.server.close()
  }
})

await check('唤起桥接 /show', async () => {
  const bridge = await startBridge(32125)
  try {
    const result = await __test.showBridge(32125)
    assert.equal(result.ok, true)
    const showCall = bridge.calls.find((c) => c.url === '/show')
    assert.ok(showCall, '没有调用 /show')
    assert.equal(showCall.method, 'POST')
    assert.equal(showCall.header, '1')
  } finally {
    bridge.server.close()
  }
})

await check('旧版 /show 失败会报错而不是静默', async () => {
  const bridge = await startBridge(32126, { showStatus: 404 })
  try {
    await assert.rejects(() => __test.showBridge(32126), /唤起失败|404|old version/)
  } finally {
    bridge.server.close()
  }
})

await check('客户端在网页与 dsh-app 桌面端都不隐藏', () => {
  const client = fs.readFileSync(path.join(root, 'client.js'), 'utf8')
  assert.match(client, /conversation\.session\.header\.actions/)
  assert.match(client, /order:\s*-9/)
  assert.match(client, /id:\s*'dsh-float\.launcher'/)
  assert.match(client, /x-dsh-float-launch/)
  assert.match(client, /dsh-app:/)
  assert.match(client, /host === 'app'/)
})

await check('默认 exe 路径不写死本机目录', () => {
  const host = fs.readFileSync(path.join(root, 'index.js'), 'utf8')
  assert.doesNotMatch(host, /['"]D:\\\\/)
  assert.match(host, /path\.resolve\(HERE,\s*'\.\.',\s*'tools'/)
})

await check('宿主路由与 spawn 安全约束', () => {
  const host = fs.readFileSync(path.join(root, 'index.js'), 'utf8')
  assert.match(host, /\/dsh-float\/api\/launch/)
  assert.match(host, /spawn\(exe/)
  assert.doesNotMatch(host, /shell:\s*true/)
  assert.match(host, /isLoopbackReq/)
  assert.match(host, /requestRejection/)
})

await check('package.json 的 dsh.client 配置', () => {
  const pkg = JSON.parse(fs.readFileSync(path.join(root, 'package.json'), 'utf8'))
  assert.equal(pkg.dsh.client.platform, 'web')
  assert.ok(pkg.dsh.client.immediately)
  assert.ok(pkg.dsh.client.inject.includes('@deepseek-ai/dsh-client-ui-slots'))
})

await check('engines.dsh 不会把 0.2.2-rc 误判为不兼容', () => {
  const pkg = JSON.parse(fs.readFileSync(path.join(root, 'package.json'), 'utf8'))
  assert.ok(pkg.engines?.dsh, '缺少 engines.dsh')
  assert.ok(!pkg.engines.dsh.includes('>=0.2.2'), '预发布版 0.2.2-rc 会被误判')
  assert.match(pkg.engines.dsh, />=0\.2\.1/)
})

await check('DshFloat 自检包含 /show', () => {
  const resultFile = path.join(root, '..', 'tools', 'dsh-float', 'dist', 'test-result.txt')
  if (!fs.existsSync(resultFile)) {
    console.log('      (跳过：尚未生成 DshFloat test-result.txt)')
    return
  }
  const text = fs.readFileSync(resultFile, 'utf8')
  assert.match(text, /唤起接口 \/show/)
})

console.log(`\n${passed}/${passed + failed} PASS`)
process.exit(failed ? 1 : 0)
