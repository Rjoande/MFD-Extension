# VVThermalMap — Hub di progetto per Claude Code

Extra di [MFDExtension](../../CLAUDE.md): nuovo custom color mode per
VesselView/VesselViewRPM, "mappatura termica" del **guscio esterno**
(shell) delle parti — heatmap continua a 5 colori (blu/ciano/verde/
giallo/rosso) sulla temperatura di superficie. Nasce come fork di
conversazione da [VVEFIS](../VVEFIS/), ma è un **progetto separato**:
DLL proprio, nessun aggancio a `VVEFISSeverity`/Tier/arbitraggio DangIt
— solo un canale visivo descrittivo indipendente.

Il termico **core/interno** (`part.temperature`/`maxTemp`) NON è più
completamente fuori scope (revisione 2026-08-30, log 3): la skin resta
il sensore primario e guida da sola tutta la fascia fredda/ambiente,
ma il core può "scavalcarla" come lettura di allarme — vedi
"Sorgente peggiore" sotto. Un eventuale screen dedicato a SystemHeat
resta comunque un altro Extra, non questo.

**Convenzioni**: eredita tutte quelle del hub MFDExtension (italiano in
chat/CLAUDE.md, inglese nel codice, file piccoli, **STOP gate esplicito**
prima di ogni implementazione — si propone qui, si aspetta conferma).

**Passi concordati in sequenza**:
1. principi di design (sorgente dato, palette, curva/soglie) — **CHIUSO**,
   vedi log 1 e sezione dedicata sotto;
2. scaffolding cartella/csproj (mirror di `Extras/VVEFIS/`) — **CHIUSO**,
   vedi log 2;
3. lettura part/vessel e calcolo colore — **CHIUSO**, vedi log 3;
4. integrazione come custom color mode VesselView (stesso meccanismo di
   registrazione di VVEFIS) — **CHIUSO**, vedi log 4;
5. test in gioco, taratura eventuale di `ShellWarnRatio`/
   `DangerCurveExponent` a vista — **CHIUSO**, vedi log 8.

## Principi di design (CHIUSO — 2026-08-30)

**Sorgente dato**: `part.skinTemperature` / `part.skinMaxTemp`
(shell/guscio esterno) come sensore primario — guida da sola tutta la
fascia fredda/ambiente. Il core (`part.temperature`/`maxTemp`) entra in
gioco solo come override di allarme, vedi "Sorgente peggiore" sotto
(revisione 2026-08-30, log 3 — la versione originale del log 1 era
"mai il core", poi rivista in chat prima di scrivere codice).

**Sorgente peggiore (skin vs core)**: come lo stock (che usa
`max(skinFrac, coreFrac)` in `TemperatureGauge.GaugeUpdate`), anche qui
ha senso trattarlo come un sensore termico generalista — non una
"HULL TEMP" dedicata come in SituationalAwareness — quindi un core che
sta scaldando internamente (es. un motore che accumula calore in
camera) mentre la skin esterna sembra ancora a posto deve poter
emergere. Ma un confronto diretto "il più alto vince" romperebbe la
fascia fredda: il core raramente scende sotto ~280-300 K, quindi
vincerebbe quasi sempre e cancellerebbe blu/ciano per la maggior parte
dei casi normali. Soluzione: il core compete **solo quando è già lui
stesso oltre `ShellWarnRatio` (0.6) della propria `maxTemp`** — sotto
quella soglia è semplicemente ignorato, non confrontato. Sopra, vince
se è messo peggio della skin (frazione più alta della propria
`maxTemp` vs frazione della skin sulla propria `skinMaxTemp`), e in tal
caso guida lui la sola porzione di curva relativa (la curva continua
verde→rosso descritta sotto, calcolata sul proprio `maxTemp`) — non ha
senso dargli anche la fascia assoluta fredda, dato che non la visita
mai in pratica.
Nessuna terza soglia inventata: riusa `ShellWarnRatio` anche come
soglia di ammissibilità del core.
Deliberatamente NON è stata definita una nozione simmetrica di
"peggio" verso il freddo (l'utente l'ha proposta come possibilità): in
KSP non esistono danni da freddo, quindi sarebbe un concetto puramente
estetico/narrativo — complicherebbe la logica senza un beneficio
funzionale reale, e la soluzione sopra evita comunque che il core
schiacci la lettura fredda della skin.

