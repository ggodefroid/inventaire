/* Poste de controle du frigo.
   Une seule source : l'instantane pousse par la WebSocket. Rien n'est calcule
   ici qui le soit deja cote serveur ; le navigateur ne fait que dessiner. */
(() => {
'use strict';

/* ------------------------------------------------------------ utilitaires */

const $  = (s, r = document) => r.querySelector(s);
const $$ = (s, r = document) => Array.from(r.querySelectorAll(s));

const ECHAPPE = {'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'};
const esc = t => String(t ?? '').replace(/[&<>"']/g, c => ECHAPPE[c]);

const fr = new Intl.NumberFormat('fr-FR');
const fr1 = new Intl.NumberFormat('fr-FR', {maximumFractionDigits: 1});
const fr2 = new Intl.NumberFormat('fr-FR', {maximumFractionDigits: 2});

const nb = v => (typeof v === 'number' && isFinite(v))
  ? (Number.isInteger(v) ? fr.format(v) : (Math.abs(v) < 10 ? fr2 : fr1).format(v))
  : String(v ?? '');

const jj = iso => {
  if (!iso) return '';
  const [a, m, j] = String(iso).slice(0, 10).split('-');
  return j ? `${j}/${m}/${a}` : iso;
};

const heureDe = ts => String(ts || '').slice(11, 16);

const echeance = (date, jours) => {
  if (jours === null || jours === undefined) return date ? jj(date) : 'sans date';
  if (jours < 0)  return `périmé ${-jours} j`;
  if (jours === 0) return "aujourd'hui";
  if (jours === 1) return 'demain';
  if (jours < 31) return `dans ${jours} j`;
  if (jours < 365) return `dans ${Math.round(jours / 30)} mois`;
  return `dans ${fr1.format(jours / 365)} ans`;
};

const classeJours = j => j === null || j === undefined ? 'inconnu'
  : j < 0 ? 'perime' : j <= 2 ? 'critique' : j <= 6 ? 'tendu'
  : j <= 20 ? 'correct' : 'large';

/* Empreinte stable d'une chaine : sert a placer un article toujours au meme
   endroit du frigo. Pas de hasard reel, sinon le rangement changerait a
   chaque rafraichissement. */
const graine = texte => {
  let h = 2166136261;
  for (let i = 0; i < texte.length; i++) {
    h ^= texte.charCodeAt(i);
    h = Math.imul(h, 16777619);
  }
  return h >>> 0;
};
const dessuite = etat => () => {           // generateur pseudo aleatoire xorshift
  etat ^= etat << 13; etat >>>= 0;
  etat ^= etat >> 17;
  etat ^= etat << 5;  etat >>>= 0;
  return etat / 4294967296;
};

const COULEURS = {
  a: '#1a9e5c', b: '#7ac943', c: '#ffd23f', d: '#ff9b3d', e: '#ff4d5e',
  '?': 'var(--tres-doux)',
  1: '#3fe08a', 2: '#7ac943', 3: '#ffb347', 4: '#ff4d5e',
};
const PALETTE = ['#2ff0c8', '#8b7cff', '#ff7ad9', '#ffb347', '#49b8ff',
                 '#3fe08a', '#ff4d5e', '#c5d0d6'];
const URGENCES = {
  perime:   {c: '#ff4d5e', l: 'périmé'},
  critique: {c: '#ff4d5e', l: 'sous 72 h'},
  tendu:    {c: '#ffb347', l: 'sous 7 j'},
  correct:  {c: '#49b8ff', l: 'sous 20 j'},
  large:    {c: '#3fe08a', l: 'au-delà'},
  inconnu:  {c: 'var(--tres-doux)', l: 'sans date'},
};

const couleurUrgence = j => URGENCES[classeJours(j)].c;

/* ------------------------------------------------------------------ etat */

const VUES = ['bord', 'frigo', 'stock', 'courses', 'flux'];

const S = {
  etat: null, vue: 'bord', filtre: '', urgence: null,
  tri: 'peremption', sens: 1, code: null, revision: 0, vivant: false,
};

/* --------------------------------------------------------- dessin adaptatif

   Les graphes sont rendus a la taille reelle de leur conteneur plutot que
   dans un viewBox fixe qu'on etirerait : etirer un SVG etire aussi ses
   libelles, et un axe illisible ne sert a rien. On memorise la fonction de
   dessin sur l'element, et on la rejoue au redimensionnement.            */

const ADAPTATIFS = new Map();

function dessiner(el, fn) {
  if (!el) return;
  ADAPTATIFS.set(el, fn);
  const largeur = Math.max(220, Math.round(el.clientWidth || 320));
  el.innerHTML = fn(largeur);
}

let minuteurTaille;
addEventListener('resize', () => {
  clearTimeout(minuteurTaille);
  minuteurTaille = setTimeout(() => {
    ADAPTATIFS.forEach((fn, el) => {
      if (el.isConnected && el.offsetParent !== null) {
        el.innerHTML = fn(Math.max(220, Math.round(el.clientWidth || 320)));
      }
    });
  }, 180);
});

/* ------------------------------------------------------------- graphiques */

/* Anneau. Les segments sont traces sur un cercle unique par decalage de
   pointilles : une seule primitive, aucune trigonometrie a relire. */
function anneau(donnees, {centre = '', sous = '', couleurs = null, largeur = 220} = {}) {
  const total = donnees.reduce((s, d) => s + d.valeur, 0);
  const cote = Math.min(largeur, 240);
  const r = 42, C = 2 * Math.PI * r;
  if (!total) {
    return `<svg viewBox="0 0 120 120" width="${cote}" height="${cote}" role="img">
      <circle cx="60" cy="60" r="${r}" fill="none" stroke="currentColor" class="grille" stroke-opacity=".07" stroke-width="15"/>
      <text x="60" y="63" text-anchor="middle" fill="currentColor" class="axe"
        font-family="var(--mono)" font-size="10">vide</text></svg>`;
  }
  let decalage = 0;
  const arcs = donnees.map((d, i) => {
    const part = d.valeur / total;
    const longueur = Math.max(0, part * C - 1.6);
    const couleur = (couleurs && couleurs[d.cle]) || COULEURS[d.cle] || PALETTE[i % PALETTE.length];
    const arc = `<circle cx="60" cy="60" r="${r}" fill="none" stroke="${couleur}"
      stroke-width="15" stroke-dasharray="${longueur.toFixed(2)} ${(C - longueur).toFixed(2)}"
      stroke-dashoffset="${(-decalage).toFixed(2)}" transform="rotate(-90 60 60)"
      opacity=".92"><title>${esc(d.cle)} : ${d.valeur}</title></circle>`;
    decalage += part * C;
    return arc;
  }).join('');
  // Le disque interieur fait 69 unites de large : passe cinq caracteres, une
  // taille fixe deborderait sur l'anneau.
  const n = String(centre).length;
  const taille = n <= 3 ? 23 : n <= 5 ? 19 : n <= 7 ? 15 : 12;
  return `<svg viewBox="0 0 120 120" width="${cote}" height="${cote}" role="img">
    <circle cx="60" cy="60" r="${r}" fill="none" stroke="currentColor" class="grille" stroke-opacity=".05" stroke-width="15"/>
    ${arcs}
    <text x="60" y="${sous ? 60 : 65}" text-anchor="middle" fill="var(--vif)"
      font-family="ui-monospace,monospace" font-size="${taille}" font-weight="700">${esc(centre)}</text>
    ${sous ? `<text x="60" y="72" text-anchor="middle" fill="var(--doux)"
      font-family="ui-monospace,monospace" font-size="7.5"
      letter-spacing=".9">${esc(sous)}</text>` : ''}
  </svg>`;
}

function legendeAnneau(donnees, couleurs) {
  const total = donnees.reduce((s, d) => s + d.valeur, 0) || 1;
  return `<ul class="legende-donut">${donnees.map((d, i) => {
    const couleur = (couleurs && couleurs[d.cle]) || COULEURS[d.cle] || PALETTE[i % PALETTE.length];
    const nom = String(d.cle).length === 1 ? String(d.cle).toUpperCase() : d.cle;
    return `<li><i class="pip" style="background:${couleur}"></i>${esc(nom)}
      <b class="n">${nb(d.valeur)}</b>
      <i class="pc">${Math.round(d.valeur / total * 100)}%</i></li>`;
  }).join('')}</ul>`;
}

/* Aire : une serie continue, avec grille, extremes annotes et derniere
   valeur mise en evidence. */
function aire(serie, {cle, largeur, hauteur = 150, couleur = '#2ff0c8', format = nb} = {}) {
  const marge = {h: 34, b: 20, t: 12, d: 8};
  const L = largeur - marge.h - marge.d, H = hauteur - marge.t - marge.b;
  if (!serie.length || L < 40) return '';
  const valeurs = serie.map(p => p[cle]);
  const haut = Math.max(1, ...valeurs), bas = Math.min(0, ...valeurs);
  const x = i => marge.h + (serie.length === 1 ? L / 2 : i * L / (serie.length - 1));
  const y = v => marge.t + H - (v - bas) / (haut - bas || 1) * H;
  const points = serie.map((p, i) => `${x(i).toFixed(1)},${y(p[cle]).toFixed(1)}`).join(' ');
  const paliers = 4;
  const grille = Array.from({length: paliers + 1}, (_, k) => {
    const v = bas + (haut - bas) * k / paliers, yy = y(v);
    return `<line x1="${marge.h}" x2="${largeur - marge.d}" y1="${yy.toFixed(1)}" y2="${yy.toFixed(1)}"
       stroke="currentColor" class="grille" stroke-opacity=".05"/>
      <text x="${marge.h - 5}" y="${(yy + 3).toFixed(1)}" text-anchor="end" fill="currentColor" class="axe"
       font-family="ui-monospace,monospace" font-size="8.5">${format(Math.round(v))}</text>`;
  }).join('');
  const etiquettes = [0, Math.floor(serie.length / 2), serie.length - 1]
    .filter((v, i, t) => t.indexOf(v) === i)
    .map(i => `<text x="${x(i).toFixed(1)}" y="${hauteur - 6}" text-anchor="middle" fill="currentColor" class="axe"
      font-family="ui-monospace,monospace" font-size="8.5">${jj(serie[i].jour).slice(0, 5)}</text>`).join('');
  const dernier = serie[serie.length - 1];
  const id = 'd' + Math.random().toString(36).slice(2, 7);
  return `<svg viewBox="0 0 ${largeur} ${hauteur}" width="${largeur}" height="${hauteur}" role="img">
    <defs><linearGradient id="${id}" x1="0" y1="0" x2="0" y2="1">
      <stop offset="0" stop-color="${couleur}" stop-opacity=".34"/>
      <stop offset="1" stop-color="${couleur}" stop-opacity="0"/>
    </linearGradient></defs>
    ${grille}
    <polygon points="${x(0).toFixed(1)},${marge.t + H} ${points} ${x(serie.length - 1).toFixed(1)},${marge.t + H}"
      fill="url(#${id})"/>
    <polyline points="${points}" fill="none" stroke="${couleur}" stroke-width="1.8"
      stroke-linejoin="round" stroke-linecap="round"/>
    <circle cx="${x(serie.length - 1).toFixed(1)}" cy="${y(dernier[cle]).toFixed(1)}" r="3.2"
      fill="${couleur}"/>
    <circle cx="${x(serie.length - 1).toFixed(1)}" cy="${y(dernier[cle]).toFixed(1)}" r="6.5"
      fill="none" stroke="${couleur}" stroke-opacity=".35"/>
    ${etiquettes}
  </svg>`;
}

/* Colonnes symetriques : entrees vers le haut, sorties vers le bas. */
function colonnes(serie, {largeur, hauteur = 150} = {}) {
  const marge = {h: 30, b: 18, t: 10, d: 6};
  const L = largeur - marge.h - marge.d, H = hauteur - marge.t - marge.b;
  if (!serie.length || L < 40) return '';
  const haut = Math.max(1, ...serie.map(p => Math.max(p.ajouts, p.retraits)));
  const pas = L / serie.length, l = Math.max(2, Math.min(pas - 2, 16));
  const zero = marge.t + H / 2;
  const echelle = (H / 2) / haut;
  const barres = serie.map((p, i) => {
    const x = marge.h + i * pas + (pas - l) / 2;
    const ha = p.ajouts * echelle, hr = p.retraits * echelle;
    return (p.ajouts ? `<rect x="${x.toFixed(1)}" y="${(zero - ha).toFixed(1)}" width="${l.toFixed(1)}"
        height="${Math.max(1, ha).toFixed(1)}" rx="1" fill="#3fe08a" opacity=".9">
        <title>${jj(p.jour)} : +${p.ajouts}</title></rect>` : '')
      + (p.retraits ? `<rect x="${x.toFixed(1)}" y="${zero.toFixed(1)}" width="${l.toFixed(1)}"
        height="${Math.max(1, hr).toFixed(1)}" rx="1" fill="#ff7ad9" opacity=".9">
        <title>${jj(p.jour)} : -${p.retraits}</title></rect>` : '');
  }).join('');
  return `<svg viewBox="0 0 ${largeur} ${hauteur}" width="${largeur}" height="${hauteur}" role="img">
    <line x1="${marge.h}" x2="${largeur - marge.d}" y1="${zero}" y2="${zero}"
      stroke="currentColor" class="grille" stroke-opacity=".12"/>
    <text x="${marge.h - 5}" y="${marge.t + 8}" text-anchor="end" fill="#3fe08a"
      font-family="ui-monospace,monospace" font-size="8.5">+${haut}</text>
    <text x="${marge.h - 5}" y="${marge.t + H}" text-anchor="end" fill="#ff7ad9"
      font-family="ui-monospace,monospace" font-size="8.5">-${haut}</text>
    ${barres}
    <text x="${marge.h}" y="${hauteur - 5}" fill="currentColor" class="axe"
      font-family="ui-monospace,monospace" font-size="8.5">${jj(serie[0].jour).slice(0, 5)}</text>
    <text x="${largeur - marge.d}" y="${hauteur - 5}" text-anchor="end" fill="currentColor" class="axe"
      font-family="ui-monospace,monospace" font-size="8.5">aujourd'hui</text>
  </svg>`;
}

/* Histogramme simple, une couleur par barre si besoin. */
function histogramme(valeurs, {largeur, hauteur = 130, etiquettes = null,
                              couleur = '#2ff0c8', teinte = null, titre = null} = {}) {
  const marge = {h: 26, b: 16, t: 10, d: 6};
  const L = largeur - marge.h - marge.d, H = hauteur - marge.t - marge.b;
  if (!valeurs.length || L < 40) return '';
  const haut = Math.max(1, ...valeurs);
  const pas = L / valeurs.length, l = Math.max(2, Math.min(pas - 2, 26));
  const barres = valeurs.map((v, i) => {
    const h = v / haut * H, x = marge.h + i * pas + (pas - l) / 2;
    const c = teinte ? teinte(i, v) : couleur;
    const t = titre ? titre(i, v) : `${i} : ${v}`;
    return `<rect x="${x.toFixed(1)}" y="${(marge.t + H - h).toFixed(1)}" width="${l.toFixed(1)}"
      height="${Math.max(v ? 1.5 : 0, h).toFixed(1)}" rx="1.5" fill="${c}" opacity=".9">
      <title>${esc(t)}</title></rect>`;
  }).join('');
  const libelles = etiquettes ? valeurs.map((v, i) => {
    const texte = etiquettes(i);
    if (!texte) return '';
    return `<text x="${(marge.h + i * pas + pas / 2).toFixed(1)}" y="${hauteur - 4}"
      text-anchor="middle" fill="currentColor" class="axe" font-family="ui-monospace,monospace"
      font-size="8.5">${esc(texte)}</text>`;
  }).join('') : '';
  return `<svg viewBox="0 0 ${largeur} ${hauteur}" width="${largeur}" height="${hauteur}" role="img">
    <line x1="${marge.h}" x2="${largeur - marge.d}" y1="${marge.t + H}" y2="${marge.t + H}"
      stroke="currentColor" class="grille" stroke-opacity=".1"/>
    <text x="${marge.h - 5}" y="${marge.t + 8}" text-anchor="end" fill="currentColor" class="axe"
      font-family="ui-monospace,monospace" font-size="8.5">${haut}</text>
    ${barres}${libelles}
  </svg>`;
}

/* Grille jour x heure. Un carre par creneau, opacite proportionnelle. */
function grilleActivite(grille, {largeur} = {}) {
  const jours = ['lun', 'mar', 'mer', 'jeu', 'ven', 'sam', 'dim'];
  const marge = {h: 28, t: 14, b: 14, d: 4};
  const L = largeur - marge.h - marge.d;
  const cote = Math.max(5, Math.min((L - 23 * 1.5) / 24, 18));
  const pas = cote + 1.5;
  const hauteur = marge.t + 7 * pas + marge.b;
  const haut = Math.max(1, ...grille.flat());
  const cases = grille.map((ligne, j) => ligne.map((v, h) => {
    const o = v ? 0.16 + 0.84 * (v / haut) : 0;
    return `<rect x="${(marge.h + h * pas).toFixed(1)}" y="${(marge.t + j * pas).toFixed(1)}"
      width="${cote.toFixed(1)}" height="${cote.toFixed(1)}" rx="1.5"
      fill="${v ? '#2ff0c8' : 'rgba(255,255,255,.05)'}" opacity="${v ? o.toFixed(2) : 1}">
      ${v ? `<title>${jours[j]} ${h}h : ${v}</title>` : ''}</rect>`;
  }).join('')).join('');
  const libJours = jours.map((nom, j) => `<text x="${marge.h - 5}"
    y="${(marge.t + j * pas + cote * .78).toFixed(1)}" text-anchor="end" fill="currentColor" class="axe"
    font-family="ui-monospace,monospace" font-size="8">${nom}</text>`).join('');
  const libHeures = [0, 6, 12, 18].map(h => `<text x="${(marge.h + h * pas + cote / 2).toFixed(1)}"
    y="${(marge.t - 4).toFixed(1)}" text-anchor="middle" fill="currentColor" class="axe"
    font-family="ui-monospace,monospace" font-size="8">${h}h</text>`).join('');
  return `<svg viewBox="0 0 ${largeur} ${hauteur}" width="${largeur}" height="${hauteur}" role="img">
    ${libHeures}${libJours}${cases}</svg>`;
}

function barresHorizontales(items, {couleur = null} = {}) {
  if (!items || !items.length) return '<p class="vide">rien à classer</p>';
  const haut = Math.max(...items.map(i => i.valeur), 1);
  return items.map((it, i) => `
    <div class="barre-ligne">
      <span class="lib">${esc(it.cle)}</span>
      <span class="n">${nb(it.valeur)}</span>
      <span class="piste"><i style="width:${(it.valeur / haut * 100).toFixed(1)}%${
        couleur ? `;background:${couleur[i % couleur.length]}` : ''}"></i></span>
    </div>`).join('');
}

/* --------------------------------------------------------------- liaison

   La WebSocket porte l'etat complet a la connexion, puis a chaque mouvement.
   Entre deux, un battement toutes les cinq secondes remet l'horloge a l'heure
   et donne le numero de revision : s'il a saute, c'est qu'un message s'est
   perdu, et on va rechercher l'etat en HTTP. La page ne peut donc pas rester
   silencieusement perimee.                                                */

let socket = null, tentatives = 0, minuteurReprise = null;

function brancher() {
  const protocole = location.protocol === 'https:' ? 'wss:' : 'ws:';
  try { socket = new WebSocket(`${protocole}//${location.host}/flux`); }
  catch (e) { return replier(); }

  socket.onopen = () => {
    tentatives = 0;
    liaison(true);
  };
  socket.onmessage = ev => {
    let message;
    try { message = JSON.parse(ev.data); } catch (e) { return; }
    if (message.type === 'pouls') {
      $('#horloge').textContent = message.heure;
      if (message.revision !== S.revision) rattraper();
      return;
    }
    appliquer(message);
  };
  socket.onclose = () => { liaison(false); replier(); };
  socket.onerror = () => { liaison(false); };
}

function replier() {
  clearTimeout(minuteurReprise);
  const attente = Math.min(20000, 800 * Math.pow(1.7, tentatives++));
  minuteurReprise = setTimeout(brancher, attente);
  if (tentatives > 1) rattraper();
}

async function rattraper() {
  try {
    const reponse = await fetch('/api/etat', {cache: 'no-store'});
    appliquer(await reponse.json());
  } catch (e) { /* hors ligne : on reessaiera au prochain battement */ }
}

/* La liaison n'est plus affichee : elle n'apprend rien a qui regarde son
   frigo, et le repli HTTP rattrape tout seul une WebSocket coupee. L'etat
   reste suivi -- le numero de revision decide s'il faut se resynchroniser. */
function liaison(vivant) {
  S.vivant = vivant;
}

/* ------------------------------------------------------------------ rendu */

function appliquer(etat) {
  S.etat = etat;
  S.revision = etat.revision || 0;
  $('#horloge').textContent = etat.horloge.heure;
  rendreTout();
  if (S.code) remplirFiche(S.code);
}

function rendreTout() {
  const e = S.etat;
  if (!e) return;
  rendreBandeau(e);
  rendreKpi(e);
  rendreDonuts(e);
  rendreSeries(e);
  rendreNiveaux(e);
  rendreCartouches('#chiffres', e.chiffres);
  rendreCartouches('#curiosites', e.curiosites);
  rendreBarres(e);
  rendreFrigo();
  rendreStock();
  rendreFlux(e);
  rendreCourses(e);
}

/* --- bandeau ----------------------------------------------------------- */

function rendreBandeau(e) {
  const c = e.compteurs, k = Object.fromEntries(e.kpi.map(x => [x.code, x]));
  const presse = (e.urgents || []).filter(p => p.jours !== null && p.jours <= 2)
    .reduce((s, p) => s + p.total, 0);
  const tuiles = [
    {lib: 'unités en stock', val: nb(c.unites), cl: 'cyan'},
    {lib: 'références', val: nb(c.references), cl: ''},
    {lib: 'lots ouverts', val: nb(c.lots), cl: ''},
    {lib: 'périmé', val: nb(c.perimes), cl: c.perimes ? 'rouge' : ''},
    {lib: 'à manger sous 72 h', val: nb(presse), cl: presse ? 'ambre' : ''},
    {lib: 'énergie', val: nb(Math.round(trouver(e.chiffres, 'Énergie totale'))),
     unite: 'kcal', cl: 'violet'},
    {lib: 'masse', val: nb(trouver(e.chiffres, 'Masse en stock')), unite: 'kg', cl: 'magenta'},
    {lib: 'mouvements', val: nb(c.mouvements), cl: ''},
    {lib: 'fraîcheur', val: nb(k.FRAICHEUR ? k.FRAICHEUR.valeur : 0), unite: '/100', cl: 'cyan'},
  ];
  $('#bandeau').innerHTML = tuiles.map(t => `
    <div class="tuile ${t.cl}">
      <span class="val">${t.val}${t.unite ? `<small>${t.unite}</small>` : ''}</span>
      <span class="lib">${t.lib}</span>
    </div>`).join('');

  const alerte = c.perimes + presse;
  const pastille = $('#pastille-stock');
  pastille.textContent = alerte > 99 ? '99+' : alerte;
  pastille.classList.toggle('on', alerte > 0);
}

const trouver = (liste, titre) => {
  const e = (liste || []).find(x => x.titre === titre);
  return e ? e.valeur : 0;
};

/* --- KPI --------------------------------------------------------------- */

function rendreKpi(e) {
  $('#kpis').innerHTML = e.kpi.map(k => `
    <article class="kpi ${k.etat}">
      <div class="kpi-tete">
        <span class="kpi-rang">KPI ${k.rang}</span>
        <span class="kpi-code">${esc(k.code)}</span>
      </div>
      <div class="kpi-titre">${esc(k.titre)}</div>
      <div class="kpi-val"><b>${nb(k.valeur)}</b><span>${esc(k.unite)}</span></div>
      <div class="kpi-jauge"><i style="width:${Math.max(0, Math.min(100, k.jauge)).toFixed(1)}%"></i></div>
      <div class="kpi-formule"><b>${esc(k.formule)}</b>${esc(k.lecture)}</div>
    </article>`).join('');
}

/* --- anneaux ----------------------------------------------------------- */

function rendreDonuts(e) {
  const d = e.donuts || {};
  const total = liste => (liste || []).reduce((s, x) => s + x.valeur, 0);
  const blocs = [
    {titre: 'Nutri-Score', sous: 'dominant', legende: 'unités notées', donnees: d.nutriscore,
     centre: () => notePrincipale(d.nutriscore)},
    {titre: 'Groupe NOVA', sous: 'dominant', legende: 'transformation', donnees: d.nova,
     centre: () => notePrincipale(d.nova)},
    {titre: 'Eco-Score', sous: 'dominant', legende: 'empreinte', donnees: d.ecoscore,
     centre: () => notePrincipale(d.ecoscore)},
    {titre: 'Échéances', sous: 'unités', legende: 'répartition', donnees: d.urgence,
     couleurs: {'périmé': '#ff4d5e', 'sous 72 h': '#ff4d5e', 'sous 7 j': '#ffb347',
                'sous 15 j': '#49b8ff', 'sous 30 j': '#8b7cff', 'au-delà': '#3fe08a',
                'sans date': 'var(--tres-doux)'},
     centre: () => nb(total(d.urgence))},
    {titre: 'Apport énergétique', sous: 'kcal', legende: 'par macronutriment', donnees: d.macros,
     couleurs: {proteines: '#49b8ff', glucides: '#ffb347', lipides: '#ff7ad9'},
     centre: () => nb(total(d.macros))},
  ];
  $('#donuts').innerHTML = blocs.map((b, i) => `
    <article class="carte">
      <header><h3>${b.titre}</h3><small>${b.legende}</small></header>
      <div class="toile" data-anneau="${i}"></div>
    </article>`).join('');
  blocs.forEach((b, i) => {
    const hote = $(`[data-anneau="${i}"]`);
    const donnees = b.donnees || [];
    dessiner(hote, l => anneau(donnees, {centre: b.centre(), sous: b.sous,
                                         couleurs: b.couleurs, largeur: l})
                        + legendeAnneau(donnees, b.couleurs));
  });
}

/* La note majoritaire, en ignorant les produits que le catalogue n'a pas
   notes : afficher « ? » au centre d'un anneau ne dit rien du frigo. */
const notePrincipale = donnees => {
  const connus = (donnees || []).filter(d => d.cle !== '?' && d.valeur);
  if (!connus.length) return '?';
  return String(connus.reduce((a, b) => b.valeur > a.valeur ? b : a).cle).toUpperCase();
};

/* --- series ------------------------------------------------------------ */

function rendreSeries(e) {
  const s = e.series || {};
  dessiner($('#graphe-charge'), l => aire(s.charge || [], {cle: 'unites', largeur: l,
    hauteur: 160, couleur: '#2ff0c8'}));
  dessiner($('#graphe-mouvements'), l => colonnes(s.mouvements || [], {largeur: l, hauteur: 160}));
  dessiner($('#graphe-horizon'), l => histogramme((s.horizon || []).map(h => h.unites), {
    largeur: l, hauteur: 160,
    teinte: i => couleurUrgence((s.horizon[i] || {}).jours),
    titre: (i, v) => {
      const j = (s.horizon[i] || {}).jours;
      const quand = j < 0 ? 'déjà périmé' : j === 0 ? "aujourd'hui"
        : j >= 45 ? 'dans 45 j et plus' : `dans ${j} j`;
      return `${quand} : ${v} u`;
    },
    etiquettes: i => {
      const j = (s.horizon[i] || {}).jours;
      if (j === -1) return 'passé';
      if (j === 45) return 'J+45 et +';
      return [0, 7, 14, 21, 28, 35].includes(j) ? `J+${j}` : '';
    }}));
  dessiner($('#graphe-heures'), l => histogramme(s.heures || [], {largeur: l, hauteur: 140,
    couleur: '#8b7cff', etiquettes: i => i % 6 === 0 ? i + 'h' : '',
    titre: (i, v) => `${i}h : ${v} mouvements`}));
  const jours = ['lun', 'mar', 'mer', 'jeu', 'ven', 'sam', 'dim'];
  dessiner($('#graphe-semaine'), l => histogramme(s.semaine || [], {largeur: l, hauteur: 140,
    couleur: '#ff7ad9', etiquettes: i => jours[i],
    titre: (i, v) => `${jours[i]} : ${v} mouvements`}));
  dessiner($('#graphe-grille'), l => grilleActivite(s.grille || [], {largeur: l}));
}

/* --- jauges nutritionnelles ------------------------------------------- */

function rendreNiveaux(e) {
  const n = e.niveaux || {};
  const noms = {graisses: 'matières grasses', satures: 'acides gras saturés',
                sucres: 'sucres', sel: 'sel'};
  const rangs = ['low', 'moderate', 'high'];
  $('#graphe-niveaux').innerHTML = Object.keys(noms).map(cle => {
    const d = n[cle] || {moyenne: 0, couvert: 0};
    const plein = Math.round(d.moyenne);
    return `<div class="jauge-n">
      <span style="min-width:110px">${noms[cle]}</span>
      <span class="crans">${rangs.map((r, i) => `<i class="cran ${i < plein ? 'on ' + rangs[plein - 1] : ''}"></i>`).join('')}</span>
      <span>${d.moyenne ? fr1.format(d.moyenne) : '-'}</span>
    </div>`;
  }).join('') + `<p class="note" style="margin:10px 0 0;font:400 11.5px/1.5 var(--mono);color:var(--tres-doux)">
    moyenne des trois crans d'Open Food Facts, pondérée par les unités en stock</p>`;
}

/* --- cartouches -------------------------------------------------------- */

const ISO = /^\d{4}-\d{2}-\d{2}$/;

function rendreCartouches(cible, liste) {
  $(cible).innerHTML = (liste || []).map(c => `
    <div class="cartouche">
      <span class="lib">${esc(c.titre)}</span>
      <span class="val">${ISO.test(c.valeur) ? jj(c.valeur) : nb(c.valeur)}${
        c.unite ? `<span>${esc(c.unite)}</span>` : ''}</span>
      <span class="note">${esc(c.note)}</span>
    </div>`).join('');
}

/* --- classements ------------------------------------------------------- */

function rendreBarres(e) {
  const b = e.barres || {};
  const blocs = [
    {titre: 'Marques', sous: 'unités par enseigne', donnees: b.marques},
    {titre: 'Catégories', sous: "telles qu'Open Food Facts les range", donnees: b.categories},
    {titre: 'Additifs', sous: 'numéros E, unités concernées', donnees: b.additifs},
    {titre: 'Allergènes', sous: 'déclarés sur les fiches', donnees: b.allergenes},
    {titre: 'Labels', sous: 'mentions traduites', donnees: b.labels},
    {titre: 'Origines', sous: 'pays ou provenance', donnees: b.origines},
  ];
  $('#barres').innerHTML = blocs.map(bl => `
    <article class="carte">
      <header><h3>${bl.titre}</h3><small>${bl.sous}</small></header>
      ${barresHorizontales(bl.donnees)}
    </article>`).join('');
}

/* ----------------------------------------------------------------- frigo

   Un frigo qui n'existe pas, range selon une regle simple : la position d'un
   article derive de son code barres. Elle ne bouge donc pas d'une visite a
   l'autre, et deux articles voisins a l'ecran n'ont aucune raison de l'etre
   dans la vraie vie. Les bouteilles vont dans la porte, le reste sur les
   etageres, comme tout le monde.                                          */

const CADRE = {L: 460, H: 636};
const ZONES_CORPS = [
  {x: 34, y: 36,  w: 254, h: 98,  rangee: 'étagère haute'},
  {x: 34, y: 142, w: 254, h: 98,  rangee: 'étagère 2'},
  {x: 34, y: 248, w: 254, h: 98,  rangee: 'étagère 3'},
  {x: 34, y: 354, w: 254, h: 98,  rangee: 'étagère basse'},
  {x: 40, y: 470, w: 242, h: 100, rangee: 'bac à légumes'},
];
const ZONES_PORTE = [
  {x: 334, y: 62,  w: 104, h: 84, rangee: 'porte, balconnet haut'},
  {x: 334, y: 168, w: 104, h: 84, rangee: 'porte, balconnet 2'},
  {x: 334, y: 274, w: 104, h: 84, rangee: 'porte, balconnet 3'},
  {x: 334, y: 380, w: 104, h: 84, rangee: 'porte, balconnet 4'},
  {x: 334, y: 486, w: 104, h: 84, rangee: 'porte, balconnet bas'},
];

function forme(poste) {
  const m = poste.masse_unitaire || 0;
  if (poste.liquide && m >= 500) return 'bouteille';
  if (poste.liquide) return 'brique';
  if (m && m < 180) return 'pot';
  return 'boite';
}

function gabarit(poste, hauteurZone, echelle) {
  const m = Math.max(60, Math.min(poste.masse_unitaire || 320, 3000));
  const t = Math.sqrt(m / 2200);
  const f = forme(poste);
  let l = (20 + t * 26) * echelle;
  let h = (32 + t * 44) * echelle;
  if (f === 'bouteille') { l *= .74; h *= 1.28; }
  if (f === 'pot') { h *= .72; }
  return {l: Math.max(13, l), h: Math.max(22, Math.min(h, hauteurZone - 4)), f};
}

function silhouette(l, h, f) {
  const r = Math.min(3.5, l / 6);
  if (f === 'bouteille') {
    const cl = l * .36, cx = (l - cl) / 2;
    return `M0 0 L0 ${-h * .58} Q0 ${-h * .71} ${l * .26} ${-h * .76}
            L${cx} ${-h * .82} L${cx} ${-h + 2} Q${cx} ${-h} ${cx + 2} ${-h}
            L${cx + cl - 2} ${-h} Q${cx + cl} ${-h} ${cx + cl} ${-h + 2}
            L${cx + cl} ${-h * .82} L${l - l * .26} ${-h * .76}
            Q${l} ${-h * .71} ${l} ${-h * .58} L${l} 0 Z`.replace(/\s+/g, ' ');
  }
  if (f === 'brique') {
    return `M0 0 L0 ${-h * .84} L${l / 2} ${-h} L${l} ${-h * .84} L${l} 0 Z`;
  }
  if (f === 'pot') {
    return `M${l * .08} 0 L0 ${-h} L${l} ${-h} L${l * .92} 0 Z`;
  }
  return `M${r} 0 L${r} 0 L0 0 L0 ${-h + r} Q0 ${-h} ${r} ${-h}
          L${l - r} ${-h} Q${l} ${-h} ${l} ${-h + r} L${l} 0 Z`.replace(/\s+/g, ' ');
}

function placer(postes) {
  const liquides = postes.filter(p => p.liquide);
  const solides = postes.filter(p => !p.liquide);
  const places = [];
  const debordent = remplir(liquides, ZONES_PORTE, places);
  remplir(solides.concat(debordent), ZONES_CORPS, places, true);
  return places;
}

/* Repartition en tourniquet : on sert les zones a tour de role plutot que de
   bourrer la premiere etagere en laissant le bac vide. Chaque rangee est
   ensuite centree, comme des articles poses a la main. */
const JEU = 6;

function remplir(articles, zones, places, forcer = false) {
  if (!articles.length) return [];
  const capacite = zones.reduce((s, z) => s + z.w, 0);
  const besoin = articles.reduce((s, p) => s + gabarit(p, 90, 1).l + JEU, 0);
  const echelle = Math.max(.4, Math.min(1, capacite / Math.max(besoin, 1)));

  const paquets = zones.map(() => ({items: [], large: 0}));
  const reste = [];
  let depart = 0;
  for (const poste of articles) {
    let pose = false;
    for (let essai = 0; essai < zones.length; essai++) {
      const i = (depart + essai) % zones.length;
      const g = gabarit(poste, zones[i].h, echelle);
      const ajout = g.l + (paquets[i].items.length ? JEU : 0);
      if (paquets[i].large + ajout <= zones[i].w - 6) {
        paquets[i].items.push({poste, g});
        paquets[i].large += ajout;
        depart = (i + 1) % zones.length;
        pose = true;
        break;
      }
    }
    if (pose) continue;
    if (!forcer) { reste.push(poste); continue; }
    // Plus rien ne rentre : on tasse dans la zone la moins chargee. Un
    // chevauchement se voit, un article disparu, non.
    const i = paquets.reduce((a, p, k) => p.large < paquets[a].large ? k : a, 0);
    const g = gabarit(poste, zones[i].h, echelle);
    paquets[i].items.push({poste, g});
    paquets[i].large += g.l + JEU;
  }

  paquets.forEach((paquet, i) => {
    const zone = zones[i];
    let x = zone.x + Math.max(3, (zone.w - paquet.large) / 2);
    for (const {poste, g} of paquet.items) {
      const alea = dessuite(graine(poste.code) || 7);
      places.push({
        poste, zone, x, y: zone.y + zone.h, l: g.l, h: g.h, f: g.f,
        inclinaison: (alea() - .5) * 3.2,
      });
      x += g.l + JEU;
    }
  });
  return reste;
}

function rendreFrigo() {
  const e = S.etat;
  if (!e) return;
  const postes = e.postes || [];
  const places = placer(postes.slice().sort((a, b) => graine(a.code) - graine(b.code)));
  const filtre = S.filtre;

  const articles = places.map(p => {
    const q = p.poste;
    const teinte = couleurUrgence(q.jours);
    const flou = filtre && !q.recherche.includes(filtre);
    const id = 'ph' + q.code;
    const pl = p.l * .74, ph = Math.min(p.h * .46, pl);
    const px = (p.l - pl) / 2, py = -p.h * .42 - ph / 2;
    const note = q.nutriscore;
    return `<g class="article-frigo ${classeJours(q.jours)}${flou ? ' terne' : ''}"
        transform="translate(${p.x.toFixed(1)} ${p.y.toFixed(1)}) rotate(${p.inclinaison.toFixed(2)})"
        data-code="${esc(q.code)}" tabindex="0" role="button"
        aria-label="${esc(q.nom || q.code)}">
      <g class="corps">
        <path class="contour" d="${silhouette(p.l, p.h, p.f)}"
          fill="var(--panneau-plein)" fill-opacity=".92" stroke="${teinte}" stroke-width="1.2" stroke-opacity=".8"/>
        <clipPath id="${id}"><rect x="${px.toFixed(1)}" y="${py.toFixed(1)}"
          width="${pl.toFixed(1)}" height="${ph.toFixed(1)}" rx="2"/></clipPath>
        <image href="/photo/${esc(q.code)}?c=64" x="${px.toFixed(1)}" y="${py.toFixed(1)}"
          width="${pl.toFixed(1)}" height="${ph.toFixed(1)}" clip-path="url(#${id})"
          preserveAspectRatio="xMidYMid slice" opacity=".92"/>
        <rect x="${px.toFixed(1)}" y="${py.toFixed(1)}" width="${pl.toFixed(1)}"
          height="${ph.toFixed(1)}" rx="2" fill="none" stroke="${teinte}"
          stroke-opacity=".35" stroke-width=".7"/>
        ${note ? `<rect x="1.5" y="-8" width="8" height="7" rx="1.5" fill="${COULEURS[note]}"/>
          <text x="5.5" y="-2.4" text-anchor="middle" font-family="ui-monospace,monospace"
            font-size="5.6" font-weight="700" fill="var(--fond)">${note.toUpperCase()}</text>` : ''}
        ${q.total > 1 ? `<circle cx="${(p.l - 4).toFixed(1)}" cy="${(-p.h + 4).toFixed(1)}" r="6"
            fill="var(--fond)" stroke="${teinte}" stroke-width=".9"/>
          <text x="${(p.l - 4).toFixed(1)}" y="${(-p.h + 6.2).toFixed(1)}" text-anchor="middle"
            font-family="ui-monospace,monospace" font-size="6.6" font-weight="700"
            fill="${teinte}">${q.total}</text>` : ''}
      </g>
    </g>`;
  }).join('');

  const etageres = ZONES_CORPS.slice(0, 4).map(z => `
    <rect x="${z.x - 3}" y="${z.y + z.h}" width="${z.w + 6}" height="3.5" rx="1.8"
      fill="rgba(47,240,200,.16)"/>
    <rect x="${z.x - 3}" y="${z.y + z.h}" width="${z.w + 6}" height="1.2" rx=".6"
      fill="var(--vif)" fill-opacity=".28"/>`).join('');
  const balconnets = ZONES_PORTE.map(z => `
    <rect x="${z.x - 4}" y="${z.y + z.h}" width="${z.w + 8}" height="9" rx="3"
      fill="rgba(47,240,200,.09)" stroke="var(--cyan)" stroke-opacity=".2" stroke-width=".8"/>`).join('');

  dessiner($('#frigo'), () => `
  <svg viewBox="0 0 ${CADRE.L} ${CADRE.H}" role="img" aria-label="Frigo">
    <defs>
      <linearGradient id="caisson" x1="0" y1="0" x2="1" y2="1">
        <stop offset="0" stop-color="#0d1726"/><stop offset="1" stop-color="#060b13"/>
      </linearGradient>
      <linearGradient id="froid" x1="0" y1="0" x2="0" y2="1">
        <stop offset="0" stop-color="rgba(47,240,200,.13)"/>
        <stop offset="1" stop-color="rgba(73,184,255,.04)"/>
      </linearGradient>
      <linearGradient id="porte" x1="0" y1="0" x2="1" y2="0">
        <stop offset="0" stop-color="#0b1522"/><stop offset="1" stop-color="#0f1c2d"/>
      </linearGradient>
    </defs>

    <rect x="8" y="6" width="304" height="608" rx="22" fill="url(#caisson)"
      stroke="var(--cyan)" stroke-opacity=".26" stroke-width="1.4"/>
    <rect x="22" y="20" width="276" height="580" rx="14" fill="url(#froid)"
      stroke="var(--cyan)" stroke-opacity=".14"/>
    ${etageres}
    <rect x="30" y="462" width="262" height="142" rx="9" fill="var(--fond-2)" fill-opacity=".5"
      stroke="var(--cyan)" stroke-opacity=".16"/>
    <rect x="96" y="592" width="130" height="4" rx="2" fill="rgba(47,240,200,.3)"/>

    <rect x="322" y="18" width="128" height="584" rx="16" fill="url(#porte)"
      stroke="var(--cyan)" stroke-opacity=".22" stroke-width="1.2"/>
    <rect x="330" y="28" width="112" height="564" rx="10" fill="var(--fond-2)" fill-opacity=".4"
      stroke="var(--cyan)" stroke-opacity=".1"/>
    ${balconnets}
    <rect x="436" y="200" width="7" height="220" rx="3.5" fill="rgba(47,240,200,.22)"/>

    <g class="articles">${articles}</g>

    <text x="20" y="630" font-family="ui-monospace,monospace" font-size="7.5"
      fill="var(--doux)" fill-opacity=".7" letter-spacing="1.6">RANGEMENT DÉRIVÉ DU CODE BARRES</text>
  </svg>`);

  const occupe = places.length;
  const capacite = ZONES_CORPS.length * 6 + ZONES_PORTE.length * 3;
  $('#legende-frigo').innerHTML = Object.entries(URGENCES).map(([cle, u]) =>
    `<li><i class="pip" style="background:${u.c}"></i>${u.l}</li>`).join('');
  $('#frigo-compteurs').innerHTML = `
    <div><b>${occupe}</b><span>articles dessinés</span></div>
    <div><b>${e.compteurs.unites}</b><span>unités réelles</span></div>
    <div><b>${Math.min(999, Math.round(occupe / capacite * 100))}%</b><span>remplissage</span></div>
    <div><b>${ZONES_CORPS.length + ZONES_PORTE.length}</b><span>emplacements</span></div>`;
}

/* ----------------------------------------------------------------- stock */

const TRIS = {
  peremption: p => p.jours === null ? 99999 : p.jours,
  nom: p => (p.nom || p.code).toLowerCase(),
  quantite: p => -p.total,
  kcal: p => -(p.nutrition.kcal || 0),
  nutriscore: p => p.nutriscore || 'z',
  nova: p => -(p.nova || 0),
  masse: p => -((p.masse_unitaire || 0) * p.total),
  recent: p => (p.dernier_ajout ? -new Date(p.dernier_ajout.replace(' ', 'T')).getTime() : 0),
};

function filtrer() {
  const e = S.etat;
  if (!e) return [];
  let liste = e.postes || [];
  if (S.filtre) liste = liste.filter(p => p.recherche.includes(S.filtre));
  if (S.urgence) liste = liste.filter(p => classeJours(p.jours) === S.urgence);
  const cle = TRIS[S.tri] || TRIS.peremption;
  return liste.slice().sort((a, b) => {
    const va = cle(a), vb = cle(b);
    return (va < vb ? -1 : va > vb ? 1 : 0) * S.sens;
  });
}

function surligner(texte) {
  const propre = esc(texte);
  if (!S.filtre) return propre;
  const motif = S.filtre.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  return propre.replace(new RegExp(`(${motif})`, 'gi'), '<mark>$1</mark>');
}

function rendreStock() {
  const e = S.etat;
  if (!e) return;
  const tout = e.postes || [];
  const compte = {};
  tout.forEach(p => { const c = classeJours(p.jours); compte[c] = (compte[c] || 0) + 1; });
  $('#puces-urgence').innerHTML =
    `<button class="puce ${S.urgence ? '' : 'on'}" data-urgence="">tout<b>${tout.length}</b></button>`
    + Object.entries(URGENCES).filter(([c]) => compte[c]).map(([c, u]) =>
      `<button class="puce ${S.urgence === c ? 'on' : ''}" data-urgence="${c}"
        style="${S.urgence === c ? `border-color:${u.c};color:${u.c};background:${u.c}22` : ''}"
        >${u.l}<b>${compte[c]}</b></button>`).join('');

  const liste = filtrer();
  $('#compte-stock').textContent = `${liste.length} / ${tout.length} références`;
  $('#stock').innerHTML = liste.length ? liste.map(p => `
    <button class="article ${classeJours(p.jours)}" data-code="${esc(p.code)}">
      <img src="/photo/${esc(p.code)}?c=96" alt="" loading="lazy" width="58" height="58">
      <span class="corps">
        <span class="nom">${surligner(p.nom || p.code)}</span>
        <span class="meta">${surligner([p.marque, p.contenance].filter(Boolean).join(' · ') || p.code)}</span>
        <span class="bas">
          <span class="echeance ${classeJours(p.jours)}">${echeance(p.peremption, p.jours)}</span>
          ${p.nutriscore ? `<i class="note-score" style="background:${COULEURS[p.nutriscore]}"
            title="Nutri-Score">${p.nutriscore.toUpperCase()}</i>` : ''}
          ${p.nova ? `<i class="nova" data-n="${p.nova}" title="groupe NOVA">N${p.nova}</i>` : ''}
          ${p.nb_lots > 1 ? `<i class="nova" title="lots distincts">${p.nb_lots} lots</i>` : ''}
        </span>
      </span>
      <span class="qte">${p.total}</span>
    </button>`).join('')
    : `<p class="vide">aucune référence ne correspond${S.filtre ? ` à « ${esc(S.filtre)} »` : ''}</p>`;
}

/* ------------------------------------------------------------------ flux */

function rendreFlux(e) {
  const journal = e.journal || [];
  $('#journal').innerHTML = journal.length ? journal.map(m => `
    <li>
      <span class="ts">${jj(m.ts)} ${heureDe(m.ts)}</span>
      <span class="delta ${m.delta > 0 ? 'plus' : m.delta < 0 ? 'moins' : 'zero'}">${
        m.delta > 0 ? '+' : ''}${m.delta}</span>
      <span class="nom" data-code="${esc(m.code)}">${esc(m.nom)}</span>
      <span class="origine">${esc(m.origine || m.action)}</span>
    </li>`).join('') : '<li class="vide">journal vide</li>';

  const urgents = e.urgents || [];
  $('#urgents').innerHTML = urgents.length ? urgents.map(p => `
    <li data-code="${esc(p.code)}">
      <i class="note-score" style="background:${couleurUrgence(p.jours)};width:9px;height:24px;border-radius:2px"></i>
      <span class="u-nom">${esc(p.nom || p.code)}</span>
      <span class="echeance ${classeJours(p.jours)}">${echeance(p.peremption, p.jours)}</span>
      <span class="qte" style="position:static">${p.total}</span>
    </li>`).join('') : '<li class="vide">rien qui presse</li>';
}

/* --------------------------------------------------------------- courses

   La liste arrive dans l'instantane, comme le reste : le terminal inscrit un
   article, la veille le voit et le pousse, la coche apparait ici sans que
   personne n'ait rafraichi. Les ecritures, elles, repartent en POST vers le
   serveur du terminal -- ce processus-ci ne sait pas ecrire.               */

function rendreCourses(e) {
  const articles = e.courses || [];
  const aPrendre = articles.filter(a => !a.pris);
  const panier = articles.filter(a => a.pris);

  $('#courses-a-prendre').innerHTML = aPrendre.length
    ? aPrendre.map(ligneCourse).join('')
    : '<li class="vide">liste vide — bippez un produit sur le terminal, ou tapez-le ci-dessus</li>';
  $('#courses-panier').innerHTML = panier.length
    ? panier.map(ligneCourse).join('')
    : '<li class="vide">rien au panier</li>';

  const unites = aPrendre.reduce((s, a) => s + a.qte, 0);
  $('#course-compte').textContent = aPrendre.length
    ? `${aPrendre.length} ligne${aPrendre.length > 1 ? 's' : ''}, ${unites} article${unites > 1 ? 's' : ''}`
    : '—';

  const pastille = $('#pastille-courses');
  pastille.textContent = unites > 99 ? '99+' : unites;
  pastille.classList.toggle('on', unites > 0);

  rendreSuggestions(e, articles);
}

function ligneCourse(a) {
  const vignette = a.photo
    ? `<img class="course-photo" src="/photo/${encodeURIComponent(a.code)}?t=64" alt="" loading="lazy">`
    : '<span class="course-photo creuse" aria-hidden="true"></span>';
  const detail = [a.marque, a.contenance].filter(Boolean).join(' · ');
  return `<li class="course ${a.pris ? 'prise' : ''}" data-course="${a.id}">
    <button class="coche" data-action="cocher" data-pris="${a.pris ? '0' : '1'}"
            title="${a.pris ? 'remettre à prendre' : 'mettre au panier'}"
            aria-label="${a.pris ? 'remettre à prendre' : 'mettre au panier'}">${a.pris ? '✓' : ''}</button>
    ${vignette}
    <span class="course-texte">
      <b>${esc(a.libelle)}</b>
      ${detail ? `<small>${esc(detail)}</small>` : ''}
    </span>
    ${a.qte > 1 ? `<span class="course-qte">×${a.qte}</span>` : ''}
    <button class="retirer" data-action="retirer" title="retirer de la liste"
            aria-label="retirer de la liste">✕</button>
  </li>`;
}

/* Ce qui est peu fourni ou bientot perime, et qui n'est pas deja sur la liste.
   Une suggestion, pas une deduction : c'est l'humain qui decide de racheter. */
function rendreSuggestions(e, articles) {
  const deja = new Set(articles.map(a => a.code).filter(Boolean));
  const candidats = (e.postes || [])
    .filter(p => !deja.has(p.code))
    .filter(p => (p.jours !== null && p.jours <= 6) || p.total <= 1)
    .sort((a, b) => (a.jours ?? 999) - (b.jours ?? 999))
    .slice(0, 12);

  $('#suggestions-courses').innerHTML = candidats.length
    ? candidats.map(p => `<button class="puce" data-suggestion="${esc(p.code)}">
        <i style="background:${couleurUrgence(p.jours)}"></i>
        ${esc(p.nom || p.code)}
        <small>${p.jours !== null && p.jours <= 6 ? echeance(p.peremption, p.jours)
                                                  : 'dernière unité'}</small>
      </button>`).join('')
    : '<p class="note">rien à suggérer : le stock est fourni et rien ne presse.</p>';
}

async function agirCourses(action, parametres = {}) {
  const corps = new URLSearchParams(parametres);
  let reponse, charge;
  try {
    reponse = await fetch(`/api/courses/${action}`, {method: 'POST', body: corps});
    charge = await reponse.json();
  } catch (e) {
    return relaisMuet('le site ne joint plus le serveur.');
  }
  if (!reponse.ok || !charge.ok) {
    return relaisMuet(charge.erreur || 'action refusée par le serveur.');
  }
  relaisMuet(null);
  await rattraper();                      // au cas ou la diffusion se perde
}

function relaisMuet(message) {
  const el = $('#course-relais');
  el.hidden = !message;
  el.textContent = message || '';
}

/* ------------------------------------------------------------ fiche article */

const NUTRIMENTS = [
  ['kcal', 'Énergie', 'kcal'], ['kj', 'Énergie', 'kJ'],
  ['lipides', 'Matières grasses', 'g'], ['satures', '  dont saturés', 'g'],
  ['glucides', 'Glucides', 'g'], ['sucres', '  dont sucres', 'g'],
  ['fibres', 'Fibres', 'g'], ['proteines', 'Protéines', 'g'], ['sel', 'Sel', 'g'],
];
const NOMS_NIVEAUX = {graisses: 'matières grasses', satures: 'acides gras saturés',
                      sucres: 'sucres', sel: 'sel'};

async function ouvrirFiche(code) {
  if (!code) return;
  S.code = code;
  $('#voile').hidden = false;
  $('#tiroir').hidden = false;
  document.body.style.overflow = 'hidden';
  $('#tiroir').scrollTop = 0;
  $('#fiche').innerHTML = '<p class="vide">lecture...</p>';
  remplirFiche(code);
}

function fermerFiche() {
  S.code = null;
  $('#voile').hidden = true;
  $('#tiroir').hidden = true;
  document.body.style.overflow = '';
}

async function remplirFiche(code) {
  let a;
  try {
    const r = await fetch('/api/article/' + encodeURIComponent(code), {cache: 'no-store'});
    if (!r.ok) throw new Error('404');
    a = await r.json();
  } catch (e) {
    $('#fiche').innerHTML = '<p class="vide">fiche introuvable</p>';
    return;
  }
  if (S.code !== code) return;

  const masse = a.masse_unitaire || 0;
  const n = a.nutrition || {};
  const lignes = NUTRIMENTS.filter(([cle]) => n[cle] !== null && n[cle] !== undefined)
    .map(([cle, nom, unite]) => {
      const cent = n[cle];
      const unitaire = masse ? cent * masse / 100 : null;
      const stock = masse ? cent * masse * a.total / 100 : null;
      return `<tr><th>${esc(nom)}</th>
        <td>${nb(cent)} ${unite}</td>
        <td>${unitaire === null ? '-' : nb(Math.round(unitaire * 10) / 10) + ' ' + unite}</td>
        <td class="total">${stock === null ? '-' : nb(Math.round(stock * 10) / 10) + ' ' + unite}</td></tr>`;
    }).join('');

  const scores = [
    a.nutriscore ? {l: a.nutriscore.toUpperCase(), c: COULEURS[a.nutriscore], t: 'Nutri-Score'} : null,
    a.ecoscore ? {l: a.ecoscore.toUpperCase(), c: COULEURS[a.ecoscore], t: 'Eco-Score'} : null,
    a.nova ? {l: 'N' + a.nova, c: COULEURS[a.nova], t: 'groupe NOVA'} : null,
  ].filter(Boolean);

  const jauges = Object.entries(NOMS_NIVEAUX).map(([cle, nom]) => {
    const v = (a.niveaux || {})[cle];
    if (!v) return '';
    const plein = {low: 1, moderate: 2, high: 3}[v] || 0;
    return `<div class="jauge-n"><span style="min-width:120px">${nom}</span>
      <span class="crans">${[0, 1, 2].map(i =>
        `<i class="cran ${i < plein ? 'on ' + v : ''}"></i>`).join('')}</span>
      <span>${v === 'low' ? 'bas' : v === 'moderate' ? 'modéré' : 'élevé'}</span></div>`;
  }).join('');

  // « fr:Bondues » est une cle de taxonomie, pas un mot : on enleve le prefixe
  // de langue que le catalogue laisse passer dans les champs libres.
  const etiquettes = (texte, classe = '') => (texte || '').split(',')
    .map(t => t.trim().replace(/^[a-z]{2}:/, '')).filter(Boolean)
    .map(t => `<span class="etiquette ${classe}">${esc(t)}</span>`).join('');

  const chiffres = [
    ['unités en stock', a.total],
    ['lots', a.nb_lots],
    masse ? ['masse unitaire', `${nb(masse)} g`] : null,
    masse ? ['masse en stock', `${nb(Math.round(masse * a.total) / 1000)} kg`] : null,
    n.kcal && masse ? ['énergie en stock', `${nb(Math.round(n.kcal * masse * a.total / 100))} kcal`] : null,
    n.kcal && masse ? ['autonomie', `${fr1.format(n.kcal * masse * a.total / 100 / 2000)} j`] : null,
    a.mouvements.length ? ['mouvements', a.mouvements.length] : null,
    a.premier_ajout ? ['première mise au frigo', jj(a.premier_ajout)] : null,
    ['source de la fiche', a.source],
    a.maj ? ['fiche relevée le', jj(a.maj)] : null,
  ].filter(Boolean);

  $('#fiche').innerHTML = `
    <div class="fiche-tete">
      <img src="/photo/${esc(a.code)}?c=192" alt="" width="96" height="96">
      <div>
        <h2>${esc(a.nom || 'Produit sans nom')}</h2>
        <div class="marque-p">${esc([a.marque, a.contenance].filter(Boolean).join(' · ') || '—')}</div>
        <div class="code">${esc(a.code)}</div>
        <div class="fiche-scores">
          ${scores.map(s => `<i class="gros-score" style="background:${s.c}" title="${s.t}">${s.l}</i>`).join('')}
          <span class="echeance ${classeJours(a.jours)}"
            style="align-self:center">${echeance(a.peremption, a.jours)}</span>
        </div>
      </div>
    </div>

    ${a.lots.length ? `<div class="bloc"><h4>Lots</h4>
      <ul class="lots">${a.lots.map(l => `
        <li class="${classeJours(l.jours)}">
          <span>${l.peremption ? jj(l.peremption) : 'sans date'}</span>
          <span style="color:var(--doux);font:400 10.5px/1 ui-monospace,monospace">${
            echeance(l.peremption, l.jours)}${l.precision === 'mois' ? ' · mois seul' : ''}</span>
          <span class="q">x${l.qte}</span>
        </li>`).join('')}</ul></div>` : ''}

    ${lignes ? `<div class="bloc"><h4>Nutrition</h4>
      <table class="tableau">
        <thead><tr><th></th><th>100 g</th><th>unité</th><th>stock</th></tr></thead>
        <tbody>${lignes}</tbody>
      </table></div>` : ''}

    ${jauges ? `<div class="bloc"><h4>Jauges</h4>${jauges}</div>` : ''}

    ${a.allergenes || a.traces ? `<div class="bloc"><h4>Allergènes</h4>
      <div class="etiquettes">${etiquettes(a.allergenes, 'chaud')}
        ${etiquettes(a.traces)}</div>
      ${a.traces ? '<p class="texte-long" style="margin-top:7px;font-size:11px">traces sans encadré</p>' : ''}
      </div>` : ''}

    ${a.additifs ? `<div class="bloc"><h4>Additifs</h4>
      <div class="etiquettes">${etiquettes(a.additifs)}</div></div>` : ''}

    ${a.labels ? `<div class="bloc"><h4>Labels</h4>
      <div class="etiquettes">${etiquettes(a.labels, 'bien')}</div></div>` : ''}

    ${a.categories || a.origine ? `<div class="bloc"><h4>Classement</h4>
      <div class="etiquettes">${etiquettes(a.categories)}${etiquettes(a.origine)}</div></div>` : ''}

    ${a.ingredients ? `<div class="bloc"><h4>Ingrédients</h4>
      <p class="texte-long">${esc(a.ingredients)}</p></div>` : ''}

    <div class="bloc"><h4>Chiffres</h4>
      <dl class="paires">${chiffres.map(([k, v]) =>
        `<dt>${esc(k)}</dt><dd>${esc(v)}</dd>`).join('')}</dl></div>

    ${a.alias.length ? `<div class="bloc"><h4>Lectures reconstituées</h4>
      <div class="etiquettes">${a.alias.map(al =>
        `<span class="etiquette">${esc(al.code_lu)} → ${esc(a.code)}</span>`).join('')}</div>
      <p class="texte-long" style="margin-top:8px;font-size:11px">code lu amputé de sa clé de contrôle,
        rétabli par le serveur</p></div>` : ''}

    ${a.mouvements.length ? `<div class="bloc"><h4>Historique</h4>
      <ol class="journal" style="max-height:260px">${a.mouvements.map(m => `
        <li><span class="ts">${jj(m.ts)} ${heureDe(m.ts)}</span>
          <span class="delta ${m.delta > 0 ? 'plus' : m.delta < 0 ? 'moins' : 'zero'}">${
            m.delta > 0 ? '+' : ''}${m.delta}</span>
          <span class="nom">${esc(m.action)}</span>
          <span class="origine">${esc(m.origine)}</span></li>`).join('')}</ol></div>` : ''}
  `;
}

/* ------------------------------------------------------------------ bulle */

let bulleVisible = false;
function bulle(evenement, poste) {
  const el = $('#bulle');
  if (!poste) { el.hidden = true; bulleVisible = false; return; }
  el.innerHTML = `<b>${esc(poste.nom || poste.code)}</b>
    <span>${esc([poste.marque, poste.contenance].filter(Boolean).join(' · '))}</span><br>
    <span>${echeance(poste.peremption, poste.jours)} · x${poste.total}</span>`;
  el.hidden = false;
  bulleVisible = true;
  const marge = 14;
  const r = el.getBoundingClientRect();
  let x = evenement.clientX + marge, y = evenement.clientY + marge;
  if (x + r.width > innerWidth - 8) x = evenement.clientX - r.width - marge;
  if (y + r.height > innerHeight - 8) y = evenement.clientY - r.height - marge;
  el.style.left = Math.max(8, x) + 'px';
  el.style.top = Math.max(8, y) + 'px';
}

/* ---------------------------------------------------------------- theme

   Sombre par defaut -- la page est un afficheur. Le choix explicite est
   retenu ; sans choix, on suit le systeme. La classe est posee sur <html>
   par un bout de script inline dans l'en-tete, avant le premier pixel :
   ce fichier-ci est charge en `defer` et arriverait trop tard.           */

function poserTheme(nom) {
  document.documentElement.dataset.theme = nom;
  try { localStorage.setItem('frigo-theme', nom); } catch (e) {}
  const meta = document.querySelector('meta[name="theme-color"]:not([media])')
            || document.head.appendChild(
                 Object.assign(document.createElement('meta'), {name: 'theme-color'}));
  meta.content = nom === 'clair' ? '#f4f6f8' : '#04070d';
  // Les graphes portent leurs couleurs en dur dans le SVG : il faut les
  // redessiner pour qu'ils suivent le theme.
  ADAPTATIFS.forEach((fn, el) => {
    if (el.isConnected && el.clientWidth > 40) el.innerHTML = fn(Math.round(el.clientWidth));
  });
}

/* ------------------------------------------------------------ interactions */

function vue(nom) {
  S.vue = nom;
  $$('.onglet').forEach(b => b.classList.toggle('actif', b.dataset.vue === nom));
  $$('.vue').forEach(v => v.classList.toggle('active', v.id === 'vue-' + nom));
  if (location.hash.slice(1) !== nom) history.replaceState(null, '', '#' + nom);
  // les graphes caches ont ete dessines a une largeur nulle : on les rejoue.
  requestAnimationFrame(() => ADAPTATIFS.forEach((fn, el) => {
    if (el.isConnected && el.clientWidth > 40) el.innerHTML = fn(Math.round(el.clientWidth));
  }));
}

let minuteurRecherche;
function brancherEvenements() {
  $('#onglets').addEventListener('click', ev => {
    const bouton = ev.target.closest('.onglet');
    if (bouton) vue(bouton.dataset.vue);
  });

  $('#q').addEventListener('input', ev => {
    clearTimeout(minuteurRecherche);
    minuteurRecherche = setTimeout(() => {
      S.filtre = ev.target.value.trim().toLowerCase();
      rendreStock();
      rendreFrigo();
      if (S.filtre && S.vue !== 'stock' && S.vue !== 'frigo') vue('stock');
    }, 140);
  });

  $('#tri').addEventListener('change', ev => { S.tri = ev.target.value; rendreStock(); });
  $('#sens').addEventListener('click', ev => {
    S.sens = -S.sens;
    ev.currentTarget.textContent = S.sens === 1 ? '↑' : '↓';
    rendreStock();
  });

  $('#puces-urgence').addEventListener('click', ev => {
    const puce = ev.target.closest('.puce');
    if (!puce) return;
    S.urgence = puce.dataset.urgence || null;
    rendreStock();
  });

  document.addEventListener('click', ev => {
    const porteur = ev.target.closest('[data-code]');
    if (porteur && porteur.dataset.code) ouvrirFiche(porteur.dataset.code);
  });

  document.addEventListener('keydown', ev => {
    if (ev.key === 'Escape') {
      if (!$('#tiroir').hidden) return fermerFiche();
      if ($('#q').value) { $('#q').value = ''; S.filtre = ''; rendreStock(); rendreFrigo(); }
      return;
    }
    if (ev.key === '/' && document.activeElement !== $('#q')) {
      ev.preventDefault(); $('#q').focus(); $('#q').select();
    }
    if (ev.key === 'Enter') {
      const cible = document.activeElement;
      if (cible && cible.classList.contains('article-frigo')) ouvrirFiche(cible.dataset.code);
    }
    if (!ev.ctrlKey && !ev.metaKey && document.activeElement !== $('#q')
        && ev.key >= '1' && ev.key <= String(VUES.length)) {
      vue(VUES[Number(ev.key) - 1]);
    }
  });

  // --- courses ---------------------------------------------------------

  $('#ajout-course').addEventListener('submit', async ev => {
    ev.preventDefault();
    const champ = $('#course-libelle');
    const saisie = champ.value.trim();
    if (!saisie) return;
    const qte = Math.max(1, Math.min(99, Number($('#course-qte').value) || 1));
    // Une suite de chiffres est un code-barres ; le reste est un libelle.
    const parametres = /^[0-9]{6,14}$/.test(saisie) ? {code: saisie} : {libelle: saisie};
    parametres.qte = qte;
    champ.value = ''; $('#course-qte').value = 1;
    await agirCourses('ajouter', parametres);
    champ.focus();
  });

  $('#course-vider').addEventListener('click', () => agirCourses('vider'));

  $('#vue-courses').addEventListener('click', ev => {
    const bouton = ev.target.closest('[data-action]');
    if (bouton) {
      const ligne = bouton.closest('[data-course]');
      if (!ligne) return;
      const id = ligne.dataset.course;
      return bouton.dataset.action === 'cocher'
        ? agirCourses('cocher', {id, pris: bouton.dataset.pris})
        : agirCourses('retirer', {id});
    }
    const suggestion = ev.target.closest('[data-suggestion]');
    if (suggestion) agirCourses('ajouter', {code: suggestion.dataset.suggestion});
  });

  // --- theme -----------------------------------------------------------

  $('#theme').addEventListener('click', () => {
    const actuel = document.documentElement.dataset.theme
      || (matchMedia('(prefers-color-scheme: light)').matches ? 'clair' : 'sombre');
    poserTheme(actuel === 'clair' ? 'sombre' : 'clair');
  });

  $('#fermer').addEventListener('click', fermerFiche);
  $('#voile').addEventListener('click', fermerFiche);

  const toile = $('#frigo');
  toile.addEventListener('mousemove', ev => {
    const g = ev.target.closest('.article-frigo');
    if (!g) { if (bulleVisible) bulle(ev, null); return; }
    const poste = (S.etat.postes || []).find(p => p.code === g.dataset.code);
    bulle(ev, poste);
  });
  toile.addEventListener('mouseleave', ev => bulle(ev, null));

  addEventListener('hashchange', () => {
    const nom = location.hash.slice(1);
    if (VUES.includes(nom)) vue(nom);
  });
}

/* --------------------------------------------------------------- demarrage */

function demarrer() {
  brancherEvenements();
  const depart = location.hash.slice(1);
  if (VUES.includes(depart)) vue(depart);
  rattraper().then(brancher);
  setInterval(() => {
    if (!S.vivant && S.etat) {
      const h = new Date();
      $('#horloge').textContent = h.toLocaleTimeString('fr-FR');
    }
  }, 1000);
}

if (document.readyState === 'loading') {
  addEventListener('DOMContentLoaded', demarrer);
} else {
  demarrer();
}

})();
