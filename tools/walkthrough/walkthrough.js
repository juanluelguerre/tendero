// The walkthrough's timing page (docs/specs/redesign-walkthrough.md). It runs from
// disk, keeps its state in this browser only, and leaves it by one route: the
// exported .json the moderator commits. The task wording is the kit's; change
// both together, and never between the baseline and R-6.
'use strict';

const TEXT = {
  es: {
    title: 'Walkthrough del rediseño',
    session: 'Sesión',
    participant: 'Participante',
    round: 'Ronda',
    baseline: 'Línea base (hoy)',
    after: 'Después (R-6)',
    firstWidth: 'Primer ancho',
    consent: 'Consentimiento leído y aceptado',
    recorded: 'Acepta grabar la pantalla (se borra tras contar pasos)',
    startsAt: 'Empieza en:',
    doneWhen: 'Hecha cuando:',
    card: 'Tarjeta (pedido, agente, descripción…):',
    start: 'Empezar',
    stop: 'Parar',
    reset: 'Reiniciar',
    outcome: 'Resultado',
    done: 'Hecha',
    helped: 'Hecha con ayuda',
    failed: 'No hecha',
    interactions: 'Interacciones',
    errors: 'Errores',
    seq: 'En conjunto, ¿qué tan fácil o difícil te ha resultado? (1 muy difícil · 7 muy fácil)',
    notes: 'Observaciones (nunca datos personales)',
    susHelp: 'Una vez, tras las cinco tareas del backoffice. 1 totalmente en desacuerdo · 5 totalmente de acuerdo.',
    susScore: 'Puntuación SUS (0–100):',
    results: 'Resultados',
    export: 'Exportar .json',
    import: 'Importar .json',
    clear: 'Vaciar',
    clearAgain: 'Pulsa otra vez para vaciar',
    privacy: 'Todo se guarda solo en este navegador. El .json lleva el código del participante, nunca su nombre.',
    task: 'Tarea', width: 'Ancho', time: 'Tiempo', result: 'Resultado',
  },
  en: {
    title: 'Redesign walkthrough',
    session: 'Session',
    participant: 'Participant',
    round: 'Round',
    baseline: 'Baseline (today)',
    after: 'After (R-6)',
    firstWidth: 'First width',
    consent: 'Consent read and given',
    recorded: 'Agrees to a screen recording (deleted once steps are counted)',
    startsAt: 'Starts at:',
    doneWhen: 'Done when:',
    card: 'Card (order, agent, description…):',
    start: 'Start',
    stop: 'Stop',
    reset: 'Reset',
    outcome: 'Outcome',
    done: 'Done',
    helped: 'Done with help',
    failed: 'Not done',
    interactions: 'Interactions',
    errors: 'Errors',
    seq: 'Overall, how difficult or easy was this task? (1 very difficult · 7 very easy)',
    notes: 'Observations (never personal data)',
    susHelp: 'Once, after the five backoffice tasks. 1 strongly disagree · 5 strongly agree.',
    susScore: 'SUS score (0–100):',
    results: 'Results',
    export: 'Export .json',
    import: 'Import .json',
    clear: 'Clear',
    clearAgain: 'Press again to clear',
    privacy: 'Everything stays in this browser. The .json carries the participant\'s code, never their name.',
    task: 'Task', width: 'Width', time: 'Time', result: 'Outcome',
  },
};

