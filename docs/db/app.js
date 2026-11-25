// Melvor Idle 2 item database. Plain JS, no framework. Data: data.json, built by Dev/ItemDb/build.py.
import { svg, glyphOf } from './glyphs.js';

const $ = (s, r = document) => r.querySelector(s);
const $$ = (s, r = document) => [...r.querySelectorAll(s)];
const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const fmt = (n) => (n == null ? '' : Math.round(n).toLocaleString('en-US'));
const LS = { get(k, d) { try { return JSON.parse(localStorage.getItem(k)) ?? d; } catch { return d; } }, set(k, v) { try { localStorage.setItem(k, JSON.stringify(v)); } catch { /* private mode */ } } };

let DB = null;
const byId = new Map();
const monsterById = new Map();
const areaById = new Map();

// ------------------------------------------------------------------ categories and columns

const TOOL_CATS = ['Tool', 'Utility', 'Trap', 'Bait', 'Compost'];
const MAT_CATS = ['Material', 'Bones', 'Openable'];
const CATS = [
  { id: 'all', name: 'All', glyph: 'all', test: () => true },
  { id: 'weapons', name: 'Weapons', glyph: 'sword', test: (i) => i.cat === 'Weapon' },
  { id: 'armour', name: 'Armour', glyph: 'helmet', test: (i) => i.cat === 'Armour' },
  { id: 'ammo', name: 'Ammo', glyph: 'ammo', test: (i) => i.cat === 'Ammo' },
  { id: 'food', name: 'Food', glyph: 'food', test: (i) => i.cat === 'Food' },
  { id: 'potions', name: 'Potions', glyph: 'potion', test: (i) => i.cat === 'Potion' },
  { id: 'tools', name: 'Tools & utility', glyph: 'tool', test: (i) => TOOL_CATS.includes(i.cat) },
  { id: 'materials', name: 'Materials', glyph: 'material', test: (i) => MAT_CATS.includes(i.cat) },
  // the One Style mod's unique items and rarities (Dev/ItemDb/onestyle.py)
  { id: 'onestyle', name: 'One Style', glyph: 'ring', test: (i) => i.mod === 'OneStyle' },
];
// One Style's rarity colours, as in the game
const RARITY = { Magic: '#5a8dff', Rare: '#ffd84a', Rare4: '#ffd84a', Unique: '#ea8538' };
const nameStyle = (i) => (i.rarity ? ` style="color:${RARITY[i.rarity]}"` : '');

const S = (key) => (i) => i.stats?.[key] ?? 0;
const COL = {
  name: { label: 'Item', get: (i) => i.name, cls: 'c-name', render: (i) => nameCell(i) },
  kind: { label: 'Type', get: (i) => i.kind, render: (i) => esc(i.kind) },
  lvl: { label: 'Level', get: (i) => i.lvl, num: true, render: (i) => reqCell(i) },
  sell: { label: 'Sell', get: (i) => i.sell, num: true, render: (i) => fmt(i.sell) },
  style: { label: 'Style', get: (i) => i.style || '', render: (i) => (i.style ? `<span class="badge ${i.style.toLowerCase()}">${esc(i.style)}</span>` : '') },
  hands: { label: 'Hands', get: (i) => i.hands || 0, render: (i) => (i.hands ? `${i.hands}H` : '') },
  speed: { label: 'Speed', get: (i) => i.stats?.AttackInterval ?? 0, num: true, render: (i) => (i.stats?.AttackInterval ? (i.stats.AttackInterval / 1000).toFixed(1) + 's' : '') },
  power: { label: 'Power', get: S('Power'), num: true, bar: true },
  atk: { label: 'Best attack', get: (i) => i.atk, num: true, bar: true },
  str: { label: 'Strength', get: (i) => i.str, num: true, bar: true },
  mdef: { label: 'Melee def', get: (i) => i.mdef, num: true, bar: true },
  rdef: { label: 'Ranged def', get: (i) => i.rdef, num: true, bar: true },
  gdef: { label: 'Magic def', get: (i) => i.gdef, num: true, bar: true },
  prot: { label: 'Protection', get: S('Protection'), num: true, bar: true },
  rstr: { label: 'Ranged str', get: S('RangedStrengthBonus'), num: true, bar: true },
  heal: { label: 'Heals', get: (i) => i.heal ?? 0, num: true, bar: true },
  bonus: { label: 'Effect', get: (i) => (i.bonuses || [])[0] || '', render: (i) => `<span class="muted">${esc(shorten((i.bonuses || [])[0] || '', 70))}</span>` },
  tool: { label: 'Slot', get: (i) => i.tool || i.cat, render: (i) => esc(i.tool || i.cat) },
  src: { label: 'Sources', get: (i) => i.src.length, num: true },
  use: { label: 'Uses', get: (i) => i.use.length, num: true },
  ammo: { label: 'Ammo', get: (i) => i.ammo || '', render: (i) => esc(i.ammo || '') },
};
const COLS = {
  all: ['name', 'kind', 'lvl', 'src', 'use', 'sell'],
  weapons: ['name', 'kind', 'style', 'lvl', 'speed', 'atk', 'str', 'power', 'sell'],
  armour: ['name', 'kind', 'lvl', 'mdef', 'rdef', 'gdef', 'prot', 'str', 'sell'],
  ammo: ['name', 'ammo', 'lvl', 'rstr', 'power', 'sell'],
  food: ['name', 'heal', 'bonus', 'src', 'sell'],
  potions: ['name', 'tool', 'bonus', 'lvl', 'sell'],
  tools: ['name', 'tool', 'bonus', 'lvl', 'sell'],
  materials: ['name', 'kind', 'src', 'use', 'sell'],
};
const DEFAULT_SORT = { all: 'name', weapons: 'lvl', armour: 'lvl', ammo: 'lvl', food: 'heal', potions: 'name', tools: 'name', materials: 'name' };

// where an item comes from: how each kind is shown
const SRC = {
  kill: { icon: 'skull', label: 'Monster', per: 'kill' },
  dungeon: { icon: 'castle', label: 'Dungeon', per: 'clear' },
  steal: { icon: 'hand', label: 'Thieving', per: 'steal' },
  wc: { icon: 'axe', label: 'Woodcutting', per: 'hit' },
  mine: { icon: 'pick', label: 'Mining', per: 'hit' },
  fish: { icon: 'fish', label: 'Fishing', per: 'catch' },
  hunt: { icon: 'paw', label: 'Hunting', per: 'catch' },
  open: { icon: 'chest', label: 'Opened from', per: 'open' },
  craft: { icon: 'anvil', label: 'Made with', per: 'craft' },
  shop: { icon: 'coin', label: 'Shop', per: 'buy' },
  quest: { icon: 'scroll', label: 'Quest', per: 'quest' },
  upgrade: { icon: 'up', label: 'Upgrade', per: 'upgrade' },
  event: { icon: 'star', label: 'Rare event', per: 'action' },
  farm: { icon: 'sprout', label: 'Farming', per: 'harvest' },
  fm: { icon: 'fire', label: 'Firemaking', per: 'burn' },
  agility: { icon: 'run', label: 'Agility', per: 'lap' },
  ranch: { icon: 'paw', label: 'Ranching', per: 'hour' },
  other: { icon: 'star', label: 'Other', per: 'action' },
};
const SRC_GROUPS = [
  ['Monsters', ['kill', 'dungeon']], ['Thieving', ['steal']], ['Gathering', ['wc', 'mine', 'fish', 'hunt', 'farm', 'ranch']],
  ['Crafting', ['craft', 'upgrade', 'fm']], ['Shop', ['shop']], ['Quests', ['quest']], ['Chests & nests', ['open']],
  ['Events & other', ['event', 'agility', 'other']],
];

// ------------------------------------------------------------------ state and URL

const blank = () => ({
  cat: 'all', q: '', sort: '', dir: 1,
  hands: '', style: '', wtype: '', elem: '', slot: '', tool: '', ammo: '',
  reqSkill: '', reqMax: '', mine: false, src: [], usedBy: '', noUse: false, hasBonus: '', conds: [],
});
let st = blank();
let compare = LS.get('mi2db-compare', []);
let myLevels = LS.get('mi2db-levels', {});

