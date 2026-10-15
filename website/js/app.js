'use strict';

const API_BASE = 'http://localhost:3000';
const SECRET_STORAGE_KEY = 'drops_admin_secret';
const ALPHABET = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';
const DAY_MS = 86400000;
const LOGS_PAGE = 'logs';

let adminSecret = '';
let currentPage = 'dashboard';
let currentCredential = '';
let currentCredentialKind = '';
let loadedKeys = [];
let currentSearch = '';
let toastId = 0;
let isLoggingOut = false;

const $ = (selector, root = document) => root.querySelector(selector);
const $$ = (selector, root = document) => Array.from(root.querySelectorAll(selector));
const accessScreen = $('#access-screen');
const appShell = $('#app-shell');
const accessForm = $('#access-form');
const accessInput = $('#admin-secret');
const accessError = $('#access-error');
const accessSubmit = $('#access-submit');
const toastRegion = $('#toast-region');

function safeStorageGet(key) {
  try { return sessionStorage.getItem(key) || ''; } catch (_) { return ''; }
}
function safeStorageSet(key, value) {
  try { sessionStorage.setItem(key, value); return true; } catch (_) { return false; }
}
function safeStorageRemove(key) {
  try { sessionStorage.removeItem(key); } catch (_) { /* armazenamento pode estar bloqueado */ }
}
function toast(message, type = 'info') {
  const element = document.createElement('div');
  element.className = `toast ${type === 'success' ? 'success' : type === 'error' ? 'error' : type === 'warning' ? 'warning' : ''}`.trim();
  element.textContent = String(message);
  element.dataset.toastId = String(++toastId);
  toastRegion.appendChild(element);
  window.setTimeout(() => element.remove(), 4600);
}
function setAccessError(message) {
  accessError.textContent = message || '';
}
function showAccess(message = '') {
  isLoggingOut = true;
  adminSecret = '';
  safeStorageRemove(SECRET_STORAGE_KEY);
  appShell.classList.add('hidden');
  accessScreen.classList.remove('hidden');
  accessInput.value = '';
  setAccessError(message);
  clearIssuedCredential();
  isLoggingOut = false;
}
function logout() {
  showAccess('');
  toast('Sessão encerrada.', 'success');
  accessInput.focus();
}
function unauthorized() {
  if (isLoggingOut) return;
  showAccess('Acesso negado.');
}
function genericFailure(error) {
  const known = error && ['REQUEST_REJECTED', 'HTTP_FAILURE', 'UNAUTHORIZED', 'RANDOM_UNAVAILABLE', 'SHA_UNAVAILABLE', 'COPY_FAILED'].includes(error.message);
  if (!known) toast('Não foi possível conectar ao Worker. Verifique a conexão e a configuração de CORS.', 'error');
}
async function api(path, options = {}) {
  if (!adminSecret) throw new Error('UNAUTHORIZED');
  const method = options.method || 'GET';
  const headers = { 'x-admin': adminSecret };
  const config = { method, headers, cache: 'no-store' };
  if (options.body !== undefined) {
    headers['Content-Type'] = 'application/json';
    config.body = JSON.stringify(options.body);
  }
  let response;
  try {
    response = await fetch(`${API_BASE}${path}`, config);
  } catch (_) {
    const error = new Error('NETWORK_FAILURE');
    throw error;
  }
  if (response.status === 401) {
    unauthorized();
    throw new Error('UNAUTHORIZED');
  }
  let data = null;
  try { data = await response.json(); } catch (_) { /* resposta pode não conter JSON */ }
  if (!response.ok) {
    toast('O servidor não concluiu a solicitação.', 'error');
    throw new Error('HTTP_FAILURE');
  }
  if (data && data.ok === false) {
    const message = (typeof data.message === 'string' && data.message.trim()) || (typeof data.error === 'string' && data.error.trim()) || 'O servidor recusou a operação.';
    toast(message, 'error');
    throw new Error('REQUEST_REJECTED');
  }
  if (!data || data.ok !== true) {
    toast('O servidor retornou uma resposta inválida.', 'error');
    throw new Error('HTTP_FAILURE');
  }
  return data;
}
async function testAuthentication(secret) {
  adminSecret = secret;
  safeStorageSet(SECRET_STORAGE_KEY, secret);
  try {
    const data = await api('/admin/stats');
    adminSecret = secret;
    accessScreen.classList.add('hidden');
    appShell.classList.remove('hidden');
    setAccessError('');
    showPage('dashboard');
    renderStats(data);
    toast('Acesso autorizado.', 'success');
    await Promise.allSettled([loadKeys(), loadTrials()]);
    return true;
  } catch (error) {
    if (error.message === 'UNAUTHORIZED') {
      setAccessError('Acesso negado.');
    } else {
      genericFailure(error);
    }
    if (error.message !== 'UNAUTHORIZED') {
      adminSecret = '';
      safeStorageRemove(SECRET_STORAGE_KEY);
    }
    return false;
  }
}
function showPage(page) {
  const titles = {
    dashboard: ['VISÃO GERAL', 'Visão geral', 'Resumo das licenças e da atividade do loader.'],
    keys: ['LICENÇAS', 'Licenças', 'Pesquise, consulte e gerencie as licenças cadastradas.'],
    'create-key': ['LICENÇAS', 'Gerar key', 'Crie uma nova licença para o loader DROPS.'],
    'create-code': ['ATIVAÇÃO', 'Gerar código', 'Crie um código de ativação de uso único.'],
    trials: ['ATIVIDADE', 'Histórico de trials', 'Consulte os IPs registrados no sistema de trial.'],
    logs: ['SISTEMA', 'Logs', 'Ações registradas no sistema: criações, bans, ativações e mais.']
  };
  if (!titles[page]) return;
  if (currentPage !== page) clearIssuedCredential();
  currentPage = page;
  $$('.page').forEach(section => section.classList.toggle('active', section.id === `page-${page}`));
  $$('.nav-button').forEach(button => button.classList.toggle('active', button.dataset.page === page));
  $('#page-eyebrow').textContent = titles[page][0];
  $('#page-title').textContent = titles[page][1];
  $('#page-description').textContent = titles[page][2];
  if (page === 'keys') loadKeys();
  if (page === 'trials') loadTrials();
  if (page === 'logs') loadLogs();
  if (page === 'dashboard') loadStats();
}
function setBusy(button, busy, busyText) {
  if (!button) return () => {};
  const originalText = button.textContent;
  button.disabled = busy;
  if (busy) button.innerHTML = `<span class="refresh-mark" aria-hidden="true"></span>${busyText || 'Processando'}`;
  else button.textContent = originalText;
  return () => { button.disabled = false; button.textContent = originalText; };
}
function renderStats(stats) {
  $('#stat-active').textContent = formatCount(stats.active);
  $('#stat-expired').textContent = formatCount(stats.expired);
  $('#stat-banned').textContent = formatCount(stats.banned);
  $('#stat-trials').textContent = formatCount(stats.trials);
  $('#stat-codes').textContent = formatCount(stats.codes_pending);
}
function formatCount(value) {
  const n = Number(value);
  return Number.isFinite(n) ? new Intl.NumberFormat('pt-BR').format(n) : '—';
}
async function loadStats() {
  if (!adminSecret) return false;
  try { renderStats(await api('/admin/stats')); return true; }
  catch (error) { genericFailure(error); return false; }
}
async function loadKeys() {
  if (!adminSecret) return;
  const body = $('#keys-tbody');
  body.innerHTML = '<tr><td colspan="7" class="table-empty">Carregando keys…</td></tr>';
  try {
    let data;
    if (currentSearch.trim()) data = await api(`/admin/search?q=${encodeURIComponent(currentSearch.trim())}`);
    else data = await api('/admin/keys?limit=100');
    loadedKeys = Array.isArray(data.keys) ? data.keys : [];
    renderKeys(loadedKeys);
    return true;
  } catch (error) {
    if (error.message !== 'UNAUTHORIZED' && error.message !== 'REQUEST_REJECTED' && error.message !== 'HTTP_FAILURE') {
      body.innerHTML = '<tr><td colspan="7" class="table-empty">Não foi possível carregar as keys.</td></tr>';
    } else if (error.message === 'HTTP_FAILURE') {
      body.innerHTML = '<tr><td colspan="7" class="table-empty">Não foi possível carregar as keys.</td></tr>';
    }
    genericFailure(error);
    return false;
  }
}
function renderKeys(keys) {
  const body = $('#keys-tbody');
  body.replaceChildren();
  $('#keys-count').textContent = `${keys.length} ${keys.length === 1 ? 'registro' : 'registros'}`;
  $('#keys-summary').textContent = currentSearch.trim() ? `Resultados para “${currentSearch.trim()}”.` : 'Até 100 registros recentes, conforme retorno do Worker.';
  if (!keys.length) {
    const row = document.createElement('tr');
    const cell = document.createElement('td');
    cell.colSpan = 7;
    cell.className = 'table-empty';
    cell.textContent = currentSearch.trim() ? 'Nenhuma key encontrada para esta busca.' : 'Nenhuma key cadastrada.';
    row.appendChild(cell); body.appendChild(row); return;
  }
  keys.forEach(key => {
    const row = document.createElement('tr');
    const hash = String(key.h ?? '—');
    const banned = key.banned === true || key.banned === 1 || key.banned === 'true';
    const expires = Number(key.expires_at);
    const hasExpiration = Number.isFinite(expires) && expires > 0;
    const expired = hasExpiration && expires <= Date.now();
    const status = banned ? ['Banida', 'banned'] : expired ? ['Expirada', 'expired'] : hasExpiration ? ['Ativa', 'active'] : ['Indefinida', 'unknown'];

    const hashCell = document.createElement('td');
    const hashWrap = document.createElement('div'); hashWrap.className = 'hash-cell';
    const hashText = document.createElement('span'); hashText.className = 'mono'; hashText.textContent = hash;
    hashWrap.appendChild(hashText); hashCell.appendChild(hashWrap);
    row.appendChild(hashCell);
    row.appendChild(textCell(key.product || '—'));

    const expirationCell = document.createElement('td'); expirationCell.appendChild(renderExpiration(hasExpiration ? expires : null)); row.appendChild(expirationCell);
    const hwid = key.hwid ? String(key.hwid) : '';
    const hwidCell = textCell(hwid ? (hwid.length > 12 ? `${hwid.slice(0, 8)}…` : hwid) : '—');
    if (hwid) { hwidCell.title = 'HWID abreviado por segurança visual'; hwidCell.classList.add('mono'); }
    row.appendChild(hwidCell);
    const noteCell = textCell(key.note || '—'); noteCell.classList.add('table-note'); noteCell.title = String(key.note || ''); row.appendChild(noteCell);

    const statusCell = document.createElement('td');
    const badge = document.createElement('span'); badge.className = `status ${status[1]}`; badge.textContent = status[0]; statusCell.appendChild(badge); row.appendChild(statusCell);

    const actionCell = document.createElement('td');
    const action = document.createElement('button');
    action.type = 'button'; action.className = `button small ${banned ? 'success' : 'danger'}`;
    action.textContent = banned ? 'DESBANIR' : 'BANIR';
    action.addEventListener('click', () => changeBanStatus(hash, banned));
    actionCell.appendChild(action); row.appendChild(actionCell);
    body.appendChild(row);
  });
}
function textCell(value) {
  const cell = document.createElement('td');
  cell.textContent = String(value);
  return cell;
}
function renderExpiration(timestamp) {
  const wrap = document.createElement('div'); wrap.className = 'expiration';
  if (!timestamp) { const main = document.createElement('span'); main.textContent = '—'; wrap.appendChild(main); return wrap; }
  const date = new Date(timestamp);
  const main = document.createElement('span');
  main.textContent = Number.isNaN(date.getTime()) ? 'Data inválida' : new Intl.DateTimeFormat('pt-BR').format(date);
  const rest = document.createElement('small');
  const remaining = Math.ceil((timestamp - Date.now()) / DAY_MS);
  rest.textContent = remaining > 0 ? `${remaining} ${remaining === 1 ? 'dia restante' : 'dias restantes'}` : remaining === 0 ? 'Expira hoje' : `Expirada há ${Math.abs(remaining)} ${Math.abs(remaining) === 1 ? 'dia' : 'dias'}`;
  wrap.append(main, rest); return wrap;
}
async function changeBanStatus(shortHash, isBanned) {
  if (!shortHash || shortHash === '—') { toast('O identificador desta key não está disponível.', 'error'); return; }
  if (!isBanned && !window.confirm(`Tem certeza de que deseja banir a key ${shortHash}?`)) return;
  try {
    await api(isBanned ? '/admin/unban' : '/admin/ban', { method: 'POST', body: { hash: shortHash } });
    toast(isBanned ? 'Key desbanida.' : 'Key banida.', 'success');
    await Promise.allSettled([loadKeys(), loadStats()]);
  } catch (error) { genericFailure(error); }
}
async function loadTrials() {
  if (!adminSecret) return;
  const body = $('#trials-tbody');
  body.innerHTML = '<tr><td colspan="2" class="table-empty">Carregando trials…</td></tr>';
  try {
    const data = await api('/admin/trials');
    const trials = Array.isArray(data.trials) ? data.trials.slice() : [];
    trials.sort((a, b) => Number(b.at || 0) - Number(a.at || 0));
    body.replaceChildren();
    $('#trials-count').textContent = `${trials.length} ${trials.length === 1 ? 'registro' : 'registros'}`;
    $('#trials-summary').textContent = `${trials.length} IPs retornados pelo Worker.`;
    if (!trials.length) {
      const row = document.createElement('tr'); const cell = document.createElement('td');
      cell.colSpan = 2; cell.className = 'table-empty'; cell.textContent = 'Nenhum trial registrado.'; row.appendChild(cell); body.appendChild(row); return;
    }
    trials.forEach(trial => {
      const row = document.createElement('tr');
      const ip = textCell(trial.ip || '—'); ip.classList.add('trial-ip'); row.appendChild(ip);
      const stamp = Number(trial.at);
      const dateCell = textCell(Number.isFinite(stamp) && stamp > 0 ? new Intl.DateTimeFormat('pt-BR', { dateStyle: 'short', timeStyle: 'medium' }).format(new Date(stamp)) : '—');
      row.appendChild(dateCell); body.appendChild(row);
    });
    return true;
  } catch (error) {
    if (error.message !== 'UNAUTHORIZED' && error.message !== 'REQUEST_REJECTED') body.innerHTML = '<tr><td colspan="2" class="table-empty">Não foi possível carregar os trials.</td></tr>';
    genericFailure(error);
    return false;
  }
}
async function loadLogs() {
  if (!adminSecret) return;
  const body = $('#logs-tbody');
  body.innerHTML = '<tr><td colspan="3" class="table-empty">Carregando logs…</td></tr>';
  try {
    const data = await api('/admin/logs');
    const logs = Array.isArray(data.logs) ? data.logs : [];
    logs.sort((a, b) => Number(b.at || 0) - Number(a.at || 0));
    body.replaceChildren();
    $('#logs-count').textContent = `${logs.length} ${logs.length === 1 ? 'registro' : 'registros'}`;
    $('#logs-summary').textContent = `${logs.length} registros retornados pelo Worker.`;
    if (!logs.length) {
      const row = document.createElement('tr'); const cell = document.createElement('td');
      cell.colSpan = 3; cell.className = 'table-empty'; cell.textContent = 'Nenhum log registrado.'; row.appendChild(cell); body.appendChild(row); return;
    }
    logs.forEach(log => {
      const row = document.createElement('tr');
      const action = textCell(log.action || '—');
      action.style.color = log.action === 'BAN' ? '#f3a6b5' : log.action === 'UNBAN' ? '#31c997' : '#f3f0f5';
      row.appendChild(action);
      const details = textCell(log.details || '—'); details.title = log.details || ''; row.appendChild(details);
      const stamp = Number(log.at);
      const dateCell = textCell(Number.isFinite(stamp) && stamp > 0 ? new Intl.DateTimeFormat('pt-BR', { dateStyle: 'short', timeStyle: 'medium' }).format(new Date(stamp)) : '—');
      row.appendChild(dateCell); body.appendChild(row);
    });
    return true;
  } catch (error) {
    if (error.message !== 'UNAUTHORIZED' && error.message !== 'REQUEST_REJECTED') body.innerHTML = '<tr><td colspan="3" class="table-empty">Não foi possível carregar os logs.</td></tr>';
    genericFailure(error);
    return false;
  }
}
function generateCredential(prefix) {
  if (!window.crypto || typeof window.crypto.getRandomValues !== 'function') throw new Error('RANDOM_UNAVAILABLE');
  const bytes = new Uint8Array(12);
  window.crypto.getRandomValues(bytes);
  const groups = [];
  for (let i = 0; i < 3; i++) {
    let group = '';
    for (let j = 0; j < 4; j++) group += ALPHABET[bytes[i * 4 + j] % ALPHABET.length];
    groups.push(group);
  }
  return `${prefix}-${groups.join('-')}`;
}
async function sha256Hex(value) {
  if (!window.crypto || !window.crypto.subtle || typeof window.crypto.subtle.digest !== 'function') throw new Error('SHA_UNAVAILABLE');
  const bytes = new TextEncoder().encode(value);
  const digest = await window.crypto.subtle.digest('SHA-256', bytes);
  return Array.from(new Uint8Array(digest), byte => byte.toString(16).padStart(2, '0')).join('');
}
function clearIssuedCredential() {
  currentCredential = '';
  currentCredentialKind = '';
  $('#issued-key-value').textContent = '';
  $('#issued-code-value').textContent = '';
  $('#issued-key-box').classList.add('hidden');
  $('#issued-code-box').classList.add('hidden');
}
function showIssuedCredential(kind, credential) {
  clearIssuedCredential();
  currentCredential = credential;
  currentCredentialKind = kind;
  const box = kind === 'key' ? $('#issued-key-box') : $('#issued-code-box');
  const value = kind === 'key' ? $('#issued-key-value') : $('#issued-code-value');
  value.textContent = credential;
  box.classList.remove('hidden');
  box.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
}
async function createCredential(kind, form) {
  const submit = form.querySelector('button[type="submit"]');
  const restore = setBusy(submit, true, kind === 'key' ? 'Gerando key' : 'Gerando código');
  clearIssuedCredential();
  try {
    const formData = new FormData(form);
    const days = Number.parseInt(String(formData.get('days') || ''), 10);
    const product = String(formData.get('product') || 'paladins');
    const note = String(formData.get('note') || '').trim();
    if (!Number.isInteger(days) || days < 1 || days > 36500) { toast('Informe uma validade entre 1 e 36500 dias.', 'error'); return; }
    const credential = generateCredential(kind === 'key' ? 'DROPS' : 'ATV');
    const hash = await sha256Hex(credential);
    if (kind === 'key') {
      const expiresAt = Date.now() + days * DAY_MS;
      await api('/admin/key', { method: 'POST', body: { hash, expires_at: expiresAt, product, note } });
    } else {
      await api('/admin/code', { method: 'POST', body: { hash, days, product, note } });
    }
    showIssuedCredential(kind, credential);
    form.reset();
    const daysInput = form.querySelector('input[name="days"]');
    if (daysInput) daysInput.value = '30';
    toast(kind === 'key' ? 'Key criada e registrada.' : 'Código criado e registrado.', 'success');
    if (kind === 'key') {
      currentSearch = '';
      $('#key-search').value = '';
      await Promise.allSettled([loadKeys(), loadStats()]);
    } else {
      await loadStats();
    }
  } catch (error) {
    if (error.message === 'RANDOM_UNAVAILABLE') toast('Este navegador não oferece geração criptográfica segura.', 'error');
    else if (error.message === 'SHA_UNAVAILABLE') toast('Não foi possível calcular SHA-256 neste contexto do navegador.', 'error');
    genericFailure(error);
  } finally {
    restore();
  }
}
async function copyCredential(kind) {
  if (!currentCredential || currentCredentialKind !== kind) return;
  try {
    if (navigator.clipboard && typeof navigator.clipboard.writeText === 'function' && window.isSecureContext) {
      await navigator.clipboard.writeText(currentCredential);
    } else {
      const helper = document.createElement('textarea');
      helper.value = currentCredential;
      helper.setAttribute('readonly', '');
      helper.style.position = 'fixed'; helper.style.left = '-9999px'; helper.style.top = '0';
      document.body.appendChild(helper); helper.select();
      const copied = document.execCommand('copy');
      helper.remove();
      if (!copied) throw new Error('COPY_FAILED');
    }
    toast(kind === 'key' ? 'Key copiada.' : 'Código copiado.', 'success');
  } catch (_) {
    toast('Não foi possível copiar automaticamente. Selecione o código exibido e copie manualmente.', 'error');
  }
}