const TASKS = [
  { id: 'B1', starts: { es: 'inicio del backoffice', en: 'backoffice home' }, done: { es: 'el producto está Activo', en: 'the product is Active' },
    es: 'Hay un producto pendiente de revisar antes de ponerse a la venta. Compruébalo y ponlo a la venta.',
    en: 'A product is waiting to be checked before it goes on sale. Check it and put it on sale.' },
  { id: 'B2', starts: { es: 'inicio del backoffice', en: 'backoffice home' }, done: { es: 'aprobada o rechazada', en: 'approved or rejected' },
    es: 'El sistema ha propuesto datos sobre los productos que alguien tiene que validar. Mira el que ofrece menos confianza y decide si es correcto.',
    en: 'The system has proposed facts about products that somebody has to validate. Look at the one it is least sure of and decide whether it is right.' },
  { id: 'B3', starts: { es: 'inicio del backoffice', en: 'backoffice home' }, done: { es: 'Enviado', en: 'Shipped' },
    es: 'El pedido de la tarjeta ya está empaquetado. Déjalo registrado como enviado.',
    en: 'The order on the card is packed. Record it as shipped.' },
  { id: 'B4', starts: { es: 'inicio del backoffice', en: 'backoffice home' }, done: { es: 'aprobada o rechazada', en: 'approved or rejected' },
    es: 'Un cliente ha pedido devolver algo. Decide esa devolución con tu criterio.',
    en: 'A customer has asked to return something. Decide that return as you see fit.' },
  { id: 'B5', starts: { es: 'inicio del backoffice', en: 'backoffice home' }, done: { es: 'Suspendido, con motivo', en: 'Suspended, with a reason' },
    es: 'El agente de la tarjeta está haciendo pedidos raros. Impide que siga comprando y deja anotado por qué.',
    en: 'The agent on the card is placing strange orders. Stop it from buying, and note why.' },
  { id: 'S1', starts: { es: 'la portada (segundo ancho: el carrito)', en: 'home (second width: the cart)' }, done: { es: 'el listado de la categoría en pantalla', en: 'the category listing is on screen' },
    es: 'Buscas una mochila y quieres ver todas las que tiene la tienda.',
    en: 'You want a backpack and would like to see every one the shop has.' },
  { id: 'S2', starts: { es: 'la portada', en: 'home' }, done: { es: 'su ficha abierta', en: 'its page is open' },
    es: 'Buscas lo que describe la tarjeta, pero no sabes cómo lo llama la tienda. Encuéntralo y abre su ficha.',
    en: 'You are looking for what the card describes, but you do not know what the shop calls it. Find it and open its page.' },
  { id: 'S3', starts: { es: 'el producto elegido', en: 'the chosen product' }, done: { es: 'el carrito lo muestra', en: 'the cart shows it' },
    es: 'Añade este producto al carrito, en la versión que prefieras.',
    en: 'Add this product to the cart, in whichever version you prefer.' },
  { id: 'S4', starts: { es: 'el carrito', en: 'the cart' }, done: { es: 'la página del pedido', en: 'the order page is shown' },
    es: 'Compra lo que hay en el carrito sin crear una cuenta. Usa la dirección de la tarjeta.',
    en: 'Buy what is in the cart without creating an account. Use the address on the card.' },
];

/** The three repeated at the second width, where width decides the most. */
const SECOND_WIDTH = ['B3', 'S1', 'S4'];

const SUS = [
  { en: 'I think that I would like to use this backoffice frequently.', es: 'Creo que me gustaría usar este backoffice con frecuencia.' },
  { en: 'I found the backoffice unnecessarily complex.', es: 'El backoffice me ha parecido innecesariamente complejo.' },
  { en: 'I thought the backoffice was easy to use.', es: 'Me ha parecido fácil de usar.' },
  { en: 'I think that I would need the support of a technical person to be able to use this backoffice.', es: 'Creo que necesitaría la ayuda de un técnico para poder usarlo.' },
  { en: 'I found the various functions in this backoffice were well integrated.', es: 'Sus distintas funciones me han parecido bien integradas.' },
  { en: 'I thought there was too much inconsistency in this backoffice.', es: 'Me ha parecido demasiado inconsistente.' },
  { en: 'I would imagine that most people would learn to use this backoffice very quickly.', es: 'Imagino que la mayoría de la gente aprendería a usarlo muy rápido.' },
  { en: 'I found the backoffice very cumbersome to use.', es: 'Me ha resultado muy engorroso de usar.' },
  { en: 'I felt very confident using the backoffice.', es: 'Me he sentido muy seguro al usarlo.' },
  { en: 'I needed to learn a lot of things before I could get going with this backoffice.', es: 'He necesitado aprender muchas cosas antes de poder manejarlo.' },
];

const OUTCOMES = ['done', 'helped', 'failed'];
const KEY = 'tendero.walkthrough';

/** Odd items give answer − 1, even ones 5 − answer; the sum times 2.5. Null until all ten are answered. */
function susScore(answers) {
  if (answers.length !== 10 || answers.some((a) => !a)) return null;
  return answers.reduce((sum, a, i) => sum + (i % 2 === 0 ? a - 1 : 5 - a), 0) * 2.5;
}

/** The running order: all nine at the first width, then the three at the second. */
function runsOf(firstWidth) {
  const second = firstWidth === 1440 ? 390 : 1440;
  return [
    ...TASKS.map((task) => ({ key: `${task.id}@${firstWidth}`, task, width: firstWidth })),
    ...SECOND_WIDTH.map((id) => ({ key: `${id}@${second}`, task: TASKS.find((t) => t.id === id), width: second })),
  ];
}