function toQuery(s) {
  const p = new URLSearchParams();
  const d = blank();
  for (const [k, v] of Object.entries(s)) {
    if (k === 'conds') { if (v.length) p.set('stat', v.map((c) => `${c.stat}~${c.op}~${c.val}`).join(',')); continue; }
    if (k === 'src') { if (v.length) p.set('src', v.join(',')); continue; }
    if (JSON.stringify(v) !== JSON.stringify(d[k])) p.set(k, v === true ? '1' : String(v));
  }
  return p.toString();
}
function fromQuery(q) {
  const s = blank();
  const p = new URLSearchParams(q);
  for (const [k, v] of p) {
    if (k === 'stat') s.conds = v.split(',').map((x) => { const [stat, op, val] = x.split('~'); return { stat, op, val: +val }; }).filter((c) => c.stat);
    else if (k === 'src') s.src = v.split(',').filter(Boolean);
    else if (k === 'dir') s.dir = +v || 1;
    else if (k in s) s[k] = typeof s[k] === 'boolean' ? v === '1' : v;
  }
  return s;
}

// ------------------------------------------------------------------ derived item fields

const TIERS = [10, 20, 35, 50, 65, 80, 95];
const tierOf = (lvl) => 1 + TIERS.filter((t) => lvl >= t).length;
const avg = (...v) => Math.round(v.reduce((a, b) => a + b, 0) / v.length);
function shorten(s, n) { return s.length > n ? s.slice(0, n - 1) + '…' : s; }

function prepare(i) {
  const s = i.stats || {};
  i.lvl = Math.max(0, ...(i.reqs || []).filter((r) => r.level).map((r) => r.level));
  i.tier = tierOf(i.lvl);
  if (i.cat === 'Weapon') i.kind = `${i.wtype}`;
  else if (i.slot) i.kind = i.slot === 'Offhand' ? 'Offhand' : i.slot;
  else if (i.tool) i.kind = i.tool;
  else i.kind = i.cat;
  const melee = Math.max(s.StabAttackBonus || 0, s.SlashAttackBonus || 0, s.CrushAttackBonus || 0, s.BlockAttackBonus || 0);
  const ranged = Math.max(s.AccurateAttackBonus || 0, s.PowerfulAttackBonus || 0, s.LongRangeAttackBonus || 0);
  const magic = Math.max(s.OffensiveAttackBonus || 0, s.DefensiveAttackBonus || 0);
  i.atk = i.style === 'Ranged' ? ranged : i.style === 'Magic' ? magic : Math.max(melee, ranged, magic);
  i.str = i.style === 'Ranged' ? (s.RangedStrengthBonus || 0) : i.style === 'Magic' ? Math.round((s.MagicDamageBonus || 0) * 100) : Math.max(s.MeleeStrengthBonus || 0, s.RangedStrengthBonus || 0);
  i.mdef = avg(s.StabDefenceBonus || 0, s.SlashDefenceBonus || 0, s.CrushDefenceBonus || 0, s.BlockDefenceBonus || 0);
  i.rdef = avg(s.AccurateDefenceBonus || 0, s.PowerfulDefenceBonus || 0, s.LongRangeDefenceBonus || 0);
  i.gdef = avg(s.OffensiveDefenceBonus || 0, s.DefensiveDefenceBonus || 0);
  i.srcKinds = new Set(i.src.map((x) => x.t));
  i.useSkills = new Set(i.use.map((x) => x.skill).filter(Boolean));
  i.blob = [i.name, i.kind, i.tool, i.style, i.wtype, ...(i.bonuses || []), ...(i.tags || []),
    ...(i.set ? [i.set.name + ' set', ...i.set.bonuses.flatMap((b) => b.lines)] : [])].join(' ').toLowerCase();
}

function canUse(i) {
  return (i.reqs || []).every((r) => !r.level || (myLevels[r.skill] ?? 1) >= r.level);
}

// ------------------------------------------------------------------ filtering

const STAT_OPTIONS = () => [
  ...DB.stats.map((s) => ({ id: s.id, name: s.name })),
  { id: 'Power', name: 'Power rating' }, { id: 'Protection', name: 'Protection rating' },
  { id: '_heal', name: 'Healing' }, { id: '_sell', name: 'Sell price' }, { id: '_lvl', name: 'Required level' },
];
function statValue(i, id) {
  if (id === '_heal') return i.heal ?? 0;
  if (id === '_sell') return i.sell ?? 0;
  if (id === '_lvl') return i.lvl;
  return i.stats?.[id] ?? 0;
}

function matches(i, s, skip = '') {
  const cat = CATS.find((c) => c.id === s.cat) || CATS[0];
  if (skip !== 'cat' && !cat.test(i)) return false;
  if (s.q) {
    for (const w of s.q.toLowerCase().split(/\s+/).filter(Boolean)) if (!i.blob.includes(w)) return false;
  }
  if (s.hands && skip !== 'hands' && String(i.hands || '') !== s.hands) return false;
  if (s.style && skip !== 'style' && i.style !== s.style) return false;
  if (s.wtype && skip !== 'wtype' && i.wtype !== s.wtype) return false;
  if (s.elem && skip !== 'elem' && !(i.elements || []).includes(s.elem)) return false;
  if (s.slot && skip !== 'slot' && i.slot !== s.slot) return false;
  if (s.tool && skip !== 'tool' && (i.tool || i.cat) !== s.tool) return false;
  if (s.ammo && skip !== 'ammo' && i.ammo !== s.ammo) return false;
  if (s.reqMax !== '' && s.reqMax != null) {
    const max = +s.reqMax;
    const rel = (i.reqs || []).filter((r) => r.level && (!s.reqSkill || r.skill === s.reqSkill));
    if (s.reqSkill && !rel.length && !(i.reqs || []).length) { /* no requirement: fine */ }
    if (rel.some((r) => r.level > max)) return false;
  } else if (s.reqSkill && !(i.reqs || []).some((r) => r.skill === s.reqSkill)) return false;
  if (s.mine && !canUse(i)) return false;
  if (s.src.length && !s.src.some((g) => (SRC_GROUPS.find((x) => x[0] === g)?.[1] || []).some((k) => i.srcKinds.has(k)))) return false;
  if (s.usedBy && !i.useSkills.has(s.usedBy)) return false;
  if (s.noUse && i.use.length) return false;
  if (s.hasBonus && !(i.bonuses || []).some((b) => b.toLowerCase().includes(s.hasBonus.toLowerCase()))) return false;
  for (const c of s.conds) {
    const v = statValue(i, c.stat);
    if (c.op === 'ge' ? v < c.val : v > c.val) return false;
  }
  return true;
}

function sorted(list, s) {
  const key = s.sort || DEFAULT_SORT[s.cat] || 'name';
  const col = COL[key];
  const get = col ? col.get : (i) => statValue(i, key);
  const dir = s.dir || 1;
  return list.sort((a, b) => {
    const x = get(a), y = get(b);
    const c = typeof x === 'string' ? x.localeCompare(y) : x - y;
    return c * dir || a.name.localeCompare(b.name);
  });
}

// ------------------------------------------------------------------ small renderers

function tile(i, big = false) {
  const pip = i.hands === 2 ? '<span class="pip">2H</span>' : '';
  const art = i.img ? `<img src="img/items/${encodeURIComponent(i.id)}.webp" alt="" loading="lazy" decoding="async">` : svg(glyphOf(i));
  return `<span class="tile tier${i.tier}${big ? ' big' : ''}${i.img ? ' art' : ''}" title="${esc(i.kind)}">${art}${pip}</span>`;
}
function nameCell(i) {
  const sub = [i.kind, i.hands ? `${i.hands}-handed` : ''].filter(Boolean).join(' · ');
  return `<span class="name">${tile(i)}<span><b${nameStyle(i)}>${esc(i.name)}</b><small>${esc(sub)}</small></span></span>`;
}
function reqCell(i) {
  if (!i.lvl) return '<span class="zero">–</span>';
  const r = (i.reqs || []).filter((x) => x.level).sort((a, b) => b.level - a.level)[0];
  const miss = Object.keys(myLevels).length && !canUse(i);
  return `<span class="lvl${miss ? ' miss' : ''}" title="${esc((i.reqs || []).map((x) => x.level ? `${x.skill} ${x.level}` : x.text).join(', '))}">${esc(r.skill)} ${r.level}</span>`;
}
function itemLink(id, q) {
  const i = byId.get(id);
  if (!i) return esc(id);
  return `<a class="ing" href="#/item/${encodeURIComponent(id)}" data-item="${esc(id)}">${tile(i)}${q != null ? `<b>${fmt(q)}×</b>` : ''} ${esc(i.name)}</a>`;
}
const pct = (p) => {
  if (p >= 1) return '100%';
  const v = p * 100;
  return (v >= 10 ? v.toFixed(1) : v >= 1 ? v.toFixed(2) : v >= 0.01 ? v.toFixed(3) : v.toPrecision(2)).replace(/\.?0+$/, '') + '%';
};
const oneIn = (p) => (p > 0 && p < 1 ? `1 in ${fmt(1 / p)}` : '');
const qty = (q) => (!q ? '' : q[0] === q[1] ? `×${fmt(q[0])}` : `×${fmt(q[0])}–${fmt(q[1])}`);

