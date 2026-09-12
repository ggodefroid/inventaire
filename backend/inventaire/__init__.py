"""Serveur d'inventaire du frigo pour terminal Datalogic Skorpio.

Le terminal est en HTTP simple (Windows CE 5.0, .NET Compact Framework 2.0) :
il ne sait ni faire du TLS moderne, ni analyser du JSON confortablement. Tout
ce qui est couteux ou chiffre se passe donc ici :

* Open Food Facts est interroge en HTTPS par le serveur, jamais par le terminal ;
* les photos produit sont retaillees et converties en BMP, seul format que le
  Compact Framework decode a coup sur sans codec d'image dans l'image OS ;
* chaque reponse est disponible en JSON (navigateur, curl) et en `cle=valeur`
  (terminal), a partir de la meme structure Python.
"""

VERSION = "1.0.0"
