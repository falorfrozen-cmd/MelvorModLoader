namespace PhoneScreen;

/// <summary>
/// The phone page: one file with its own styles and script, nothing loaded from anywhere else (pictures come from the
/// game itself). Data is shown with textContent only (never as HTML). Opened as a local file with "?demo" it shows
/// sample data (for pictures); "?demo&amp;tab=combat&amp;seg=gear" opens a screen.
/// </summary>
public static class Page
{
    public const string Html = """
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
<meta name="theme-color" content="#110f0d">
<meta name="apple-mobile-web-app-capable" content="yes">
<meta name="mobile-web-app-capable" content="yes">
<meta name="apple-mobile-web-app-title" content="Melvor">
<title>Melvor</title>
<style>
:root{--bg:#110f0d;--surface:#1d1a16;--surface2:#28231d;--surface3:#342e26;--inset:#151210;--hair:rgba(243,234,214,.10);--line:#463e34;--gline:#9a7836;--text:#f3ead6;--dim:#b3a793;--faint:#7d7364;--gold:#ecc05b;--green:#3d9a3f;--greenT:#8fd18a;--greenBg:#16291a;--amber:#e5a336;--amberT:#f2c66d;--amberBg:#33260f;--red:#d94c3d;--redT:#ffaaa0;--redBg:#351612;--blue:#46a3cc;--cheat:#b07cf0;--cheatBg:#241a33}
*{box-sizing:border-box;-webkit-tap-highlight-color:transparent;min-width:0}
html,body{margin:0;background:var(--bg);color:var(--text);font:16px/1.4 system-ui,-apple-system,"Segoe UI",Roboto,sans-serif;overflow-x:hidden}
body{padding-bottom:calc(70px + env(safe-area-inset-bottom))}
.num{font-variant-numeric:tabular-nums}
button{font:inherit;color:inherit;cursor:pointer}
header{position:sticky;top:0;z-index:20;background:rgba(17,15,13,.94);backdrop-filter:blur(8px);-webkit-backdrop-filter:blur(8px);border-bottom:1px solid var(--hair);padding:8px 16px;padding-top:calc(8px + env(safe-area-inset-top));display:flex;align-items:center;gap:10px;min-height:58px}
.dot{width:10px;height:10px;border-radius:50%;background:var(--dim);flex:none}
.dot.ok{background:#5bc45b;box-shadow:0 0 0 3px rgba(91,196,91,.18)}.dot.stale{background:var(--amber)}.dot.off{background:var(--red)}
.who{flex:1}.who b{display:block;font-size:17px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}.who small{display:block;color:var(--dim);font-size:13px}
.pill{font-size:12px;font-weight:700;padding:4px 10px;border-radius:999px;border:1px solid var(--amber);color:var(--amberT);background:var(--amberBg);white-space:nowrap}
.iconbtn{position:relative;width:44px;height:44px;border-radius:12px;border:1px solid var(--hair);background:var(--surface);display:flex;align-items:center;justify-content:center;flex:none}
.iconbtn svg{width:22px;height:22px;fill:none;stroke:var(--dim);stroke-width:2}
.iconbtn .count{position:absolute;top:-5px;right:-5px;background:var(--red);color:#fff;font-size:11px;font-weight:700;border-radius:999px;padding:1px 6px}
main{padding:12px 16px;max-width:560px;margin:0 auto}
section[hidden]{display:none}
.banner{border-radius:12px;padding:10px 12px;margin:0 0 12px;font-weight:600;font-size:14px;display:flex;gap:10px;align-items:center}
.banner.warn{background:var(--amberBg);border:1px solid rgba(229,163,54,.55);color:var(--amberT)}
.banner.bad{background:var(--redBg);border:1px solid rgba(217,76,61,.6);color:var(--redT)}
.banner .grow{flex:1}
.card{position:relative;overflow:hidden;background:linear-gradient(180deg,rgba(236,192,91,.035),transparent 48px),var(--surface);border:1px solid var(--hair);border-radius:14px;padding:16px;margin:0 0 12px;box-shadow:0 1px 0 rgba(255,236,195,.04) inset,0 6px 16px rgba(0,0,0,.28)}
.card.accent::before,.card.combat::before,.card.ready::before{content:"";position:absolute;left:0;top:14px;bottom:14px;width:3px;border-radius:0 3px 3px 0;background:var(--gold)}
.card.combat::before{background:var(--red)}.card.ready::before{background:var(--green)}
.ctitle{display:flex;align-items:center;gap:12px}
.ctitle .t{flex:1}.ctitle .t b{display:block;font-size:17px;color:var(--gold)}.ctitle .t span{display:block;color:var(--dim);font-size:13px}
.ic{width:40px;height:40px;border-radius:10px;background:var(--inset);border:1px solid var(--hair);display:flex;align-items:center;justify-content:center;flex:none;overflow:hidden;font-weight:700;color:var(--gold);font-size:15px}
.ic img{width:100%;height:100%;object-fit:contain;animation:fade .18s ease}
.ic.s{width:32px;height:32px;border-radius:8px;font-size:12px}
@keyframes fade{from{opacity:0}to{opacity:1}}
.row{display:flex;justify-content:space-between;align-items:baseline;gap:8px;margin:10px 0 5px;font-size:14px}
.row .k{color:var(--dim)}
.bar{position:relative;height:10px;background:var(--inset);border:1px solid var(--hair);border-radius:6px;overflow:hidden}
.bar>i{display:block;height:100%;width:0;background:var(--blue);border-radius:5px;transition:width .35s cubic-bezier(.22,1,.36,1)}
.bar.live>i{transition:none}
.bar.hp>i{background:linear-gradient(90deg,#b23a30,#d4553f)}.bar.xp{height:6px}.bar.xp>i{background:var(--gold)}.bar.ok>i{background:var(--green)}.bar.thin{height:5px}
.bar .tick{position:absolute;top:-1px;bottom:-1px;width:3px;background:var(--gold);box-shadow:0 0 5px var(--gold)}
.btns{display:flex;gap:8px;margin-top:12px;flex-wrap:wrap}
.btn{flex:1;min-height:46px;padding:0 14px;border-radius:12px;border:1px solid var(--line);background:var(--surface2);font-size:15px;font-weight:700;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;transition:transform 80ms ease,filter 80ms ease}
.btn.go{border-color:#4f9a45;background:linear-gradient(#3f9c41,#2b7431);color:#fff}
.btn.gold{border-color:var(--gline);background:linear-gradient(#5a4524,#3d2f19);color:var(--gold)}
.btn.risk{border-color:var(--amber);background:var(--amberBg);color:var(--amberT)}
.btn.danger{border-color:rgba(217,76,61,.7);background:var(--redBg);color:var(--redT)}
.btn.cheat{border-color:var(--cheat);background:var(--cheatBg);color:#dcc4ff}
.btn.small{flex:none;min-height:40px;padding:0 13px;font-size:14px;border-radius:10px}
.btn:disabled{opacity:.38}
.btn:active:not(:disabled),.tile:active,.chip:active,.slot:active,.plot:active{transform:scale(.97);filter:brightness(1.1)}
.btn.busy{position:relative;color:transparent!important}
.btn.busy::after{content:"";position:absolute;left:50%;top:50%;width:16px;height:16px;margin:-8px 0 0 -8px;border-radius:50%;border:2px solid var(--dim);border-top-color:transparent;animation:spin .7s linear infinite}
@keyframes spin{to{transform:rotate(360deg)}}
.seg{display:flex;background:var(--inset);border:1px solid var(--hair);border-radius:12px;padding:3px;gap:3px;margin:0 0 12px}
.seg button{flex:1;min-height:40px;border:none;border-radius:9px;background:none;color:var(--dim);font-weight:700;font-size:14px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
.seg button.on{background:var(--surface3);color:var(--gold);box-shadow:inset 0 0 0 1px rgba(154,120,54,.6)}
.note{color:var(--dim);font-size:13px;margin-top:8px}
.badge{font-size:12px;font-weight:700;padding:3px 8px;border-radius:999px;border:1px solid var(--line);color:var(--dim);white-space:nowrap;letter-spacing:.03em}
.badge.SAFE,.badge.good{color:var(--greenT);border-color:rgba(61,154,63,.55);background:transparent}
.badge.RISKY,.badge.warn{color:var(--amberT);border-color:rgba(229,163,54,.6);background:transparent}
.badge.DEADLY,.badge.bad{color:var(--redT);border-color:rgba(217,76,61,.65);background:transparent}
.badge.on{color:var(--bg);background:var(--gold);border-color:var(--gold)}
.it{display:flex;align-items:center;gap:12px;padding:11px 0}
.list .it{border-top:1px solid var(--hair)}
.list .it:first-child{border-top:none}
.it .t{flex:1}.it .t b{display:block;font-size:15px;font-weight:600}.it .t span{display:block;color:var(--dim);font-size:13px}
.it .t span.why{color:var(--amberT)}.it .t span.bad{color:var(--redT)}.it .t span.aff{font-size:12px;color:var(--text);opacity:.8}
.rare{border:1px solid var(--hair);border-radius:12px;background:var(--inset);padding:8px 12px;margin:0 0 10px}.rare b{display:block;font-size:15px}.rare span{display:block;color:var(--dim);font-size:13px}.rare span.aff{color:var(--text)}
.it.off{opacity:.55}
.prob{display:flex;align-items:center;gap:10px;border-radius:12px;padding:12px;margin:0 0 10px;font-size:15px;font-weight:600}
.prob.warn{background:var(--amberBg);border:1px solid rgba(229,163,54,.55);color:var(--amberT)}
.prob.bad{background:var(--redBg);border:1px solid rgba(217,76,61,.6);color:var(--redT)}
.prob.info{background:var(--greenBg);border:1px solid rgba(61,154,63,.5);color:var(--greenT)}
.prob .grow{flex:1}
.calm{color:var(--faint);font-size:13px;margin:2px 0 12px}
.sum{display:flex;align-items:center;gap:12px;cursor:pointer}
.sum .t{flex:1}.sum .t b{display:block}.sum .t span{display:block;color:var(--dim);font-size:13px}
.chev{color:var(--faint);font-size:22px;line-height:1}
.search{width:100%;min-height:46px;border-radius:12px;border:1px solid var(--hair);background:var(--inset);color:var(--text);padding:0 14px;font-size:16px;margin:0 0 12px}
.search:focus{outline:none;border-color:var(--gline)}
.grid{display:grid;grid-template-columns:1fr 1fr;gap:10px;margin-bottom:12px}
.grid3{display:grid;grid-template-columns:repeat(3,1fr);gap:8px;margin-bottom:12px}
.tile{background:var(--surface);border:1px solid var(--hair);border-radius:14px;padding:10px;display:flex;gap:10px;align-items:center;text-align:left;transition:transform 80ms ease}
.tile.on{border-color:rgba(61,154,63,.7);box-shadow:inset 0 0 0 1px rgba(61,154,63,.5)}
.tile .t{flex:1}.tile .t b{display:block;font-size:14px}.tile .t span{display:block;color:var(--dim);font-size:12px}
.tile .bar{margin-top:5px}
.hero{display:flex;align-items:center;gap:12px;width:100%;text-align:left;background:linear-gradient(135deg,rgba(61,154,63,.16),rgba(29,26,22,0) 60%),var(--surface);border:1px solid rgba(61,154,63,.35);border-radius:14px;padding:12px 14px;margin:0 0 12px}
.hero .t{flex:1}.hero b{display:block;font-size:16px}.hero span{display:block;color:var(--dim);font-size:13px}
.group{color:var(--faint);font-size:12px;font-weight:700;letter-spacing:.08em;text-transform:uppercase;margin:18px 2px 8px}
.chips{display:flex;gap:8px;overflow-x:auto;padding-bottom:4px;margin-bottom:10px;scrollbar-width:none;-webkit-mask-image:linear-gradient(90deg,#000 88%,transparent);mask-image:linear-gradient(90deg,#000 88%,transparent)}
.chips::-webkit-scrollbar{display:none}
.chips.wrap{flex-wrap:wrap;overflow:visible;-webkit-mask-image:none;mask-image:none}
.chip{flex:none;min-height:40px;padding:0 14px;border-radius:999px;border:1px solid var(--hair);background:var(--surface);font-size:14px;font-weight:600;white-space:nowrap}
.chip.on{border-color:var(--gline);color:var(--gold);background:var(--surface2)}
.back{display:flex;align-items:center;gap:10px;margin-bottom:12px}
.back b{font-size:21px;flex:1}
.area{background:var(--surface);border:1px solid var(--hair);border-radius:14px;margin:0 0 10px;overflow:hidden}
.area>button{width:100%;min-height:54px;display:flex;align-items:center;gap:10px;padding:0 14px;background:none;border:none;text-align:left;font-weight:700}
.area>button .nm{flex:1}
.area .body{padding:0 14px 6px}
.big{font-size:24px;font-weight:800;color:var(--gold)}
.stat3{display:grid;grid-template-columns:repeat(3,1fr);gap:8px;margin-top:12px}
.stat3 div{background:var(--inset);border:1px solid var(--hair);border-radius:10px;padding:8px 10px}
.stat3 small{display:block;color:var(--faint);font-size:11px;font-weight:700;letter-spacing:.06em}
.stat3 b{font-size:16px}
.kit{display:flex;flex-wrap:wrap;gap:6px;margin-top:10px}
.kit span{font-size:12px;font-weight:600;color:var(--dim);background:var(--inset);border:1px solid var(--hair);border-radius:999px;padding:4px 10px}
.empty{color:var(--dim);text-align:center;padding:28px 10px}
.slot{display:flex;flex-direction:column;align-items:center;gap:5px;padding:10px 4px 9px;min-height:96px;border-radius:12px;border:1px solid var(--hair);background:var(--surface);text-align:center}
.slot.empty{border-style:dashed;background:transparent}
.slot small{color:var(--faint);font-size:11px;font-weight:700;letter-spacing:.04em;text-transform:uppercase}
.slot span{font-size:12px;font-weight:600;line-height:1.2;max-width:100%;overflow:hidden;text-overflow:ellipsis;display:-webkit-box;-webkit-line-clamp:2;-webkit-box-orient:vertical}
.slot .q{font-size:11px;color:var(--gold)}
.plot{display:flex;flex-direction:column;gap:6px;padding:12px;border-radius:12px;border:1px solid var(--hair);background:var(--surface);text-align:left}
.plot.ready{border-color:rgba(61,154,63,.7);background:linear-gradient(180deg,rgba(61,154,63,.18),transparent),var(--surface)}
.plot.empty{border-style:dashed;background:transparent}
.plot .top{display:flex;align-items:center;gap:8px}
.plot b{font-size:14px;flex:1}.plot span{font-size:12px;color:var(--dim)}
.toggle{position:relative;width:50px;height:30px;flex:none;border-radius:999px;border:1px solid var(--line);background:var(--inset);transition:background .15s}
.toggle::after{content:"";position:absolute;top:3px;left:3px;width:22px;height:22px;border-radius:50%;background:var(--dim);transition:transform .15s,background .15s}
.toggle.on{background:rgba(61,154,63,.35);border-color:var(--green)}.toggle.on::after{transform:translateX(20px);background:#fff}
.toggle.cheat.on{background:rgba(176,124,240,.3);border-color:var(--cheat)}
.cheatzone{border:1px solid rgba(176,124,240,.45);background:linear-gradient(180deg,rgba(176,124,240,.08),transparent 60px),var(--surface)}
.step{display:flex;align-items:center;gap:6px}
.step input{width:84px;min-height:40px;border-radius:10px;border:1px solid var(--hair);background:var(--inset);color:var(--text);padding:0 8px;font-size:15px;text-align:center}
nav{position:fixed;left:0;right:0;bottom:0;z-index:20;background:rgba(17,15,13,.97);backdrop-filter:blur(8px);-webkit-backdrop-filter:blur(8px);border-top:1px solid var(--hair);display:grid;grid-template-columns:repeat(5,1fr);padding-bottom:env(safe-area-inset-bottom)}
nav button{background:none;border:none;color:var(--faint);font-size:12px;font-weight:700;min-height:62px;display:flex;flex-direction:column;align-items:center;justify-content:center;gap:3px;position:relative}
nav button svg{width:24px;height:24px;fill:none;stroke:currentColor;stroke-width:2;stroke-linecap:round;stroke-linejoin:round}
nav button.on{color:var(--gold)}
nav button.on::before{content:"";position:absolute;left:30%;right:30%;top:0;height:3px;background:var(--gold);border-radius:0 0 3px 3px}
nav .flag{position:absolute;top:9px;left:calc(50% + 9px);width:9px;height:9px;border-radius:50%;background:var(--red);border:2px solid var(--bg)}
nav .flag.ok{background:#5bc45b}
.scrim{position:fixed;inset:0;background:rgba(0,0,0,.6);z-index:40;opacity:0;transition:opacity .2s}
.scrim.show{opacity:1}
.sheet{position:fixed;left:0;right:0;bottom:0;z-index:41;background:var(--surface);border-top:1px solid var(--gline);border-radius:20px 20px 0 0;max-height:88vh;overflow:auto;overscroll-behavior:contain;padding:6px 16px calc(18px + env(safe-area-inset-bottom));transform:translateY(100%);transition:transform .26s cubic-bezier(.32,.72,0,1);max-width:560px;margin:0 auto;box-shadow:0 -10px 30px rgba(0,0,0,.45)}
.sheet.show{transform:none}
.sheet .grip{width:44px;height:5px;border-radius:3px;background:var(--line);margin:6px auto 8px}
.sheet .x{position:absolute;right:10px;top:10px;width:40px;height:40px;border-radius:50%;border:none;background:var(--surface2);color:var(--dim);font-size:20px}
.sheet h2{margin:4px 44px 4px 0;font-size:19px}
.sheet .sub{color:var(--dim);font-size:14px;margin-bottom:10px}
.field{margin:12px 0}
.field label{display:block;color:var(--dim);font-size:13px;font-weight:700;margin-bottom:6px}
.field input,.field select{width:100%;min-height:46px;border-radius:12px;border:1px solid var(--hair);background:var(--inset);color:var(--text);padding:0 12px;font-size:16px}
.toast{position:fixed;left:16px;right:16px;bottom:calc(78px + env(safe-area-inset-bottom));z-index:50;max-width:528px;margin:0 auto;background:#0c0b09;border:1px solid var(--line);border-radius:12px;padding:11px 14px;font-size:14px;font-weight:600;box-shadow:0 8px 22px rgba(0,0,0,.55);display:none;animation:up .2s ease}
.toast.show{display:block}.toast.ok{border-color:rgba(61,154,63,.7);color:var(--greenT)}.toast.err{border-color:rgba(217,76,61,.7);color:var(--redT)}.toast.wait{color:var(--dim)}
@keyframes up{from{transform:translateY(8px);opacity:0}to{transform:none;opacity:1}}
.pair{text-align:center;padding:30px 10px}
.pair h2{color:var(--gold)}.pair ol{text-align:left;color:var(--dim);line-height:1.7}
.skel{height:84px;border-radius:14px;background:linear-gradient(90deg,var(--surface),var(--surface2),var(--surface));background-size:200% 100%;animation:sk 1.2s infinite;margin-bottom:12px}
.skel.s{height:48px}
@keyframes sk{to{background-position:-200% 0}}
details.hist summary{cursor:pointer;color:var(--dim);font-weight:700;font-size:14px;list-style:none;padding:2px 0}
details.hist summary::-webkit-details-marker{display:none}
.more{display:flex;align-items:center;gap:12px;width:100%;min-height:62px;padding:10px 14px;background:var(--surface);border:1px solid var(--hair);border-radius:14px;margin:0 0 10px;text-align:left}
.more .t{flex:1}.more b{display:block;font-size:16px}.more span{display:block;color:var(--dim);font-size:13px}
.mi{width:40px;height:40px;border-radius:10px;display:flex;align-items:center;justify-content:center;background:var(--inset);border:1px solid var(--hair);flex:none}
.mi svg{width:22px;height:22px;fill:none;stroke:var(--gold);stroke-width:2;stroke-linecap:round;stroke-linejoin:round}
.more.cheat .mi svg{stroke:var(--cheat)}
@media (prefers-reduced-motion:reduce){.sheet,.scrim,.bar>i,.btn,.toast,.ic img{transition:none;animation:none}.skel{animation:none}}
</style>
</head>
<body>
<header>
  <div class="dot" id="dot"></div>
  <div class="who"><b id="who">Melvor</b><small id="age" class="num">Connecting to your game...</small></div>
  <span class="pill" id="viewonly" hidden>View only</span>
  <button class="iconbtn" id="bell" aria-label="Alerts"><svg viewBox="0 0 24 24"><path d="M6 16V11a6 6 0 0 1 12 0v5l2 2H4z"/><path d="M10 20a2 2 0 0 0 4 0"/></svg><span class="count" id="unread" hidden></span></button>
</header>
<main>
  <div id="banner"></div>
  <section id="tab-now"><div class="skel"></div><div class="skel"></div><div class="skel s"></div></section>
  <section id="tab-skills" hidden></section>
  <section id="tab-combat" hidden></section>
  <section id="tab-bank" hidden></section>
  <section id="tab-more" hidden></section>
  <section id="pairing" class="pair" hidden>
    <h2>Connect this phone</h2>
    <ol><li>In the game, open <b>Phone Screen</b> (the Phone button in the top bar).</li><li>Press <b>Start</b>.</li><li>Scan the QR code with this phone's camera.</li></ol>
    <p class="note" id="pairwhy"></p>
    <div class="field"><label for="code">Or type the code shown under the QR code</label><input id="code" autocomplete="off" autocapitalize="off" spellcheck="false"></div>
    <button class="btn gold" id="codego">Connect</button>
  </section>
</main>
<nav id="nav">
  <button data-tab="now" class="on"><svg viewBox="0 0 24 24"><path d="M13 2 4 14h7l-1 8 9-12h-7z"/></svg>Now<i class="flag" id="flag-now" hidden></i></button>
  <button data-tab="skills"><svg viewBox="0 0 24 24"><rect x="3" y="3" width="7" height="7" rx="1"/><rect x="14" y="3" width="7" height="7" rx="1"/><rect x="3" y="14" width="7" height="7" rx="1"/><rect x="14" y="14" width="7" height="7" rx="1"/></svg>Skills<i class="flag ok" id="flag-skills" hidden></i></button>
  <button data-tab="combat"><svg viewBox="0 0 24 24"><path d="M14.5 17.5 3 6V3h3l11.5 11.5"/><path d="M13 19l6-6"/><path d="M16 16l4 4"/><path d="M19 21l2-2"/></svg>Combat<i class="flag" id="flag-combat" hidden></i></button>
  <button data-tab="bank"><svg viewBox="0 0 24 24"><path d="M4 9h16v11H4z"/><path d="M4 9l2-5h12l2 5"/><path d="M10 13h4"/></svg>Bank<i class="flag" id="flag-bank" hidden></i></button>
  <button data-tab="more"><svg viewBox="0 0 24 24"><circle cx="5" cy="12" r="1.5"/><circle cx="12" cy="12" r="1.5"/><circle cx="19" cy="12" r="1.5"/></svg>More<i class="flag" id="flag-more" hidden></i></button>
</nav>
<div class="scrim" id="scrim" hidden></div>
<div class="sheet" id="sheet" hidden role="dialog" aria-modal="true"><div class="grip" id="grip"></div><button class="x" id="sheetx" aria-label="Close">✕</button><div id="sheetbody"></div></div>
<div class="toast" id="toast" role="status"></div>
<script>
(function(){
"use strict";
var T=window.melvorTunnel||null;
function xfetch(u,o){return T?T.fetch(u,o):fetch(u,o)}
var catAsked=false;
var KEY="melvorPhoneToken"+(T?":"+T.room:""),token=null,data=null,cat=null,lastTaken=0,lastSeq=0,unread=0,sound=false,tab="now",failures=0,inflight=false,timer=null,recvAt=0;
var demo=/[?&]demo/.test(location.search)&&(location.protocol==="file:"||location.port==="8765");
var ui={skill:null,filter:"avail",search:"",openArea:null,bankView:"items",bankSearch:"",advice:{},adviceTry:{},seg:"fight",more:null,farmCat:null,shopCat:null,shopSearch:"",shopAfford:false,cheatsShown:false,cheatTab:null,histOpen:false};
var parts={},partRaw={};
try{token=localStorage.getItem(KEY)}catch(e){}
(function(){var v=store("ui:view");if(v){if(v.bankView)ui.bankView=v.bankView;if(v.seg)ui.seg=v.seg}})();
function $(id){return document.getElementById(id)}
function el(tag,cls,text){var e=document.createElement(tag);if(cls)e.className=cls;if(text!=null)e.textContent=text;return e}
function add(p){for(var i=1;i<arguments.length;i++)if(arguments[i])p.appendChild(arguments[i]);return p}
function clear(n){while(n.firstChild)n.removeChild(n.firstChild)}
function fmt(n){n=+n||0;var a=Math.abs(n);if(a>=1e9)return(n/1e9).toFixed(a>=1e10?0:1)+"B";if(a>=1e6)return(n/1e6).toFixed(a>=1e7?0:1)+"M";if(a>=1e4)return(n/1e3).toFixed(a>=1e5?0:1)+"k";return Math.round(n).toLocaleString("en")}
function full(n){return Math.round(+n||0).toLocaleString("en")}
function dur(ms){var s=Math.max(0,Math.round(ms/1000));if(s<60)return s+"s";var m=Math.floor(s/60);if(m<60)return m+"m "+(s%60)+"s";var h=Math.floor(m/60);return h+"h "+(m%60)+"m"}
function newId(){return Date.now().toString(36)+Math.random().toString(36).slice(2,10)}
function S(){return (data&&data.state)||{}}
function has(k){var h=S().has;return !!(h&&h[k])}
function controls(){return !!(data&&data.controls)&&!stale()}
function offline(){var w=T?T.state():"";return w==="relay"?"No internet connection on this phone.":w==="pc"?"Your game is not connected: the PC may be off or asleep, or the phone screen was turned off.":"Cannot reach your PC."}
function stale(){var s=S();if(demo)return false;return !data||!s.ready||(data.now-(s.taken||0))>4000||failures>=3||Date.now()-recvAt>(T?9000:5000)}
function store(k,v){try{if(v===undefined)return JSON.parse(localStorage.getItem(k)||"null");localStorage.setItem(k,JSON.stringify(v))}catch(e){return null}}
function remember(){store("ui:view",{bankView:ui.bankView,seg:ui.seg})}
function cap(s){s=s||"";return s.charAt(0).toUpperCase()+s.slice(1)}
// One Style's rare items: the name in its rarity's colour, "Rare · ilvl 40 · Bronze Sword", the affixes
function rc(e,x){if(x&&x.color&&/^[0-9a-f]{6}$/i.test(x.color))e.style.color="#"+x.color;return e}
function rareLine(x){return x&&x.rarity?x.rarity+(x.ilvl?" · ilvl "+x.ilvl:"")+(x.base?" · "+x.base:""):""}
function affixes(x){return x&&x.affixes&&x.affixes.length?el("span","aff",x.affixes.join(" · ")):null}
function rareBox(x){if(!x||!x.rarity)return null;var d=el("div","rare");d.appendChild(rc(el("b","",x.name),x));d.appendChild(el("span","",rareLine(x)));(x.affixes||[]).forEach(function(a){d.appendChild(el("span","aff",a))});return d}
function buzz(){try{if(navigator.vibrate)navigator.vibrate(10)}catch(e){}}

// ---- pictures from the game (a letter while there is none)
var pictures={};
function icon(key,letter,small){var b=el("span","ic"+(small?" s":""),(letter||"?").charAt(0).toUpperCase());if(key&&!demo){var img=new Image();img.alt="";img.decoding="async";img.addEventListener("load",function(){b.textContent="";b.appendChild(img)});var u="/icon/"+key.split("/").map(encodeURIComponent).join("/")+".png";if(T)tunnelPicture(u).then(function(src){if(src)img.src=src});else img.src=u}return b}
function tunnelPicture(u){if(!pictures[u])pictures[u]=T.fetch(u).then(function(r){return r.ok?r.blob():null}).then(function(b){return b?URL.createObjectURL(b):null}).catch(function(){delete pictures[u];return null});return pictures[u]}
function bar(cls,frac){var b=el("div","bar "+(cls||""));var i=el("i");i.style.width=Math.max(0,Math.min(1,frac||0))*100+"%";b.appendChild(i);return b}
function btn(text,cls,fn,disabled){var b=el("button","btn "+(cls||""),text);b.type="button";if(disabled)b.disabled=true;b.addEventListener("click",function(e){e.stopPropagation();fn(b)});return b}
function toggle(on,cls,fn,disabled){var t=el("button","toggle"+(on?" on":"")+(cls?" "+cls:""));t.type="button";t.setAttribute("role","switch");t.setAttribute("aria-checked",on?"true":"false");if(disabled)t.disabled=true;t.addEventListener("click",function(e){e.stopPropagation();fn(t)});return t}
function seg(items,cur,fn){var g=el("div","seg");items.forEach(function(f){var b=el("button",cur===f[0]?"on":"",f[1]);b.type="button";b.addEventListener("click",function(){fn(f[0])});g.appendChild(b)});return g}
function backBar(title,fn,iconEl){var bk=el("div","back");add(bk,btn("‹","small",fn),iconEl||null,el("b","",title));return bk}

// ---- tabs
document.querySelectorAll("nav button").forEach(function(b){b.addEventListener("click",function(){if(tab===b.dataset.tab){if(tab==="skills")ui.skill=null;if(tab==="more")ui.more=null}go(b.dataset.tab)})});
var TABS=["now","skills","combat","bank","more"];
function go(t){if(sheetOpen)closeSheet();tab=t;document.querySelectorAll("nav button").forEach(function(x){x.classList.toggle("on",x.dataset.tab===t)});TABS.forEach(function(x){$("tab-"+x).hidden=x!==t});window.scrollTo(0,0);lastView="";if(t!=="now")fetchCatalog();wantParts();render()}

// ---- sheets, toasts
var sheetOpen=false;
function sheet(build){var b=$("sheetbody");clear(b);build(b);$("scrim").hidden=false;$("sheet").hidden=false;$("sheet").scrollTop=0;requestAnimationFrame(function(){$("scrim").classList.add("show");$("sheet").classList.add("show")});sheetOpen=true}
function closeSheet(){$("scrim").classList.remove("show");$("sheet").classList.remove("show");$("sheet").style.transform="";sheetOpen=false;lastView="";setTimeout(function(){if(!sheetOpen){$("scrim").hidden=true;$("sheet").hidden=true}},260)}
$("scrim").addEventListener("click",closeSheet);$("sheetx").addEventListener("click",closeSheet);
(function(){var y0=null,dy=0;var g=$("grip"),sh=$("sheet");g.addEventListener("touchstart",function(e){y0=e.touches[0].clientY;dy=0;sh.style.transition="none"},{passive:true});g.addEventListener("touchmove",function(e){if(y0==null)return;dy=Math.max(0,e.touches[0].clientY-y0);sh.style.transform="translateY("+dy+"px)"},{passive:true});g.addEventListener("touchend",function(){sh.style.transition="";if(dy>80)closeSheet();else sh.style.transform="";y0=null})})();
var toastTimer=null;
function toast(text,kind,ms){var t=$("toast");t.textContent=text;t.className="toast show "+(kind||"");clearTimeout(toastTimer);if(ms!==0)toastTimer=setTimeout(function(){t.className="toast"},ms||3200)}

// ---- commands: sent, waited for, answered (the answer comes with the next snapshots)
var pending={};
function send(cmd,args,label,b){
  if(demo){toast(label+" (demo)","ok");return}
  if(!controls()){toast(data&&data.controls?"The game is not answering right now.":"View only: allow phone controls in the game's Phone window.","err");return}
  var id=newId(),body={};for(var k in args)body[k]=String(args[k]);body.id=id;body.cmd=cmd;body.character=S().character||"";
  pending[id]={label:label,at:Date.now()};if(b&&b.classList){b.classList.add("busy");b.disabled=true}else toast(label+"...","wait",0);buzz();
  xfetch("/api/cmd",{method:"POST",headers:{"Content-Type":"application/json","Authorization":"Bearer "+token},body:JSON.stringify(body)})
   .then(function(r){return r.json().then(function(j){if(!r.ok){delete pending[id];toast(j.error||"Not done.","err");lastView="";render()}})})
   .catch(function(){toast("Result unknown: the game could not be reached. Checking...","err")});
}
function answers(s){var any=false;(s.answers||[]).forEach(function(a){if(pending[a.id]){delete pending[a.id];any=true;toast(a.text,a.ok?"ok":"err",a.ok?3200:6000);if(a.ok){setTimeout(fetchCatalog,400);partRaw={};setTimeout(wantParts,300)}}});for(var id in pending)if(Date.now()-pending[id].at>12000){toast("Result unknown for "+pending[id].label+". Look at the game's state above.","err");delete pending[id];any=true}if(any)lastView=""}
function confirmSheet(title,sub,yes,cls,fn){sheet(function(b){add(b,el("h2","",title),el("div","sub",sub));var r=el("div","btns");add(r,btn("Cancel","",closeSheet),btn(yes,cls||"go",function(){closeSheet();fn()}));b.appendChild(r)})}

// ---- pairing
function pairFirst(){if(T){var c=T.pairCode();return c?pair(c):Promise.resolve()}var m=/[#&]p=([A-Za-z0-9]+)/.exec(location.hash);if(!m)return Promise.resolve();history.replaceState(null,"",location.pathname);return pair(m[1])}
function pair(code){return xfetch("/api/pair",{method:"POST",headers:{"Content-Type":"application/json"},body:JSON.stringify({code:code})}).then(function(r){return r.json().then(function(j){if(r.ok&&j.token){token=j.token;try{localStorage.setItem(KEY,token)}catch(e){}if(T)T.paired();$("pairwhy").textContent=""}else $("pairwhy").textContent=j.error||"Pairing failed."})}).catch(function(){$("pairwhy").textContent="The game could not be reached."})}
$("codego").addEventListener("click",function(){var c=$("code").value.trim();if(c)pair(c).then(function(){if(token){$("pairing").hidden=true;poll()}})});
function showPairing(why){TABS.forEach(function(t){$("tab-"+t).hidden=true});$("pairing").hidden=false;$("nav").style.display="none";if(why)$("pairwhy").textContent=why;$("dot").className="dot off";$("age").textContent="not connected"}

// ---- polling: one request at a time, a late answer never overwrites a newer one
function poll(){
  if(demo||document.hidden||inflight)return;if(!token){showPairing();return}
  inflight=true;
  xfetch("/api/state",{headers:{"Authorization":"Bearer "+token},cache:"no-store"}).then(function(r){
    if(r.status===401){token=null;try{localStorage.removeItem(KEY)}catch(e){};showPairing("This phone is no longer paired. Scan the new QR code in the game.");return null}
    if(r.status===429){inflight=false;schedule(2500);return null}   // (asked too often: a short pause, not "cannot reach")
    if(!r.ok)throw new Error(r.status);return r.json();
  }).then(function(j){inflight=false;if(!j)return;var t=(j.state&&j.state.taken)||0;if(t&&t<lastTaken)return;lastTaken=t;failures=0;if(!data||catCached&&!catAsked){catAsked=true;setTimeout(fetchCatalog,300)}data=j;recvAt=Date.now();$("pairing").hidden=true;$("nav").style.display="";$("tab-"+tab).hidden=false;alerts();answers(S());render();schedule(T?2000:1000)})
  .catch(function(){inflight=false;failures++;render();schedule(failures<3?1000:failures<6?2000:5000)});
}
function schedule(ms){clearTimeout(timer);timer=setTimeout(poll,ms)}
document.addEventListener("visibilitychange",function(){if(!document.hidden){poll();fetchCatalog();wantParts()}});
window.addEventListener("online",poll);
setInterval(function(){if(data&&!document.hidden)render()},1000);

var catVersion=-1,catTimer=null,catCached=false,CATKEY="melvorPhoneCatalog"+(T?":"+T.room:"");
// the last catalogue this phone saw: Skills opens at once, the fresh one replaces it a moment later
try{var cc=JSON.parse(localStorage.getItem(CATKEY)||"null");if(cc&&cc.skills&&!demo){cat=cc;catCached=true}}catch(e){}
function fetchCatalog(){
  if(demo||!token)return;
  clearTimeout(catTimer);
  xfetch("/api/catalog"+(catVersion>0?"?v="+catVersion:""),{headers:{"Authorization":"Bearer "+token},cache:"no-store"}).then(function(r){if(r.status===202){catTimer=setTimeout(fetchCatalog,400);return null}return r.ok?r.json():null}).then(function(j){if(!j||j.same||!j.skills)return;if(j.version!==catVersion||!cat||catCached){cat=j;catVersion=j.version;catCached=false;try{localStorage.setItem(CATKEY,JSON.stringify(j))}catch(e){}lastView="";if(!sheetOpen&&tab!=="now")render()}}).catch(function(){});
  if(tab!=="now")catTimer=setTimeout(fetchCatalog,6000);
}
function fetchAdvice(area){
  if(demo||ui.advice[area])return;var n=(ui.adviceTry[area]||0)+1;ui.adviceTry[area]=n;if(n>40)return;
  xfetch("/api/advise?area="+encodeURIComponent(area),{headers:{"Authorization":"Bearer "+token},cache:"no-store"}).then(function(r){if(r.status===202){setTimeout(function(){fetchAdvice(area)},600);return null}return r.ok?r.json():null}).then(function(j){if(j){ui.advice[area]=j;lastView="";if(!sheetOpen)render()}}).catch(function(){});
}
// ---- the other screens: asked for while shown (the game makes them again every second and a half)
function partsFor(){if(tab==="combat")return ui.seg==="fight"?[]:[ui.seg==="buffs"?"buffs":ui.seg];if(tab==="skills"&&ui.skill==="farming")return["farm"];if(tab==="bank"&&ui.bankView==="shop")return["shop"];if(tab==="bank"&&ui.bankView==="items"&&has("bulk"))return["more"];if(tab==="more")return["more"];return[]}
var partTimer=null;
function wantParts(){clearTimeout(partTimer);if(demo||!token)return;var names=partsFor();names.forEach(fetchPart);if(names.length)partTimer=setTimeout(wantParts,2000);else if(!S().has)partTimer=setTimeout(wantParts,500)}
function fetchPart(n){xfetch("/api/part?n="+n,{headers:{"Authorization":"Bearer "+token},cache:"no-store"}).then(function(r){if(r.status===202){setTimeout(function(){fetchPart(n)},500);return null}return r.ok?r.text():null}).then(function(t){if(t==null||t===partRaw[n])return;partRaw[n]=t;try{parts[n]=JSON.parse(t)}catch(e){return}lastView="";if(!sheetOpen)render()}).catch(function(){})}

// ---- alerts: history in the bell; what still holds lives in "problems"
var audio=null;
function beep(f,len){try{audio=audio||new (window.AudioContext||window.webkitAudioContext)();var o=audio.createOscillator(),g=audio.createGain();o.frequency.value=f;g.gain.value=.15;o.connect(g);g.connect(audio.destination);o.start();o.stop(audio.currentTime+(len||.25))}catch(e){}}
function alerts(){var al=S().alerts||[],fresh=al.filter(function(a){return a.seq>lastSeq});if(fresh.length&&lastSeq>0){unread+=fresh.length;if(fresh.some(function(a){return a.kind!=="info"})){if(sound)beep(660,.3);if(navigator.vibrate)navigator.vibrate([200,100,200])}}if(al.length)lastSeq=Math.max(lastSeq,al[al.length-1].seq);$("unread").hidden=!unread;$("unread").textContent=unread>9?"9+":unread;document.title=(unread?"("+unread+") ":"")+(S().character||"Melvor")}
$("bell").addEventListener("click",function(){unread=0;alerts();sheet(function(b){add(b,el("h2","","Alerts"),el("div","sub","Alerts work only while this page is open. Sound and vibration depend on your browser."));
  var r=el("div","it");add(r,add(el("div","t"),el("b","","Sound and vibration"),el("span","","For warnings while the page is open")),toggle(sound,"",function(x){sound=!sound;x.classList.toggle("on",sound);if(sound)beep(440,.08)}));b.appendChild(add(el("div","list"),r));
  var l=el("div","list");var al=(S().alerts||[]).slice().reverse();if(!al.length)l.appendChild(el("div","empty","Nothing happened yet."));
  al.forEach(function(a){var it=el("div","it"),tt=el("div","t");add(tt,el("b","",a.text),el("span","num",ago(a.time)));add(it,tt,el("span","badge "+(a.kind==="info"?"":a.kind),a.kind==="info"?"info":a.kind==="bad"?"urgent":"warning"));l.appendChild(it)});b.appendChild(l)})});
function ago(t){var s=Math.max(0,(Date.now()-t)/1000);return s<60?"just now":s<3600?Math.round(s/60)+" min ago":Math.round(s/3600)+" h ago"}

// ---- the whole page from the latest snapshot; a screen is built again only when what it shows changed
var lastView="";
function render(){
  var s=S(),st=stale();
  $("who").textContent=s.character||"Melvor";
  var age=data?Math.max(0,(Date.now()-recvAt)/1000+(data.now-(s.taken||0))/1000):0;
  $("age").textContent=!data?"Connecting to your game...":failures>=3?offline().replace(/\..*$/,""):!s.ready?"No character open":st?"Updates delayed: last "+Math.round(age)+" s ago":"Live";
  $("dot").className="dot "+(!data?"":failures>=3?"off":st?"stale":"ok");
  $("viewonly").hidden=!data||!!data.controls;
  var bn=$("banner"),bsig=[failures>=3,data&&s.ready,st,data&&data.controls].join();if(bn.dataset.sig!==bsig){bn.dataset.sig=bsig;clear(bn);
  if(failures>=3){var b=el("div","banner bad");add(b,el("span","grow",offline()+" The numbers below are the last ones seen."),btn("Retry","small",function(){failures=0;poll()}));bn.appendChild(b)}
  else if(data&&!s.ready)bn.appendChild(el("div","banner warn",s.why||"Open a character in the PC game."));
  else if(st&&data)bn.appendChild(el("div","banner warn","The game is busy (loading or offline progress). Controls wait until it is back."));
  else if(data&&!data.controls){var v=el("div","banner warn");v.appendChild(el("span","grow","View only: allow phone controls in the game's Phone window to start, stop and change things."));bn.appendChild(v)}}
  var pr=s.problems||[];$("flag-now").hidden=!pr.some(function(p){return p.level!=="info"});$("flag-combat").hidden=!pr.some(function(p){return p.fix==="food"});$("flag-skills").hidden=!pr.some(function(p){return p.fix==="farm"});$("flag-more").hidden=!pr.some(function(p){return p.fix==="team"||p.fix==="queue"});$("flag-bank").hidden=!pr.some(function(p){return p.fix==="bank"});
  if(!data||sheetOpen)return;
  var sig=tab+"|"+viewSig();if(sig===lastView){tick();return}lastView=sig;
  if(tab==="now")now(s);else if(tab==="skills")skills();else if(tab==="combat")combat(s);else if(tab==="bank")bank(s);else if(tab==="more")more(s);
}
// what a screen depends on (numbers that move every second are left to tick())
function viewSig(){var s=S(),k=s.combat||{};var base=[controls(),JSON.stringify(ui)];
  if(tab==="now")return base.concat([JSON.stringify((s.actions||[]).map(function(a){return[a.skillId,a.target,a.level,a.byQueue]})),JSON.stringify(s.problems),JSON.stringify(s.queue),JSON.stringify(s.adventurers),JSON.stringify(s.farm&&[s.farm.ready,s.farm.growing,s.farm.empty]),JSON.stringify(s.fightKit),k.enemy,k.food,k.foodCount,k.stance,(s.bank||{}).gold,(s.bank||{}).used,(s.alerts||[]).length]).join("|");
  if(tab==="combat")return base.concat([catVersion,partRaw[ui.seg],JSON.stringify(s.fightKit),k.enemy,k.food,k.foodCount,k.stance,(s.actions||[]).length]).join("|");
  if(tab==="skills")return base.concat([catVersion,partRaw.farm,(s.actions||[]).map(function(a){return a.skillId+a.target}).join()]).join("|");
  if(tab==="bank")return base.concat([catVersion,partRaw.shop,partRaw.more,JSON.stringify(s.bank)]).join("|");
  return base.concat([catVersion,partRaw.more,JSON.stringify(s.queue),JSON.stringify(s.adventurers)]).join("|")}
// numbers that move between snapshots
function tick(){var s=S(),k=s.combat||{};document.querySelectorAll("[data-live]").forEach(function(e){var w=e.dataset.live;
  if(w==="hp"){e.textContent=full(k.hp)+" / "+full(k.maxHp)}else if(w==="hpbar"){e.firstChild.style.width=(k.hp/Math.max(1,k.maxHp)*100)+"%"}else if(w==="ehp"){e.textContent=full(k.enemyHp)+" / "+full(k.enemyMaxHp)}else if(w==="ehpbar"){e.firstChild.style.width=(k.enemyHp/Math.max(1,k.enemyMaxHp)*100)+"%"}
  else if(w==="grow"){var left=+e.dataset.ms-(Date.now()-(+e.dataset.at));e.textContent=left>0?"Ready in "+dur(left):"Ready"}});}

// ================================================================== NOW
function now(s){
  var n=$("tab-now");clear(n);if(!s.ready){n.appendChild(el("div","empty","Open a character in the PC game."));return}
  var pr=(s.problems||[]).slice().sort(function(a,b){return(a.level==="bad"?0:a.level==="warn"?1:2)-(b.level==="bad"?0:b.level==="warn"?1:2)});
  if(!pr.length)n.appendChild(el("div","calm","No issues detected."));
  pr.slice(0,4).forEach(function(p){var d=el("div","prob "+p.level);d.appendChild(el("span","grow",p.text));var f=fixButton(p);if(f)d.appendChild(f);n.appendChild(d)});
  var acts=s.actions||[];
  if(!acts.length){var c=el("div","card accent");add(c,add(el("div","ctitle"),add(el("div","t"),el("b","","Your character is idle"),el("span","","Nothing is running in the game."))));var r=el("div","btns");r.appendChild(btn("Choose an activity","go",function(){go("skills")}));var last=store("recent:"+s.character);if(last&&last[0])r.appendChild(btn("Resume "+last[0].name,"gold",function(b){startTarget(last[0],b)},!controls()));c.appendChild(r);n.appendChild(c)}
  // (no "Slot N" here: the game's own order differs from Multitask's numbering, which the More tab shows)
  acts.forEach(function(a){n.appendChild(a.combat?combatCard(a,s.combat,s.fightKit,0):skillCard(a,0))});
  var f=s.farm;if(f&&(f.ready||f.growing||f.empty)){var cf=el("div","card"+(f.ready?" ready":"")),sf=el("div","sum");add(sf,icon("skill/MelvorBase:Farming","F"),add(el("div","t"),el("b","",f.ready?"Farming: "+f.ready+" ready":"Farming"),el("span","",[f.growing?f.growing+" growing":"",f.empty?f.empty+" empty":"",f.nextMs&&!f.ready?"next in "+dur(f.nextMs):""].filter(Boolean).join(" · "))));if(f.ready)sf.appendChild(btn("Harvest all","small go",function(b){send("harvest",{plot:""},"Harvest all",b)},!controls()));sf.appendChild(el("span","chev","›"));sf.addEventListener("click",function(){ui.skill="farming";go("skills")});cf.appendChild(sf);n.appendChild(cf)}
  var q=s.queue;if(q&&q.steps){var c2=el("div","card");var sm=el("div","sum");var run=q.steps.filter(function(x){return x.run==="Running"})[0];var idx=run?q.steps.indexOf(run)+1:0;add(sm,add(el("div","t"),el("b","",q.steps.length?(q.running?"Queue: running · step "+idx+" of "+q.steps.length:"Queue: paused · "+q.steps.length+" step"+(q.steps.length===1?"":"s")):"Queue: empty"),el("span","",run?run.name+" · "+run.goal:q.steps.length?"Resume picks up where it stopped":"Plan steps in More › Action Queue")));if(q.steps.length)sm.appendChild(btn(q.running?"Pause":"Resume","small "+(q.running?"":"go"),function(b){send(q.running?"pausequeue":"resumequeue",{},q.running?"Pause queue":"Resume queue",b)},!controls()));sm.appendChild(el("span","chev","›"));sm.addEventListener("click",function(){ui.more="queue";go("more")});c2.appendChild(sm);n.appendChild(c2)}
  var ad=s.adventurers;if(ad&&ad.team&&ad.team.length){var c3=el("div","card"),sm3=el("div","sum");var stp=ad.team.filter(function(x){return/^Stopped/.test(x.status)}).length;add(sm3,add(el("div","t"),el("b","","Adventurers"),el("span","",ad.team.length+" hired · "+ad.team.filter(function(x){return x.status==="Hunting"}).length+" hunting"+(stp?" · "+stp+" need attention":""))),el("span","chev","›"));sm3.addEventListener("click",function(){ui.more="team";go("more")});c3.appendChild(sm3);n.appendChild(c3)}
  var bk=s.bank;if(bk){var c4=el("div","card"),sm4=el("div","sum");add(sm4,add(el("div","t"),el("b","num",fmt(bk.gold)+" gold"),el("span","num",full(bk.used)+" / "+full(bk.max)+" bank slots"+(bk.recent&&bk.recent.length?" · "+bk.recent.length+" items gained lately":""))),el("span","chev","›"));sm4.addEventListener("click",function(){go("bank")});c4.appendChild(sm4);n.appendChild(c4)}
  var al=(s.alerts||[]).slice(-6).reverse();if(al.length){var d=el("details","hist card");if(ui.histOpen)d.open=true;d.addEventListener("toggle",function(){ui.histOpen=d.open});d.appendChild(el("summary","","Recent activity ("+al.length+")"));var l=el("div","list");al.forEach(function(a){var it=el("div","it"),t=el("div","t");add(t,el("b","",a.text),el("span","",ago(a.time)));it.appendChild(t);l.appendChild(it)});d.appendChild(l);n.appendChild(d)}
}
function fixButton(p){
  var c=!controls();
  if(p.fix==="food")return btn("Change food","small",function(){foodSheet()},c);
  if(p.fix==="skills")return btn("Choose","small",function(){go("skills")});
  if(p.fix==="bank")return btn("Free space","small",function(){ui.bankView="items";go("bank")});
  if(p.fix==="team")return btn("Open","small",function(){ui.more="team";go("more")});
  if(p.fix==="queue")return btn("Open","small",function(){ui.more="queue";go("more")});
  if(p.fix==="farm")return btn("Harvest all","small go",function(b){send("harvest",{plot:""},"Harvest all",b)},c);
  return null;
}
function skillCard(a,slot){
  var c=el("div","card accent"),h=el("div","ctitle");add(h,icon(a.icon||("skill/"+a.skillId),a.skill),add(el("div","t"),el("b","",a.skill),el("span","",(slot?"Slot "+slot+" · ":"")+(a.target||"")+(a.byQueue?" · from the queue":""))));c.appendChild(h);
  if(a.progress!=null){var r0=el("div","row num");add(r0,el("span","k","Action"),el("span","num",a.seconds+" s each"));var b=bar("live",a.progress);b.dataset.p=a.progress;b.dataset.sec=a.seconds;add(c,r0,b)}
  if(a.level!=null){var r=el("div","row num");add(r,el("span","k","Level "+a.level),el("span","num",full(a.toNext)+" XP to "+(a.level+1)));add(c,r,bar("xp",a.levelProgress))}
  var bs=el("div","btns");add(bs,btn("Change","gold",function(){ui.skill=a.skillId;ui.filter="avail";go("skills")}),btn("Stop","",function(b){send("stop",{skill:a.skillId},"Stop "+a.skill,b)},!controls()));c.appendChild(bs);return c;
}
function combatCard(a,k,kit,slot){
  var c=el("div","card combat"),h=el("div","ctitle");k=k||{};kit=kit||{};
  add(h,icon(k.enemyId?"enemy/"+k.enemyId:"skill/"+a.skillId,k.enemy||"C"),add(el("div","t"),el("b","",(k.enemy||a.target||"Combat")),el("span","",(slot?"Slot "+slot+" · ":"")+(k.area||"Combat")+(a.byQueue?" · from the queue":""))));c.appendChild(h);
  if(k.maxHp){var r=el("div","row");var hv=el("span","num",full(k.hp)+" / "+full(k.maxHp));hv.dataset.live="hp";add(r,el("span","k","Your HP"),hv);var b=bar("hp",k.hp/Math.max(1,k.maxHp));b.dataset.live="hpbar";if(k.eatAt!=null){var t=el("span","tick");t.style.left=Math.min(99,k.eatAt/Math.max(1,k.maxHp)*100)+"%";b.appendChild(t)}add(c,r,b);if(k.eatAt!=null)c.appendChild(el("div","note num","Auto-eats at "+full(k.eatAt)+" HP (gold line)"))}
  if(k.enemyMaxHp){var r2=el("div","row");var ev=el("span","num",full(k.enemyHp)+" / "+full(k.enemyMaxHp));ev.dataset.live="ehp";add(r2,el("span","k",k.enemy),ev);var eb=bar("hp thin",k.enemyHp/Math.max(1,k.enemyMaxHp));eb.dataset.live="ehpbar";add(c,r2,eb)}
  var fr=el("div","row");add(fr,el("span","k","Food"),el("span","num",k.food?full(k.foodCount)+" "+k.food+(k.heal?" (+"+k.heal+" HP)":""):"none selected"));c.appendChild(fr);
  var kt=el("div","kit");[kit.loadout?"Loadout: "+kit.loadout:"",k.stance?"Stance: "+k.stance:"",kit.potion?kit.potion+" × "+full(kit.potionQty):"",kit.prayers?"Prayer: "+kit.prayers:"",kit.spell?"Spell: "+kit.spell:"",kit.slayer?"Slayer · "+kit.slayer:""].filter(Boolean).forEach(function(x){kt.appendChild(el("span","",x))});if(kt.firstChild)c.appendChild(kt);
  var bs=el("div","btns");var full1=k.hp>=k.maxHp;
  add(bs,btn(full1?"HP full":"Eat once","go",function(b){send("eat",{},"Eat once",b)},!controls()||!k.foodCount||full1),btn("Food","",function(){foodSheet()},!controls()),btn("Gear","",function(){ui.seg="gear";go("combat")}));c.appendChild(bs);
  var b2=el("div","btns");add(b2,btn("New target","gold",function(){ui.seg="fight";go("combat")}),btn("Stop combat","danger",function(b){send("stop",{skill:a.skillId},"Stop combat",b)},!controls()));c.appendChild(b2);
  return c;
}
// the fight in a few lines, above the Combat screens
function combatHud(a,k){
  k=k||{};var c=el("div","card combat");c.style.padding="12px 14px";var h=el("div","ctitle");add(h,icon(k.enemyId?"enemy/"+k.enemyId:"skill/"+a.skillId,k.enemy||"C",true),add(el("div","t"),el("b","",k.enemy||a.target||"Combat"),el("span","",k.area||"")),btn("Stop","small danger",function(b){send("stop",{skill:a.skillId},"Stop combat",b)},!controls()));c.appendChild(h);
  if(k.maxHp){var r=el("div","row");r.style.marginTop="8px";var hv=el("span","num",full(k.hp)+" / "+full(k.maxHp));hv.dataset.live="hp";add(r,el("span","k","You"),hv);var b=bar("hp",k.hp/Math.max(1,k.maxHp));b.dataset.live="hpbar";if(k.eatAt!=null){var t=el("span","tick");t.style.left=Math.min(99,k.eatAt/Math.max(1,k.maxHp)*100)+"%";b.appendChild(t)}add(c,r,b)}
  if(k.enemyMaxHp){var eb=bar("hp thin",k.enemyHp/Math.max(1,k.enemyMaxHp));eb.dataset.live="ehpbar";eb.style.marginTop="6px";c.appendChild(eb)}
  var full1=k.hp>=k.maxHp;var bs=el("div","btns");bs.style.marginTop="10px";add(bs,btn(full1?"HP full":"Eat · "+fmt(k.foodCount)+" left","go",function(bb){send("eat",{},"Eat once",bb)},!controls()||!k.foodCount||full1),btn(k.food?"Food: "+k.food:"Pick food","",function(){foodSheet()},!controls()));c.appendChild(bs);
  return c;
}
function foodSheet(){fetchCatalog();sheet(function(b){add(b,el("h2","","Food"),el("div","sub","From your bank. The game eats it whenever your HP falls to the auto-eat line."));var l=el("div","list");var foods=(cat&&cat.combat&&cat.combat.foods)||[];if(!foods.length)l.appendChild(el("div","empty",cat?"No food in the bank.":"Loading..."));
  foods.slice().sort(function(x,y){return y.heal-x.heal}).forEach(function(f){var it=el("div","it"),t=el("div","t");add(t,el("b","",f.name),el("span","num",full(f.count)+" in the bank · heals "+f.heal));var on=cat.combat.food===f.id;add(it,icon("item/"+f.id,f.name,true),t,on?el("span","badge good","in use"):btn("Use","small go",function(){closeSheet();send("food",{item:f.id},"Use "+f.name)},!controls()));l.appendChild(it)});b.appendChild(l);
  var st=(cat&&cat.combat&&cat.combat.stances)||[];if(st.length){b.appendChild(el("div","group","Stance"));b.appendChild(seg(st.map(function(x){return[x.id,x.name]}),cat.combat.stance,function(id){closeSheet();if(id!==cat.combat.stance)send("stance",{stance:id},"Stance")}))}})}

// ================================================================== SKILLS
function skills(){
  var n=$("tab-skills");clear(n);
  if(ui.skill==="farming"){farm(n);return}
  if(!cat){n.appendChild(el("div","skel s"));n.appendChild(el("div","skel"));n.appendChild(el("div","skel"));return}
  if(ui.skill){skillView(n,cat.skills.filter(function(x){return x.id===ui.skill})[0]);return}
  var q=el("input","search");q.placeholder="Find a skill, tree, recipe or monster";q.value=ui.search;q.addEventListener("input",function(){ui.search=q.value;var pos=q.selectionStart;lastView="";skills();var nq=document.querySelector("#tab-skills .search");nq.focus();nq.setSelectionRange(pos,pos)});n.appendChild(q);
  if(ui.search.trim()){searchResults(n,ui.search.trim().toLowerCase());return}
  var f=S().farm;var hero=el("button","hero");hero.type="button";add(hero,icon("skill/MelvorBase:Farming","F"),add(el("div","t"),el("b","","Farming"),el("span","",f?(f.ready?f.ready+" ready to harvest":"")+(f.ready&&f.growing?" · ":"")+(f.growing?f.growing+" growing":"")+(!f.ready&&!f.growing?"Plant your plots":""):"Your plots")),el("span","chev","›"));hero.addEventListener("click",function(){ui.skill="farming";render();window.scrollTo(0,0)});n.appendChild(hero);
  var rec=store("recent:"+(cat.character||""))||[];
  if(rec.length){n.appendChild(el("div","group","Recent"));var ch=el("div","chips");rec.forEach(function(r){var c=el("button","chip",r.name);c.type="button";c.addEventListener("click",function(){startTarget(r,null)});ch.appendChild(c)});n.appendChild(ch)}
  var groups=[["Gathering",["Woodcutting","Mining","Fishing","Hunting"]],["Making",["Cooking","Firemaking","Smithing","Fletching","Crafting","Runecrafting","Herblore"]],["Other",["Thieving","Agility","Astrology"]]];
  var used={};cat.skills.forEach(function(x){if(x.combat)used[x.id]=1});
  groups.forEach(function(g){var list=cat.skills.filter(function(x){return g[1].indexOf(x.name)>=0});if(!list.length)return;n.appendChild(el("div","group",g[0]));var gr=el("div","grid");list.forEach(function(x){used[x.id]=1;gr.appendChild(skillTile(x))});n.appendChild(gr)});
  var rest=cat.skills.filter(function(x){return!used[x.id]});if(rest.length){n.appendChild(el("div","group","More"));var gr2=el("div","grid");rest.forEach(function(x){gr2.appendChild(skillTile(x))});n.appendChild(gr2)}
}
function skillTile(x){var t=el("button","tile"+(x.running?" on":""));t.type="button";var tt=el("div","t");add(tt,el("b","",x.name),el("span","","Level "+(x.level||"-")+(x.running?" · running":"")));tt.appendChild(bar("xp",x.xp));add(t,icon("skill/"+x.id,x.name),tt);t.addEventListener("click",function(){ui.skill=x.id;ui.filter="avail";render();window.scrollTo(0,0)});return t}
function searchResults(n,q){var l=el("div","card list"),count=0;cat.skills.forEach(function(x){x.targets.forEach(function(t){if(count>=40)return;if((t.name+" "+(t.area||"")+" "+x.name).toLowerCase().indexOf(q)<0)return;count++;l.appendChild(x.combat?monsterRow(x,t,null,true):targetRow(x,t,true))})});if(!count)l.appendChild(el("div","empty","Nothing matches “"+q+"”."));n.appendChild(l)}
function skillView(n,x){
  if(!x){ui.skill=null;skills();return}
  if(x.combat){ui.skill=null;ui.seg="fight";go("combat");return}
  var bk=backBar(x.name,function(){ui.skill=null;render();window.scrollTo(0,0)},icon("skill/"+x.id,x.name));bk.appendChild(el("span","badge","Level "+x.level));n.appendChild(bk);
  n.appendChild(seg([["avail","Can start now"],["all","All"]],ui.filter,function(f){ui.filter=f;render()}));n.appendChild(el("div","note",startEffect(x)));
  var list=x.targets.filter(function(t){return ui.filter==="all"||t.can});
  var l=el("div","card list");l.style.marginTop="10px";if(!list.length)l.appendChild(el("div","empty",ui.filter==="avail"?"Nothing can be started right now. Look under All for what is missing.":"Nothing here."));
  list.forEach(function(t){l.appendChild(targetRow(x,t,false))});n.appendChild(l);
}
function startEffect(x){var s=S(),acts=s.actions||[],slots=(s.has&&s.has.slots)||1;var other=acts.filter(function(a){return a.skillId!==x.id});var same=acts.filter(function(a){return a.skillId===x.id})[0];
  if(same)return "Starting another one switches "+x.name+" over.";if(!acts.length)return "Nothing is running: it starts right away.";if(other.length<slots)return "Multitask: it runs beside "+other.map(function(a){return a.skill}).join(" and ")+".";return "Starting it replaces "+other[other.length-1].skill+(other.some(function(a){return a.byQueue})?" and pauses the queue":"")+"."}
function targetRow(x,t,showSkill){
  var it=el("div","it"+(t.can?"":" off")),tt=el("div","t");
  add(tt,el("b","",t.name),el("span",t.can?"":"why",t.can?((showSkill?x.name:"")+(t.area?(showSkill?" · ":"")+t.area:"")+(t.level?" · level "+t.level:"")):cap(t.why||"Not available")));
  var running=x.current===t.id+"|"+t.extra;
  add(it,icon(t.icon,t.name,true),tt);
  it.appendChild(running?el("span","badge good","running"):btn("Start","small go",function(b){startTarget({skill:x.id,target:t.id,extra:t.extra,name:t.name},b)},!controls()||!t.can));
  if(has("queue")&&!running)it.appendChild(btn("+ Queue","small",function(){addStepSheet(x,t)},!controls()));
  return it;
}
function startTarget(r,b){var rec=store("recent:"+(S().character||""))||[];rec=rec.filter(function(x){return!(x.skill===r.skill&&x.target===r.target&&x.extra===r.extra)});rec.unshift({skill:r.skill,target:r.target,extra:r.extra,name:r.name});store("recent:"+(S().character||""),rec.slice(0,6));send("start",{skill:r.skill,target:r.target,extra:r.extra||""},"Start "+r.name,b)}

// ---- Farming
function farm(n){
  var f=parts.farm;n.appendChild(backBar("Farming",function(){ui.skill=null;render();window.scrollTo(0,0)},icon("skill/MelvorBase:Farming","F")));
  if(!f){n.appendChild(el("div","skel"));n.appendChild(el("div","skel"));return}
  if(!f.available){n.appendChild(el("div","card empty","Farming is not open for this character yet."));return}
  var ready=0;f.categories.forEach(function(c){c.plots.forEach(function(p){if(p.state==="ready")ready++})});
  if(ready){var hb=btn("Harvest all · "+ready+" ready","go",function(b){send("harvest",{plot:""},"Harvest all",b)},!controls());hb.style.width="100%";hb.style.marginBottom="12px";n.appendChild(hb)}
  var cats=f.categories.filter(function(c){return c.plots.length});if(!ui.farmCat||!cats.some(function(c){return c.id===ui.farmCat}))ui.farmCat=cats.length?cats[0].id:null;
  if(cats.length>1){var fc=el("div","chips");cats.forEach(function(c){var r=c.plots.filter(function(p){return p.state==="ready"}).length;var own=c.plots.filter(function(p){return p.state!=="locked"}).length;var b=el("button","chip"+(c.id===ui.farmCat?" on":""),c.name+(r?" · "+r+" ready":own?"":" · none yet"));b.type="button";b.addEventListener("click",function(){ui.farmCat=c.id;render()});fc.appendChild(b)});n.appendChild(fc)}
  var c=cats.filter(function(x){return x.id===ui.farmCat})[0];if(!c){n.appendChild(el("div","card empty","No plots yet."));return}
  var g=el("div","grid");var locked=[];
  c.plots.forEach(function(p){if(p.state==="locked"){locked.push(p);return}
    var b=el("button","plot "+(p.state==="ready"?"ready":p.state==="empty"?"empty":""));b.type="button";var top=el("div","top");add(top,p.cropItem?icon("item/"+p.cropItem,p.crop,true):null,el("b","",p.state==="empty"?"Empty":p.crop||p.name));b.appendChild(top);
    if(p.state==="ready")b.appendChild(el("span","badge good","Ready · tap"));
    else if(p.state==="empty")b.appendChild(el("span","","+ Plant"));
    else{var tl=el("span","num");tl.dataset.live="grow";tl.dataset.ms=p.msLeft||0;tl.dataset.at=Date.now();tl.textContent="Ready in "+dur(p.msLeft||0);b.appendChild(tl);b.appendChild(bar("thin ok",p.progress||0));if(p.state==="bonus")b.appendChild(el("span","","Giving its bonus"))}
    if(p.compost)b.appendChild(el("span","",p.compost));
    b.addEventListener("click",function(){if(p.state==="ready")send("harvest",{plot:p.id},"Harvest "+(p.crop||p.name),b);else if(p.state==="empty")seedSheet(c,p,f);else toast((p.crop||"Crop")+": "+(p.msLeft?"ready in "+dur(p.msLeft):"growing"),"wait")});g.appendChild(b)});
  n.appendChild(g);
  if(locked.length){var d=el("details","hist card");d.appendChild(el("summary","","Plots to buy ("+locked.length+")"));var l=el("div","list");locked.forEach(function(p){var it=el("div","it"),t=el("div","t");add(t,el("b","",p.name),el("span",p.why?"why":"num",p.why?cap(p.why):(p.costs||[]).map(function(x){return full(x.qty)+" "+x.name}).join(", ")));add(it,t,btn("Buy","small gold",function(b){confirmSheet("Buy "+p.name+"?",(p.costs||[]).map(function(x){return full(x.qty)+" "+x.name}).join(", ")+". This cannot be undone.","Buy","go",function(){send("buyplot",{plot:p.id},"Buy "+p.name)})},!controls()||!p.canBuy));l.appendChild(it)});d.appendChild(l);n.appendChild(d)}
}
function seedSheet(c,p,f){var key="seed:"+c.id,last=store(key);sheet(function(b){add(b,el("h2","","Plant in "+p.name),el("div","sub","Seeds from your bank. Compost goes in before the seed."));
  var comp="";var cs=(f.compost||[]).filter(function(x){return x.qty>0});if(cs.length){var ch=el("div","chips wrap");var none=el("button","chip on","No compost");none.type="button";ch.appendChild(none);none.addEventListener("click",function(){comp="";ch.querySelectorAll(".chip").forEach(function(z){z.classList.toggle("on",z===none)})});cs.forEach(function(x){var cc=el("button","chip",x.name+" ("+full(x.qty)+")");cc.type="button";cc.addEventListener("click",function(){comp=x.id;ch.querySelectorAll(".chip").forEach(function(z){z.classList.toggle("on",z===cc)})});ch.appendChild(cc)});b.appendChild(ch)}
  var l=el("div","list");var seeds=c.seeds.slice().sort(function(x,y){return(y.id===last?1:0)-(x.id===last?1:0)||(y.can?1:0)-(x.can?1:0)||(y.have>=y.need?1:0)-(x.have>=x.need?1:0)});if(!seeds.length)l.appendChild(el("div","empty","No seeds for this plot."));
  seeds.forEach(function(sd){var ok=sd.can&&sd.have>=sd.need;var it=el("div","it"+(ok?"":" off")),t=el("div","t");add(t,el("b","",sd.name),el("span",sd.can?"num":"why",sd.can?full(sd.have)+" "+(sd.seedName||"seeds")+" · needs "+sd.need+" · "+sd.minutes+" min":cap(sd.why)));add(it,icon("item/"+sd.product,sd.name,true),t,btn("Plant","small go",function(){store(key,sd.id);closeSheet();send("plant",{plot:p.id,seed:sd.id,compost:comp},"Plant "+sd.name)},!controls()||!ok));l.appendChild(it)});b.appendChild(l)})}

// ================================================================== COMBAT
function combat(s){
  var n=$("tab-combat");clear(n);
  var a=(s.actions||[]).filter(function(x){return x.combat})[0];if(a)n.appendChild(combatHud(a,s.combat));
  n.appendChild(seg([["fight","Fight"],["gear","Gear"],["buffs","Buffs"],["slayer","Slayer"]],ui.seg,function(id){ui.seg=id;remember();lastView="";wantParts();render()}));
  if(ui.seg==="fight")fightView(n);else if(ui.seg==="gear")gearView(n);else if(ui.seg==="buffs")buffsView(n);else slayerView(n);
}
function fightView(n){
  if(!cat){n.appendChild(el("div","skel"));return}
  var x=cat.skills.filter(function(k){return k.combat})[0];if(!x){n.appendChild(el("div","card empty","Combat is not open."));return}
  n.appendChild(seg([["avail","Can fight"],["all","All"]],ui.filter,function(f){ui.filter=f;render()}));
  var list=x.targets.filter(function(t){return ui.filter==="all"||t.can});
  var areas={},order=[];list.forEach(function(t){var key=t.extra==="dungeon"?"dungeon:"+t.id:t.extra==="slayer"?"slayer":t.extra;if(!areas[key]){areas[key]={name:t.extra==="dungeon"?t.name:t.extra==="slayer"?"Slayer tasks":(t.area||t.extra),list:[],id:t.extra};order.push(key)}areas[key].list.push(t)});
  if(!order.length){n.appendChild(el("div","card empty","Nothing can be fought right now."));return}
  if(!cat.advisor)n.appendChild(el("div","note","Install the Combat Advisor mod to see SAFE / RISKY / DEADLY here before you fight."));
  order.forEach(function(key){var a=areas[key],box=el("div","area"),head=el("button");head.type="button";var open=ui.openArea===key;add(head,el("span","nm",a.name),el("span","badge",a.list.length+(a.list.length===1?" monster":" monsters")),el("span","chev",open?"⌄":"›"));head.addEventListener("click",function(){ui.openArea=open?null:key;render()});box.appendChild(head);
    if(open){var body=el("div","body list");var adv=ui.advice[a.id];if(cat.advisor&&a.id&&a.id!=="dungeon"&&a.id!=="slayer"&&!adv){fetchAdvice(a.id);body.appendChild(el("div","note","Working out how safe each monster is..."))}
      a.list.forEach(function(t){body.appendChild(monsterRow(x,t,adv,false))});box.appendChild(body)}
    n.appendChild(box)});
}
function monsterRow(x,t,adv,showArea){
  var v=null;if(adv&&adv.monsters)adv.monsters.forEach(function(m){if(m.id===t.id)v=m});
  var it=el("div","it"+(t.can?"":" off")),tt=el("div","t");
  add(tt,el("b","",t.name+(t.level?" · Lv "+t.level:"")));
  if(!t.can)tt.appendChild(el("span","why",cap(t.why||"Not available")));
  else if(v)tt.appendChild(el("span","num",fmt(v.kills)+"/h · "+fmt(v.xp)+" XP/h · "+fmt(v.food)+" food/h"));
  else if(showArea&&t.area)tt.appendChild(el("span","",t.area));
  add(it,icon(t.icon,t.name,true),tt);if(v)it.appendChild(el("span","badge "+v.verdict.toUpperCase(),v.verdict.toUpperCase()));
  var running=x.current===t.id+"|"+t.extra;
  if(running)it.appendChild(el("span","badge good","fighting"));
  else it.appendChild(btn("Fight","small "+(v&&v.verdict==="Safe"?"go":v&&v.verdict==="Deadly"?"danger":"risk"),function(b){var r={skill:x.id,target:t.id,extra:t.extra,name:t.name};if(v&&v.verdict==="Safe")startTarget(r,b);else confirmSheet(v?v.verdict.toUpperCase()+": "+t.name:"Fight "+t.name+"?",v?v.why:"No safety estimate for this one. With auto-eat and food you are usually fine on monsters at your level.",v&&v.verdict==="Deadly"?"Fight anyway":"Fight",v&&v.verdict==="Deadly"?"danger":"risk",function(){startTarget(r,null)})},!controls()||!t.can));
  return it;
}
// ---- gear and loadouts
var SLOT_ORDER=["Helmet","Cape","Amulet","Weapon","Torso","Offhand","Ammo","Legs","Gloves","RingSlot1","Boots"];
function gearView(n){
  var g=parts.gear;if(!g){n.appendChild(el("div","skel s"));n.appendChild(el("div","skel"));n.appendChild(el("div","skel"));return}
  if(!g.available){n.appendChild(el("div","card empty","Gear is not available."));return}
  if(g.loadouts&&g.loadouts.length>1){n.appendChild(el("div","group","Loadout"));var ch=el("div","chips");g.loadouts.forEach(function(l){var c=el("button","chip"+(l.current?" on":""),(l.current?"✓ ":"")+l.name+(l.unlocked?"":" 🔒"));c.type="button";c.disabled=!l.unlocked||!controls();c.addEventListener("click",function(){if(!l.current)send("loadout",{loadout:l.id},"Loadout "+l.name,c)});ch.appendChild(c)});n.appendChild(ch);n.appendChild(el("div","note","Each loadout keeps its own gear, food, potion, prayers and spell. Changing gear changes the loadout that is on."))}
  n.appendChild(el("div","group","Equipment"));
  var slots=g.slots.slice().sort(function(a,b){return slotRank(a.id)-slotRank(b.id)});var gr=el("div","grid3");
  slots.forEach(function(sl){var b=el("button","slot"+(sl.item?"":" empty"));b.type="button";b.appendChild(el("small","",sl.name));if(sl.item){b.appendChild(icon("item/"+sl.item.id,sl.item.name,true));b.appendChild(rc(el("span","",sl.item.name),sl.item));if(sl.item.qty>1)b.appendChild(el("span","q num","× "+fmt(sl.item.qty)))}else b.appendChild(el("span","",sl.unlocked?"Empty":"Locked"));
    b.addEventListener("click",function(){slotSheet(sl,g)});gr.appendChild(b)});
  n.appendChild(gr);
}
function slotRank(id){var k=id.replace(/^.*:/,"");var i=SLOT_ORDER.indexOf(k);return i<0?99:i}
function slotSheet(sl,g){sheet(function(b){add(b,el("h2","",sl.name),el("div","sub",sl.item?"Worn now: "+sl.item.name+(sl.item.qty>1?" × "+full(sl.item.qty):""):"Nothing worn here."),rareBox(sl.item));
  if(sl.item&&!sl.occupied){var r=el("div","btns");r.appendChild(btn("Take off "+sl.item.name,"",function(){closeSheet();send("unequip",{slot:sl.id},"Take off "+sl.item.name)},!controls()));b.appendChild(r)}
  var opts=(g.options||{})[sl.id]||[];b.appendChild(el("div","group",opts.length?"From your bank":"Nothing in the bank fits here"));
  var l=el("div","list");opts.forEach(function(o){var it=el("div","it"+(o.can?"":" off")),t=el("div","t");add(t,rc(el("b","",o.name),o),o.rarity?el("span","",rareLine(o)):null,el("span",o.can?"num":"why",o.can?full(o.qty)+" in the bank":cap(o.why)),affixes(o));add(it,icon("item/"+o.id,o.name,true),t,btn(o.stack?"Equip all":"Equip","small go",function(){closeSheet();send("equip",{item:o.id,slot:sl.id,qty:o.stack?o.qty:1},"Equip "+o.name)},!controls()||!o.can));l.appendChild(it)});b.appendChild(l)})}
// ---- potions, prayers, spells
function buffsView(n){
  var bf=parts.buffs;if(!bf){n.appendChild(el("div","skel"));n.appendChild(el("div","skel"));return}
  if(!bf.available){n.appendChild(el("div","card empty","Not available."));return}
  // potions
  n.appendChild(el("div","group","Potions"));var pc=el("div","card list");
  (bf.belts||[]).forEach(function(belt,i){if(i>0&&!ui.allBelts)return;belt.slots.forEach(function(sl){var it=el("div","it"+(sl.unlocked?"":" off")),t=el("div","t");var sn=sl.name.indexOf(belt.name+" ")===0?sl.name.slice(belt.name.length+1):sl.name;add(t,el("b","",belt.name+(belt.slots.length>1?" · "+sn:"")),el("span","num",sl.item?sl.item.name+" × "+full(sl.item.qty):sl.unlocked?"No potion":"Locked"));add(it,sl.item?icon("item/"+sl.item.id,sl.item.name,true):icon("skill/"+belt.entity,belt.name,true),t,btn(sl.item?"Change":"Add","small",function(){potionSheet(belt,sl,bf)},!controls()||!sl.unlocked));pc.appendChild(it)})});
  n.appendChild(pc);if((bf.belts||[]).length>1){var tb=btn(ui.allBelts?"Show combat only":"Show skill potions too","small",function(){ui.allBelts=!ui.allBelts;render()});n.appendChild(tb)}
  // prayers
  var p=bf.prayers;if(p&&p.list&&p.list.length){var unl=p.slots.filter(function(x){return x.unlocked}).length;var sel=p.list.filter(function(x){return x.selected}).length;
    n.appendChild(el("div","group","Prayers · "+sel+" / "+unl+" on"+(p.points&&p.points[0]?" · "+full(p.points[0].qty)+" "+p.points[0].name:"")));
    function prow(x){var it=el("div","it"+(x.can?"":" off")),t=el("div","t");add(t,el("b","",x.name),el("span",x.can?"num":"why",x.can?x.cost+" each use"+(x.selected&&!x.active?" · no points: not working":""):cap(x.why)));add(it,icon("prayer/"+x.id,x.name,true),t,toggle(x.selected,"",function(tg){send("prayer",{prayer:x.id},(x.selected?"Stop ":"Pray ")+x.name,tg)},!controls()||(!x.can&&!x.selected)));return it}
    var pl=el("div","card list");p.list.filter(function(x){return x.can||x.selected}).forEach(function(x){pl.appendChild(prow(x))});if(!pl.firstChild)pl.appendChild(el("div","empty","No prayer is open yet."));n.appendChild(pl);
    var lockedP=p.list.filter(function(x){return!x.can&&!x.selected});if(lockedP.length){var d=el("details","hist card");d.appendChild(el("summary","","Not yet open ("+lockedP.length+")"));var ll=el("div","list");lockedP.forEach(function(x){ll.appendChild(prow(x))});d.appendChild(ll);n.appendChild(d)}}
  // magic
  var m=bf.magic;if(m&&m.spells&&m.spells.length){var cur=m.spells.filter(function(x){return x.id===m.selected})[0];n.appendChild(el("div","group","Magic"));var mc=el("div","card list");
    var it=el("div","it"),t=el("div","t");add(t,el("b","",cur?cur.name:"No spell"),el("span",cur&&cur.short?"bad":"",m.canUse?(cur?cur.runes:"Pick a spell"):"Your weapon and stance do not cast spells now"));add(it,cur?icon("spell/"+cur.id,cur.name,true):null,t,btn("Change","small",function(){spellSheet(m,false)},!controls()));mc.appendChild(it);
    if(m.imbuements&&m.imbuements.length){var im=m.imbuements.filter(function(x){return x.id===m.imbuement})[0];var it2=el("div","it"),t2=el("div","t");add(t2,el("b","",im?im.name:"No imbuement"),el("span","",im?im.runes:"Optional: extra effects for runes"));add(it2,im?icon("imbue/"+im.id,im.name,true):null,t2,btn("Change","small",function(){spellSheet(m,true)},!controls()));mc.appendChild(it2)}
    n.appendChild(mc)}
}
function potionSheet(belt,sl,bf){sheet(function(b){add(b,el("h2","",belt.name+": "+sl.name),el("div","sub","Potions in your bank that go here. All of the stack goes in; one is used per action or turn."));
  if(sl.item){var r=el("div","btns");r.appendChild(btn("Take out "+sl.item.name,"",function(){closeSheet();send("potion",{entity:belt.entity,slot:sl.id,item:""},"Take out potion")},!controls()));b.appendChild(r)}
  var l=el("div","list");var ps=(bf.potions||[]).filter(function(x){return x.fits===sl.fits});if(!ps.length)l.appendChild(el("div","empty","No potions for this in the bank."));
  ps.forEach(function(x){var it=el("div","it"+(x.can?"":" off")),t=el("div","t");add(t,el("b","",x.name),el("span",x.can?"num":"why",x.can?full(x.qty)+" in the bank":cap(x.why)));add(it,icon("item/"+x.id,x.name,true),t,btn("Use","small go",function(){closeSheet();send("potion",{entity:belt.entity,slot:sl.id,item:x.id},"Use "+x.name)},!controls()||!x.can));l.appendChild(it)});b.appendChild(l)})}
function spellSheet(m,imb){var list=imb?m.imbuements:m.spells,cur=imb?m.imbuement:m.selected;sheet(function(b){add(b,el("h2","",imb?"Imbuement":"Attack spell"),el("div","sub",imb?"Adds an effect while you have its runes.":"Runes come from your bank; the numbers in brackets are what you have."));
  if(imb&&cur){var r=el("div","btns");r.appendChild(btn("No imbuement","",function(){closeSheet();send("imbue",{spell:""},"No imbuement")},!controls()));b.appendChild(r)}
  var books={},order=[];list.forEach(function(x){var k=x.book||"Spells";if(!books[k]){books[k]=[];order.push(k)}books[k].push(x)});
  order.forEach(function(k){if(order.length>1)b.appendChild(el("div","group",k));var l=el("div","list");books[k].forEach(function(x){var ok=x.can&&x.available;var it=el("div","it"+(ok?"":" off")),t=el("div","t");add(t,el("b","",x.name),el("span",!x.can?"why":x.short?"bad":"",!x.can?cap(x.why):!x.available?"Needs a staff of its element":x.runes));add(it,icon((imb?"imbue/":"spell/")+x.id,x.name,true),t,x.id===cur?el("span","badge on","on"):btn("Use","small go",function(){closeSheet();send(imb?"imbue":"spell",{spell:x.id},"Use "+x.name)},!controls()||!ok));l.appendChild(it)});b.appendChild(l)})})}
// ---- Slayer
function slayerView(n){
  var sl=parts.slayer;if(!sl){n.appendChild(el("div","skel"));return}
  if(!sl.available){n.appendChild(el("div","card empty","Slayer is not open for this character yet."));return}
  var any=false;sl.masters.forEach(function(m){if(!m.can)return;any=true;var c=el("div","card"+(m.task?" accent":"")),h=el("div","ctitle");add(h,icon("master/"+m.id,m.name),add(el("div","t"),el("b","",m.name),el("span","num",full(m.coins)+" "+m.coin)));c.appendChild(h);
    if(m.task){var r=el("div","row");add(r,el("span","k",m.task.name+(m.task.areaName?" · "+m.task.areaName:"")),el("span","num",full(m.task.left)+" / "+full(m.task.total)+" left"));add(c,r,bar("ok",1-m.task.left/Math.max(1,m.task.total)));
      var bs=el("div","btns");bs.appendChild(m.task.fighting?btn("Fighting it now","",function(){},true):btn("Fight task","go",function(b){send("slayer",{master:m.id,op:"fight"},"Fight "+m.task.name,b)},!controls()));c.appendChild(bs);
      var b2=el("div","btns");add(b2,btn("Swap ("+fmt(m.reroll)+")","",function(){confirmSheet("Swap your "+m.name+" task?","You pay "+full(m.reroll)+" "+m.coin+" for a different monster.","Swap task","risk",function(){send("slayer",{master:m.id,op:"reroll"},"New task")})},!controls()||m.coins<m.reroll),btn(m.extended?"Extended":"Extend","",function(b){send("slayer",{master:m.id,op:"extend"},"Extend task",b)},!controls()||m.extended));c.appendChild(b2)}
    else{var bs2=el("div","btns");bs2.appendChild(btn("Get a task","go",function(b){send("slayer",{master:m.id,op:"new"},"Get a task",b)},!controls()));c.appendChild(bs2)}
    var ar=el("div","it");ar.style.marginTop="8px";add(ar,add(el("div","t"),el("b","","Auto Slayer"),el("span","","Moves on to the next task's monster by itself")),toggle(m.auto,"",function(tg){send("slayer",{master:m.id,op:"auto"},"Auto Slayer",tg)},!controls()));c.appendChild(ar);
    n.appendChild(c)});
  if(!any)n.appendChild(el("div","card empty","No Slayer master is open yet."));
}

// ================================================================== BANK
function bank(s){
  var n=$("tab-bank");clear(n);var b=s.bank;if(!b)return;
  var c=el("div","card accent");add(c,el("div","note","Gold"),el("div","big num",full(b.gold)));var r=el("div","row");add(r,el("span","k","Bank space"),el("span","num",full(b.used)+" / "+full(b.max)+" · "+full(Math.max(0,b.max-b.used))+" free"));add(c,r,bar(b.used>=b.max?"hp":"ok",b.used/Math.max(1,b.max)));n.appendChild(c);
  n.appendChild(seg([["items","Items"],["gains","Gains"],["food","Food"],["shop","Shop"]],ui.bankView,function(v){ui.bankView=v;remember();lastView="";wantParts();render()}));
  if(ui.bankView==="gains"){var l=el("div","card list");l.appendChild(el("div","note","Bank increases in the last 10 minutes (checked every few seconds)."));if(!b.recent.length)l.appendChild(el("div","empty","Nothing yet."));b.recent.forEach(function(x){var it=el("div","it"),t=el("div","t");add(t,el("b","",x.item),el("span","num",full(x.have)+" in the bank"));add(it,icon(x.id?"item/"+x.id:null,x.item,true),t,el("span","big num","+"+fmt(x.qty)));l.appendChild(it)});n.appendChild(l);return}
  if(ui.bankView==="shop"){shopView(n);return}
  if(!cat){n.appendChild(el("div","skel"));return}
  if(ui.bankView==="food"){var lf=el("div","card list");var foods=(cat.combat&&cat.combat.foods)||[];if(!foods.length)lf.appendChild(el("div","empty","No food in the bank."));foods.forEach(function(f){var it=el("div","it"),t=el("div","t");add(t,el("b","",f.name),el("span","num",full(f.count)+" · heals "+f.heal));var on=cat.combat.food===f.id;add(it,icon("item/"+f.id,f.name,true),t,on?el("span","badge good","in use"):btn("Use","small go",function(bb){send("food",{item:f.id},"Use "+f.name,bb)},!controls()));lf.appendChild(it)});n.appendChild(lf);return}
  // bulk actions (Bulk Sell mod)
  var mo=parts.more;if(has("bulk")&&mo&&mo.bulkNow){var bn=mo.bulkNow,ch=el("div","chips wrap");
    var j=el("button","chip","Sell junk"+(bn.junk?" ("+fmt(bn.junk)+")":""));j.type="button";j.disabled=!controls()||!bn.junk;j.addEventListener("click",function(){confirmSheet("Sell your junk?","Sells "+full(bn.junk)+" items of the "+bn.junkKinds+" kinds on your junk list"+(bn.junkWorth?", for about "+bn.junkWorth:"")+". Locked items stay. This cannot be undone.","Sell junk","danger",function(){send("bulk",{op:"junk"},"Sell junk")})});
    var bu=el("button","chip","Bury all"+(bn.bury?" ("+fmt(bn.bury)+")":""));bu.type="button";bu.disabled=!controls()||!bn.bury;bu.addEventListener("click",function(){confirmSheet("Bury everything buriable?",full(bn.bury)+" items"+(bn.buryReward?" for "+bn.buryReward:"")+". This cannot be undone.","Bury all","risk",function(){send("bulk",{op:"bury"},"Bury all")})});
    var op=el("button","chip","Open all"+(bn.open?" ("+fmt(bn.open)+")":""));op.type="button";op.disabled=!controls()||!bn.open;op.addEventListener("click",function(){confirmSheet("Open everything?",full(bn.open)+" chests and bags. It stops if the bank gets full.","Open all","go",function(){send("bulk",{op:"open"},"Open all")})});
    add(ch,j,bu,op);n.appendChild(ch)}
  var q=el("input","search");q.placeholder="Search the bank";q.value=ui.bankSearch;q.addEventListener("input",function(){ui.bankSearch=q.value;var p=q.selectionStart;lastView="";bank(S());var nq=document.querySelector("#tab-bank .search");nq.focus();nq.setSelectionRange(p,p)});n.appendChild(q);
  var items=cat.bank.filter(function(i){var q=(ui.bankSearch||"").toLowerCase();return!q||(i.name+" "+rareLine(i)).toLowerCase().indexOf(q)>=0}).sort(function(x,y){return y.qty*y.price-x.qty*x.price});
  var li=el("div","card list");if(!items.length)li.appendChild(el("div","empty","No items match."));
  var shown=ui.bankMore||60;items.slice(0,shown).forEach(function(i){var it=el("div","it"),t=el("div","t");add(t,rc(el("b","",i.name),i),i.rarity?el("span","",rareLine(i)):null,el("span","num",full(i.qty)+" · "+fmt(i.price)+" gold each"+(i.locked?" · locked":"")),affixes(i));add(it,icon("item/"+i.id,i.name,true),t,btn("Sell","small",function(){sellSheet(i)},!controls()||i.locked||!i.price));li.appendChild(it)});
  n.appendChild(li);if(items.length>shown){var more1=btn("Show more ("+full(items.length-shown)+")","",function(){ui.bankMore=shown+60;lastView="";render()});n.appendChild(more1)}
}
function sellSheet(i){sheet(function(b){add(b,el("h2","","Sell "+i.name),el("div","sub num","You have "+full(i.qty)+". About "+fmt(i.price)+" gold each (bonuses may add more). Selling cannot be undone."),rareBox(i));
  var f=el("div","field"),inp=el("input");inp.type="number";inp.inputMode="numeric";inp.min="1";inp.max=String(i.qty);inp.value="";inp.placeholder="How many?";f.appendChild(el("label","","How many"));f.appendChild(inp);b.appendChild(f);
  var quick=el("div","chips wrap");[1,10,100,1000].filter(function(v){return v<i.qty}).concat(i.qty>1?[i.qty-1]:[]).forEach(function(v){var c=el("button","chip",v===i.qty-1?"All but one":full(v));c.type="button";c.addEventListener("click",function(){inp.value=v;upd()});quick.appendChild(c)});b.appendChild(quick);
  var go1=btn("Sell","danger",function(){var v=Math.floor(+inp.value);if(!(v>=1&&v<=i.qty))return;closeSheet();send("sell",{item:i.id,qty:v},"Sell "+full(v)+" "+i.name)},true);
  function upd(){var v=Math.floor(+inp.value);var ok=v>=1&&v<=i.qty;go1.disabled=!ok||!controls();go1.textContent=ok?"Sell "+full(v)+" for about "+fmt(v*i.price)+" gold":"Sell"}
  inp.addEventListener("input",upd);var r=el("div","btns");add(r,btn("Cancel","",closeSheet),go1);b.appendChild(r)})}
// ---- the shop
function shopView(n){
  var sh=parts.shop;if(!sh){n.appendChild(el("div","skel"));n.appendChild(el("div","skel"));return}
  if(!sh.available){n.appendChild(el("div","card empty","The shop is not available."));return}
  var w=el("div","kit");w.style.margin="0 0 12px";sh.wallet.slice(0,4).forEach(function(c){w.appendChild(el("span","num",fmt(c.qty)+" "+c.name))});n.appendChild(w);
  var q=el("input","search");q.placeholder="Search the shop";q.value=ui.shopSearch;q.addEventListener("input",function(){ui.shopSearch=q.value;var p=q.selectionStart;lastView="";render();var nq=document.querySelector("#tab-bank .search");nq.focus();nq.setSelectionRange(p,p)});n.appendChild(q);
  var cats=sh.categories;if(!ui.shopCat||!cats.some(function(c){return c.id===ui.shopCat}))ui.shopCat=cats.length?cats[0].id:null;
  var ch=el("div","chips");var af=el("button","chip"+(ui.shopAfford?" on":""),"Can buy now");af.type="button";af.addEventListener("click",function(){ui.shopAfford=!ui.shopAfford;render()});ch.appendChild(af);
  if(!ui.shopSearch)cats.forEach(function(c){var b=el("button","chip"+(c.id===ui.shopCat?" on":""),c.name);b.type="button";b.addEventListener("click",function(){ui.shopCat=c.id;render()});ch.appendChild(b)});n.appendChild(ch);
  var s=ui.shopSearch.trim().toLowerCase();var items=[];var groups={};cats.forEach(function(c){var g={};c.items.forEach(function(x){g[x.group||""]=1});groups[c.id]=Object.keys(g).length>1});cats.forEach(function(c){if(!s&&c.id!==ui.shopCat)return;c.items.forEach(function(x){x.showGroup=groups[c.id];if(s&&(x.name+" "+(x.group||"")).toLowerCase().indexOf(s)<0)return;if(ui.shopAfford&&(!x.can||x.max<1))return;items.push(x)})});
  var l=el("div","card list");if(!items.length)l.appendChild(el("div","empty",s?"Nothing in the shop matches.":"Nothing here."));
  items.slice(0,80).forEach(function(x){var ok=x.can&&x.max>=1;var it=el("div","it"+(x.owned||!x.can?" off":"")),t=el("div","t");var price=x.costs.map(function(c){return fmt(c.qty)+" "+c.name}).join(" + ")||"Free";add(t,el("b","",x.name),el("span",x.owned?"":!x.can?"why":x.max<1?"bad":"num",(x.group&&x.showGroup?x.group+" · ":"")+(x.owned?"Bought":!x.can?cap(x.why):price+(x.limit>0?" · "+x.bought+"/"+x.limit:""))));add(it,icon(x.item?"item/"+x.item:"shop/"+x.id,x.name,true),t,x.owned?el("span","badge good","owned"):btn("Buy","small "+(ok?"gold":""),function(){buySheet(x)},!controls()||!ok));it.addEventListener("click",function(){if(x.desc)toast(x.desc,"wait",5000)});l.appendChild(it)});
  n.appendChild(l);
}
function buySheet(x){sheet(function(b){add(b,el("h2","",x.name),el("div","sub",x.desc||""));
  var l=el("div","list");x.costs.forEach(function(c){var it=el("div","it"),t=el("div","t");add(t,el("b","",full(c.qty)+" "+c.name),el("span",c.have>=c.qty?"num":"bad",full(c.have)+" owned"));add(it,c.item?icon("item/"+c.item,c.name,true):null,t);l.appendChild(it)});if(x.costs.length)b.appendChild(l);
  var qty=1;var go1=btn("","gold",function(){closeSheet();send("buy",{purchase:x.id,qty:qty},"Buy "+x.name)},!controls());
  function upd(){go1.textContent="Buy"+(qty>1?" "+full(qty):"")+" · "+(x.costs.map(function(c){return fmt(c.qty*qty)+" "+c.name}).join(" + ")||"free");go1.disabled=!controls()||qty<1||qty>x.max}
  if(x.bulk&&x.max>1){var f=el("div","field"),inp=el("input");inp.type="number";inp.inputMode="numeric";inp.min="1";inp.max=String(x.max);inp.value="1";f.appendChild(el("label","","How many (up to "+full(x.max)+")"));f.appendChild(inp);b.appendChild(f);inp.addEventListener("input",function(){qty=Math.floor(+inp.value)||0;upd()});
    var quick=el("div","chips wrap");[1,10,100].filter(function(v){return v<x.max}).concat([x.max]).forEach(function(v){var c=el("button","chip",v===x.max?"Max ("+full(v)+")":full(v));c.type="button";c.addEventListener("click",function(){qty=v;inp.value=v;upd()});quick.appendChild(c)});b.appendChild(quick)}
  upd();var r=el("div","btns");add(r,btn("Cancel","",closeSheet),go1);b.appendChild(r)})}

// ================================================================== MORE
function more(s){
  var n=$("tab-more");clear(n);
  if(ui.more==="queue"){queueView(n,s);return}if(ui.more==="team"){teamView(n,s);return}if(ui.more==="multi"){multiView(n);return}if(ui.more==="cheats"){cheatView(n);return}
  var q=s.queue,ad=s.adventurers,mo=parts.more;
  function entry(id,title,sub,svg,cls,flag){var b=el("button","more"+(cls?" "+cls:""));b.type="button";var mi=el("span","mi");mi.innerHTML=svg;add(b,mi,add(el("div","t"),el("b","",title),el("span","",sub)),flag?el("span","badge warn",flag):null,el("span","chev","›"));b.addEventListener("click",function(){ui.more=id;lastView="";wantParts();render();window.scrollTo(0,0)});n.appendChild(b)}
  if(has("queue"))entry("queue","Action Queue",q&&q.steps.length?(q.running?"Running":"Paused")+" · "+q.steps.length+" step"+(q.steps.length===1?"":"s"):"No steps yet",'<svg viewBox="0 0 24 24"><path d="M8 6h13M8 12h13M8 18h13"/><circle cx="4" cy="6" r="1"/><circle cx="4" cy="12" r="1"/><circle cx="4" cy="18" r="1"/></svg>',"",(s.problems||[]).some(function(p){return p.fix==="queue"})?"waiting":"");
  if(has("team"))entry("team","Adventurers",ad&&ad.team.length?ad.team.length+" hired · "+ad.team.filter(function(x){return x.status==="Hunting"}).length+" hunting":"None hired",'<svg viewBox="0 0 24 24"><circle cx="9" cy="8" r="3"/><path d="M3 20a6 6 0 0 1 12 0"/><circle cx="17" cy="9" r="2.5"/><path d="M15 15a5 5 0 0 1 6 5"/></svg>',"",ad&&ad.team.some(function(x){return/^Stopped/.test(x.status)})?"needs you":"");
  if(has("multitask"))entry("multi","Multitask",mo&&mo.slots?(mo.slots.extra?(1+mo.slots.extra)+" actions at once":"One action at a time"):"Run several actions at once",'<svg viewBox="0 0 24 24"><rect x="3" y="4" width="18" height="6" rx="1.5"/><rect x="3" y="14" width="18" height="6" rx="1.5"/></svg>');
  if(has("cheats"))entry("cheats","Cheat Menu","Opens closed: nothing changes until you switch something",'<svg viewBox="0 0 24 24"><path d="M12 3l2.5 5.5L20 9l-4 4 1 6-5-3-5 3 1-6-4-4 5.5-.5z"/></svg>',"cheat");
  n.appendChild(el("div","group","This phone"));var c=el("div","card list");
  var r1=el("div","it");add(r1,add(el("div","t"),el("b","","Sound and vibration"),el("span","","For warnings while this page is open")),toggle(sound,"",function(x){sound=!sound;x.classList.toggle("on",sound);if(sound)beep(440,.08)}));c.appendChild(r1);
  var r2=el("div","it");add(r2,add(el("div","t"),el("b","","Add to home screen"),el("span","","In the browser's menu: opens like an app, without the address bar")));c.appendChild(r2);n.appendChild(c);
}
function moreBack(title){return backBar(title,function(){ui.more=null;lastView="";render();window.scrollTo(0,0)})}
// ---- Action Queue
function queueView(n,s){
  var q=s.queue;n.appendChild(moreBack("Action Queue"));if(!q){n.appendChild(el("div","card empty","The Action Queue mod is not installed."));return}
  var run=q.steps.filter(function(x){return x.run==="Running"})[0];
  var c=el("div","card accent"),h=el("div","ctitle");add(h,add(el("div","t"),el("b","",q.running?"Running · step "+(q.steps.indexOf(run)+1)+" of "+q.steps.length:q.steps.length?"Paused · "+q.steps.length+" step"+(q.steps.length===1?"":"s"):"No steps yet"),el("span","",q.running?"Pausing stops the queue from moving on; what runs now keeps going.":q.steps.length?"Resume picks up where it stopped.":"Add steps, or load a saved queue.")));c.appendChild(h);
  var bs=el("div","btns");if(q.steps.length)bs.appendChild(btn(q.running?"Pause queue":"Resume queue",q.running?"":"go",function(b){send(q.running?"pausequeue":"resumequeue",{},q.running?"Pause queue":"Resume queue",b)},!controls()));bs.appendChild(btn("+ Add step","gold",function(){addStepSheet(null,null)},!controls()));c.appendChild(bs);
  var b2=el("div","btns");add(b2,btn("Saved queues","",function(){presetSheet()},!controls()),q.steps.length?btn("Save as…","",function(){saveSheet()},!controls()):null,q.steps.length?btn("Clear","danger",function(){confirmSheet("Clear "+q.steps.length+" step"+(q.steps.length===1?"":"s")+"?","What is running now keeps running. Saved queues are not touched.","Clear queue","danger",function(){send("qclear",{},"Clear queue")})},!controls()):null);c.appendChild(b2);n.appendChild(c);
  var l=el("div","card list");if(!q.steps.length)l.appendChild(el("div","empty","No steps yet."));
  var nextIdx=-1;q.steps.forEach(function(x,i){if(nextIdx<0&&x.run==="Idle")nextIdx=i});
  q.steps.forEach(function(x,i){var it=el("div","it"),t=el("div","t");add(t,el("b","",(i+1)+". "+x.name),el("span","",x.goal+(x.state?" · "+x.state:"")));add(it,icon("skill/"+x.skill,x.name,true),t,x.run==="Idle"?(i===nextIdx?el("span","badge","next"):null):el("span","badge "+(x.run==="Running"?"good":x.run==="Waiting"?"warn":""),x.run.toLowerCase()));
    var ctl=el("div","btns");ctl.style.margin="0 0 6px";ctl.style.justifyContent="flex-end";add(ctl,btn("↑","small",function(b){send("qmove",{index:i,by:-1},"Move step up",b)},!controls()||i===0),btn("↓","small",function(b){send("qmove",{index:i,by:1},"Move step down",b)},!controls()||i===q.steps.length-1),btn("Remove","small danger",function(b){if(x.run==="Running")confirmSheet("Remove the running step?",x.name+" keeps running in the game; the queue moves on without it.","Remove","danger",function(){send("qremove",{index:i},"Remove step")});else send("qremove",{index:i},"Remove step "+(i+1),b)},!controls()));
    l.appendChild(it);l.appendChild(ctl)});n.appendChild(l);
}
function presetSheet(){fetchCatalog();sheet(function(b){add(b,el("h2","","Load a saved queue"),el("div","sub","It replaces the steps in the queue now (the game keeps a copy of unsaved steps)."));var l=el("div","list"),p=(cat&&cat.presets)||[];if(!p.length)l.appendChild(el("div","empty",cat?"No saved queues yet.":"Loading..."));p.forEach(function(name){var it=el("div","it");add(it,add(el("div","t"),el("b","",name)),btn("Load","small go",function(){closeSheet();send("qload",{name:name},"Load "+name)},!controls()));l.appendChild(it)});b.appendChild(l)})}
function saveSheet(){sheet(function(b){add(b,el("h2","","Save this queue"),el("div","sub","Saves the steps now in the queue under a name. A saved queue with the same name is replaced."));var f=el("div","field"),inp=el("input");inp.maxLength=40;inp.placeholder="Name";f.appendChild(el("label","","Name"));f.appendChild(inp);b.appendChild(f);var go1=btn("Save","go",function(){var v=inp.value.trim();if(!v)return;closeSheet();send("qsave",{name:v},"Save queue")},!controls());var r=el("div","btns");add(r,btn("Cancel","",closeSheet),go1);b.appendChild(r);setTimeout(function(){inp.focus()},300)})}
function addStepSheet(x,t){
  fetchCatalog();
  sheet(function(b){
    add(b,el("h2","","Add a step"),el("div","sub","Do this until the goal is reached, then the queue moves on."));
    if(!cat){b.appendChild(el("div","empty","Loading..."));return}
    var fs=el("div","field"),sl=el("select");fs.appendChild(el("label","","Skill"));cat.skills.forEach(function(k){var o=el("option","",k.name);o.value=k.id;if(x&&x.id===k.id)o.selected=true;sl.appendChild(o)});fs.appendChild(sl);b.appendChild(fs);
    var ft=el("div","field"),tl=el("select");ft.appendChild(el("label","","Target"));ft.appendChild(tl);b.appendChild(ft);
    function fill(){clear(tl);var k=cat.skills.filter(function(s){return s.id===sl.value})[0];(k?k.targets:[]).forEach(function(tg){var o=el("option","",tg.name+(tg.area?" ("+tg.area+")":"")+(tg.can?"":" - locked"));o.value=tg.id+"|"+tg.extra;if(t&&t.id===tg.id&&t.extra===tg.extra)o.selected=true;tl.appendChild(o)})}
    sl.addEventListener("change",function(){t=null;fill()});fill();
    var goals=[["Item","Until I own"],["Made","Make this many more"],["Level","Until skill level"],["Minutes","For minutes"],["Times","This many times"],["Stops","Until it stops by itself"]],kind="Item";
    var fg=el("div","field");fg.appendChild(el("label","","Goal"));var sg=el("div","chips wrap");goals.forEach(function(g){var c=el("button","chip"+(g[0]===kind?" on":""),g[1]);c.type="button";c.addEventListener("click",function(){kind=g[0];sg.querySelectorAll(".chip").forEach(function(z){z.classList.toggle("on",z===c)});amt.hidden=kind==="Stops";upd()});sg.appendChild(c)});fg.appendChild(sg);b.appendChild(fg);
    var amt=el("div","field"),inp=el("input");inp.type="number";inp.inputMode="numeric";inp.min="1";inp.value="1000";amt.appendChild(el("label","","Number"));amt.appendChild(inp);b.appendChild(amt);
    var prev=el("div","note");b.appendChild(prev);
    function upd(){var o=tl.options[tl.selectedIndex];prev.textContent=o?o.text+(kind==="Stops"?" · until it stops by itself":" · "+goals.filter(function(g){return g[0]===kind})[0][1].toLowerCase().replace(/\bi\b/,"I")+" "+(inp.value||"?")):""}
    inp.addEventListener("input",upd);tl.addEventListener("change",upd);upd();
    var r=el("div","btns");add(r,btn("Cancel","",closeSheet),btn("Add to queue","go",function(){var o=tl.value.split("|");closeSheet();send("qadd",{skill:sl.value,target:o[0],extra:o.slice(1).join("|"),until:kind,amount:inp.value||"0"},"Add step")},!controls()));b.appendChild(r);
  });
}
// ---- Adventurers
function teamView(n,s){
  n.appendChild(moreBack("Adventurers"));var mo=parts.more,a=s.adventurers;
  var team=(mo&&mo.adventurers)||[];if(!mo){n.appendChild(el("div","skel"));return}
  if(!team.length){n.appendChild(el("div","card empty","No adventurers hired. Hire them in the PC game (the Adventurers window)."));return}
  team.slice().sort(function(x,y){return(/^Stopped/.test(y.status)?1:0)-(/^Stopped/.test(x.status)?1:0)}).forEach(function(x){var c=el("div","card"),h=el("div","ctitle");var bad=/^Stopped/.test(x.status);add(h,icon(x.enemy?"enemy/"+x.enemy:null,x.name),add(el("div","t"),el("b","",x.name),el("span","",x.target||(x.dungeon?"In a dungeon":"No target"))),el("span","badge "+(bad?"bad":x.working?"good":"warn"),bad?"stopped":x.working?"hunting":"resting"));c.appendChild(h);if(bad)c.appendChild(el("div","prob warn",x.status));
    var st=el("div","stat3");add(st,add(el("div",""),el("small","","KILLS"),el("b","num",fmt(x.kills))),add(el("div",""),el("small","","IN BAG"),el("b","num",fmt(x.bag))),add(el("div",""),el("small","","FOOD"),el("b","",x.food?fmt(x.foodInBank)+" "+x.food:"none")));c.appendChild(st);
    var bs=el("div","btns");add(bs,x.working?btn("Recall","",function(b){send("adv",{who:x.id,op:"recall"},"Recall "+x.name,b)},!controls()):btn("Resume","go",function(b){send("adv",{who:x.id,op:"resume"},"Resume "+x.name,b)},!controls()),btn("Food","",function(){advFoodSheet(x)},!controls()),btn("Target","gold",function(){advTargetSheet(x)},!controls()));c.appendChild(bs);
    if(x.bag>0){var b3=el("div","btns");b3.appendChild(btn("Put the loot in the bank ("+fmt(x.bag)+")","",function(b){send("adv",{who:x.id,op:"collect"},"Collect loot",b)},!controls()));c.appendChild(b3)}
    n.appendChild(c)});
  n.appendChild(el("div","note","Hiring, gear and dungeons stay in the PC game's Adventurers window."));
}
function advFoodSheet(x){fetchCatalog();sheet(function(b){add(b,el("h2","","Food for "+x.name),el("div","sub","They eat it from your bank while they hunt."));var l=el("div","list");var foods=(cat&&cat.combat&&cat.combat.foods)||[];if(!foods.length)l.appendChild(el("div","empty",cat?"No food in the bank.":"Loading..."));foods.forEach(function(f){var it=el("div","it"),t=el("div","t");add(t,el("b","",f.name),el("span","num",full(f.count)+" in the bank · heals "+f.heal));add(it,icon("item/"+f.id,f.name,true),t,x.foodId===f.id?el("span","badge good","in use"):btn("Give","small go",function(){closeSheet();send("adv",{who:x.id,op:"food",a:f.id},x.name+": "+f.name)},!controls()));l.appendChild(it)});b.appendChild(l)})}
function advTargetSheet(x){fetchCatalog();sheet(function(b){add(b,el("h2","","Send "+x.name),el("div","sub","Pick a monster for them to hunt."));if(!cat){b.appendChild(el("div","empty","Loading..."));return}var cx=cat.skills.filter(function(k){return k.combat})[0];var q=el("input","search");q.placeholder="Find a monster";b.appendChild(q);var l=el("div","list");b.appendChild(l);
  function fill(){clear(l);var s=q.value.trim().toLowerCase();var n2=0;(cx?cx.targets:[]).forEach(function(t){if(t.extra==="dungeon"||t.extra==="slayer"||n2>=40)return;if(s&&(t.name+" "+(t.area||"")).toLowerCase().indexOf(s)<0)return;n2++;var it=el("div","it"),tt=el("div","t");add(tt,el("b","",t.name+(t.level?" · Lv "+t.level:"")),el("span","",t.area||""));add(it,icon(t.icon,t.name,true),tt,btn("Send","small go",function(){closeSheet();send("adv",{who:x.id,op:"send",a:t.extra,b:t.id},"Send "+x.name)},!controls()));l.appendChild(it)});if(!n2)l.appendChild(el("div","empty","No monsters match."))}
  q.addEventListener("input",fill);fill()})}
// ---- Multitask
function multiView(n){
  n.appendChild(moreBack("Multitask"));var mo=parts.more;if(!mo||!mo.slots){n.appendChild(el("div","skel"));return}var sl=mo.slots;
  var c=el("div","card accent");add(c,el("div","note","Actions at once"),el("div","big num",String(1+sl.extra)));
  var step=el("div","btns");add(step,btn("−","",function(b){send("slots",{extra:sl.extra-1},"Fewer slots",b)},!controls()||sl.extra<=0),btn("+","go",function(b){send("slots",{extra:sl.extra+1},"More slots",b)},!controls()||sl.extra>=sl.max));c.appendChild(step);
  var pre=el("div","chips wrap");pre.style.marginTop="10px";[[0,"Off"],[1,"2"],[2,"3"],[sl.max,"All"]].forEach(function(p){var b=el("button","chip"+(sl.extra===p[0]?" on":""),p[1]);b.type="button";b.disabled=!controls();b.addEventListener("click",function(){if(sl.extra!==p[0])send("slots",{extra:p[0]},"Multitask "+p[1],b)});pre.appendChild(b)});c.appendChild(pre);
  c.appendChild(el("div","note","With more than one, starting something keeps what ran before going. Fewer slots stop the actions that no longer fit."));n.appendChild(c);
  var l=el("div","card list");(sl.recent||[]).slice(0,1+sl.extra).forEach(function(k,i){var it=el("div","it"),t=el("div","t");add(t,el("b","","Slot "+(i+1)+": "+k.name),el("span",/^waiting/.test(k.state||"")?"why":"",k.state||""));it.appendChild(t);if(sl.canDrop)it.appendChild(btn("✕","small danger",function(b){send("slotdrop",{index:i},"Take "+k.name+" off",b)},!controls()));l.appendChild(it)});if(l.firstChild)n.appendChild(l);
  if(sl.wait!=null){var w=el("div","card"),r=el("div","it");r.style.padding="0";add(r,add(el("div","t"),el("b","","Wait for materials"),el("span","",sl.wait?"An action whose materials ran out stays on the list and starts again when they are back.":"An action whose materials ran out is dropped from the list.")),toggle(sl.wait,"",function(tg){send("slotwait",{on:sl.wait?0:1},"Wait for materials",tg)},!controls()));w.appendChild(r);n.appendChild(w)}
}
// ---- Cheat Menu (opens closed; nothing changes until a switch is pressed)
function cheatView(n){
  n.appendChild(moreBack("Cheat Menu"));var mo=parts.more;
  if(!ui.cheatsShown){var c=el("div","card cheatzone");add(c,el("div","ctitle"),el("div","note","Cheats change how the game plays for this character (never your save file). They show here only after you open them."));c.firstChild.appendChild(add(el("div","t"),el("b","","Cheat Menu")));var bs=el("div","btns");bs.appendChild(btn("Show cheats","cheat",function(){ui.cheatsShown=true;render()}));c.appendChild(bs);n.appendChild(c);return}
  if(!mo){n.appendChild(el("div","skel"));return}var list=mo.cheatList||[];if(!list.length){n.appendChild(el("div","card empty","The Cheat Menu is not ready for this character yet."));return}
  var tabs=[],names={bank:"Bank",xp:"XP",speed:"Speed",skills:"Skills",combat:"Combat",perskill:"Per skill"};list.forEach(function(f){if(tabs.indexOf(f.tab)<0)tabs.push(f.tab)});if(!ui.cheatTab||tabs.indexOf(ui.cheatTab)<0)ui.cheatTab=tabs[0];
  var on=list.filter(function(f){return f.value!==f.off}).length;n.appendChild(el("div","note",on?on+" cheat"+(on===1?" is":"s are")+" on.":"No cheats are on."));
  var ch=el("div","chips");tabs.forEach(function(t){var b=el("button","chip"+(t===ui.cheatTab?" on":""),names[t]||cap(t));b.type="button";b.addEventListener("click",function(){ui.cheatTab=t;render()});ch.appendChild(b)});n.appendChild(ch);
  var l=el("div","card list cheatzone");list.filter(function(f){return f.tab===ui.cheatTab}).forEach(function(f){var it=el("div","it"),t=el("div","t");var active=f.value!==f.off;add(t,el("b","",f.name),el("span","",f.hint||(f.kind==="Toggle"?"":"Normal: "+f.off+f.suffix+" · "+f.min+"–"+f.max+f.suffix)));it.appendChild(t);
    if(f.kind==="Toggle")it.appendChild(toggle(active,"cheat",function(tg){send("cheat",{cheat:f.id,value:active?0:1},f.name,tg)},!controls()));
    else{var sp=el("div","step"),inp=el("input");inp.type="number";inp.inputMode="decimal";inp.min=f.min;inp.max=f.max;inp.step=f.step;inp.value=f.value;add(sp,inp,btn("Set","small cheat",function(b){var v=+inp.value;if(isNaN(v))return;send("cheat",{cheat:f.id,value:Math.min(f.max,Math.max(f.min,v))},f.name,b)},!controls()));it.appendChild(sp);if(active){var rs=btn("Reset","small",function(b){send("cheat",{cheat:f.id,value:f.off},f.name+" reset",b)},!controls());it.appendChild(rs)}}
    l.appendChild(it)});n.appendChild(l);
}

// ---- the action bars move between snapshots (the game sends progress once a second)
function animate(){if(!stale())document.querySelectorAll(".bar.live").forEach(function(b){var p=+b.dataset.p,sec=+b.dataset.sec;if(!(sec>0))return;var x=(p+(Date.now()-recvAt)/1000/sec)%1;b.firstChild.style.width=(x*100)+"%"});requestAnimationFrame(animate)}
if(!window.matchMedia||!window.matchMedia("(prefers-reduced-motion: reduce)").matches)requestAnimationFrame(animate);

if(demo){
  var t0=Date.now();data={now:t0,controls:true,state:{ready:true,character:"Falor",taken:t0,has:{queue:true,team:true,advisor:true,slots:2,bulk:true,multitask:true,cheats:true},
  actions:[{skill:"Combat",skillId:"MelvorBase:Combat",target:"Hobgolbin",combat:true,byQueue:false},{skill:"Woodcutting",skillId:"MelvorBase:Woodcutting",target:"Yew Tree",targetId:"MelvorBase:YewTree",combat:false,progress:.62,seconds:3.2,level:87,levelProgress:.41,toNext:184211,byQueue:true}],
  combat:{hp:312,maxHp:420,enemy:"Hobgolbin",enemyId:"MelvorBase:Hobgolbin",enemyHp:41,enemyMaxHp:70,area:"Golbin Forest",food:"Shrimp",foodId:"MelvorBase:Shrimp",foodCount:18,heal:15,eatAt:105,stance:"Slash"},
  fightKit:{loadout:"Melee",potion:"Melee Potion I",potionQty:42,prayers:"Thick Skin",slayer:"Kuren: 18 Hobgolbin left"},farm:{ready:2,growing:3,empty:1,nextMs:740000},
  queue:{running:true,note:"",count:3,steps:[{index:0,name:"Woodcutting: Yew Tree",goal:"until 5,000 Yew Logs in the bank",state:"3,120 / 5,000",run:"Running",skill:"MelvorBase:Woodcutting"},{index:1,name:"Firemaking: Yew Logs",goal:"until it stops by itself",state:"",run:"Idle",skill:"MelvorBase:Firemaking"},{index:2,name:"Cooking: Lobster",goal:"until 500 more Lobster are made",state:"needs Raw Lobster",run:"Waiting",skill:"MelvorBase:Cooking"}]},
  adventurers:{team:[{name:"Soren",target:"Golbin",status:"Hunting",kills:1234,carried:388},{name:"Mira",target:"Hobgolbin",status:"Stopped: out of food",kills:610,carried:120}]},
  bank:{gold:120513224,used:364,max:10100,recent:[{item:"Yew Logs",id:"MelvorBase:YewLogs",qty:412,have:9120},{item:"Bones",qty:88,have:1255}]},
  alerts:[{seq:1,time:t0-260000,text:"Mira stopped: out of food",kind:"warn"},{seq:2,time:t0-60000,text:"Food is low: 18 Shrimp left.",kind:"warn"}],answers:[],
  problems:[{key:"food",level:"warn",text:"Food is low: 18 Shrimp left.",fix:"food"},{key:"farm",level:"info",text:"Farming: 2 ready to harvest.",fix:"farm"}]}};
  cat={version:1,character:"Falor",advisor:true,presets:["Morning grind","Slayer night"],
  skills:[{id:"MelvorBase:Combat",name:"Combat",combat:true,running:true,current:"MelvorBase:Hobgolbin|MelvorBase:GolbinForest",targets:[{id:"MelvorBase:Golbin",extra:"MelvorBase:GolbinForest",area:"Golbin Forest",name:"Golbin",level:1,can:true},{id:"MelvorBase:Hobgolbin",extra:"MelvorBase:GolbinForest",area:"Golbin Forest",name:"Hobgolbin",level:12,can:true},{id:"MelvorBase:Treant",extra:"MelvorBase:MistyThicket",area:"Misty Thicket",name:"Treant",level:38,can:true}]},
   {id:"MelvorBase:Woodcutting",name:"Woodcutting",level:87,xp:.41,running:true,current:"MelvorBase:YewTree|",targets:[{id:"MelvorBase:OakTree",extra:"",name:"Oak Tree",can:true},{id:"MelvorBase:YewTree",extra:"",name:"Yew Tree",can:true}]},
   {id:"MelvorBase:Mining",name:"Mining",level:62,xp:.7,targets:[]},{id:"MelvorBase:Fishing",name:"Fishing",level:55,xp:.2,targets:[]},{id:"MelvorBase:Cooking",name:"Cooking",level:70,xp:.5,targets:[]},{id:"MelvorBase:Smithing",name:"Smithing",level:48,xp:.9,targets:[]},{id:"MelvorBase:Thieving",name:"Thieving",level:33,xp:.1,targets:[]},{id:"MelvorBase:Agility",name:"Agility",level:21,xp:.3,targets:[]}],
  combat:{stance:"MelvorBase:Slash",stances:[{id:"MelvorBase:Stab",name:"Stab"},{id:"MelvorBase:Slash",name:"Slash"},{id:"MelvorBase:Block",name:"Block"}],food:"MelvorBase:Shrimp",foods:[{id:"MelvorBase:Shrimp",name:"Shrimp",count:18,heal:15},{id:"MelvorBase:Lobster",name:"Lobster",count:840,heal:60}]},
  bank:[{id:"MelvorBase:YewLogs",name:"Yew Logs",qty:9120,price:40},{id:"MelvorBase:Bones",name:"Bones",qty:1255,price:1},{id:"MelvorBase:Lobster",name:"Lobster",qty:840,price:30,food:true},{id:"MelvorBase:OsR1a2b",name:"Gloom Guard",qty:1,price:9000,rarity:"Rare",color:"ffd94a",ilvl:42,base:"Mithril Shield",affixes:["+38 max HP","+9% damage reduction","+12% fire resistance"]}]};
  parts.gear={available:true,loadouts:[{id:"L1",name:"Melee",current:true,unlocked:true},{id:"L2",name:"Ranged",current:false,unlocked:true},{id:"L3",name:"Loadout 3",current:false,unlocked:false}],slots:[{id:"MelvorBase:Helmet",name:"Helmet",unlocked:true,item:{id:"MelvorBase:IronHelmet",name:"Iron Helmet",qty:1}},{id:"MelvorBase:Cape",name:"Cape",unlocked:true,item:null},{id:"MelvorBase:Amulet",name:"Amulet",unlocked:true,item:null},{id:"MelvorBase:Weapon",name:"Weapon",unlocked:true,item:{id:"MelvorBase:SteelSword",name:"Steel Sword",qty:1}},{id:"MelvorBase:Torso",name:"Body",unlocked:true,item:{id:"MelvorBase:IronPlatebody",name:"Iron Platebody",qty:1}},{id:"MelvorBase:Offhand",name:"Shield",unlocked:true,item:null},{id:"MelvorBase:Ammo",name:"Ammo",unlocked:true,item:null},{id:"MelvorBase:Legs",name:"Legs",unlocked:true,item:{id:"MelvorBase:IronPlatelegs",name:"Iron Platelegs",qty:1}},{id:"MelvorBase:Gloves",name:"Gloves",unlocked:true,item:null},{id:"MelvorBase:RingSlot1",name:"Ring",unlocked:true,item:null},{id:"MelvorBase:Boots",name:"Boots",unlocked:true,item:null}],options:{"MelvorBase:Weapon":[{id:"MelvorBase:OsR3c4d",name:"Keen Mithril Sword of the Fox",qty:1,can:true,rarity:"Magic",color:"73a8ff",ilvl:35,base:"Mithril Sword",affixes:["+14% max hit","+6% attack speed"]},{id:"MelvorBase:MithrilSword",name:"Mithril Sword",qty:1,can:true},{id:"MelvorBase:RuneSword",name:"Rune Sword",qty:1,can:false,why:"needs Attack level 40"}]}};
  parts.buffs={available:true,belts:[{entity:"MelvorBase:Combat",name:"Combat",slots:[{id:"MelvorBase:CombatPotion",name:"Potion",fits:"P",unlocked:true,item:{id:"MelvorBase:MeleePotion",name:"Melee Potion I",qty:42}}]}],potions:[],prayers:{points:[{name:"Prayer Points",qty:1240}],slots:[{id:"S1",name:"CombatSlot1",fits:"C",unlocked:true},{id:"S2",name:"CombatSlot2",fits:"C",unlocked:false}],list:[{id:"P1",name:"Thick Skin",fits:"C",cost:"1 Prayer Points",selected:true,active:true,can:true},{id:"P2",name:"Burst of Strength",fits:"C",cost:"1 Prayer Points",selected:false,active:false,can:true},{id:"P3",name:"Rapid Heal",fits:"C",cost:"2 Prayer Points",selected:false,active:false,can:false,why:"needs Prayer level 20"}]},magic:{canUse:false,selected:"",imbuement:"",spells:[{id:"W1",name:"Wind Strike",book:"Standard",available:false,runes:"1 Air Rune (300)",short:false,can:true}],imbuements:[]}};
  parts.slayer={available:true,masters:[{id:"K",name:"Kuren",can:true,coin:"Slayer Coins",coins:5230,reroll:250,extended:false,auto:true,task:{id:"MelvorBase:Hobgolbin",name:"Hobgolbin",left:18,total:30,areaName:"Golbin Forest",fighting:true}},{id:"A",name:"Allevia",can:true,coin:"Slayer Coins",coins:5230,reroll:750,extended:false,auto:false,task:null}]};
  parts.farm={available:true,compost:[{id:"C",name:"Compost",qty:40}],categories:[{id:"A",name:"Allotments",seeds:[{id:"S1",name:"Potatoes",product:"MelvorBase:Potato",seedName:"Potato Seeds",need:3,have:120,minutes:5,can:true}],plots:[{id:"p1",name:"Plot 1",state:"ready",crop:"Potatoes",cropItem:"MelvorBase:Potato"},{id:"p2",name:"Plot 2",state:"growing",crop:"Onions",cropItem:"MelvorBase:Onion",msLeft:740000,progress:.4},{id:"p3",name:"Plot 3",state:"empty"},{id:"p4",name:"Plot 4",state:"ready",crop:"Potatoes",cropItem:"MelvorBase:Potato"},{id:"p5",name:"Plot 5",state:"locked",costs:[{name:"Gold Pieces",qty:5000,have:1e8}],canBuy:true}]},{id:"H",name:"Herbs",seeds:[],plots:[{id:"h1",name:"Herb 1",state:"growing",crop:"Guam",msLeft:1800000,progress:.1}]}]};
  parts.shop={available:true,wallet:[{id:"G",name:"Gold Pieces",qty:120513224},{id:"S",name:"Slayer Coins",qty:5230}],categories:[{id:"c1",name:"General",items:[{id:"x1",name:"Extra Bank Slot",desc:"+1 bank space",bulk:true,bought:12,limit:0,owned:false,costs:[{name:"Gold Pieces",qty:4500,have:120513224}],max:9999,can:true},{id:"x2",name:"Battle Loadout",bulk:false,bought:0,limit:1,owned:false,costs:[{name:"Gold Pieces",qty:500000,have:120513224}],max:1,can:true}]},{id:"c2",name:"Slayer",items:[]}]};
  parts.more={team:true,adventurers:[{id:"a1",name:"Soren",status:"Hunting",working:true,enemy:"MelvorBase:Golbin",target:"Golbin",food:"Shrimp",foodId:"MelvorBase:Shrimp",foodInBank:18,kills:1234,bag:388},{id:"a2",name:"Mira",status:"Stopped: out of food",working:false,target:"Hobgolbin",kills:610,bag:0}],bulk:true,bulkNow:{junk:340,junkKinds:6,junkWorth:"12,400 Gold Pieces",bury:1255,buryReward:"",open:12},multitask:true,slots:{extra:1,max:15,recent:[{name:"Woodcutting: Yew Tree",state:"running"},{name:"Combat: Hobgolbin",state:"running"}]},cheats:true,cheatList:[{id:"combat.god",tab:"combat",name:"God mode",kind:"Toggle",min:0,max:1,step:1,off:0,suffix:"",value:0},{id:"xp.multiplier",tab:"xp",name:"XP multiplier",kind:"Number",min:1,max:1000,step:1,off:1,suffix:"×",value:1}]};
  partRaw={gear:"1",buffs:"1",slayer:"1",farm:"1",shop:"1",more:"1"};
  ui.advice["MelvorBase:GolbinForest"]={available:true,monsters:[{id:"MelvorBase:Golbin",verdict:"Safe",kills:535,xp:3300,skill:"Strength",food:0,why:""},{id:"MelvorBase:Hobgolbin",verdict:"Safe",kills:426,xp:9200,skill:"Strength",food:71,why:""}]};
  recvAt=t0;var qs=function(k){var m=new RegExp(k+"=(\\w+)").exec(location.search);return m?m[1]:null};
  if(qs("seg"))ui.seg=qs("seg");if(qs("skill"))ui.skill=qs("skill")==="farming"?"farming":"MelvorBase:"+qs("skill");if(qs("area"))ui.openArea="MelvorBase:"+qs("area");if(qs("bank"))ui.bankView=qs("bank");if(qs("more"))ui.more=qs("more");if(qs("cheats"))ui.cheatsShown=true;
  go(qs("tab")||"now");alerts();var sh=qs("sheet");if(sh==="slot")slotSheet(parts.gear.slots[3],parts.gear);if(sh==="buy")buySheet(parts.shop.categories[0].items[0]);if(sh==="seed")seedSheet(parts.farm.categories[0],parts.farm.categories[0].plots[2],parts.farm)
}else{
  // a screen named in the address ("#v=combat.gear", "#v=skills.farming", "#v=more.queue"): the page opens there
  var vm=/[#&]v=([a-z]+)(?:\.([a-z]+))?/.exec(location.hash);
  if(vm&&TABS.indexOf(vm[1])>=0){tab=vm[1];var sub=vm[2]||"";if(tab==="combat"&&sub)ui.seg=sub;if(tab==="bank"&&sub)ui.bankView=sub;if(tab==="more"&&sub)ui.more=sub==="multitask"?"multi":sub;if(tab==="skills"&&sub)ui.skill=sub==="farming"?"farming":"MelvorBase:"+cap(sub)}
  var am=/[#&]a=([A-Za-z]+)/.exec(location.hash);if(am)ui.openArea="MelvorBase:"+am[1];
  pairFirst().then(function(){go(tab);poll()});
}
})();
</script>
</body>
</html>
""";
}