// ------------------------------------------------------------------ the items view

let rendered = 0;
let current = [];
let observer = null;
const PAGE = 80;

function viewItems() {
  const app = $('#app');
  const counts = Object.fromEntries(CATS.map((c) => [c.id, 0]));
  const loose = { ...st, hands: '', style: '', wtype: '', elem: '', slot: '', tool: '', ammo: '', reqSkill: '', reqMax: '' };
  const others = DB.items.filter((i) => matches(i, loose, 'cat'));
  for (const i of others) for (const c of CATS) if (c.test(i)) counts[c.id]++;
  current = sorted(DB.items.filter((i) => matches(i, st)), st);
  const cols = COLS[st.cat] || COLS.all;
  const sortKey = st.sort || DEFAULT_SORT[st.cat];
  app.innerHTML = `
    <div class="hero">
      <div><h1>Items</h1><p>Every item in Melvor Idle 2: filter by what matters, see where it drops and what it makes.</p></div>
      <div class="ver">Game data ${esc(DB.version)} · ${fmt(DB.items.length)} items<br>Built ${esc(DB.built || '')}</div>
    </div>
    <div class="tabs" role="toolbar" aria-label="Category">
      ${CATS.map((c) => `<button class="tab" data-cat="${c.id}" aria-pressed="${st.cat === c.id}">${svg(c.glyph)}${c.name} <b>${counts[c.id]}</b></button>`).join('')}
    </div>
    <div class="tools">
      <label class="search"><span class="sr-only">Search items</span>${svg('search')}<input id="q" type="search" value="${esc(st.q)}" placeholder="Search names, types and effects (e.g. bird nest, poison, double)" autocomplete="off"></label>
      <button class="btn" id="more">${svg('filter')}More filters ${activeCount() ? `<span class="count">${activeCount()}</span>` : ''}</button>
      <label class="sr-only" for="sortsel">Sort by</label>
      <select class="btn" id="sortsel">${sortOptions(sortKey)}</select>
      <button class="btn" id="dir" aria-label="Sort direction">${st.dir > 0 ? '↑ Low to high' : '↓ High to low'}</button>
      <span class="result-count" id="count">${fmt(current.length)} of ${fmt(DB.items.length)} items</span>
    </div>
    <div class="quick" id="quick">${quickFilters()}</div>
    <div class="chips" id="chips">${chips()}</div>
    <div class="table-wrap"><table class="items">
      <thead><tr>${cols.map((k) => headCell(k, sortKey)).join('')}<th><span class="sr-only">Compare</span></th></tr></thead>
      <tbody id="rows"></tbody>
    </table>
    <div id="more-rows" class="more-rows"></div></div>
    <footer class="site">Unofficial and community-made, from the game's own data (version ${esc(DB.version)}). Item and monster pictures belong to Games by Malcs and Jagex and are shown for reference only; we will take them down on request. Not affiliated with Games by Malcs or Jagex.</footer>`;
  rendered = 0;
  appendRows(cols);
  if (!current.length) $('#rows').innerHTML = `<tr><td colspan="${cols.length + 1}" class="empty">No item matches. Try removing a filter.</td></tr>`;
  observer?.disconnect();
  observer = new IntersectionObserver((e) => { if (e[0].isIntersecting) appendRows(cols); });
  observer.observe($('#more-rows'));
  $('#live').textContent = `${current.length} items`;
  bindItems();
}

function sortOptions(sel) {
  const cols = COLS[st.cat] || COLS.all;
  const opts = cols.map((k) => [k, COL[k].label]);
  const extra = STAT_OPTIONS().filter((o) => !opts.some((x) => x[0] === o.id)).map((o) => [o.id, o.name]);
  return `<optgroup label="Columns">${opts.map(([k, l]) => `<option value="${k}"${k === sel ? ' selected' : ''}>Sort: ${esc(l)}</option>`).join('')}</optgroup>
    <optgroup label="Any stat">${extra.map(([k, l]) => `<option value="${esc(k)}"${k === sel ? ' selected' : ''}>Sort: ${esc(l)}</option>`).join('')}</optgroup>`;
}

function headCell(k, sortKey) {
  const c = COL[k];
  const on = k === sortKey;
  const aria = on ? ` aria-sort="${st.dir > 0 ? 'ascending' : 'descending'}"` : '';
  return `<th class="${c.num ? 'num' : ''}"${aria}><button data-sort="${k}">${esc(c.label)}${on ? (st.dir > 0 ? ' ▲' : ' ▼') : ''}</button></th>`;
}

const colMax = {};
function appendRows(cols) {
  const tbody = $('#rows');
  if (!tbody) return;
  const slice = current.slice(rendered, rendered + PAGE);
  for (const k of cols) if (COL[k].bar && colMax[st.cat + k] == null) colMax[st.cat + k] = Math.max(1, ...current.map((i) => COL[k].get(i)));
  const phone = new Set(cols.filter((k) => COL[k].num).slice(0, 3));
  const html = slice.map((i) => {
    const cells = cols.map((k, n) => {
      const c = COL[k];
      if (k === 'name') return `<td class="c-name">${nameCell(i)}</td>`;
      let v = c.render ? c.render(i) : c.get(i);
      if (!c.render && typeof v === 'number') {
        const max = colMax[st.cat + k];
        v = v === 0 ? '<span class="zero">0</span>' : (c.bar && v > 0 ? `<span class="bar" style="width:${Math.round(28 * v / max)}px"></span>` : '') + `<span class="${v < 0 ? 'neg' : ''}">${fmt(v)}</span>`;
      }
      const raw = c.get(i);
      const zero = raw === 0 || raw === '';
      const sortedCol = k === (st.sort || DEFAULT_SORT[st.cat]);
      return `<td class="extra ${c.num ? 'num' : ''}${zero ? ' zero-cell' : ''}${phone.has(k) ? ' top3' : ''}${sortedCol ? ' sorted' : ''}" data-label="${esc(c.label)}">${v}</td>`;
    }).join('');
    const on = compare.includes(i.id);
    return `<tr data-id="${esc(i.id)}"${on ? ' class="on"' : ''}>${cells}<td class="c-cmp"><input type="checkbox" class="cmp-box" aria-label="Compare ${esc(i.name)}" data-cmp="${esc(i.id)}"${on ? ' checked' : ''}></td></tr>`;
  }).join('');
  tbody.insertAdjacentHTML('beforeend', html);
  rendered += slice.length;
  const more = $('#more-rows');
  if (more) more.textContent = rendered < current.length ? `Showing ${fmt(rendered)} of ${fmt(current.length)} · scroll for more` : '';
}

function quickFilters() {
  const pool = DB.items.filter((i) => CATS.find((c) => c.id === st.cat).test(i));
  const uniq = (f) => [...new Set(pool.map(f).filter(Boolean))].sort();
  const pills = (key, values, label, cls = () => '') => `<div class="group"><span>${label}</span>${values.map(([v, l]) =>
    `<button class="pill ${cls(v)}" data-set="${key}" data-val="${esc(v)}" aria-pressed="${st[key] === v}">${esc(l)}</button>`).join('')}</div>`;
  const select = (key, values, label) => `<div class="group"><span>${label}</span><select class="btn mini-select" data-select="${key}" aria-label="${label}">
      <option value="">Any</option>${values.map((v) => `<option${st[key] === v ? ' selected' : ''}>${esc(v)}</option>`).join('')}</select></div>`;
  const out = [];
  if (st.cat === 'weapons') {
    out.push(pills('hands', [['1', 'One-handed'], ['2', 'Two-handed']], 'Hands'));
    out.push(pills('style', [['Melee', 'Melee'], ['Ranged', 'Ranged'], ['Magic', 'Magic']], 'Style', (v) => v.toLowerCase()));
    out.push(select('wtype', uniq((i) => (st.style && i.style !== st.style ? null : i.wtype)), 'Type'));
    const elems = uniq((i) => (i.elements || [])[0]);
    if (elems.length) out.push(pills('elem', elems.map((e) => [e, e]), 'Element'));
  } else if (st.cat === 'armour') {
    out.push(`<div class="group"><span>Slot</span>${doll()}<b style="margin-left:6px">${esc(st.slot || 'Any slot')}</b></div>`);
  } else if (st.cat === 'ammo') {
    out.push(pills('ammo', uniq((i) => i.ammo).map((v) => [v, v]), 'Ammo'));
  } else if (st.cat === 'potions' || st.cat === 'tools') {
    out.push(select('tool', uniq((i) => i.tool || i.cat), 'Slot'));
  }
  if (st.cat !== 'all') {
    const skills = [...new Set(pool.flatMap((i) => (i.reqs || []).map((r) => r.skill)).filter(Boolean))].sort();
    if (skills.length) out.push(`<div class="group"><span>Max level</span><select class="btn mini-select" data-select="reqSkill" aria-label="Skill for the level limit"><option value="">Any skill</option>${skills.map((s) => `<option${st.reqSkill === s ? ' selected' : ''}>${esc(s)}</option>`).join('')}</select>
      <input class="btn mini-select" style="width:80px" type="number" min="1" max="200" placeholder="≤ level" value="${esc(st.reqMax)}" data-input="reqMax" aria-label="Highest required level"></div>`);
  }
  out.push(`<div class="group"><label class="check"><input type="checkbox" data-check="mine"${st.mine ? ' checked' : ''}> Only what I can use</label> <button class="pill" data-open="levels">My levels</button></div>`);
  return out.join('');
}

