/**
 * dsh-float-launcher —— 浏览器半边。
 *
 * 在 conversation.session.header.actions 注册一个紧凑 chip：
 *   id: dsh-float.launcher / order: -9
 * order -9 只是靠左排序；若用户同时启用了 order 更小的 header 插件
 * （例如「梁神模式」，注意它是第三方插件，不是 dsh 原生功能），
 * 这个 chip 会排在它右侧；未启用时也不依赖它。
 * 同时排在 subagent-catalog(10)、job-list(20) 前面。
 *
 * 只在本机回环页面或桌面版 dsh-app 页面显示；远程/手机页面不提供“启动本机 exe”的入口。
 */
window.__ModuleLoader__.load({
  id: 'dsh-float-launcher',
  factory: (require) => {
    const React = require('react')
    const h = React.createElement

    const CSS = `
      .dsh-float-launcher{
        appearance:none;cursor:pointer;display:inline-flex;align-items:center;gap:6px;
        height:22px;padding:0 8px 0 7px;border-radius:7px;flex:none;white-space:nowrap;
        border:1px solid var(--dsw-alias-border-l2,rgba(217,182,101,.48));
        background:linear-gradient(180deg,var(--dsw-alias-bg-layer-2,rgba(25,40,67,.96)),rgba(9,17,31,.96));
        color:var(--dsw-alias-label-primary,#f1e7d3);
        font:11px/1 "Segoe UI","Microsoft YaHei",system-ui,sans-serif;
        box-shadow:0 4px 14px rgba(0,0,0,.28),inset 0 1px 0 rgba(255,255,255,.06);
        transition:border-color .15s,color .15s,transform .15s,opacity .15s;
      }
      .dsh-float-launcher:hover{
        border-color:var(--dsw-alias-border-l3,rgba(241,223,178,.78));
        color:var(--dsw-alias-label-primary,#fff8e8);
        transform:translateY(-1px);
      }
      .dsh-float-launcher:active{transform:translateY(0)}
      .dsh-float-launcher:focus-visible{outline:2px solid rgba(217,182,101,.55);outline-offset:2px}
      .dsh-float-launcher[data-phase="busy"]{cursor:wait;opacity:.72}
      .dsh-float-launcher[data-phase="error"]{
        border-color:rgba(228,122,122,.55);color:#f3d1d1;
      }
      .dsh-float-launcher svg{width:12px;height:12px;flex:none;opacity:.95}
    `

    function ensureCss() {
      if (typeof document === 'undefined') return () => {}
      const existing = document.getElementById('dsh-float-launcher-css')
      if (existing) return () => {}
      const style = document.createElement('style')
      style.id = 'dsh-float-launcher-css'
      style.textContent = CSS
      document.head.appendChild(style)
      return () => {
        try { style.remove() } catch { /* ignore */ }
      }
    }

    function FloatIcon() {
      return h('svg', {
        viewBox: '0 0 24 24',
        fill: 'none',
        stroke: 'currentColor',
        strokeWidth: 1.6,
        strokeLinecap: 'round',
        strokeLinejoin: 'round',
        'aria-hidden': 'true',
      },
      h('rect', { x: 3.5, y: 6.5, width: 12, height: 14, rx: 2 }),
      h('rect', { x: 8.5, y: 3, width: 12, height: 14, rx: 2, opacity: 0.55 }))
    }

    function isLocalHost() {
      if (typeof window === 'undefined') return false
      const protocol = String(window.location.protocol || '').toLowerCase()
      const host = String(window.location.hostname || '').toLowerCase()
      // 桌面版 dsh 的页面来源是 dsh-app://app；它的自定义协议会把
      // 相对 fetch 转发给本机 Host，因此这里也算本机页面。
      if (protocol === 'dsh-app:' && host === 'app') return true
      return host === '127.0.0.1' || host === 'localhost' || host === '[::1]' || host === '::1'
    }

    const LABELS = {
      idle: '浮窗',
      busy: '启动中',
      launched: '已启动',
      shown: '已唤起',
      error: '重试',
    }

    function FloatLauncher() {
      const [phase, setPhase] = React.useState('idle')
      const [message, setMessage] = React.useState('打开 dsh 浮窗')
      const timer = React.useRef(0)

      React.useEffect(() => () => {
        if (timer.current) clearTimeout(timer.current)
      }, [])

      if (!isLocalHost()) return null

      function resetSoon(nextPhase) {
        setPhase(nextPhase)
        if (timer.current) clearTimeout(timer.current)
        timer.current = setTimeout(() => {
          setPhase('idle')
          setMessage('打开 dsh 浮窗')
        }, 1800)
      }

      async function onClick() {
        if (phase === 'busy') return
        setPhase('busy')
        setMessage('正在启动 dsh 浮窗…')
        try {
          const res = await fetch('/dsh-float/api/launch', {
            method: 'POST',
            cache: 'no-store',
            credentials: 'same-origin',
            headers: { 'x-dsh-float-launch': '1' },
          })
          const data = await res.json().catch(() => null)
          if (!res.ok || !data || data.ok !== true) {
            const reason = data?.error || data?.message || `HTTP ${res.status}`
            throw new Error(String(reason))
          }
          const shown = data.action === 'shown'
          setMessage(shown ? '已把现有浮窗唤到前台' : '已启动，正在打开浮窗')
          resetSoon(shown ? 'shown' : 'launched')
        } catch (err) {
          const reason = String(err?.message ?? err)
          console.warn('[dsh-float-launcher] launch failed:', reason)
          setPhase('error')
          setMessage(`启动失败：${reason}`)
          if (timer.current) clearTimeout(timer.current)
          timer.current = setTimeout(() => {
            setPhase('idle')
            setMessage('打开 dsh 浮窗')
          }, 4200)
        }
      }

      return h('button', {
        type: 'button',
        className: 'dsh-float-launcher',
        'data-phase': phase,
        title: message,
        'aria-label': message,
        disabled: phase === 'busy',
        onClick,
      }, h(FloatIcon), h('span', null, LABELS[phase] || LABELS.idle))
    }

    return {
      inject: ['slots'],
      apply(ctx) {
        let removeCss = () => {}
        try {
          removeCss = ensureCss()
          ctx.slots.inject('conversation.session.header.actions', () => ctx.slots.register({
            name: 'conversation.session.header.actions',
            id: 'dsh-float.launcher',
            order: -9,
            label: '浮窗',
          }, FloatLauncher))
        } catch (err) {
          console.warn('[dsh-float-launcher] header action 注册失败:', err && err.message)
        }
        if (typeof ctx.effect === 'function') {
          ctx.effect(() => () => {
            try { removeCss() } catch { /* ignore */ }
          })
        }
      },
    }
  },
})