**Palette**: interpolazione HSV a hue singola, S=1 V=1 fissi, solo hue
varia — continua per costruzione, nessuno step/banding discreto:
blu 240° → ciano 180° → verde 120° → rosso 0°.

**Curva (REVISIONATA al log 6, poi al log 7, 2026-08-30, dopo il primo
test in gioco)** — non più piecewise-lineare a 3 tratti col plateau
piatto chiuso al log 1: quel plateau (fino a `0.6·skinMaxTemp`) su
parti stock reali arrivava a valori assurdi (es. 1320 K sul Mk1 Pod,
`skinMaxTemp` 2200), restando verde ben oltre il punto in cui il gioco
mostra già plasma di rientro e la gauge stock. Il log 6 l'aveva prima
sostituita con un plateau piatto confinato a 0-100°C (273-373 K); il
log 7 ha ulteriormente semplificato quel plateau in un **punto unico**:

| Temperatura                          | Hue           | Colore percepito        |
|----------------------------------------|---------------|--------------------------|
| 3 K (floor)                            | 240°          | blu                      |
| 250 K                                  | 180°          | ciano                    |
| 300 K (`GreenPeak`)                    | 120°          | verde (punto, non fascia)|
| sopra `GreenPeak`, in su               | curva continua → 0° | verde→rosso, eased |
| `skinMaxTemp`/`maxTemp` (parte)        | 0°            | rosso pieno              |