function doll() {
  const slots = [[null, 'Helmet', null], ['Cape', 'Amulet', 'Ammo'], ['Weapon', 'Torso', 'Offhand'], ['Gloves', 'Legs', 'Ring'], [null, 'Boots', null]];
  const glyph = { Helmet: 'helmet', Cape: 'cape', Amulet: 'amulet', Ammo: 'ammo', Weapon: 'sword', Torso: 'torso', Offhand: 'shield', Gloves: 'gloves', Legs: 'legs', Ring: 'ring', Boots: 'boots' };
  return `<div class="doll" role="group" aria-label="Pick a slot">${slots.flat().map((s) => (s && s !== 'Weapon' && s !== 'Ammo'
    ? `<button data-set="slot" data-val="${s}" aria-pressed="${st.slot === s}" title="${s}" aria-label="${s}">${svg(glyph[s])}</button>`
    : '<span></span>')).join('')}</div>`;
}

function activeCount() {
  const d = blank();
  return ['reqSkill', 'reqMax', 'usedBy', 'hasBonus'].filter((k) => st[k] !== d[k]).length + st.src.length + st.conds.length + (st.noUse ? 1 : 0);
}

function chips() {
  const out = [];
  const add = (label, reset) => out.push(`<span class="chip">${esc(label)}<button aria-label="Remove ${esc(label)}" data-reset='${esc(JSON.stringify(reset))}'>×</button></span>`);
  if (st.q) add(`“${st.q}”`, { q: '' });
  if (st.hands) add(st.hands === '1' ? 'One-handed' : 'Two-handed', { hands: '' });
  if (st.style) add(st.style, { style: '' });
  if (st.wtype) add(st.wtype, { wtype: '' });
  if (st.elem) add(st.elem, { elem: '' });
  if (st.slot) add(st.slot, { slot: '' });
  if (st.tool) add(st.tool, { tool: '' });
  if (st.ammo) add(st.ammo, { ammo: '' });
  if (st.reqMax !== '') add(`${st.reqSkill || 'Any skill'} ≤ ${st.reqMax}`, { reqMax: '', reqSkill: '' });
  else if (st.reqSkill) add(`Needs ${st.reqSkill}`, { reqSkill: '' });
  if (st.mine) add('Only what I can use', { mine: false });
  for (const g of st.src) add(`From: ${g}`, { src: st.src.filter((x) => x !== g) });
  if (st.usedBy) add(`Used in ${st.usedBy}`, { usedBy: '' });
  if (st.noUse) add('No known use', { noUse: false });
  if (st.hasBonus) add(`Effect: ${st.hasBonus}`, { hasBonus: '' });
  st.conds.forEach((c, n) => add(`${statName(c.stat)} ${c.op === 'ge' ? '≥' : '≤'} ${c.val}`, { conds: st.conds.filter((_, m) => m !== n) }));
  if (out.length) out.push('<button class="clear" data-clear>Clear all</button>');
  return out.join('');
}
const statName = (id) => STAT_OPTIONS().find((o) => o.id === id)?.name || id;

function setState(patch, push = false) {
  st = { ...st, ...patch };
  const url = '#/items' + (toQuery(st) ? '?' + toQuery(st) : '');
  if (push) history.pushState(null, '', url); else history.replaceState(null, '', url);
  const y = window.scrollY;
  viewItems();
  window.scrollTo(0, y);
}

function bindItems() {
  const app = $('#app');
  app.onclick = (e) => {
    const t = e.target.closest('button, input, tr, a');
    if (!t) return;
    if (t.matches('[data-cat]')) return setState({ ...blank(), q: st.q, cat: t.dataset.cat, mine: st.mine }, true);
    if (t.matches('[data-sort]')) {
      const k = t.dataset.sort;
      const cur = st.sort || DEFAULT_SORT[st.cat];
      return setState({ sort: k, dir: cur === k ? -st.dir : (COL[k]?.num ? -1 : 1) });
    }
    if (t.matches('[data-set]')) { const k = t.dataset.set, v = t.dataset.val; return setState({ [k]: st[k] === v ? '' : v }); }
    if (t.matches('[data-reset]')) return setState(JSON.parse(t.dataset.reset));
    if (t.matches('[data-clear]')) return setState({ ...blank(), cat: st.cat });
    if (t.matches('[data-open="levels"]')) return openDrawer(true);
    if (t.id === 'more') return openDrawer();
    if (t.id === 'dir') return setState({ dir: -st.dir });
    if (t.matches('[data-cmp]')) { toggleCompare(t.dataset.cmp); t.closest('tr').classList.toggle('on', t.checked); e.stopPropagation(); return; }
    if (t.matches('tr[data-id]')) return openItem(t.dataset.id);
  };
  app.onchange = (e) => {
    const t = e.target;
    if (t.id === 'sortsel') return setState({ sort: t.value, dir: COL[t.value] && !COL[t.value].num ? 1 : -1 });
    if (t.matches('[data-select]')) return setState({ [t.dataset.select]: t.value });
    if (t.matches('[data-input]')) return setState({ [t.dataset.input]: t.value });
    if (t.matches('[data-check]')) return setState({ [t.dataset.check]: t.checked });
  };
  let timer;
  $('#q').oninput = (e) => { clearTimeout(timer); timer = setTimeout(() => { setState({ q: e.target.value }); const q = $('#q'); q.focus(); q.setSelectionRange(q.value.length, q.value.length); }, 140); };
}

// ------------------------------------------------------------------ more filters drawer