function emptyState() {
  return { lang: 'es', participant: 'P1', round: 'baseline', firstWidth: 1440, consent: false, recorded: false, runs: {}, sus: Array(10).fill(0) };
}

function load() {
  try {
    const saved = JSON.parse(localStorage.getItem(KEY) ?? 'null');
    return saved ? { ...emptyState(), ...saved } : emptyState();
  } catch {
    return emptyState();
  }
}

let state = load();
const timers = {};

function save() {
  try { localStorage.setItem(KEY, JSON.stringify(state)); } catch { /* the page still works, it just forgets on reload */ }
}

function t(key) { return TEXT[state.lang][key]; }

function runOf(key) {
  state.runs[key] ??= { target: '', elapsedMs: 0, outcome: '', interactions: null, errors: null, seq: 0, notes: '' };
  return state.runs[key];
}

function clock(ms) {
  const s = Math.round(ms / 1000);
  return `${Math.floor(s / 60)}:${String(s % 60).padStart(2, '0')}`;
}

function radios(container, name, values, labelOf, checked, onChange) {
  for (const value of values) {
    const label = document.createElement('label');
    const input = document.createElement('input');
    input.type = 'radio';
    input.name = name;
    input.value = String(value);
    input.checked = checked === value;
    input.addEventListener('change', () => onChange(value));
    label.append(input, ' ', labelOf(value));
    container.append(label);
  }
}

function renderTasks() {
  const list = document.getElementById('tasks');
  const template = document.getElementById('task');
  list.replaceChildren();
  for (const { key, task, width } of runsOf(state.firstWidth)) {
    const run = runOf(key);
    const item = template.content.firstElementChild.cloneNode(true);
    item.querySelector('.code').textContent = task.id;
    item.querySelector('.width').textContent = `${width} px`;
    item.querySelector('.wording').textContent = task[state.lang];
    item.querySelector('.starts').textContent = task.starts[state.lang];
    item.querySelector('.done').textContent = task.done[state.lang];

    const bind = (name, parse) => {
      const input = item.querySelector(`[name="${name}"]`);
      input.value = run[name] ?? '';
      input.addEventListener('input', () => { run[name] = parse(input.value); save(); renderSummary(); });
    };
    bind('target', (v) => v);
    bind('interactions', (v) => (v === '' ? null : Number(v)));
    bind('errors', (v) => (v === '' ? null : Number(v)));
    bind('notes', (v) => v);

    const output = item.querySelector('.clock');
    const shown = () => run.elapsedMs + (timers[key] ? Date.now() - timers[key].since : 0);
    output.textContent = clock(shown());
    item.querySelector('.start').addEventListener('click', () => {
      if (timers[key]) return;
      timers[key] = { since: Date.now(), tick: setInterval(() => { output.textContent = clock(shown()); }, 250) };
      item.classList.add('running');
    });
    item.querySelector('.stop').addEventListener('click', () => {
      if (!timers[key]) return;
      run.elapsedMs = shown();
      clearInterval(timers[key].tick);
      delete timers[key];
      item.classList.remove('running');
      output.textContent = clock(run.elapsedMs);
      save(); renderSummary();
    });
    item.querySelector('.reset').addEventListener('click', () => {
      if (timers[key]) { clearInterval(timers[key].tick); delete timers[key]; }
      run.elapsedMs = 0;
      item.classList.remove('running');
      output.textContent = clock(0);
      save(); renderSummary();
    });

    radios(item.querySelector('.outcome'), `outcome-${key}`, OUTCOMES, (o) => t(o), run.outcome,
      (o) => { run.outcome = o; save(); renderSummary(); });
    radios(item.querySelector('.seq'), `seq-${key}`, [1, 2, 3, 4, 5, 6, 7], String, run.seq,
      (v) => { run.seq = v; save(); renderSummary(); });

    translate(item);
    list.append(item);
  }
}

function renderSus() {
  const list = document.getElementById('susItems');
  list.replaceChildren();
  SUS.forEach((item, i) => {
    const li = document.createElement('li');
    const text = document.createElement('p');
    text.textContent = item[state.lang];
    const scale = document.createElement('div');
    scale.className = 'scale';
    radios(scale, `sus-${i}`, [1, 2, 3, 4, 5], String, state.sus[i], (v) => {
      state.sus[i] = v; save(); showSus(); renderSummary();
    });
    li.append(text, scale);
    list.append(li);
  });
  showSus();
}