- **Verde pieno è un punto a 300 K (`GreenPeak`), non più una fascia
  piatta 273-373 K** (log 7, richiesta esplicita dell'utente): l'
  intervallo 273-373 K resta comunque "verosimilmente uniforme" nella
  pratica di gioco, quindi un plateau dedicato non aggiungeva nulla che
  un singolo picco non desse già — semplificazione, non un cambio di
  intento. Continuità garantita alla giunzione: a `skinTemperature ==
  GreenPeak` sia la rampa ciano→verde sotto sia la curva di pericolo
  sopra restituiscono esattamente hue 120°, nessuna cucitura visibile.
- **Sopra 300 K, un'unica curva continua** (invariata nella forma dal
  log 6, solo l'ancora si è spostata da 373 a 300 K): `t = (frazione -
  300/max) / (1 - 300/max)`, poi `hue = lerp(verde, rosso,
  t^DangerCurveExponent)`. `max` è `skinMaxTemp` quando guida la skin,
  `maxTemp` quando guida il core (override). La rinormalizzazione fa sí
  che "appena sopra il picco verde" resti vicino al verde su OGNI
  parte, a prescindere da quanto sia alto il suo massimo.
- **`DangerCurveExponent = 0.64`**, calibrato sul Mk1 Pod
  (`skinMaxTemp=2200`) perché un arancio vivo (hue≈30°) compaia
  esattamente a frazione 0.7 — lo stesso punto in cui compare la gauge
  stock (`gaugeThreshold=0.7`, verificato al log 1), confermato
  dall'utente come bersaglio (log 6). Non ricalibrato al log 7: lo
  spostamento dell'ancora da 373 a 300 K sposta l'hue a frazione 0.7 di
  meno di 2° (verificato sulla carta: 29.9°→28.7° sul Mk1 Pod),
  variazione trascurabile. Essendo l'ancora fissa mentre `skinMaxTemp`
  è molto variabile tra parti reali (verificato su `.cfg` stock:
  800-3500 K), l'hue esatto a frazione 0.7 si sposta comunque
  leggermente da parte a parte — atteso, non un difetto da correggere
  con un secondo parametro.
- `ShellWarnRatio` (0.6, ripreso da `HullWarnRatio` di
  SituationalAwareness) **resta**, ma con un ruolo ristretto: soglia di
  ammissibilità del core nell'override "sorgente peggiore" sopra, non
  più un breakpoint della curva. `ShellDangerRatio`/`HueYellow`
  **rimossi** al log 6 (zero chiamanti, verificato con grep);
  `GreenFloor`/`PlateauHigh` **rimossi** al log 7 per lo stesso motivo,
  sostituiti da `GreenPeak`.

**Verificato su codice reale** (decompilato KSP, `Claude/ksp-decomp-full.zip`,
estratti in scratchpad `KSP.UI.Screens.Flight/TemperatureGauge.cs` e
`Part.cs`, non assunto a memoria):
- `part.skinTemperature`/`skinTemperature`/`maxTemp`/`skinMaxTemp` sono
  tutti `public double` su `Part` — confermato, nessuna sorpresa di tipo.
- Il floor di 3 K è coerente con quanto fa KSP stesso:
  `Part.skinUnexposedExternalTemp = 4.0` è il default stock per
  l'equilibrio di una skin non esposta/in ombra profonda — non identico
  ma nello stesso ordine di grandezza della scelta dell'utente, buona
  validazione indipendente.
- Lo stock **non ha un'unica soglia universale**: `TemperatureGauge.
  GaugeUpdate` usa `edgeHighlightThreshold = 0.5` (bagliore arancio/rosso
  sul bordo parte, quello a cui probabilmente si riferiva l'utente con
  "barre arancio-marroni") e `gaugeThreshold = 0.7` (comparsa della
  barra fluttuante), **entrambi moltiplicati per un `*Mult` per-parte**
  (`edgeHighlightThresholdMult`/`gaugeThresholdMult`, default 1). Quindi
  anche lo stock varia soglia per parte — 0.6/0.8 (da SA) non sono una
  replica esatta dei valori stock, ma cadono nello stesso ordine di
  grandezza: validazione ragionevole, non serve inseguire un match
  perfetto che nemmeno lo stock ha.
- `skinMaxTemp` è dichiarato con default `-1.0` nel sorgente (sentinella
  pre-risoluzione PartLoader), ma il codice stock stesso
  (`TemperatureGauge.GaugeUpdate`) divide `skinTemperature/skinMaxTemp`
  **senza alcun guard**: per una parte realmente in volo il loader l'ha
  già risolta a un valore positivo. Nessun guard difensivo aggiuntivo
  necessario nella nostra implementazione — stessa assunzione dello
  stock stesso.

**Nessun aggancio a VVEFISSeverity**: confermato dall'utente
("progetto separato... anche questa chat è un fork di VVEFIS"). Nessun
Tier/PartStatus/Alarm/bordo lampeggiante — puro overlay descrittivo,
colore = funzione diretta di `skinTemperature`, niente arbitraggio con
DangIt/FAR/RealBattery.

**Nome confermato**: `VVThermalMap`, sotto
`GameData/MFDExtension/Extras/VVThermalMap/` (sibling di `VVEFIS/`).

## Stato (aggiornare a ogni sessione)

- **2026-09-02 (9)** — **Toggle "wireframe in volo" portato da VVEFIS**
  (implementato e testato con successo lí per primo, vedi
  `MFDExtension/CLAUDE.md` log 76) — stessa identica tecnica,
  adattata ai nomi di questo progetto, istruzione data direttamente
  dall'utente nella sessione MFDExtension (non riaperta qui in chat,
  già autorizzata). Verificato sul sorgente reale di `VVEFISAddon.cs`
  prima di applicare (non assunto dalla descrizione): il pattern è
  esattamente quello riportato. `VVThermalMapAddon.cs`: nuovo campo
  `private static bool wireframeEnabled = false` (stato globale di
  processo, non per-schermo, stessa scelta di VVEFIS); `wireColorDullDelegate`
  passa da `mode => false` a `mode => wireframeEnabled` (il meccanismo
  "dull" nativo di `VesselViewer.GetPartColor`, che dimezza R/G/B del
  wire, È GIÀ la tinta più scura della stessa tonalità — nessuna
  funzione di attenuazione scritta a mano); `CreateMenu` guadagna una
  seconda voce `VViewSimpleCustomMenuItem("WIREFRAME ", () =>
  wireframeEnabled, v => wireframeEnabled = v)` accanto a "MODE ACTIVE"
  — spazio finale nella label mantenuto (bug di concatenazione
  "WIREFRAME"+"On" senza separatore, osservato e corretto su VVEFIS).
  `fillColorDelegate`/`boxColorDelegate`/`VVThermalMapColor.cs` non
  toccati — puro toggle di presentazione sul wire, indipendente dalla
  curva termica. Build Release pulita (0 errori/0 warning), DLL
  rideployata (`GameData/MFDExtension/GameData/MFDExtension/Extras/
  VVThermalMap/VVThermalMap.dll`, timestamp verificato). Nessun nuovo
  DLL, nessun cablaggio di tasti aggiuntivo — stesso sottomenu di
  "SHELL TEMP" già esistente. README/CHANGELOG del progetto principale
  non toccati (dettaglio troppo piccolo per giustificarlo da solo).
  **Prossimo**: test in gioco — voce "WIREFRAME Off" attesa sotto
  "MODE ACTIVE", commutabile a "On", spigoli in tinta più scura della
  heatmap quando attivo — non ancora confermato dall'utente.
- **2026-08-30 (8)** — **Secondo test in gioco: esito positivo,
  confermato dall'utente** ("la scala colore è più intuitiva e
  informativa! Missione compiuta") — la curva del log 7 (`GreenPeak` a
  punto singolo, `DangerCurveExponent=0.64` mai ricalibrato dopo lo
  spostamento dell'ancora) regge alla prova reale, nessun'altra
  correzione richiesta. Tutti e 5 i passi dello STOP-gate concordati
  all'inizio del progetto sono ora **CHIUSI**. Nessuna modifica di
  codice in questa voce — solo chiusura formale del piano.
  **Stato del modulo**: funzionale e validato in gioco. Possibili
  sviluppi futuri non ancora richiesti né pianificati: uno screen
  separato orientato al termico core/SystemHeat (menzionato come fuori
  scope fin dall'apertura del progetto), eventuale ulteriore taratura
  se emergessero casi limite su parti con `skinMaxTemp` molto diverso
  dal Mk1 Pod usato come riferimento per l'esponente.
- **2026-08-30 (7)** — **Semplificazione del plateau verde del log 6**:
  su richiesta dell'utente, il plateau piatto 273-373 K (0-100°C)
  diventa un **punto unico a 300 K** (`GreenPeak`) — l'utente osserva
  che l'intervallo 273-373 K resterà "verosimilmente uniforme" nella
  pratica di gioco, quindi il plateau non aggiungeva risoluzione
  reale. `VVThermalMapColor.cs`: `GreenFloor`/`PlateauHigh` rimossi,
  sostituiti da `GreenPeak = 300.0`; `ShellHue` perde il ramo "flat" e
  passa direttamente dalla rampa ciano→verde (`PlateauLow`→`GreenPeak`)
  a `DangerHue`; `DangerHue` rinormalizza su `GreenPeak/max` invece di
  `PlateauHigh/max`. Continuità alla giunzione verificata a mano (non
  assunta): a `skinTemperature == GreenPeak`, `frac == ambientFrac`
  esattamente, quindi `t=0` e hue=verde su entrambi i lati — nessuna
  cucitura. `DangerCurveExponent` **non** ricalibrato: lo spostamento
  dell'ancora (373→300 K) sposta l'hue a frazione 0.7 sul Mk1 Pod da
  29.9° a 28.7° (calcolo su carta), variazione irrilevante rispetto al
  bersaglio "arancio vivo" confermato al log 6. Build Release pulita (0
  errori/0 warning), DLL rideployata, nessun riferimento residuo a
  `GreenFloor`/`PlateauHigh` (verificato con grep). "Principi di
  design" riscritto di conseguenza. **Prossimo**: invariato dal log 6 —
  nuovo test in gioco per validare la curva (mai ancora confermata in
  volo, solo su carta).
- **2026-08-30 (6)** — **Revisione della curva post-plateau, in seguito
  alla diagnosi del log 5.** Utente ha confermato il bersaglio proposto
  in chat (arancio vivo esattamente a frazione 0.7, allineato alla
  gauge stock) e dato il via all'implementazione.
  `VVThermalMapColor.cs` riscritto: (1) `PlateauHigh` 350→373 K (100°C);
  nuova costante `GreenFloor = 273` K (0°C) — tra i due un vero plateau
  PIATTO (non più un punto singolo), esattamente il "verde pieno 0-100°C"
  richiesto; (2) `DangerHue` non è più piecewise-lineare a due tratti
  su `ShellWarnRatio`/`ShellDangerRatio` (0.6/0.8) — un'unica curva
  continua a potenza, `t^0.64` su `t` rinormalizzato sopra la frazione
  di `PlateauHigh` rispetto al max del canale che guida (skin o core,
  qual è l'override). Esponente calibrato a mano (non da dati reali di
  gioco, da conti su carta) sul Mk1 Pod come parte di riferimento
  (`skinMaxTemp=2200`) per ottenere hue≈30° (arancio vivo) esattamente
  a frazione 0.7; punti di verifica sulla stessa parte: 500 K→hue≈98°
  (verde-giallo, appena sopra il plateau), 1000 K→hue≈60° (giallo),
  1540 K (frazione 0.7)→hue≈30° (arancio vivo, bersaglio), 2000 K→
  hue≈9° (rosso profondo), 2200 K (max)→hue=0° (rosso pieno). **Pulizia
  conseguente**: `ShellDangerRatio` e `HueYellow` rimossi (zero
  chiamanti dopo la riscrittura, verificato con grep sull'intero
  `Extras/VVThermalMap`) — `ShellWarnRatio` resta, ma solo per
  l'ammissibilità del core nell'override (log 3), non più come
  breakpoint della curva. "Principi di design" sopra riscritto per
  intero nella sezione Palette/Curva a riflettere il nuovo stato (non
  solo loggato qui). Build Release pulita (0 errori/0 warning), DLL
  rideployata. **Prossimo**: nuovo giro di test in gioco per validare
  la curva rivista contro l'esperienza reale (l'esponente è stato
  calibrato solo sulla carta, non ancora confermato in volo) — non
  ancora fatto.
- **2026-08-30 (5)** — **Primo test in gioco: esito "nel complesso bene",
  due riscontri.** (1) **Bordo nero aggiunto** su richiesta esplicita:
  `VVThermalMapAddon.boxColorDelegate` passa da `Color.clear` a
  `Color.black` — resta comunque senza semantica di allarme (nessun
  Tier/stato dietro), è un contorno statico per leggibilità, stessa
  scelta statica dell'esempio `VVDiscoDisplay` di VesselView stesso.
  Build pulita, DLL rideployata.
  (2) **Verde troppo esteso, aperta in chat (non ancora implementata)**:
  l'utente segnala che durante il rientro (500-1000 K sulla skin, quando
  in gioco è già visibile il plasma/tinta di rientro e sta per comparire
  la gauge stock) il monitor mostra ancora verde pieno; vorrebbe verde
  pieno confinato a 0-100°C e "arancio vivo" già quando compare la
  gauge stock. Confronto con SA: dava letture "peggiori" (più corrette).
  **Verificato sui .cfg stock reali** (non assunto): `mk1Pod_v2.cfg` ha
  `maxTemp=1200` **ma `skinMaxTemp=2200`** — non uguali. Altri comand
  pod/cockpit stock nello stesso pattern: cupola/mk1Cockpit/mk1LanderCan
  `skinMaxTemp=2000`, mk1-3 `2400`, mk2Cockpit `2500`, mk3Cockpit/
  mk3Fuselage CREW `2700`, l'HeatShield gonfiabile `3500` — tutti ben
  sopra il loro `maxTemp` di core (confermato anche in decompilato,
  `Part.cs` riga 2564: `skinMaxTemp` eredita `maxTemp` SOLO se il .cfg
  non lo specifica affatto, altrimenti resta il valore esplicito). È
  design stock deliberato: la skin di una capsula è pensata come strato
  scudo-termico, tollera molto più calore del core sottostante.
  **Diagnosi**: il plateau verde piatto (da 350/373 K fino a
  `0.6·skinMaxTemp`) su queste parti si estende fino a valori assurdi in
  Kelvin assoluti (0.6·2200=1320 K per il Mk1 Pod, 0.6·3500=2100 K per
  l'HeatShield) — è il vero colpevole, non un bug nella scelta
  skin/peggiore. **Nota sul confronto con SA**: non è mele con mele — la
  lettura "peggiore" di SA è calcolata sull'INTERA nave (la singola
  parte più vicina al proprio limite ovunque si trovi), non parte per
  parte; un'heatmap per-parte mostrerà normalmente una lettura "più
  calma" di un indicatore vessel-wide, per costruzione — non è un
  difetto da inseguire replicandolo pari pari.
  **Prossimo**: proposta di revisione della curva (sostituire il
  plateau piatto con una curva continua/esponenziale sopra la fascia
  ambiente) discussa in chat, non ancora implementata — vedi prossimo
  log per l'esito.
- **2026-08-30 (4)** — **Step 4: integrazione come custom color mode
  VesselView.** Nuovo `Extras/VVThermalMap/src/VVThermalMapAddon.cs`,
  mirror pressoché letterale di `VVEFISAddon.cs` (stesso
  `[KSPAddon(Flight, true)]`, stesso check `ModPresence.IsLoaded
  ("VesselViewRPM")` isolato in un metodo separato dal resto per evitare
  `TypeLoadException` se VesselView manca, stessa registrazione
  `VViewCustomMenusMenu.registerMenu` + `VesselViewPlugin.
  registerCustomMode`, stesso guard sull'item-array vuoto in
  `CreateMenu`). Nome del modo: **"SHELL TEMP"**.
  Prima di scrivere codice, **letto per intero il sorgente reale di
  VesselView** (disponibile in chiaro in
  `GameData/VesselView/VesselViewer-master/`, non decompilato):
  `CustomModeSettings.cs` e la parte di `VesselViewer.cs` che consuma i
  delegate (`GetBoxColor`/`GetPartColor`/`renderRects`). Due scoperte
  concrete che hanno cambiato l'implementazione rispetto a un mirror
  ingenuo di VVEFIS:
  - **`boxColorDelegate` non può restare `null`**: con
    `ColorModeOverride = FUNCTION`, `VesselViewer.GetBoxColor` lo invoca
    incondizionatamente (`customMode.boxColorDelegate(customMode, part)`,
    nessun null-check) — lasciarlo non impostato avrebbe prodotto una
    `NullReferenceException` al primo render. Dato che questo screen non
    ha (di proposito) alcun concetto di bordo/allarme, il delegate
    ritorna sempre `Color.clear`.
  - **Alpha, non un colore "neutro", è il modo giusto per dire "non
    disegnare"**: verificato su `VesselViewer.cs` (`renderRects`:
    `if (next.color.a != 0) renderRect(...)`, e lo stesso pattern
    `if (fillPartColor.a != 0)`/`if (wirePartColor.a != 0)` per
    fill/wire) — un rettangolo a alpha 0 viene accodato ma mai
    effettivamente disegnato. `Color.clear` (alpha 0) è quindi
    esattamente "nessun bordo", non un'approssimazione — a differenza di
    un nero opaco (che invece disegnerebbe un riquadro nero visibile).
  Per il resto mirror diretto di VVEFIS: `fillColorDelegate`/
  `wireColorDelegate` entrambi su `VVThermalMapColor.GetColor(part)`
  (stesso colore per fill e wire, come VVEFIS), `staticSettings.
  displayEngines = false` con la stessa motivazione (le icone motore
  hardcoded si sovrapporrebbero al nostro fill), `*DullDelegate` tutti
  `false`. Build Release pulita (0 errori/0 warning), DLL rideployata
  (`GameData/MFDExtension/GameData/MFDExtension/Extras/VVThermalMap/
  VVThermalMap.dll`, timestamp verificato). **Prossimo**: step 5, test
  in gioco — non ancora fatto, in attesa di via libera. Da ricordare:
  copiare `Extras/VVThermalMap/` (cartella completa) sull'install reale
  prima del test, stesso passaggio manuale che serve per VVEFIS.
- **2026-08-30 (3)** — **Rivista in chat la scelta "solo skin" del log 1,
  poi implementato lo step 3.** L'utente ha chiesto di riconsiderare la
  logica stock (`max(skinFrac, coreFrac)`, verificata al log 1 su
  `TemperatureGauge.cs`): questo screen è un sensore termico
  generalista, non una "HULL TEMP" dedicata come in SA, quindi ha senso
  che un core che scalda internamente possa emergere anche se la skin
  sembra a posto. Punto critico sollevato dall'utente e risolto in
  chat: un confronto diretto "il più alto vince" avrebbe quasi sempre
  fatto vincere il core (che raramente scende sotto ~280-300 K),
  cancellando la fascia blu/ciano per la maggior parte dei casi
  normali. Soluzione: il core compete solo quando è GIÀ oltre il
  proprio `ShellWarnRatio` (0.6) — sotto quella soglia è ignorato, non
  confrontato — e in tal caso vince solo se messo peggio della skin,
  guidando solo la porzione di curva relativa (mai la fascia fredda
  assoluta, che comunque non visita mai). Scartata esplicitamente una
  nozione simmetrica di "peggio" verso il freddo proposta come
  alternativa dall'utente: in KSP il freddo non causa danni, sarebbe
  puramente estetico e avrebbe complicato la logica senza beneficio
  reale. Dettagli in "Principi di design" sopra (sezione "Sorgente
  peggiore", aggiornata in loco, non solo loggata qui).
  **Implementazione**: nuovo `Extras/VVThermalMap/src/VVThermalMapColor.cs`
  — `VVThermalMapColor.GetColor(Part)`, unico entry point pubblico
  (`internal`), converte la logica sopra in hue HSV
  (`Color.HSVToRGB`, S=1 V=1 fissi) e ritorna il `Color` finale. Nessun
  guard su `skinMaxTemp`/`maxTemp` (stessa assunzione già verificata
  allo stock al log 1: per una parte in volo sono già risolti a valori
  positivi). Build Release verificata pulita (0 errori/0 warning — il
  metodo `GetColor` non ha ancora chiamanti, nessun problema: un
  `internal` non referenziato non genera warning, stessa nota già
  vista su VVEFIS/`AdvisoryCyan`), DLL rideployata. **Prossimo**: step 4,
  integrazione come custom color mode VesselView (mirror del
  meccanismo di registrazione di `VVEFISAddon.cs`) — non ancora
  iniziato, in attesa di via libera.
- **2026-08-30 (2)** — **Scaffolding cartella/csproj**, mirror di
  `Extras/VVEFIS/src/` con una deviazione deliberata: il basket
  condiviso NON viene incluso per intero (`Shared/*.cs`) perché questo
  screen non usa DangIt/FAR/RealBattery/Tier/FuelEngineReader — solo
  `src/Shared/ModPresence.cs` è source-linkato (stesso pattern
  `Link>Shared\...` di VVEFIS), per il check "VesselViewRPM è
  installato?" che servirà nel futuro Addon. Creati
  `Extras/VVThermalMap/src/VVThermalMap.csproj` (net472, `AssemblyName`/
  `RootNamespace` = `VVThermalMap`, versione iniziale 0.1.0, stessi
  riferimenti stock/VesselView di VVEFIS con gli stessi percorsi
  relativi — la profondità cartella è identica, 5 livelli sotto la
  root KSP) e `Directory.Build.props` (stessa regola exFAT: intermedi
  MSBuild fuori da E:, sotto `%LOCALAPPDATA%\VVThermalMap\`). Nessun
  file `.cs` proprio ancora (solo il link a `ModPresence.cs`) — build
  Release verificata pulita (0 errori/0 warning, assembly quasi vuoto
  ma valido) e `DeployToGameData` confermato funzionante: DLL comparsa
  in `GameData/MFDExtension/GameData/MFDExtension/Extras/VVThermalMap/
  VVThermalMap.dll`. **Prossimo**: step 3, lettura
  `skinTemperature`/`skinMaxTemp` e calcolo colore secondo la curva
  chiusa al log 1 — non ancora iniziato, in attesa di via libera.
- **2026-08-30 (1)** — Prima sessione: principi di design chiusi in chat
  (nessun codice ancora). Discussi e risolti in sequenza: sorgente dato
  (skin, non core — il core è rimandato a un altro, futuro screen
  orientato a SystemHeat), floor a 3 K con motivazione fisica (fondo
  cosmico) e conferma indiretta da `skinUnexposedExternalTemp` stock,
  plateau assoluto 250-350 K su esperienza empirica dell'utente con
  SituationalAwareness, forma della curva (piecewise lineare in hue, non
  eased — la non-linearità viene dai tratti diseguali), soglie del
  ramo pericoloso riprese identiche da `HullWarnRatio`/`HullDangerRatio`
  di SituationalAwareness (0.6/0.8) invece di inventarne di nuove,
  verificate contro `TemperatureGauge.cs`/`Part.cs` decompilati reali
  (estratti da `Claude/ksp-decomp-full.zip` in scratchpad — mai
  estratto l'intero archivio su E:, coerente con la regola cluster
  exFAT). Confermato che questo screen resta indipendente da
  VVEFISSeverity (nessun Tier/Alarm/arbitraggio). Nome del modulo
  confermato: `VVThermalMap`. **Prossimo**: scaffolding cartella/csproj
  (mirror di `Extras/VVEFIS/`) — non ancora iniziato, in attesa di via
  libera esplicita (STOP gate).