function openDrawer(levels = false) {
  const d = $('#drawer');
  const skills = [...new Set(DB.items.flatMap((i) => (i.reqs || []).map((r) => r.skill)).filter(Boolean))].sort();
  const useSkills = [...new Set(DB.items.flatMap((i) => [...i.useSkills]))].sort();
  const preview = (patch) => DB.items.filter((i) => matches(i, { ...st, ...patch })).length;
  d.innerHTML = `
    <header><h2>${levels ? 'My levels' : 'More filters'}</h2><button class="icon-btn" data-close aria-label="Close">${svg('close')}</button></header>
    <div class="body">
      ${levels ? `
      <p class="muted" style="margin:0">Type your levels once. "Only what I can use" then hides what you cannot equip yet. Saved in this browser only.</p>
      <div class="levels">${skills.map((s) => `<label>${esc(s)}<input type="number" min="1" max="200" data-level="${esc(s)}" value="${esc(myLevels[s] ?? '')}" placeholder="1"></label>`).join('')}</div>`
    : `
      <div class="field"><span class="label">Obtained through</span><div class="row">${SRC_GROUPS.map(([g]) => `<label class="check"><input type="checkbox" data-src="${esc(g)}"${st.src.includes(g) ? ' checked' : ''}> ${esc(g)}</label>`).join('')}</div></div>
      <div class="field"><label for="f-used">Used in</label><select id="f-used"><option value="">Any skill</option>${useSkills.map((s) => `<option${st.usedBy === s ? ' selected' : ''}>${esc(s)}</option>`).join('')}</select>
        <label class="check" style="margin-top:8px"><input type="checkbox" id="f-nouse"${st.noUse ? ' checked' : ''}> No known use (safe to sell)</label></div>
      <div class="field"><label for="f-bonus">Effect contains</label><input id="f-bonus" type="search" class="btn" style="width:100%" value="${esc(st.hasBonus)}" placeholder="e.g. preservation, double, poison, XP"></div>
      <div class="field"><span class="label">Stat conditions</span><div id="conds" style="display:grid;gap:6px">${st.conds.map((c, n) => condRow(c, n)).join('')}</div>
        <button class="btn" id="add-cond" style="margin-top:6px">+ Add condition</button></div>
      <div class="field"><label for="f-skill">Required level</label><div class="row"><select id="f-skill"><option value="">Any skill</option>${skills.map((s) => `<option${st.reqSkill === s ? ' selected' : ''}>${esc(s)}</option>`).join('')}</select>
        ≤ <input id="f-max" type="number" min="1" max="200" value="${esc(st.reqMax)}"></div></div>`}
    </div>
    <footer><button class="btn" data-reset-drawer>${levels ? 'Clear levels' : 'Reset'}</button><button class="btn gold" data-close id="apply">Show ${fmt(preview({}))} items</button></footer>`;
  d.classList.add('open'); d.setAttribute('aria-hidden', 'false');
  $('#shade').classList.add('open');
  const first = d.querySelector('input, select, button'); first?.focus();
  const sync = () => {
    if (levels) {
      myLevels = {};
      $$('[data-level]', d).forEach((x) => { if (x.value) myLevels[x.dataset.level] = +x.value; });
      LS.set('mi2db-levels', myLevels);
    } else {
      st.src = $$('[data-src]', d).filter((x) => x.checked).map((x) => x.dataset.src);
      st.usedBy = $('#f-used', d).value; st.noUse = $('#f-nouse', d).checked; st.hasBonus = $('#f-bonus', d).value;
      st.reqSkill = $('#f-skill', d).value; st.reqMax = $('#f-max', d).value;
      st.conds = $$('.cond', d).map((r) => ({ stat: $('select.s', r).value, op: $('select.o', r).value, val: +$('input', r).value || 0 }));
    }
    $('#apply', d).textContent = `Show ${fmt(preview({}))} items`;
  };
  d.oninput = sync; d.onchange = sync;
  d.onclick = (e) => {
    const t = e.target.closest('button');
    if (!t) return;
    if (t.id === 'add-cond') { $('#conds', d).insertAdjacentHTML('beforeend', condRow({ stat: 'Power', op: 'ge', val: 0 }, st.conds.length)); sync(); }
    else if (t.matches('[data-del]')) { t.closest('.cond').remove(); sync(); }
    else if (t.matches('[data-reset-drawer]')) {
      if (levels) { myLevels = {}; LS.set('mi2db-levels', {}); } else Object.assign(st, { src: [], usedBy: '', noUse: false, hasBonus: '', conds: [], reqSkill: '', reqMax: '' });
      openDrawer(levels);
    } else if (t.matches('[data-close]')) closeDrawer();
  };
}
function condRow(c, n) {
  return `<div class="cond row" style="display:flex;gap:6px"><select class="s" aria-label="Stat">${STAT_OPTIONS().map((o) => `<option value="${esc(o.id)}"${o.id === c.stat ? ' selected' : ''}>${esc(o.name)}</option>`).join('')}</select>
    <select class="o" aria-label="Comparison"><option value="ge"${c.op === 'ge' ? ' selected' : ''}>≥</option><option value="le"${c.op === 'le' ? ' selected' : ''}>≤</option></select>
    <input type="number" value="${esc(c.val)}" aria-label="Value"><button class="icon-btn" data-del aria-label="Remove condition">${svg('close')}</button></div>`;
}
function closeDrawer() {
  const d = $('#drawer');
  if (!d.classList.contains('open')) return;
  d.classList.remove('open'); d.setAttribute('aria-hidden', 'true');
  if (!$('#panel').classList.contains('open')) $('#shade').classList.remove('open');
  if (route().view === 'items') setState({});
}

// ------------------------------------------------------------------ item detail

function openItem(id, asPage = false) {
  const i = byId.get(id);
  if (!i) return;
  if (asPage || window.innerWidth < 760 && route().view !== 'items') {
    $('#app').innerHTML = `<div class="detail page">${itemDetail(i)}</div>`;
    bindDetail($('#app'));
    return;
  }
  const p = $('#panel');
  p.innerHTML = `<div class="detail">${itemDetail(i, true)}</div>`;
  p.classList.add('open'); p.setAttribute('aria-hidden', 'false');
  $('#shade').classList.add('open');
  p.scrollTop = 0;
  bindDetail(p);
  $('.icon-btn', p)?.focus();
}
function closePanel() {
  const p = $('#panel');
  p.classList.remove('open'); p.setAttribute('aria-hidden', 'true');
  if (!$('#drawer').classList.contains('open')) $('#shade').classList.remove('open');
}

const STAT_GROUPS = [
  ['Attack', ['AttackInterval', 'StabAttackBonus', 'SlashAttackBonus', 'CrushAttackBonus', 'BlockAttackBonus', 'AccurateAttackBonus', 'PowerfulAttackBonus', 'LongRangeAttackBonus', 'OffensiveAttackBonus', 'DefensiveAttackBonus']],
  ['Strength', ['MeleeStrengthBonus', 'RangedStrengthBonus', 'MagicDamageBonus', 'SpellMaxHit', 'Power']],
  ['Defence', ['StabDefenceBonus', 'SlashDefenceBonus', 'CrushDefenceBonus', 'BlockDefenceBonus', 'AccurateDefenceBonus', 'PowerfulDefenceBonus', 'LongRangeDefenceBonus', 'OffensiveDefenceBonus', 'DefensiveDefenceBonus', 'Protection', 'Armour', 'MaximumWard']],
  ['Other', ['Lifesteal']],
];
const statMax = {};
function statLabel(id) {
  if (id === 'Power') return 'Power rating';
  if (id === 'Protection') return 'Protection rating';
  return DB.stats.find((s) => s.id === id)?.name || id;
}
function statText(id, v) {
  if (id === 'AttackInterval') return (v / 1000).toFixed(2).replace(/0$/, '') + 's';
  if (DB.stats.find((s) => s.id === id)?.pct) return (v > 0 ? '+' : '') + Math.round(v * 100) + '%';
  return (v > 0 && id !== 'Power' && id !== 'Protection' ? '+' : '') + fmt(v);
}

function itemDetail(i, inPanel = false) {
  const s = i.stats || {};
  const badges = [
    i.style && `<span class="badge ${i.style.toLowerCase()}">${esc(i.style)}</span>`,
    i.hands && `<span class="badge">${i.hands === 2 ? 'Two-handed (takes the offhand)' : 'One-handed'}</span>`,
    ...(i.elements || []).map((e) => `<span class="badge ${e.toLowerCase()}">${esc(e)}</span>`),
    i.needsAmmo && `<span class="badge">Needs ${esc(i.needsAmmo)}</span>`,
    i.ammo && `<span class="badge">${esc(i.ammo)}</span>`,
    i.stack && '<span class="badge">Equips as a stack</span>',
  ].filter(Boolean).join('');
  const reqs = (i.reqs || []).map((r) => (r.level ? `<span class="lvl${(myLevels[r.skill] ?? 99) < r.level && Object.keys(myLevels).length ? ' miss' : ''}">${esc(r.skill)} ${r.level}</span>` : `<span class="lvl">${esc(r.text)}</span>`)).join('');
  let stats = '';
  for (const [g, keys] of STAT_GROUPS) {
    const rows = keys.filter((k) => s[k]).map((k) => {
      const key = i.cat + k;
      if (statMax[key] == null) statMax[key] = Math.max(1, ...DB.items.filter((x) => x.cat === i.cat).map((x) => Math.abs(x.stats?.[k] || 0)));
      const w = k === 'AttackInterval' ? 0 : Math.round(100 * Math.min(1, Math.abs(s[k]) / statMax[key]));
      return `<div class="stat"><span class="k">${esc(statLabel(k))}</span><span class="v ${s[k] < 0 ? 'neg' : ''}">${statText(k, s[k])}</span>${w ? `<span class="meter" title="Compared to the best ${esc(i.cat.toLowerCase())}"><i style="width:${w}%"></i></span>` : '<span class="meter"></span>'}</div>`;
    });
    if (rows.length) stats += `<div class="stat-head">${g}</div>${rows.join('')}`;
  }
  const facts = [
    i.heal && `<div class="stat"><span class="k">Heals</span><span class="v">${fmt(i.heal)}</span><span class="meter"></span></div>`,
    i.bury && `<div class="stat"><span class="k">Bury for</span><span class="v">${fmt(i.bury.q)} ${esc(i.bury.cur)}</span><span class="meter"></span></div>`,
    i.trap && `<div class="stat"><span class="k">Trap type</span><span class="v">${esc(i.trap)}</span><span class="meter"></span></div>`,
    i.tool && `<div class="stat"><span class="k">Toolbelt slot</span><span class="v">${esc(i.tool)}</span><span class="meter"></span></div>`,
    i.cat !== 'One Style rarity' && `<div class="stat"><span class="k">Sells for</span><span class="v">${fmt(i.sell)} ${esc(i.sellCur === 'Gold Pieces' ? 'GP' : i.sellCur)}</span><span class="meter"></span></div>`,
  ].filter(Boolean).join('');
  return `
    <div class="detail-head">${tile(i, true)}
      <div><h2${nameStyle(i)}>${esc(i.name)}</h2><div class="sub">${esc(i.kind)}${i.mod ? ' · One Style mod' + (i.rarity ? ' · ' + (i.rarity === 'Rare4' ? 'Rare (4 affixes)' : i.rarity) : '') : ''} ${badges}</div></div>
      <div class="detail-actions">
        <button class="icon-btn" data-compare="${esc(i.id)}" title="Add to compare" aria-label="Add to compare">${svg('compare')}</button>
        <a class="icon-btn" href="#/item/${encodeURIComponent(i.id)}" title="Open as a page (link)" aria-label="Open as a page">${svg('link')}</a>
        ${inPanel ? `<button class="icon-btn" data-close-panel title="Close" aria-label="Close">${svg('close')}</button>` : ''}
      </div>
    </div>
    ${i.desc ? `<p class="desc">${esc(i.desc)}</p>` : ''}
    ${reqs ? `<div class="section"><h3>Requires</h3><div class="reqs">${reqs}</div></div>` : ''}
    ${stats || facts ? `<div class="section"><h3>Stats</h3><div class="statgrid">${stats}${stats ? '<div class="stat-head">Item</div>' : ''}${facts}</div></div>` : ''}
    ${(i.bonuses || []).length ? `<div class="section"><h3>Effects <span class="n">${i.bonuses.length}</span></h3><ul class="bonus">${i.bonuses.map((b) => `<li>${esc(b)}</li>`).join('')}</ul>${i.consumed ? `<p class="note">Used up: ${esc(i.consumed.join(', '))}</p>` : ''}</div>` : ''}
    ${i.set ? setSection(i) : ''}
    ${i.contents ? contentsSection(i) : ''}
    <div class="section"><h3>How to get it <span class="n">${i.src.length}</span></h3>${sourcesSection(i)}</div>
    <div class="section"><h3>Used for <span class="n">${i.use.length}</span></h3>${usesSection(i)}</div>
    <p class="note">Item id: <code>${esc(i.id)}</code> · game data ${esc(DB.version)}</p>`;
}