function showSus() {
  const score = susScore(state.sus);
  document.getElementById('susScore').textContent = score === null ? '—' : String(score);
}

function renderSummary() {
  const table = document.getElementById('summary');
  const head = [t('task'), t('width'), t('time'), t('result'), t('interactions'), t('errors'), 'SEQ'];
  const rows = runsOf(state.firstWidth).map(({ key, task, width }) => {
    const run = runOf(key);
    return [task.id, `${width}`, run.elapsedMs ? clock(run.elapsedMs) : '—', run.outcome ? t(run.outcome) : '—',
      run.interactions ?? '—', run.errors ?? '—', run.seq || '—'];
  });
  table.replaceChildren();
  const thead = table.createTHead().insertRow();
  head.forEach((h) => { const th = document.createElement('th'); th.textContent = h; thead.append(th); });
  const body = table.createTBody();
  rows.forEach((cells) => { const row = body.insertRow(); cells.forEach((c) => { row.insertCell().textContent = String(c); }); });
}

function translate(root = document) {
  root.querySelectorAll('[data-t]').forEach((el) => { el.textContent = t(el.dataset.t); });
}

function renderAll() {
  document.documentElement.lang = state.lang;
  document.querySelectorAll('.lang').forEach((b) => b.setAttribute('aria-pressed', String(b.dataset.lang === state.lang)));
  for (const id of ['participant', 'round', 'firstWidth']) document.getElementById(id).value = String(state[id]);
  for (const id of ['consent', 'recorded']) document.getElementById(id).checked = state[id];
  translate();
  renderTasks();
  renderSus();
  renderSummary();
}

/** What is committed: the code, the round, and the numbers. Never a name. */
function exported() {
  return {
    kit: 'docs/specs/redesign-walkthrough.md',
    participant: state.participant,
    round: state.round,
    date: new Date().toISOString().slice(0, 10),
    consent: state.consent,
    firstWidth: state.firstWidth,
    tasks: runsOf(state.firstWidth).map(({ key, task, width }) => {
      const run = runOf(key);
      return { task: task.id, width, seconds: Math.round(run.elapsedMs / 1000), outcome: run.outcome || null,
        interactions: run.interactions, errors: run.errors, seq: run.seq || null, notes: run.notes || null };
    }),
    sus: { answers: state.sus, score: susScore(state.sus) },
  };
}

document.querySelectorAll('.lang').forEach((b) => b.addEventListener('click', () => { state.lang = b.dataset.lang; save(); renderAll(); }));
document.getElementById('participant').addEventListener('change', (e) => { state.participant = e.target.value; save(); });
document.getElementById('round').addEventListener('change', (e) => { state.round = e.target.value; save(); });
document.getElementById('firstWidth').addEventListener('change', (e) => { state.firstWidth = Number(e.target.value); save(); renderTasks(); renderSummary(); });
for (const id of ['consent', 'recorded']) document.getElementById(id).addEventListener('change', (e) => { state[id] = e.target.checked; save(); });

document.getElementById('export').addEventListener('click', () => {
  const data = exported();
  const url = URL.createObjectURL(new Blob([JSON.stringify(data, null, 2) + '\n'], { type: 'application/json' }));
  const a = document.createElement('a');
  a.href = url;
  a.download = `walkthrough-${data.round}-${data.participant}-${data.date}.json`;
  a.click();
  URL.revokeObjectURL(url);
});

document.getElementById('import').addEventListener('change', async (e) => {
  const file = e.target.files?.[0];
  if (!file) return;
  const data = JSON.parse(await file.text());
  state = { ...emptyState(), lang: state.lang, participant: data.participant, round: data.round, firstWidth: data.firstWidth, consent: data.consent };
  for (const row of data.tasks) {
    state.runs[`${row.task}@${row.width}`] = { target: '', elapsedMs: row.seconds * 1000, outcome: row.outcome ?? '',
      interactions: row.interactions, errors: row.errors, seq: row.seq ?? 0, notes: row.notes ?? '' };
  }
  state.sus = data.sus.answers;
  save(); renderAll();
});

let clearArmed = false;
document.getElementById('clear').addEventListener('click', (e) => {
  if (!clearArmed) { clearArmed = true; e.target.textContent = t('clearAgain'); return; }
  clearArmed = false;
  state = { ...emptyState(), lang: state.lang };
  save(); renderAll();
});

renderAll();
