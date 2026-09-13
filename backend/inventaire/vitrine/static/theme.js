/* Applique le theme retenu avant le premier pixel.

   Ce fichier existe parce que la page interdit le script inline
   (`script-src 'self'`, voir application.py) : le meme code place dans une
   balise <script> de l'en-tete etait rejete en silence, et le choix de
   l'utilisateur ne survivait donc pas a un rechargement.

   Charge sans `defer` : il doit s'executer avant que le corps ne soit peint,
   sinon la page s'affiche en sombre puis bascule sous les yeux. */
(() => {
  try {
    const retenu = localStorage.getItem('frigo-theme');
    if (retenu === 'clair' || retenu === 'sombre') {
      document.documentElement.dataset.theme = retenu;
    }
  } catch (e) {
    /* stockage refuse (navigation privee, cookies bloques) : on suit le
       systeme, ce que fait deja la feuille de style. */
  }
})();