function contentsSection(i) {
  const c = i.contents;
  const rows = [
    ...(c.always || []).map((x) => ({ ...x, ch: 1 })),
    ...(c.table || []),
  ].sort((a, b) => b.ch - a.ch);
  return `<div class="section"><h3>Contains <span class="n">${rows.length}</span></h3>
    ${rows.map((x) => `<div class="src"><span>${tileFor(x.id)}</span><div class="what">${nameLink(x.id)}</div><div class="rate"><b>${pct(x.ch)}</b><small>${qty(x.q)} per open${x.ch < 1 ? ' · ' + oneIn(x.ch) : ''}</small></div></div>`).join('')}
    ${c.note ? `<p class="note">${esc(c.note)}</p>` : ''}</div>`;
}
// An equipment set (game 0.11.7): its pieces, and the bonus for each number of pieces worn.
function setSection(i) {
  const s = i.set;
  const pieces = s.items.map((id) => byId.get(id) ? `<a class="pill" href="#/item/${encodeURIComponent(id)}" data-item="${esc(id)}">${id === i.id ? '<b>' : ''}${esc(byId.get(id).name)}${id === i.id ? '</b>' : ''}</a>` : '').join(' ');
  const steps = s.bonuses.map((b) => `<li>${b.n} pieces: ${b.lines.map(esc).join(', ')}</li>`).join('');
  return `<div class="section"><h3>${esc(s.name)} set <span class="n">${s.items.length}</span></h3><div class="reqs">${pieces}</div><ul class="bonus" style="margin-top:8px">${steps}</ul></div>`;
}

const nameLink = (id) => { const i = byId.get(id); return i ? `<a href="#/item/${encodeURIComponent(id)}" data-item="${esc(id)}"><b>${esc(i.name)}</b></a><small>${esc(i.kind)}</small>` : esc(id); };
const tileFor = (id) => (byId.get(id) ? tile(byId.get(id)) : '');

let srcSort = 'chance';
function sourcesSection(i) {
  if (!i.src.length) return '<p class="muted">No known source in the game data. It may come from something this database does not cover yet.</p>';
  const rows = [...i.src].map((x) => ({ ...x, exp: x.exp ?? (x.ch != null && x.q ? x.ch * (x.q[0] + x.q[1]) / 2 : null) }));
  const key = srcSort === 'amount' ? 'exp' : 'ch';
  rows.sort((a, b) => (b[key] ?? -1) - (a[key] ?? -1));
  const bestDrop = rows.find((r) => r.ch != null && !['craft', 'shop', 'quest', 'upgrade'].includes(r.t));
  const head = rows.length > 2 ? `<div class="src-sort"><button class="pill" data-srcsort="chance" aria-pressed="${srcSort === 'chance'}">Highest chance</button><button class="pill" data-srcsort="amount" aria-pressed="${srcSort === 'amount'}">Most per action</button></div>` : '';
  return head + rows.map((r) => sourceRow(r, r === bestDrop)).join('');
}

function sourceRow(r, best) {
  const k = SRC[r.t] || SRC.other;
  const per = r.per || k.per;
  const link = r.link ? `<a href="#/${r.link.kind}/${encodeURIComponent(r.link.id)}">${esc(r.via)}</a>` : esc(r.via || '');
  let what = `<b>${esc(r.label || k.label)}</b>${r.via ? ': ' + link : ''}`;
  const small = [];
  if (r.skill && r.t !== 'craft') small.push(`${esc(r.skill)}${r.lvl ? ' ' + r.lvl : ''}`);
  if (['craft', 'fm', 'farm'].includes(r.t) && r.skill) what = `<b>${esc(r.label || r.skill)}${r.lvl ? ' ' + r.lvl : ''}</b>: ${esc(r.via)}`;
  if (r.t === 'open' && r.link) {
    const box = byId.get(r.link.id);
    const from = box && [...box.src].filter((x) => x.ch != null).sort((a, b) => b.ch - a.ch)[0];
    if (from) small.push(`the ${esc(box.name)} itself: ${esc(from.label || SRC[from.t]?.label || '')} ${esc(from.via || '')} (${pct(from.ch)} per ${esc(from.per || SRC[from.t]?.per || '')})`);
  }
  if (r.area) small.push(esc(r.area));
  if (r.note) small.push(esc(r.note));
  let ings = '';
  if (r.ing?.length || r.cur?.length) ings = `<div class="ings">${(r.ing || []).map((x) => itemLink(x.id, x.q)).join('')}${(r.cur || []).map((c) => `<span class="ing">${svg('coin')} ${fmt(c.q)} ${esc(c.cur)}</span>`).join('')}</div>`;
  let rate = '';
  if (r.ch != null) {
    const e = r.exp ?? (r.q ? r.ch * (r.q[0] + r.q[1]) / 2 : r.ch);
    rate = `<b>${pct(r.ch)}</b><small>${qty(r.q)} per ${esc(per)}${r.ch < 1 ? ' · ' + oneIn(r.ch) : ''}</small>${r.ch < 1 && r.q && (r.q[1] > 1) ? `<small>≈ ${e < 0.01 ? e.toPrecision(2) : e.toFixed(2).replace(/\.?0+$/, '')} per ${esc(per)} on average</small>` : ''}`;
  } else if (r.q) rate = `<b>${qty(r.q)}</b><small>per ${esc(per)}</small>`;
  return `<div class="src${best ? ' best' : ''}"><span class="kindicon" title="${esc(k.label)}">${svg(k.icon)}</span>
    <div class="what">${what}${small.length ? `<small>${small.join(' · ')}</small>` : ''}${r.unverified ? '<span class="unver" title="How the game rolls this was not proven from its code">unverified</span>' : ''}${ings}</div>
    <div class="rate">${rate}</div></div>`;
}

function usesSection(i) {
  if (!i.use.length) return '<p class="muted">Not used in any recipe, upgrade or shop the database knows. Probably safe to sell.</p>';
  const groups = new Map();
  for (const u of i.use) {
    const g = u.skill || u.label || 'Other';
    if (!groups.has(g)) groups.set(g, []);
    groups.get(g).push(u);
  }
  return [...groups].map(([g, list]) => `<div class="stat-head">${esc(g)} · ${list.length}</div>${list.slice(0, 60).map((u) => `
    <div class="src"><span class="kindicon">${svg(SRC[u.t]?.icon || 'anvil')}</span>
      <div class="what"><b>${esc(u.via)}</b>${u.lvl ? ` <span class="lvl">${esc(u.skill)} ${u.lvl}</span>` : ''}<div class="ings">${(u.makes || []).map((m) => itemLink(m)).join('')}</div></div>
      <div class="rate"><b>${fmt(u.q)}×</b><small>needed</small></div></div>`).join('')}${list.length > 60 ? `<p class="note">…and ${list.length - 60} more.</p>` : ''}`).join('');
}