accessForm.addEventListener('submit', async event => {
  event.preventDefault();
  const secret = accessInput.value;
  if (!secret) { setAccessError('Informe o segredo administrativo.'); return; }
  accessSubmit.disabled = true;
  accessSubmit.textContent = 'Verificando…';
  setAccessError('');
  await testAuthentication(secret);
  accessSubmit.disabled = false;
  accessSubmit.textContent = 'Entrar';
});
$$('.nav-button, [data-page].action-tile').forEach(button => button.addEventListener('click', () => showPage(button.dataset.page)));
$('#logout-button').addEventListener('click', logout);
$('#refresh-button').addEventListener('click', async () => {
  const button = $('#refresh-button');
  const restore = setBusy(button, true, 'Atualizando');
  try {
    const results = [await loadStats()];
    if (currentPage === 'keys') results.push(await loadKeys());
    if (currentPage === 'trials') results.push(await loadTrials());
    if (results.every(Boolean)) toast('Dados atualizados.', 'success');
  } finally { restore(); }
});
$('#search-form').addEventListener('submit', async event => {
  event.preventDefault();
  currentSearch = $('#key-search').value.trim();
  await loadKeys();
});
$('#clear-search').addEventListener('click', async () => {
  $('#key-search').value = '';
  currentSearch = '';
  await loadKeys();
});
$('#create-key-form').addEventListener('submit', event => { event.preventDefault(); createCredential('key', event.currentTarget); });
$('#create-code-form').addEventListener('submit', event => { event.preventDefault(); createCredential('code', event.currentTarget); });
$('#copy-key-button').addEventListener('click', () => copyCredential('key'));
$('#copy-code-button').addEventListener('click', () => copyCredential('code'));
window.addEventListener('pagehide', clearIssuedCredential);

const savedSecret = safeStorageGet(SECRET_STORAGE_KEY);
if (savedSecret) {
  accessInput.value = '';
  testAuthentication(savedSecret);
}
