"""Tableau de bord de secours, dans un navigateur.

Le terminal reste l'outil principal : c'est lui qu'on a en main devant le
frigo. Cette page sert pour ce qu'un pave numerique fait mal -- nommer un
produit absent d'Open Food Facts, corriger une date tapee de travers -- et
pour regarder l'etat du frigo depuis un telephone.

Elle est volontairement sans dependance : une seule page, style et script
compris. Rien a installer, rien a construire.
"""

from __future__ import annotations

from . import VERSION

FAVICON = (
    b'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 32 32">'
    b'<rect width="32" height="32" rx="6" fill="#1a7f4b"/>'
    b'<rect x="9" y="6" width="14" height="20" rx="2" fill="#fff"/>'
    b'<rect x="9" y="14" width="14" height="1.6" fill="#1a7f4b"/>'
    b'<rect x="11" y="9" width="1.8" height="3.4" rx=".9" fill="#1a7f4b"/>'
    b'<rect x="11" y="17.5" width="1.8" height="3.4" rx=".9" fill="#1a7f4b"/>'
    b'</svg>'
)

GABARIT = """<!doctype html>
<html lang="fr"><head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>Inventaire</title>
<link rel="icon" href="/favicon.ico">
<style>
:root{
  --fond:#f6f7f5; --carte:#fff; --trait:#dfe2dd; --texte:#1b1d1a; --doux:#6a706a;
  --vert:#1a7f4b; --orange:#d97706; --rouge:#c0392b; --accent:#1a7f4b;
}
@media (prefers-color-scheme:dark){:root{
  --fond:#14171a; --carte:#1c2024; --trait:#2c3238; --texte:#e8eae7; --doux:#99a1a0;
  --vert:#3fae74; --orange:#e2953a; --rouge:#e2604f; --accent:#3fae74;
}}
*{box-sizing:border-box}
body{margin:0;padding:1rem;background:var(--fond);color:var(--texte);
  font:15px/1.45 system-ui,-apple-system,Segoe UI,Roboto,sans-serif}
main{max-width:820px;margin:0 auto}
h1{font-size:1.25rem;margin:0 0 .2rem;letter-spacing:-.01em}
.sous{color:var(--doux);font-size:.82rem;margin:0 0 1rem}
.barre{display:flex;gap:.5rem;flex-wrap:wrap;margin-bottom:1rem}
input,select,button{font:inherit;padding:.5rem .65rem;border-radius:8px;
  border:1px solid var(--trait);background:var(--carte);color:var(--texte)}
input[type=text]{flex:1;min-width:9rem}
button{cursor:pointer}
button.p{background:var(--accent);border-color:var(--accent);color:#fff;font-weight:600}
button:disabled{opacity:.45;cursor:default}
.chiffres{display:flex;gap:1.1rem;flex-wrap:wrap;margin-bottom:1rem;
  color:var(--doux);font-size:.8rem}
.chiffres b{color:var(--texte);font-size:1.05rem;font-weight:650;
  font-variant-numeric:tabular-nums}
ul{list-style:none;margin:0;padding:0;display:flex;flex-direction:column;gap:.4rem}
li{background:var(--carte);border:1px solid var(--trait);border-left-width:4px;
  border-radius:9px;padding:.55rem .7rem;display:flex;align-items:center;gap:.7rem}
li.vert{border-left-color:var(--vert)} li.orange{border-left-color:var(--orange)}
li.rouge{border-left-color:var(--rouge)} li.gris{border-left-color:var(--trait)}
.vignette{width:40px;height:40px;border-radius:6px;object-fit:contain;
  background:#fff;flex:none;border:1px solid var(--trait)}
.bloc{flex:1;min-width:0}
.nom{font-weight:600;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
.detail{color:var(--doux);font-size:.78rem;white-space:nowrap;overflow:hidden;
  text-overflow:ellipsis}
.note{display:inline-block;width:1.15rem;height:1.15rem;line-height:1.15rem;
  text-align:center;border-radius:4px;color:#fff;font-size:.7rem;font-weight:700;
  text-transform:uppercase;vertical-align:-2px;margin-right:.25rem}
.qte{font-variant-numeric:tabular-nums;font-weight:650;min-width:1.6rem;
  text-align:right}
.actions{display:flex;gap:.3rem;flex:none}
.actions button{padding:.3rem .55rem;min-width:2rem}
.vide{color:var(--doux);text-align:center;padding:2.5rem 0}
.msg{padding:.5rem .7rem;border-radius:8px;background:var(--carte);
  border:1px solid var(--trait);margin-bottom:.7rem;font-size:.85rem}
.msg.ko{border-color:var(--rouge);color:var(--rouge)}
footer{margin-top:1.6rem;color:var(--doux);font-size:.75rem;text-align:center}
code{font-family:ui-monospace,SFMono-Regular,Menlo,monospace;font-size:.9em}
</style></head><body><main>
<h1>Inventaire du frigo</h1>
<p class="sous">__SOUS__</p>
<div id="msg"></div>
<form class="barre" id="f">
  <input type="text" id="code" placeholder="code-barres" inputmode="numeric" autofocus>
  <input type="date" id="date" title="peremption">
  <input type="number" id="qte" value="1" min="1" max="99" style="width:4.5rem">
  <button class="p" type="submit">Ajouter</button>
</form>
<div class="barre">
  <select id="tri">
    <option value="peremption">par urgence</option>
    <option value="nom">par nom</option>
    <option value="recent">par ajout</option>
  </select>
  <button type="button" id="raf">Rafraichir</button>
</div>
<div class="chiffres" id="chiffres"></div>
<ul id="liste"></ul>
<footer>inventaire __VERSION__ &middot; <code>/api/inventaire</code>
 &middot; <code>/api/scan?code=...&amp;fmt=kv</code></footer>
</main>
<script>
const $ = s => document.querySelector(s);
const api = async (route, params, methode) => {
  const q = new URLSearchParams(params || {});
  const r = methode === 'POST'
    ? await fetch(route, {method:'POST', body:q,
        headers:{'Content-Type':'application/x-www-form-urlencoded'}})
    : await fetch(route + (q.toString() ? '?' + q : ''));
  return r.json();
};
const dire = (texte, ko) => {
  $('#msg').innerHTML = texte
    ? `<div class="msg${ko ? ' ko' : ''}">${texte.replace(/[<>&]/g, c =>
        ({'<':'&lt;','>':'&gt;','&':'&amp;'}[c]))}</div>` : '';
  if (texte && !ko) setTimeout(() => { $('#msg').innerHTML = ''; }, 4000);
};
const urgence = j => j === null || j === '' ? 'gris'
  : j < 0 ? 'rouge' : j <= 2 ? 'rouge' : j <= 6 ? 'orange' : 'vert';
const echeance = (d, j) => !d ? 'sans date'
  : j < 0 ? `perime depuis ${-j} j` : j === 0 ? "perime aujourd'hui"
  : j === 1 ? 'demain' : `dans ${j} j &middot; ${d.split('-').reverse().join('/')}`;
const COULEURS = {a:'#1a7f4b', b:'#85bb2f', c:'#f5c400', d:'#e07b00', e:'#d33a1f'};

let postes = [];
async function charger() {
  const d = await api('/api/inventaire', {tri: $('#tri').value});
  postes = d.postes || [];
  const c = d.compteurs || {};
  $('#chiffres').innerHTML =
    `<span><b>${c.unites||0}</b> unites</span><span><b>${c.references||0}</b> references</span>` +
    `<span><b>${c.lots||0}</b> lots</span>` +
    (c.perimes ? `<span style="color:var(--rouge)"><b>${c.perimes}</b> perimes</span>` : '');
  $('#liste').innerHTML = postes.length ? postes.map(p => `
    <li class="${urgence(p.jours)}">
      ${p.nutriscore || p.image !== 0
        ? `<img class="vignette" src="/api/image?code=${p.code}&l=80&h=80&img=png"
             alt="" loading="lazy" onerror="this.style.visibility='hidden'">` : ''}
      <div class="bloc">
        <div class="nom">${p.nutriscore
          ? `<span class="note" style="background:${COULEURS[p.nutriscore]}">${p.nutriscore}</span>` : ''}${p.nom}</div>
        <div class="detail">${echeance(p.peremption, p.jours)}${
          p.nb_lots > 1 ? ` &middot; ${p.nb_lots} lots` : ''}${
          p.marque ? ' &middot; ' + p.marque : ''}${
          p.quantite ? ' &middot; ' + p.quantite : ''}</div>
      </div>
      <span class="qte">${p.total}</span>
      <div class="actions">
        <button type="button" onclick="bouger('${p.code}','retirer')">&minus;</button>
        <button type="button" onclick="bouger('${p.code}','ajouter')">+</button>
        <button type="button" onclick="renommer('${p.code}')" title="nommer">&hellip;</button>
      </div>
    </li>`).join('') : '<li class="vide">Le frigo est vide.</li>';
}
window.bouger = async (code, action) => {
  const d = await api('/api/' + action, {code, qte: 1, origine: 'web'}, 'POST');
  d.ok ? dire(d.message) : dire(d.erreur, true);
  charger();
};
window.renommer = async code => {
  const poste = postes.find(p => p.code === code);
  const nom = prompt('Libelle pour ' + code, poste ? poste.nom : '');
  if (nom === null) return;
  const d = await api('/api/nommer', {code, nom}, 'POST');
  d.ok ? dire('libelle enregistre') : dire(d.erreur, true);
  charger();
};
$('#f').onsubmit = async e => {
  e.preventDefault();
  const code = $('#code').value.trim();
  if (!code) return;
  const d = await api('/api/ajouter', {code, qte: $('#qte').value,
    peremption: $('#date').value, origine: 'web'}, 'POST');
  d.ok ? dire(d.message) : dire(d.erreur, true);
  $('#code').value = ''; $('#code').focus();
  charger();
};
$('#tri').onchange = charger;
$('#raf').onclick = charger;
charger();
</script></body></html>
"""