function bindDetail(root) {
  root.onclick = (e) => {
    const t = e.target.closest('button, a');
    if (!t) return;
    if (t.matches('[data-close-panel]')) { closePanel(); return; }
    if (t.matches('[data-compare]')) { toggleCompare(t.dataset.compare, true); return; }
    if (t.matches('[data-srcsort]')) {
      srcSort = t.dataset.srcsort;
      const id = root.querySelector('[data-compare]').dataset.compare;
      const box = t.closest('.section');
      box.innerHTML = `<h3>How to get it <span class="n">${byId.get(id).src.length}</span></h3>${sourcesSection(byId.get(id))}`;
      return;
    }
    if (t.matches('a[data-item]') && route().view === 'items' && root.id === 'panel') { e.preventDefault(); openItem(t.dataset.item); }
  };
}

// ------------------------------------------------------------------ compare

function toggleCompare(id, add = false) {
  if (compare.includes(id)) { if (!add) compare = compare.filter((x) => x !== id); }
  else compare = [...compare, id].slice(-4);
  LS.set('mi2db-compare', compare);
  drawTray();
}
function drawTray() {
  const t = $('#tray');
  if (!compare.length || route().view === 'compare') { t.hidden = true; return; }
  t.hidden = false;
  t.innerHTML = `<strong>Compare</strong><div class="slots">${compare.map((id) => { const i = byId.get(id); return i ? `<span class="slot-item">${tile(i)}<span>${esc(i.name)}</span></span>` : ''; }).join('')}</div>
    <a class="btn gold" href="#/compare?ids=${compare.map(encodeURIComponent).join(',')}">Compare ${compare.length}</a><button class="icon-btn" data-clear-cmp aria-label="Clear compare">${svg('close')}</button>`;
  t.onclick = (e) => { if (e.target.closest('[data-clear-cmp]')) { compare = []; LS.set('mi2db-compare', []); drawTray(); if (route().view === 'items') viewItems(); } };
}

function viewCompare(ids) {
  const items = ids.map((id) => byId.get(id)).filter(Boolean);
  const keys = [...new Set(items.flatMap((i) => Object.keys(i.stats || {})))];
  const order = STAT_GROUPS.flatMap((g) => g[1]);
  keys.sort((a, b) => order.indexOf(a) - order.indexOf(b));
  const lowerBetter = new Set(['AttackInterval']);
  const row = (label, vals, better = 'high') => {
    const nums = vals.filter((v) => typeof v === 'number');
    const hi = Math.max(...nums), lo = Math.min(...nums);
    return `<tr><td>${esc(label)}</td>${vals.map((v) => {
      if (typeof v !== 'number') return `<td>${v ?? '–'}</td>`;
      const best = nums.length > 1 && hi !== lo && (better === 'high' ? v === hi : v === lo);
      const worst = nums.length > 1 && hi !== lo && (better === 'high' ? v === lo : v === hi);
      return `<td class="${best ? 'best-v' : worst ? 'worst-v' : ''}">${label === statLabel('AttackInterval') ? (v / 1000).toFixed(2).replace(/0$/, '') + 's' : fmt(v)}</td>`;
    }).join('')}</tr>`;
  };
  $('#app').innerHTML = `
    <div class="hero"><div><h1>Compare</h1><p>Green is the best value in a row, red the worst.</p></div><a class="btn" href="#/items">${svg('back')} Back to items</a></div>
    ${items.length ? `<div class="table-wrap"><table class="cmp"><thead><tr><th></th>${items.map((i) => `<th><a href="#/item/${encodeURIComponent(i.id)}" class="name" style="display:inline-flex;gap:8px;align-items:center">${tile(i)} ${esc(i.name)}</a><br><button class="pill" data-rm="${esc(i.id)}">Remove</button></th>`).join('')}</tr></thead><tbody>
      <tr><td>Type</td>${items.map((i) => `<td>${esc(i.kind)}${i.hands ? ` · ${i.hands}H` : ''}</td>`).join('')}</tr>
      <tr><td>Requires</td>${items.map((i) => `<td>${(i.reqs || []).map((r) => esc(r.level ? `${r.skill} ${r.level}` : r.text)).join(', ') || '–'}</td>`).join('')}</tr>
      ${keys.map((k) => row(statLabel(k), items.map((i) => i.stats?.[k] ?? 0), lowerBetter.has(k) ? 'low' : 'high')).join('')}
      ${items.some((i) => i.heal) ? row('Heals', items.map((i) => i.heal ?? 0)) : ''}
      ${row('Sell price', items.map((i) => i.sell))}
      <tr><td>Effects</td>${items.map((i) => `<td style="text-align:left;white-space:normal">${(i.bonuses || []).map(esc).join('<br>') || '–'}</td>`).join('')}</tr>
    </tbody></table></div>` : '<p class="empty">Nothing to compare yet. Tick items in the list.</p>'}`;
  $('#app').onclick = (e) => {
    const t = e.target.closest('[data-rm]');
    if (!t) return;
    compare = compare.filter((x) => x !== t.dataset.rm); LS.set('mi2db-compare', compare);
    location.hash = '#/compare?ids=' + compare.join(',');
  };
}

// ------------------------------------------------------------------ monsters and areas

function viewMonsters() {
  const list = [...(DB.monsters || [])].sort((a, b) => (a.levels?.Hitpoints || 0) - (b.levels?.Hitpoints || 0) || a.name.localeCompare(b.name));
  $('#app').innerHTML = `<div class="hero"><div><h1>Monsters</h1><p>${fmt(list.length)} monsters with their loot tables.</p></div></div>
    <div class="tools"><label class="search"><span class="sr-only">Search monsters</span>${svg('search')}<input id="mq" type="search" placeholder="Search monsters or areas"></label></div>
    <div class="cards" id="mcards">${list.map(monsterCard).join('')}</div>`;
  $('#mq').oninput = (e) => {
    const q = e.target.value.toLowerCase();
    $('#mcards').innerHTML = list.filter((m) => (m.name + ' ' + (m.areas || []).map((a) => a.name).join(' ')).toLowerCase().includes(q)).map(monsterCard).join('');
  };
}
function monsterCard(m) {
  return `<a class="card mcard" href="#/monster/${encodeURIComponent(m.id)}">${m.img ? `<img class="mimg" src="img/monsters/${encodeURIComponent(m.id)}.webp" alt="" loading="lazy">` : ''}<h4>${esc(m.name)}</h4><div class="meta">Hitpoints ${fmt(m.levels?.Hitpoints)} · ${esc(m.style || '')} · ${m.loot.length} drop${m.loot.length === 1 ? '' : 's'}</div>
    <div class="meta">${esc((m.areas || []).map((a) => a.name).join(', ') || 'No area')}</div></a>`;
}
function viewMonster(id) {
  const m = monsterById.get(id);
  if (!m) { $('#app').innerHTML = '<p class="empty">Monster not found.</p>'; return; }
  const rows = (m.loot || []).slice().sort((a, b) => b.ch - a.ch);
  $('#app').innerHTML = `<div class="detail page">
    <div class="detail-head"><span class="tile big monster tier${tierOf(m.lvl || 0)}${m.img ? ' art' : ''}">${m.img ? `<img src="img/monsters/${encodeURIComponent(m.id)}.webp" alt="">` : svg('skull')}</span><div><h2>${esc(m.name)}</h2>
      <div class="sub"><span class="badge ${esc((m.style || '').toLowerCase())}">${esc(m.style || '')}</span></div></div></div>
    ${m.note ? `<p class="note">${esc(m.note)}</p>` : ''}
    <div class="section"><h3>Found in</h3>${(m.areas || []).map((a) => `<a class="pill" href="#/area/${encodeURIComponent(a.id)}">${esc(a.name)}${a.dungeon ? ' · dungeon, no drops' : ''}</a>`).join(' ') || '<span class="muted">No area</span>'}</div>
    ${m.levels ? `<div class="section"><h3>Levels</h3><div class="reqs">${Object.entries(m.levels).map(([k, v]) => `<span class="lvl">${esc(k)} ${v}</span>`).join('')}</div></div>` : ''}
    <div class="section"><h3>Loot per kill <span class="n">${rows.length}</span></h3>
      ${rows.map((x) => `<div class="src"><span>${tileFor(x.id)}</span><div class="what">${nameLink(x.id)}</div><div class="rate"><b>${pct(x.ch)}</b><small>${x.ch < 1 ? oneIn(x.ch) + ' · ' : ''}${qty(x.q)}</small>${x.ch < 1 ? `<small>≈ ${(+x.exp.toPrecision(2))} per kill</small>` : ''}</div></div>`).join('') || '<p class="muted">No item drops.</p>'}
      ${m.gp ? `<p class="note">Also drops ${fmt(m.gp[0])}–${fmt(m.gp[1])} GP.</p>` : ''}</div></div>`;
}
function viewAreas() {
  const list = DB.areas || [];
  const groups = new Map();
  for (const a of list) { if (!groups.has(a.cat)) groups.set(a.cat, []); groups.get(a.cat).push(a); }
  $('#app').innerHTML = `<div class="hero"><div><h1>Areas & dungeons</h1><p>Every place to fight, with the combined loot of its monsters.</p></div></div>
    ${[...groups].map(([g, l]) => `<div class="section"><h3>${esc(g)} <span class="n">${l.length}</span></h3><div class="cards">${l.map((a) => `<a class="card" href="#/area/${encodeURIComponent(a.id)}"><h4>${esc(a.name)}</h4><div class="meta">${esc(a.diff || '')} · ${a.monsters.length} monster${a.monsters.length === 1 ? '' : 's'}</div></a>`).join('')}</div></div>`).join('')}`;
}
function viewArea(id) {
  const a = areaById.get(id);
  if (!a) { $('#app').innerHTML = '<p class="empty">Area not found.</p>'; return; }
  const loot = new Map();
  for (const mid of a.monsters) {
    const m = monsterById.get(mid);
    for (const x of m?.loot || []) {
      const best = loot.get(x.id);
      if (!best || x.ch > best.ch) loot.set(x.id, { ...x, from: m.name, fromId: m.id });
    }
  }
  const rows = [...loot.values()].sort((x, y) => y.ch - x.ch);
  $('#app').innerHTML = `<div class="detail page">
    <div class="detail-head"><span class="tile big">${svg(a.dungeon ? 'castle' : 'skull')}</span><div><h2>${esc(a.name)}</h2><div class="sub">${esc(a.cat)} · ${esc(a.diff || '')}</div></div></div>
    <div class="section"><h3>Monsters <span class="n">${a.monsters.length}</span></h3><div class="cards">${a.monsters.map((id) => monsterById.get(id)).filter(Boolean).map(monsterCard).join('')}</div></div>
    ${a.rewards?.length ? `<div class="section"><h3>Reward for a full clear</h3>${a.rewards.map((x) => `<div class="src"><span>${tileFor(x.id)}</span><div class="what">${nameLink(x.id)}</div><div class="rate"><b>${pct(x.ch)}</b><small>${qty(x.q)} per clear</small></div></div>`).join('')}
      ${a.gp ? `<p class="note">Plus ${fmt(a.gp[0])}${a.gp[1] !== a.gp[0] ? '–' + fmt(a.gp[1]) : ''} GP. Monsters inside a dungeon drop nothing; this reward is the loot.</p>` : '<p class="note">Monsters inside a dungeon drop nothing; this reward is the loot.</p>'}</div>
      ${a.rewards.map((x) => byId.get(x.id)?.contents ? contentsSection(byId.get(x.id)).replace('<h3>Contains', `<h3>${esc(byId.get(x.id).name)} contains`) : '').join('')}` : ''}
    ${a.dungeon ? '' : `<div class="section"><h3>All loot here <span class="n">${rows.length}</span></h3>
      ${rows.map((x) => `<div class="src"><span>${tileFor(x.id)}</span><div class="what">${nameLink(x.id)}<small>Best from <a href="#/monster/${encodeURIComponent(x.fromId)}">${esc(x.from)}</a></small></div><div class="rate"><b>${pct(x.ch)}</b><small>${qty(x.q)} per kill</small></div></div>`).join('')}</div>`}</div>`;
}

// ------------------------------------------------------------------ quick jump

function jumpIndex() {
  return [
    ...DB.items.map((i) => ({ name: i.name, kind: i.kind, href: `#/item/${encodeURIComponent(i.id)}`, item: i })),
    ...(DB.monsters || []).map((m) => ({ name: m.name, kind: 'Monster', href: `#/monster/${encodeURIComponent(m.id)}` })),
    ...(DB.areas || []).map((a) => ({ name: a.name, kind: a.dungeon ? 'Dungeon' : 'Area', href: `#/area/${encodeURIComponent(a.id)}` })),
  ];
}
function setupJump() {
  const input = $('#jump'), list = $('#jump-list');
  let idx = [], hits = [], sel = 0;
  const draw = () => {
    list.innerHTML = hits.map((h, n) => `<a href="${h.href}" role="option" class="${n === sel ? 'on' : ''}" aria-selected="${n === sel}">${h.item ? tile(h.item) : ''}<span>${esc(h.name)}</span><span class="kind">${esc(h.kind)}</span></a>`).join('');
    list.hidden = !hits.length; input.setAttribute('aria-expanded', String(!!hits.length));
  };
  input.oninput = () => {
    if (!idx.length) idx = jumpIndex();
    const q = input.value.trim().toLowerCase();
    if (!q) { hits = []; draw(); return; }
    hits = idx.map((x) => { const n = x.name.toLowerCase(); const s = n === q ? 0 : n.startsWith(q) ? 1 : n.includes(' ' + q) ? 2 : n.includes(q) ? 3 : 9; return [s, x]; })
      .filter((x) => x[0] < 9).sort((a, b) => a[0] - b[0] || a[1].name.length - b[1].name.length).slice(0, 12).map((x) => x[1]);
    sel = 0; draw();
  };
  input.onkeydown = (e) => {
    if (e.key === 'ArrowDown') { sel = Math.min(sel + 1, hits.length - 1); draw(); e.preventDefault(); }
    else if (e.key === 'ArrowUp') { sel = Math.max(sel - 1, 0); draw(); e.preventDefault(); }
    else if (e.key === 'Enter' && hits[sel]) { location.hash = hits[sel].href; input.value = ''; hits = []; draw(); input.blur(); }
    else if (e.key === 'Escape') { input.value = ''; hits = []; draw(); input.blur(); }
  };
  list.onclick = () => { input.value = ''; hits = []; draw(); };
  input.onblur = () => setTimeout(() => { hits = []; draw(); }, 150);
  document.addEventListener('keydown', (e) => {
    const typing = /INPUT|SELECT|TEXTAREA/.test(document.activeElement?.tagName);
    if ((e.key === '/' && !typing) || (e.key === 'k' && (e.ctrlKey || e.metaKey))) { e.preventDefault(); input.focus(); }
    if (e.key === 'Escape') { closePanel(); closeDrawer(); }
  });
}

// ------------------------------------------------------------------ routing

function route() {
  const h = location.hash.replace(/^#\/?/, '');
  const [path, query = ''] = h.split('?');
  const [view, id] = path.split('/');
  return { view: view || 'items', id: id && decodeURIComponent(id), query };
}
function render() {
  const r = route();
  $$('[data-nav]').forEach((a) => a.classList.toggle('on', a.dataset.nav === r.view || (r.view === 'monster' && a.dataset.nav === 'monsters') || (r.view === 'area' && a.dataset.nav === 'areas')));
  if (r.view !== 'items') closePanel();
  if (r.view === 'items') { st = fromQuery(r.query); viewItems(); }
  else if (r.view === 'item') openItem(r.id, true);
  else if (r.view === 'compare') viewCompare((new URLSearchParams(r.query).get('ids') || '').split(',').filter(Boolean));
  else if (r.view === 'monsters') viewMonsters();
  else if (r.view === 'monster') viewMonster(r.id);
  else if (r.view === 'areas') viewAreas();
  else if (r.view === 'area') viewArea(r.id);
  else { st = blank(); viewItems(); }
  drawTray();
  if (r.view !== 'items') window.scrollTo(0, 0);
}

async function start() {
  try {
    const res = await fetch('data.json', { cache: 'no-cache' });
    DB = await res.json();
  } catch (e) {
    $('#app').innerHTML = `<p class="empty">The database could not be loaded (${esc(e.message)}).</p>`;
    return;
  }
  for (const s of DB.stats) s.pct = !!s.pct;
  for (const i of DB.items) { prepare(i); byId.set(i.id, i); }
  for (const m of DB.monsters || []) monsterById.set(m.id, m);
  for (const a of DB.areas || []) areaById.set(a.id, a);
  $('#shade').onclick = () => { closePanel(); closeDrawer(); };
  window.addEventListener('hashchange', render);
  setupJump();
  render();
}
start();