def page(application) -> str:
    compteurs = application.base.compteurs()
    etat = "hors ligne" if not application.en_ligne else "Open Food Facts actif"
    sous = (f"{compteurs['unites']} unites &middot; {compteurs['references']} references "
            f"&middot; {etat}")
    return GABARIT.replace("__SOUS__", sous).replace("__VERSION__", VERSION)


GABARIT_TELECHARGEMENT = """<html><head><meta charset="utf-8"><title>Inventaire</title>
</head><body style="font-family:sans-serif;font-size:14px;margin:8px">
<h3 style="margin:0 0 8px">Installer le client</h3>
__LISTE__
<p style="color:#555;font-size:12px">Sur Internet Explorer de Windows CE :
maintenir le doigt sur le lien, puis <i>Enregistrer la cible sous...</i>, et
deposer les deux fichiers dans un dossier de la memoire persistante du
terminal.</p>
</body></html>
"""


def page_telechargement(application) -> str:
    """Page volontairement en HTML de 1998 : c'est un IE de Windows CE en face.

    Pas de CSS externe, pas de script, pas d'attribut `download` -- rien de ce
    qui a ete invente apres 2005 n'y serait compris.
    """
    fichiers = application.inventaire_livrables()
    if not fichiers:
        liste = ("<p><b>Rien dans dist/.</b><br>Lancez <tt>./build.sh</tt> sur le "
                 "PC, puis rechargez cette page.</p>")
    else:
        lignes = "".join(
            f'<li><a href="/telecharger/{f["nom"]}">{f["nom"]}</a> '
            f'({f["taille"] // 1024 or 1} ko)</li>' for f in fichiers)
        liste = f"<ul>{lignes}</ul>"
    return GABARIT_TELECHARGEMENT.replace("__LISTE__", liste)
